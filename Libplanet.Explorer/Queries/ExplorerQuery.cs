#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Types;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blocks;
using Libplanet.Explorer.GraphTypes;
using Libplanet.Explorer.Indexing;
using Libplanet.Explorer.Interfaces;
using Libplanet.Store;
using Libplanet.Tx;

namespace Libplanet.Explorer.Queries
{
    public class ExplorerQuery<T> : ObjectGraphType
        where T : IAction, new()
    {
        public ExplorerQuery(IBlockChainContext<T> chainContext)
        {
            ChainContext = chainContext;
            Field<BlockQuery<T>>("blockQuery", resolve: context => new { });
            Field<TransactionQuery<T>>("transactionQuery", resolve: context => new { });
            Field<StateQuery<T>>("stateQuery", resolve: context => (
                (IBlockChainStates<T>)chainContext.BlockChain,
                chainContext.BlockChain.Policy));
            Field<NonNullGraphType<NodeStateType<T>>>(
                "nodeState",
                resolve: context => chainContext
            );
            Field<NonNullGraphType<BlockPolicyType<T>>>(
                "blockPolicy",
                resolve: context => chainContext.BlockChain.Policy
            );

            Name = "ExplorerQuery";
        }

        private static IBlockChainContext<T> ChainContext { get; set; }

        private static BlockChain<T> Chain => ChainContext.BlockChain;

        private static IStore Store => ChainContext.Store;

        private static IBlockChainIndex Index => ChainContext.Index;

        internal static IEnumerable<Block<T>> ListBlocks(
            bool desc,
            long offset,
            long? limit,
            bool excludeEmptyTxs,
            Address? miner)
        {
            Block<T> tip = Chain.Tip;
            long tipIndex = tip.Index;
            IStore store = ChainContext.Store;

            if (desc)
            {
                if (offset < 0)
                {
                    offset = tipIndex + offset + 1;
                }

                if (limit is { } limitVal)
                {
                    offset = tipIndex - offset + 1 - limitVal;
                }
                else
                {
                    limit = tipIndex - offset + 1;
                    offset = 0;
                }
            }
            else
            {
                if (offset < 0)
                {
                    offset = tipIndex + offset + 1;
                }
            }

            var indexList = store.IterateIndexes(
                    Chain.Id,
                    (int)offset,
                    limit == null ? null : (int)limit)
                .Select((value, i) => new { i, value } );

            if (desc)
            {
                indexList = indexList.Reverse();
            }

            foreach (var index in indexList)
            {
                var block = store.GetBlock<T>(index.value);
                bool isMinerValid = miner is null || miner == block.Miner;
                bool isTxValid = !excludeEmptyTxs || block.Transactions.Any();
                if (!isMinerValid || !isTxValid)
                {
                    continue;
                }

                yield return block;
            }
        }

        internal static IEnumerable<Transaction<T>> ListTransactions(
            Address? signer, Address? involved, bool desc, long offset, int? limit)
        {
            long tipIndex = Index is not null
                ? Index.Tip.Index
                : Chain.Tip.Index;

            if (offset < 0)
            {
                offset = tipIndex + offset + 1;
            }

            if (tipIndex < offset || offset < 0)
            {
                yield break;
            }

            if (Index is null || (signer is null && involved is null))
            {
                Block<T> block = Chain[desc ? tipIndex - offset : offset];

                while (block is { } && limit is null or > 0)
                {
                    foreach (var tx in desc ? block.Transactions.Reverse() : block.Transactions)
                    {
                        if (Index is null && !IsValidTransaction(tx, signer, involved))
                        {
                            continue;
                        }

                        yield return tx;
                        limit--;
                        if (limit <= 0)
                        {
                            break;
                        }
                    }

                    block = GetNextBlock(block, desc);
                }
            }
            else if (signer is { } signerValue)
            {
                foreach (var tx in Index
                             .GetSignedTxIdsByAddress(signerValue, (int)offset, limit, desc)
                             .Select(item => Chain.GetTransaction(item)))
                {
                    yield return tx;
                }
            }
            else if (involved is { } involvedValue)
            {
                foreach (var tx in Index
                             .GetInvolvedTxIdsByAddress(involvedValue, (int)offset, limit, desc)
                             .Select(item => Chain.GetTransaction(item)))
                {
                    yield return tx;
                }
            }
        }

        internal static IEnumerable<Transaction<T>> ListStagedTransactions(
            Address? signer, Address? involved, bool desc, int offset, int? limit)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"{nameof(ListStagedTransactions)} doesn't support negative offset.");
            }

            var stagedTxs = Chain.StagePolicy.Iterate(Chain)
                .Where(tx => IsValidTransaction(tx, signer, involved))
                .Skip(offset);

            stagedTxs = desc ? stagedTxs.OrderByDescending(tx => tx.Timestamp)
                : stagedTxs.OrderBy(tx => tx.Timestamp);

            stagedTxs = stagedTxs.TakeWhile((tx, index) => limit is null || index < limit);

            return stagedTxs;
        }

        internal static Block<T> GetBlockByHash(BlockHash hash) => Store.GetBlock<T>(hash);

        internal static Block<T> GetBlockByIndex(long index) =>
            Index is not null
                ? Store.GetBlock<T>(Index[index])
                : Chain[index];

        internal static Transaction<T> GetTransaction(TxId id) => Chain.GetTransaction(id);

        private static Block<T> GetNextBlock(Block<T> block, bool desc)
        {
            if (desc && block.PreviousHash is { } prev)
            {
                return Chain[prev];
            }
            else if (!desc && block != Chain.Tip)
            {
                return Chain[block.Index + 1];
            }

            return null;
        }

        private static bool IsValidTransaction(
            Transaction<T> tx,
            Address? signer,
            Address? involved)
        {
            if (signer is { } signerVal)
            {
                return tx.Signer.Equals(signerVal);
            }

            if (involved is { } involvedVal)
            {
                return tx.UpdatedAddresses.Contains(involvedVal);
            }

            return true;
        }
    }
}
