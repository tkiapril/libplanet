using System;
using System.Collections.Immutable;
using System.Linq;
using Libplanet.Action;
using Libplanet.Action.Sys;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Consensus;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Tx;

namespace Libplanet.Explorer.Tests;

public class GeneratedBlockChainFixture
{
    public static Currency TestCurrency => Currency.Uncapped("TEST", 0, null);

    public BlockChain<PolymorphicAction<SimpleAction>> Chain { get; }

    public ImmutableArray<PrivateKey> PrivateKeys { get; }

    public ImmutableDictionary<Address, ImmutableArray<Block<PolymorphicAction<SimpleAction>>>>
        MinedBlocks { get; private set; }

    public ImmutableDictionary<
            Address,
            ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>>
        SignedTxs { get; private set; }

    public ImmutableDictionary<
            Address,
            ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>>
        InvolvedTxs { get; private set; }

    public int BlockCount { get; }

    public int MaxTxCount { get; }

    public GeneratedBlockChainFixture(
        int seed,
        int blockCount = 20,
        int maxTxCount = 20,
        int privateKeyCount = 10,
        ImmutableArray<ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>>?
            txActionsForPrefixBlocks = null,
        ImmutableArray<ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>>?
            txActionsForSuffixBlocks = null)
    {
        var random = new System.Random(seed);
        var stateStore = new TrieStateStore(new MemoryKeyValueStore());
        BlockCount = blockCount;
        MaxTxCount = maxTxCount;
        PrivateKeys = Enumerable.Range(0, privateKeyCount)
            .Aggregate(ImmutableArray<PrivateKey>.Empty, (arr, _) => arr.Add(new PrivateKey()));
        MinedBlocks = PrivateKeys.Aggregate(
            ImmutableDictionary<Address, ImmutableArray<Block<PolymorphicAction<SimpleAction>>>>
                .Empty,
            (dict, pk) =>
                dict.SetItem(
                    pk.ToAddress(),
                    ImmutableArray<Block<PolymorphicAction<SimpleAction>>>.Empty));
        SignedTxs = PrivateKeys.Aggregate(
            ImmutableDictionary<
                Address,
                ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>>.Empty,
            (dict, pk) =>
                dict.SetItem(
                    pk.ToAddress(),
                    ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>.Empty));
        InvolvedTxs = PrivateKeys.Aggregate(
            ImmutableDictionary<
                Address,
                ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>>.Empty,
            (dict, pk) =>
                dict.SetItem(
                    pk.ToAddress(),
                    ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>.Empty));

        Chain = new BlockChain<PolymorphicAction<SimpleAction>>(
            new BlockPolicy<PolymorphicAction<SimpleAction>>(
                blockInterval: TimeSpan.FromMilliseconds(1),
                getMaxTransactionsPerBlock: _ => int.MaxValue,
                getMaxTransactionsBytes: _ => long.MaxValue,
                nativeTokens: ImmutableHashSet<Currency>.Empty.Add(TestCurrency)
            ),
            new VolatileStagePolicy<PolymorphicAction<SimpleAction>>(),
            new MemoryStore(),
            stateStore,
            BlockChain<PolymorphicAction<SimpleAction>>.ProposeGenesisBlock(
                systemActions: PrivateKeys
                    .OrderBy(pk => pk.ToAddress().ToHex())
                    .Select(
                    pk => new SetValidator(new Validator(pk.PublicKey, 1)))));

        MinedBlocks = MinedBlocks.SetItem(
            Chain.Genesis.Miner,
            ImmutableArray<Block<PolymorphicAction<SimpleAction>>>.Empty.Add(Chain.Genesis));

        if (txActionsForPrefixBlocks is { } txActionsForPrefixBlocksVal)
        {
            foreach (var actionsForTransactions in txActionsForPrefixBlocksVal)
            {
                var pk = PrivateKeys[random.Next(PrivateKeys.Length)];
                AddBlock(
                    random.Next(),
                    actionsForTransactions.Select(actions =>
                            Transaction<PolymorphicAction<SimpleAction>>.Create(
                                Chain.GetNextTxNonce(pk.ToAddress()),
                                pk,
                                Chain.Genesis.Hash,
                                actions))
                        .ToImmutableArray());
            }
        }

        while (Chain.Count < BlockCount + (txActionsForPrefixBlocks?.Length ?? 0) + 1)
        {
            AddBlock(
                random.Next(),
                GetRandomTransactions(random.Next(), MaxTxCount, Chain.Count == 1));
        }

        if (txActionsForSuffixBlocks is { } txActionsForSuffixBlocksVal)
        {
            foreach (var actionsForTransactions in txActionsForSuffixBlocksVal)
            {
                var pk = PrivateKeys[random.Next(PrivateKeys.Length)];
                AddBlock(
                    random.Next(),
                    actionsForTransactions.Select(actions =>
                            Transaction<PolymorphicAction<SimpleAction>>.Create(
                                Chain.GetNextTxNonce(pk.ToAddress()),
                                pk,
                                Chain.Genesis.Hash,
                                actions))
                        .ToImmutableArray());
            }
        }
    }

