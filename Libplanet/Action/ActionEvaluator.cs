#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Security.Cryptography;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Store.Trie;
using Libplanet.Tx;
using Serilog;

namespace Libplanet.Action
{
    public class ActionEvaluator<T>
        where T : IAction, new()
    {
        public static readonly StateGetter<T> NullStateGetter =
            (address, hashDigest, stateCompleter) => null;

        public static readonly BalanceGetter<T> NullBalanceGetter =
            (address, currency, hashDigest, fungibleAssetStateCompleter)
            => new FungibleAssetValue(currency);

        public static readonly AccountStateGetter NullAccountStateGetter = address => null;
        public static readonly AccountBalanceGetter NullAccountBalanceGetter =
            (address, currency) => new FungibleAssetValue(currency);

        private readonly ILogger _logger;
        private readonly IAction? _policyBlockAction;
        private readonly StateGetter<T> _stateGetter;
        private readonly BalanceGetter<T> _balanceGetter;

        private readonly Func<BlockHash, ITrie>? _trieGetter;

        internal ActionEvaluator(
            IAction? policyBlockAction,
            StateGetter<T> stateGetter,
            BalanceGetter<T> balanceGetter,
            Func<BlockHash, ITrie>? trieGetter)
        {
            _logger = Log.ForContext(typeof(ActionEvaluator<T>));
            _policyBlockAction = policyBlockAction;
            _stateGetter = stateGetter;
            _balanceGetter = balanceGetter;
            _trieGetter = trieGetter;
        }

        /// <summary>
        /// Executes every <see cref="IAction"/> in <see cref="Block{T}.Transactions"/>
        /// and gets result states of each step of every <see cref="Transaction{T}"/>.
        /// <para>It throws an <see cref="InvalidBlockException"/> or
        /// an <see cref="InvalidTxException"/> if there is any
        /// integrity error.</para>
        /// <para>Otherwise it enumerates an <see cref="ActionEvaluation"/>
        /// for each <see cref="IAction"/>.</para>
        /// </summary>
        /// <param name="block">A <see cref="Block{T}"/> instance to evaluate.</param>
        /// <param name="currentTime">The current time to validate
        /// time-wise conditions.</param>
        /// <param name="accountStateGetter">An <see cref="AccountStateGetter"/> delegate to get
        /// a previous state. This affects the execution of <see cref="Transaction{T}.Actions"/>.
        /// </param>
        /// <param name="accountBalanceGetter">An <see cref="AccountBalanceGetter"/> delegate to
        /// get previous account balance. This affects the execution of
        /// <see cref="Transaction{T}.Actions"/>.
        /// </param>
        /// <param name="previousBlockStatesTrie">The trie to contain states at previous block.
        /// </param>
        /// <returns>An <see cref="ActionEvaluation"/> for each
        /// <see cref="IAction"/>.</returns>
        /// <exception cref="InvalidBlockHashException">Thrown when
        /// the <paramref name="block.Hash"/> is invalid.</exception>
        /// <exception cref="InvalidBlockTimestampException">Thrown when
        /// the <paramref name="block.Timestamp"/> is invalid, for example, it is the far
        /// future than the given <paramref name="currentTime"/>.</exception>
        /// <exception cref="InvalidBlockIndexException">Thrown when
        /// the <paramref name="block.Index"/>is invalid, for example, it is a negative
        /// integer.</exception>
        /// <exception cref="InvalidBlockDifficultyException">Thrown when
        /// the <paramref name="block.Difficulty"/> is not properly configured,
        /// for example, it is too easy.</exception>
        /// <exception cref="InvalidBlockPreviousHashException">Thrown when
        /// <paramref name="block.PreviousHash"/> is invalid so that
        /// the <see cref="Block{T}"/>s are not continuous.</exception>
        /// <exception cref="InvalidBlockNonceException">Thrown when
        /// the <paramref name="block.Nonce"/> does not satisfy its
        /// <paramref name="block.Difficulty"/> level.</exception>
        /// <exception cref="InvalidBlockTxHashException">Thrown when
        /// the <paramref name="block.TxHash" /> does not match with its
        /// <paramref name="block.Transactions"/>.</exception>
        /// <exception cref="InvalidTxUpdatedAddressesException">Thrown when
        /// any <see cref="IAction"/> of <paramref name="block.Transactions"/> tries
        /// to update the states of <see cref="Address"/>es not included
        /// in <see cref="Transaction{T}.UpdatedAddresses"/>.</exception>
        /// <exception cref="InvalidTxSignatureException">Thrown when its
        /// <see cref="Transaction{T}.Signature"/> is invalid or not signed by
        /// the account who corresponds to its <see cref="PublicKey"/>.
        /// </exception>
        /// <exception cref="InvalidTxPublicKeyException">Thrown when its
        /// <see cref="Transaction{T}.Signer"/> is not derived from its
        /// <see cref="Transaction{T}.PublicKey"/>.</exception>
        /// <remarks>Publicly exposed for benchmarking.</remarks>
        [Pure]
        public static IEnumerable<ActionEvaluation> EvaluateBlock(
            Block<T> block,
            DateTimeOffset currentTime,
            AccountStateGetter accountStateGetter,
            AccountBalanceGetter accountBalanceGetter,
            ITrie? previousBlockStatesTrie = null)
        {
            // FIXME: Probably not the best place to have Validate().
            block.Validate(currentTime);

            IEnumerable<Tuple<Transaction<T>, ActionEvaluation>> txEvaluationPairs =
                EvaluateTxsGradually(
                    block, accountStateGetter, accountBalanceGetter, previousBlockStatesTrie)
                    .ToArray();
            var updatedTxAddressPairs = txEvaluationPairs
                    .GroupBy(tuple => tuple.Item1)
                    .Select(
                        grp => (
                            grp.Key,
                            grp.Last().Item2.OutputStates.UpdatedAddresses));
            foreach (
                (Transaction<T> tx, IImmutableSet<Address> updatedAddresses)
                in updatedTxAddressPairs)
            {
                if (!tx.UpdatedAddresses.IsSupersetOf(updatedAddresses))
                {
                    const string msg =
                        "Actions in the transaction try to update " +
                        "the addresses not granted.";
                    throw new InvalidTxUpdatedAddressesException(
                        tx.Id,
                        tx.UpdatedAddresses,
                        updatedAddresses,
                        msg);
                }
            }

            return txEvaluationPairs.Select(te => te.Item2);
        }

        /// <summary>
        /// Executes every <see cref="IAction"/> in a given
        /// <see cref="Block{T}.Transactions"/> step by step, and emits a
        /// <see cref="Transaction{T}"/> and an <see cref="ActionEvaluation"/> as a pair
        /// for each step.
        /// </summary>
        /// <param name="block">A <see cref="Block{T}"/> instance to evaluate.</param>
        /// <param name="accountStateGetter">An <see cref="AccountStateGetter"/>
        /// delegate to get a previous state.</param>
        /// <param name="accountBalanceGetter">An <see cref="AccountBalanceGetter"/> delegate to
        /// get previous account balance.</param>
        /// <param name="previousBlockStatesTrie">The trie to contain states at previous block.
        /// </param>
        /// <returns>Enumerates pair of a transaction, and <see cref="ActionEvaluation"/>
        /// for each action.  The order of pairs are the same to
        /// the <paramref name="block.Transactions"/> and their <see cref="Transaction{T}.Actions"/>
        /// (e.g., tx&#xb9;-act&#xb9;, tx&#xb9;-act&#xb2;, tx&#xb2;-act&#xb9;, tx&#xb2;-act&#xb2;,
        /// &#x2026;).
        /// <para>If a <see cref="Transaction{T}"/> has multiple
        /// <see cref="Transaction{T}.Actions"/>, each <see cref="ActionEvaluation"/> includes
        /// all previous <see cref="ActionEvaluation"/>s' delta in the same
        /// <see cref="Transaction{T}"/> besides its own delta.</para>
        /// <para>Note that each <see cref="IActionContext.Random"/> object has a unconsumed state.
        /// </para>
        /// </returns>
        [Pure]
        internal static IEnumerable<Tuple<Transaction<T>, ActionEvaluation>> EvaluateTxsGradually(
            Block<T> block,
            AccountStateGetter accountStateGetter,
            AccountBalanceGetter accountBalanceGetter,
            ITrie? previousBlockStatesTrie = null)
        {
            IAccountStateDelta delta;
            foreach (Transaction<T> tx in block.Transactions)
            {
                delta = block.ProtocolVersion > 0
                    ? new AccountStateDeltaImpl(accountStateGetter, accountBalanceGetter, tx.Signer)
                    : new AccountStateDeltaImplV0(
                        accountStateGetter, accountBalanceGetter, tx.Signer);
                IEnumerable<ActionEvaluation> evaluations = EvaluateTxGradually(
                    tx: tx,
                    preEvaluationHash: block.PreEvaluationHash,
                    blockIndex: block.Index,
                    previousStates: delta,
                    minerAddress: block.Miner!.Value,
                    rehearsal: false,
                    previousBlockStatesTrie: previousBlockStatesTrie);
                foreach (var evaluation in evaluations)
                {
                    yield return Tuple.Create(tx, evaluation);
                    delta = evaluation.OutputStates;
                }

                accountStateGetter = delta.GetState;
                accountBalanceGetter = delta.GetBalance;
            }
        }

        /// <summary>
        /// Executes the <see cref="Transaction{T}.Actions"/> of given <see cref="Transaction{T}"/>
        /// instance step by step, and emits <see cref="ActionEvaluation"/> for each step.
        /// <para>If the needed value is only the final states,
        /// use <see cref="EvaluateTxResult"/> method instead.</para>
        /// </summary>
        /// <param name="tx">A <see cref="Transaction{T}"/> instance to evaluate.</param>
        /// <param name="preEvaluationHash">The <see cref="Block{T}.PreEvaluationHash"/> of
        /// <see cref="Block{T}"/> that <paramref name="tx"/> will belong to.</param>
        /// <param name="blockIndex">The <see cref="Block{T}.Index"/> of
        /// <see cref="Block{T}"/> that <paramref name="tx"/> will belong to.</param>
        /// <param name="previousStates">The states immediately before
        /// <paramref name="tx.Actions"/> being executed.  Note that its
        /// <see cref="IAccountStateDelta.UpdatedAddresses"/> are remained
        /// to the returned next states.</param>
        /// <param name="minerAddress">An address of block miner.</param>
        /// <param name="rehearsal">Pass <c>true</c> if it is intended
        /// to be dry-run (i.e., the returned result will be never used).
        /// The default value is <c>false</c>.</param>
        /// <param name="previousBlockStatesTrie">The trie to contain states at previous block.
        /// </param>
        /// <returns>Enumerates <see cref="ActionEvaluation"/>s for each one in
        /// <paramref name="tx.Actions"/>.
        /// <para>The order is the same to the <paramref name="tx.Actions"/>.</para>
        /// <para>Each <see cref="ActionEvaluation"/> includes its previous
        /// <see cref="ActionEvaluation"/>s' delta besides its own delta.</para>
        /// <para>Note that each <see cref="IActionContext.Random"/> object has a unconsumed state.
        /// </para>
        /// </returns>
        [Pure]
        internal static IEnumerable<ActionEvaluation> EvaluateTxGradually(
            Transaction<T> tx,
            BlockHash preEvaluationHash,
            long blockIndex,
            IAccountStateDelta previousStates,
            Address minerAddress,
            bool rehearsal = false,
            ITrie? previousBlockStatesTrie = null)
        {
            return EvaluateGradually(
                preEvaluationHash: preEvaluationHash,
                blockIndex: blockIndex,
                txid: tx.Id,
                previousStates: previousStates,
                minerAddress: minerAddress,
                signer: tx.Signer,
                signature: tx.Signature,
                actions: tx.Actions.Cast<IAction>().ToImmutableList(),
                rehearsal: rehearsal,
                previousBlockStatesTrie: previousBlockStatesTrie);
        }

        /// <summary>
        /// Executes the <see cref="Transaction{T}.Actions"/> of given <see cref="Transaction{T}"/>
        /// and gets the result states.
        /// </summary>
        /// <param name="tx">A <see cref="Transaction{T}"/> instance to evaluate.</param>
        /// <param name="preEvaluationHash">The <see cref="Block{T}.PreEvaluationHash"/> of
        /// <see cref="Block{T}"/> that <paramref name="tx"/> will belong to.</param>
        /// <param name="blockIndex">The <see cref="Block{T}.Index"/> of
        /// <see cref="Block{T}"/> that <paramref name="tx"/> will belong to.</param>
        /// <param name="previousStates">The states immediately before
        /// <paramref name="tx.Actions"/> being executed.  Note that its
        /// <see cref="IAccountStateDelta.UpdatedAddresses"/> are remained
        /// to the returned next states.</param>
        /// <param name="minerAddress">An address of block miner.</param>
        /// <param name="rehearsal">Pass <c>true</c> if it is intended
        /// to be dry-run (i.e., the returned result will be never used).
        /// The default value is <c>false</c>.</param>
        /// <returns>The states immediately after <paramref name="tx"/>
        /// being executed.  Note that it maintains
        /// <see cref="IAccountStateDelta.UpdatedAddresses"/> of the given
        /// <paramref name="previousStates"/> as well.</returns>
        [Pure]
        internal static IAccountStateDelta EvaluateTxResult(
            Transaction<T> tx,
            BlockHash preEvaluationHash,
            long blockIndex,
            IAccountStateDelta previousStates,
            Address minerAddress,
            bool rehearsal = false
        )
        {
            var evaluations = ActionEvaluator<T>.EvaluateTxGradually(
                tx,
                preEvaluationHash,
                blockIndex,
                previousStates,
                minerAddress,
                rehearsal: rehearsal
            );

            ActionEvaluation lastEvaluation;
            try
            {
                lastEvaluation = evaluations.Last();
            }
            catch (InvalidOperationException)
            {
                // If "evaluations" is empty:
                return previousStates;
            }

            return lastEvaluation.OutputStates;
        }

        /// <summary>
        /// Executes the <paramref name="actions"/> step by step, and emits
        /// <see cref="ActionEvaluation"/> for each step.
        /// </summary>
        /// <param name="preEvaluationHash">The <see cref="Block{T}.PreEvaluationHash"/> of
        /// <see cref="Block{T}"/> that <paramref name="actions"/> belongs to.</param>
        /// <param name="blockIndex">The <see cref="Block{T}.Index"/> of <see cref="Block{T}"/> that
        /// <paramref name="actions"/> belongs to.</param>
        /// <param name="txid">The <see cref="Transaction{T}.Id"/> of <see cref="Transaction{T}"/>
        /// that <paramref name="actions"/> belongs to.  This can be <c>null</c> on rehearsal mode
        /// or if an action is a <see cref="IBlockPolicy{T}.BlockAction"/>.</param>
        /// <param name="previousStates">The states immediately before <paramref name="actions"/>
        /// being executed.  Note that its <see cref="IAccountStateDelta.UpdatedAddresses"/> are
        /// remained to the returned next states.</param>
        /// <param name="minerAddress">An address of block miner.</param>
        /// <param name="signer">Signer of the <paramref name="actions"/>.</param>
        /// <param name="signature"><see cref="Transaction{T}"/> signature used to generate random
        /// seeds.</param>
        /// <param name="actions">Actions to evaluate.</param>
        /// <param name="rehearsal">Pass <c>true</c> if it is intended
        /// to be dry-run (i.e., the returned result will be never used).
        /// The default value is <c>false</c>.</param>
        /// <param name="previousBlockStatesTrie">The trie to contain states at previous block.
        /// </param>
        /// <param name="blockAction">Pass <c>true</c> if it is
        /// <see cref="IBlockPolicy{T}.BlockAction"/>.</param>
        /// <returns>Enumerates <see cref="ActionEvaluation"/>s for each one in
        /// <paramref name="actions"/>.  The order is the same to the <paramref name="actions"/>.
        /// Note that each <see cref="IActionContext.Random"/> object
        /// has a unconsumed state.
        /// </returns>
        [Pure]
        internal static IEnumerable<ActionEvaluation> EvaluateGradually(
            BlockHash preEvaluationHash,
            long blockIndex,
            TxId? txid,
            IAccountStateDelta previousStates,
            Address minerAddress,
            Address signer,
            byte[] signature,
            IImmutableList<IAction> actions,
            bool rehearsal = false,
            ITrie? previousBlockStatesTrie = null,
            bool blockAction = false)
        {
            ActionContext CreateActionContext(
                IAccountStateDelta prevStates,
                int randomSeed
            ) =>
                new ActionContext(
                    signer: signer,
                    txid: txid,
                    miner: minerAddress,
                    blockHash: preEvaluationHash,
                    blockIndex: blockIndex,
                    previousStates: prevStates,
                    randomSeed: randomSeed,
                    rehearsal: rehearsal,
                    previousBlockStatesTrie: previousBlockStatesTrie,
                    blockAction: blockAction);

            byte[] hashedSignature;
            using (var hasher = SHA1.Create())
            {
                hashedSignature = hasher.ComputeHash(signature);
            }

            byte[] preEvaluationHashBytes = preEvaluationHash.ToByteArray();
            int seed =
                (preEvaluationHashBytes.Length > 0
                    ? BitConverter.ToInt32(preEvaluationHashBytes, 0) : 0)
                ^ (signature.Any() ? BitConverter.ToInt32(hashedSignature, 0) : 0);

            IAccountStateDelta states = previousStates;
            ILogger logger = Log.ForContext<ActionEvaluation>();
            foreach (IAction action in actions)
            {
                Exception? exc = null;
                ActionContext context = CreateActionContext(states, seed);
                IAccountStateDelta nextStates = context.PreviousStates;
                try
                {
                    DateTimeOffset actionExecutionStarted = DateTimeOffset.Now;
                    nextStates = action.Execute(context);
                    TimeSpan spent = DateTimeOffset.Now - actionExecutionStarted;
                    logger.Verbose($"{action} execution spent {spent.TotalMilliseconds} ms.");
                }
                catch (Exception e)
                {
                    if (rehearsal)
                    {
                        var msg =
                            $"The action {action} threw an exception during its " +
                            "rehearsal.  It is probably because the logic of the " +
                            $"action {action} is not enough generic so that it " +
                            "can cover every case including rehearsal mode.\n" +
                            "The IActionContext.Rehearsal property also might be " +
                            "useful to make the action can deal with the case of " +
                            "rehearsal mode.\n" +
                            "See also this exception's InnerException property.";
                        exc = new UnexpectedlyTerminatedActionException(
                            null, null, null, null, action, msg, e);
                    }
                    else
                    {
                        var stateRootHash = context.PreviousStateRootHash;
                        var msg =
                            $"The action {action} (block #{blockIndex} " +
                            $"pre-evaluation hash {preEvaluationHash}, " +
                            $"tx {txid}, state root hash {stateRootHash}) threw an exception " +
                            "during execution.  See also this exception's InnerException property.";
                        logger.Error("{Message}\nInnerException: {ExcMessage}", msg, e.Message);
                        exc = new UnexpectedlyTerminatedActionException(
                            preEvaluationHash, blockIndex, txid, stateRootHash, action, msg, e);
                    }
                }

                // As IActionContext.Random is stateful, we cannot reuse
                // the context which is once consumed by Execute().
                ActionContext equivalentContext = CreateActionContext(states, seed);

                yield return new ActionEvaluation(
                    action,
                    equivalentContext,
                    nextStates,
                    exc);

                if (exc is { })
                {
                    yield break;
                }

                states = nextStates;
                unchecked
                {
                    seed++;
                }
            }
        }

        /// <summary>
        /// Main entry point for evaluating a <see cref="Block{T}"/> instance.
        /// <para>Executes every <see cref="IAction"/> in <see cref="Block{T}.Transactions"/>
        /// and <see cref="IBlockPolicy{T}.BlockAction"/>.</para>
        /// <para>Mainly calls <see cref="EvaluateBlock"/>
        /// and appends the result of <see cref="EvaluatePolicyBlockAction"/> at the end.</para>
        /// </summary>
        /// <param name="block">The <see cref="Block{T}"/> instance to evaluate.</param>
        /// <param name="stateCompleterSet">The <see cref="StateCompleterSet{T}"/> to use.</param>
        /// <returns>A list of <see cref="ActionEvaluation"/>s for every <see cref="IAction"/>
        /// related to given <paramref name="block"/>.</returns>
        /// <seealso cref="EvaluateBlock"/>
        /// <seealso cref="EvaluatePolicyBlockAction"/>
        [Pure]
        internal IReadOnlyList<ActionEvaluation> Evaluate(
            Block<T> block,
            StateCompleterSet<T> stateCompleterSet)
        {
            var (accountStateGetter, accountBalanceGetter) = GetAccountGettersPair(
                block: block,
                evaluations: ImmutableArray<ActionEvaluation>.Empty,
                stateCompleterSet: stateCompleterSet);
            ITrie? previousBlockStatesTrie =
                !(_trieGetter is null) && block.PreviousHash is { } h
                    ? _trieGetter(h)
                    : null;

            ImmutableList<ActionEvaluation> evaluations = EvaluateBlock(
                block: block,
                currentTime: DateTimeOffset.UtcNow,
                accountStateGetter: accountStateGetter,
                accountBalanceGetter: accountBalanceGetter,
                previousBlockStatesTrie: previousBlockStatesTrie).ToImmutableList();

            if (_policyBlockAction is null)
            {
                return evaluations;
            }
            else
            {
                return evaluations.Add(
                    EvaluatePolicyBlockAction(
                        block, evaluations, stateCompleterSet, previousBlockStatesTrie));
            }
        }

        /// <summary>
        /// Evaluates the <see cref="IBlockPolicy{T}.BlockAction"/> set by the policy for
        /// the given <see cref="Block{T}"/> object.
        /// </summary>
        /// <param name="block">The <see cref="Block{T}"/> instance to evaluate.</param>
        /// <param name="evaluations">The list of evaluations for <paramref name="block"/>
        /// made up to this point.</param>
        /// <param name="stateCompleterSet">The <see cref="StateCompleterSet{T}"/> to use.</param>
        /// <param name="previousBlockStatesTrie">The trie to contain states at previous block.
        /// </param>
        /// <returns>The <see cref="ActionEvaluation"/> of evaluating the
        /// <see cref="IBlockPolicy{T}.BlockAction"/> for the <paramref name="block"/>.</returns>
        [Pure]
        internal ActionEvaluation EvaluatePolicyBlockAction(
            Block<T> block,
            IReadOnlyList<ActionEvaluation> evaluations,
            StateCompleterSet<T> stateCompleterSet,
            ITrie? previousBlockStatesTrie)
        {
            if (_policyBlockAction is null)
            {
                var message =
                    "To evaluate policy block action, " +
                    "_policyBlockAction must not be null.";
                throw new InvalidOperationException(message);
            }

            _logger.Debug(
                $"Evaluating policy block action for block #{block.Index} " +
                $"{block.PreEvaluationHash}");

            Address miner = block.Miner.GetValueOrDefault();
            IAccountStateDelta previousStates = GetPreviousStates(
                block: block,
                evaluations: evaluations,
                stateCompleterSet: stateCompleterSet);

            return EvaluateGradually(
                preEvaluationHash: block.PreEvaluationHash,
                blockIndex: block.Index,
                txid: null,
                previousStates: previousStates,
                minerAddress: miner,
                signer: miner,
                signature: Array.Empty<byte>(),
                actions: new[] { _policyBlockAction }.ToImmutableList(),
                rehearsal: false,
                previousBlockStatesTrie: previousBlockStatesTrie,
                blockAction: true).First();
        }

        /// <summary>
        /// Retrieves the last previous states of an evaluation.
        /// </summary>
        /// <param name="block">The <see cref="Block{T}"/> instance to reference in case
        /// <paramref name="evaluations"/> is <c>Empty</c>.</param>
        /// <param name="evaluations">The list of evaluations for <paramref name="block"/>
        /// made up to this point.</param>
        /// <param name="stateCompleterSet">The <see cref="StateCompleterSet{T}"/> to use.</param>
        /// <returns>The last previous <see cref="IAccountStateDelta"/> for the given arguments.
        /// </returns>
        private IAccountStateDelta GetPreviousStates(
            Block<T> block,
            IReadOnlyList<ActionEvaluation> evaluations,
            StateCompleterSet<T> stateCompleterSet)
        {
            if (evaluations.Count > 0)
            {
                return evaluations[evaluations.Count - 1].OutputStates;
            }
            else
            {
                var (accountStateGetter, accountBalanceGetter) = GetAccountGettersPair(
                    block: block,
                    evaluations: evaluations,
                    stateCompleterSet: stateCompleterSet);
                Address miner = block.Miner.GetValueOrDefault();

                return block.ProtocolVersion > 0
                    ? new AccountStateDeltaImpl(accountStateGetter, accountBalanceGetter, miner)
                    : new AccountStateDeltaImplV0(accountStateGetter, accountBalanceGetter, miner);
            }
        }

        private Tuple<AccountStateGetter, AccountBalanceGetter> GetAccountGettersPair(
            Block<T> block,
            IReadOnlyList<ActionEvaluation> evaluations,
            StateCompleterSet<T> stateCompleterSet)
        {
            AccountStateGetter accountStateGetter;
            AccountBalanceGetter accountBalanceGetter;

            if (evaluations.Count > 0 || !(block.PreviousHash is null))
            {
                accountStateGetter = address => _stateGetter(
                    address,
                    block.PreviousHash,
                    stateCompleterSet.StateCompleter);
                accountBalanceGetter = (address, currency) => _balanceGetter(
                    address,
                    currency,
                    block.PreviousHash,
                    stateCompleterSet.FungibleAssetStateCompleter);
            }
            else
            {
                accountStateGetter = NullAccountStateGetter;
                accountBalanceGetter = NullAccountBalanceGetter;
            }

            return Tuple.Create(accountStateGetter, accountBalanceGetter);
        }
    }
}
