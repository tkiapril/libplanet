using System;
using System.Collections.Immutable;
using System.Linq;
using Libplanet.Action.Sys;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Tx;

namespace Libplanet.Explorer.Tests.Indexing;

public class GeneratedBlockChainFixture
{
    public BlockChain<SimpleAction> Chain { get; }

    public ImmutableArray<PrivateKey> PrivateKeys { get; }

    public Currency TestCurrency => Currency.Uncapped("TEST", 0, null);

    public ImmutableDictionary<Address, ImmutableArray<Block<SimpleAction>>> MinedBlocks { get; }

    public ImmutableDictionary<Address, ImmutableArray<Transaction<SimpleAction>>>
        SignedTxs { get; }

    public ImmutableDictionary<Address, ImmutableArray<Transaction<SimpleAction>>>
        InvolvedTxs { get; }

    public GeneratedBlockChainFixture(int seed, int blockCount = 20, int maxTxCount = 20)
    {
        var random = new Random(seed);
        var stateStore = new TrieStateStore(new MemoryKeyValueStore());
        PrivateKeys = Enumerable.Range(0, 10)
            .Aggregate(ImmutableArray<PrivateKey>.Empty, (arr, _) => arr.Add(new PrivateKey()));
        MinedBlocks = PrivateKeys.Aggregate(
            ImmutableDictionary<Address, ImmutableArray<Block<SimpleAction>>>.Empty,
            (dict, pk) => dict.SetItem(pk.ToAddress(), ImmutableArray<Block<SimpleAction>>.Empty));
        SignedTxs = PrivateKeys.Aggregate(
            ImmutableDictionary<Address, ImmutableArray<Transaction<SimpleAction>>>.Empty,
            (dict, pk) => dict.SetItem(pk.ToAddress(), ImmutableArray<Transaction<SimpleAction>>.Empty));
        InvolvedTxs = PrivateKeys.Aggregate(
            ImmutableDictionary<Address, ImmutableArray<Transaction<SimpleAction>>>.Empty,
            (dict, pk) => dict.SetItem(pk.ToAddress(), ImmutableArray<Transaction<SimpleAction>>.Empty));

        var genesisMiner = PrivateKeys[random.Next(10)];

        Chain = new BlockChain<SimpleAction>(
            new BlockPolicy<SimpleAction>(
                blockInterval: TimeSpan.FromMilliseconds(1),
                difficultyStability: 1,
                minimumDifficulty: 1,
                nativeTokens: ImmutableHashSet<Currency>.Empty.Add(TestCurrency)
            ),
            new VolatileStagePolicy<SimpleAction>(),
            new MemoryStore(),
            stateStore,
            new BlockContent<SimpleAction>(
                    new BlockMetadata(
                        0,
                        DateTimeOffset.UtcNow,
                        genesisMiner.PublicKey,
                        0,
                        0,
                        null,
                        null))
                .Mine()
                .Evaluate(genesisMiner, null, _ => true, stateStore));

        MinedBlocks = MinedBlocks.SetItem(
            genesisMiner.ToAddress(), MinedBlocks[genesisMiner.ToAddress()].Add(Chain.Genesis));

        while (Chain.Count < blockCount)
        {
            var pk = PrivateKeys[random.Next(10)];
            var transactions = GetRandomTransactions(random.Next(), maxTxCount);
            var block = new BlockContent<SimpleAction>(
                    new BlockMetadata(
                        Chain.Tip.Index + 1,
                        DateTimeOffset.UtcNow,
                        pk.PublicKey,
                        1,
                        Chain.Tip.TotalDifficulty + 1,
                        Chain.Tip.Hash,
                        BlockContent<SimpleAction>.DeriveTxHash(transactions)),
                    transactions)
                .Mine()
                .Evaluate(pk, Chain);
            Chain.Append(block);
            MinedBlocks =
                MinedBlocks.SetItem(pk.ToAddress(), MinedBlocks[pk.ToAddress()].Add(block));
            SignedTxs = transactions.Aggregate(
                SignedTxs,
                (dict, tx) => dict.SetItem(tx.Signer, dict[tx.Signer].Add(tx)));
            InvolvedTxs = transactions.Aggregate(
                InvolvedTxs,
                (dict, tx) => tx.UpdatedAddresses.Aggregate(
                    dict,
                    (dict, addr) => dict.SetItem(addr, dict[addr].Add(tx))));
        }
    }

    private ImmutableArray<Transaction<SimpleAction>> GetRandomTransactions(int seed, int maxCount)
    {
        var random = new Random(seed);
        var nonces = ImmutableDictionary<PrivateKey, long>.Empty;
        return Enumerable.Range(0, random.Next(maxCount))
            .Aggregate(
                ImmutableArray<Transaction<SimpleAction>>.Empty,
                (arr, _) =>
                {
                    var pk = PrivateKeys[random.Next(10)];
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

    private Transaction<SimpleAction> GetRandomTransaction(int seed, PrivateKey pk, long nonce)
    {
        var random = new Random(seed);
        var addr = pk.ToAddress();
        var bal = (int)(Chain.GetBalance(addr, TestCurrency).MajorUnit & int.MaxValue);
        return (random.Next() % 3) switch
        {
            0 => Transaction<SimpleAction>.Create(
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
            _ => Transaction<SimpleAction>.Create(
                nonce,
                pk,
                Chain.Genesis.Hash,
                random.Next() % 2 == 0
                    ? GetRandomActions(random.Next())
                    : ImmutableHashSet<SimpleAction>.Empty,
                GetRandomAddresses(random.Next())
            ),
        };
    }

    private ImmutableArray<SimpleAction> GetRandomActions(int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, random.Next(10))
            .Aggregate(
                ImmutableArray<SimpleAction>.Empty,
                (arr, _) => arr.Add(SimpleAction.GetAction(random.Next())));
    }

    private IImmutableSet<Address> GetRandomAddresses(int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, random.Next(10))
            .Aggregate(
                ImmutableHashSet<Address>.Empty,
                (arr, _) => arr.Add(PrivateKeys[random.Next(10)].ToAddress()));
    }
}