    private ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>> GetRandomTransactions(
        int seed, int maxCount, bool giveMax = false)
    {
        var random = new System.Random(seed);
        var nonces = ImmutableDictionary<PrivateKey, long>.Empty;
        return Enumerable.Range(0, giveMax ? maxCount : random.Next(maxCount))
            .Aggregate(
                ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>>.Empty,
                (arr, _) =>
                {
                    var pk = PrivateKeys[random.Next(PrivateKeys.Length)];
                    if (!nonces.TryGetValue(pk, out var nonce))
                    {
                        nonce = Chain.GetNextTxNonce(pk.ToAddress());
                    }

                    nonces = nonces.SetItem(pk, nonce + 1);

                    return arr.Add(GetRandomTransaction(random.Next(), pk, nonce));
                })
            .OrderBy(tx => tx.Id)
            .ToImmutableArray();
    }

    private Transaction<PolymorphicAction<SimpleAction>>
        GetRandomTransaction(int seed, PrivateKey pk, long nonce)
    {
        var random = new System.Random(seed);
        var addr = pk.ToAddress();
        var bal = (int)(Chain.GetBalance(addr, TestCurrency).MajorUnit & int.MaxValue);
        return (random.Next() % 3) switch
        {
            0 => Transaction<PolymorphicAction<SimpleAction>>.Create(
                nonce,
                pk,
                Chain.Genesis.Hash,
                Chain.GetBalance(addr, TestCurrency).MajorUnit > 0 &&
                random.Next() % 2 == 0
                    ? new Transfer(addr,
                        TestCurrency * random.Next(1, bal))
                    : new Mint(addr, TestCurrency * random.Next(1, 100)),
                GetRandomAddresses(random.Next())
            ),
            _ => Transaction<PolymorphicAction<SimpleAction>>.Create(
                nonce,
                pk,
                Chain.Genesis.Hash,
                random.Next() % 2 == 0
                    ? GetRandomActions(random.Next())
                    : ImmutableHashSet<PolymorphicAction<SimpleAction>>.Empty,
                GetRandomAddresses(random.Next())
            ),
        };
    }

    private ImmutableArray<PolymorphicAction<SimpleAction>> GetRandomActions(int seed)
    {
        var random = new System.Random(seed);
        return Enumerable.Range(0, random.Next(10))
            .Aggregate(
                ImmutableArray<PolymorphicAction<SimpleAction>>.Empty,
                (arr, _) => arr.Add(SimpleAction.GetAction(random.Next())));
    }

    private IImmutableSet<Address> GetRandomAddresses(int seed)
    {
        var random = new System.Random(seed);
        return Enumerable.Range(0, random.Next(PrivateKeys.Length - 1) + 1)
            .Aggregate(
                ImmutableHashSet<Address>.Empty,
                (arr, _) => arr.Add(PrivateKeys[random.Next(PrivateKeys.Length)].ToAddress()));
    }

    private void AddBlock(
        int seed,
        ImmutableArray<Transaction<PolymorphicAction<SimpleAction>>> transactions)
    {
        var random = new System.Random(seed);
        var pk = PrivateKeys[random.Next(PrivateKeys.Length)];
        var block = new BlockContent<PolymorphicAction<SimpleAction>>(
                new BlockMetadata(
                    Chain.Tip.Index + 1,
                    DateTimeOffset.UtcNow,
                    pk.PublicKey,
                    Chain.Tip.Hash,
                    BlockContent<PolymorphicAction<SimpleAction>>.DeriveTxHash(transactions),
                    Chain.Store.GetBlockCommit(Chain.Tip.Hash)),
                transactions)
            .Propose()
            .Evaluate(pk, Chain);
        Chain.Append(
            block,
            new BlockCommit(
                Chain.Tip.Index + 1,
                0,
                block.Hash,
                PrivateKeys
                    .OrderBy(pk => pk.ToAddress().ToHex())
                    .Select(pk => new VoteMetadata(
                        Chain.Tip.Index + 1,
                        0,
                        block.Hash,
                        DateTimeOffset.UtcNow,
                        pk.PublicKey,
                        VoteFlag.PreCommit).Sign(pk)).ToImmutableArray()));
        MinedBlocks =
            MinedBlocks.SetItem(pk.ToAddress(), MinedBlocks[pk.ToAddress()].Add(block));
        SignedTxs = transactions.Aggregate(
            SignedTxs,
            (dict, tx) =>
                dict.SetItem(
                    tx.Signer,
                    dict[tx.Signer]
                        .Add(tx)
                        .OrderBy(tx => tx.Nonce)
                        .ToImmutableArray()));
        InvolvedTxs = transactions.Aggregate(
            InvolvedTxs,
            (dict, tx) => tx.UpdatedAddresses.Aggregate(
                dict,
                (dict, addr) => dict.SetItem(addr, dict[addr].Add(tx))));
    }
}
