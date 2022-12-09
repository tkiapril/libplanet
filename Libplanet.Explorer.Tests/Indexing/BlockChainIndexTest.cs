using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Consensus;
using Libplanet.Explorer.Indexing;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Libplanet.Tx;
using Xunit;

namespace Libplanet.Explorer.Tests.Indexing;

public abstract class BlockChainIndexTest
{
    protected const int BlockCount = 20;

    protected const int MaxTxCount = 20;

    protected abstract IBlockChainIndexFixture<SimpleAction> Fx { get; set; }

    protected GeneratedBlockChainFixture ChainFx { get; set; }

    protected Random RandomGenerator { get; }

    protected BlockChainIndexTest()
    {
        RandomGenerator = new Random();
        ChainFx = new GeneratedBlockChainFixture(
            RandomGenerator.Next(), BlockCount, MaxTxCount);
    }

    [Fact]
    public async Task Synchronize()
    {
        var index = Fx.CreateEphemeralIndexInstance();
        await index.SynchronizeAsync<SimpleAction>(ChainFx.Chain.Store, CancellationToken.None);

        var forkedChain = new BlockChain<SimpleAction>(
            ChainFx.Chain.Policy,
            new VolatileStagePolicy<SimpleAction>(),
            new MemoryStore(),
            new TrieStateStore(new MemoryKeyValueStore()),
            ChainFx.Chain.Genesis);
        foreach (var block in ChainFx.Chain.IterateBlocks((int)ChainFx.Chain.Count - 1))
        {
            forkedChain.Append(block, ChainFx.Chain.Store.GetBlockCommit(block.Hash));
        }

        var divergentBlock = forkedChain.ProposeBlock(
            ChainFx.PrivateKeys[0],
            lastCommit: forkedChain.Store.GetBlockCommit(forkedChain.Tip.Hash));
        forkedChain.Append(
            divergentBlock,
            new BlockCommit(
                forkedChain.Tip.Index + 1,
                0,
                divergentBlock.Hash,
                ChainFx.PrivateKeys
                    .OrderBy(pk => pk.ToAddress().ToHex())
                    .Select(pk => new VoteMetadata(
                        forkedChain.Tip.Index + 1,
                        0,
                        divergentBlock.Hash,
                        DateTimeOffset.UtcNow,
                        pk.PublicKey,
                        VoteFlag.PreCommit)
                        .Sign(pk))
                    .ToImmutableArray()));
        await index.IndexAsync(
            ChainFx.Chain.Store.GetBlockDigest(ChainFx.Chain.Tip.Hash)!.Value,
            ChainFx.Chain.Tip.Transactions,
            CancellationToken.None);
        await Assert.ThrowsAsync<IndexMismatchException>(
            async () => await index.IndexAsync(
                forkedChain.Store.GetBlockDigest(forkedChain.Tip.Hash)!.Value,
                forkedChain.Tip.Transactions,
                CancellationToken.None));
    }

    [Fact]
    public async Task Tip()
    {
        var tip = await Fx.Index.GetTipAsync();
        Assert.Equal(tip, Fx.Index.Tip);
        Assert.Equal(ChainFx.Chain.Tip.Hash, tip.Hash);
        Assert.Equal(ChainFx.Chain.Tip.Index, tip.Index);
    }

    [Fact]
    public async Task GetLastNonceByAddress()
    {
        foreach (var pk in ChainFx.PrivateKeys)
        {
            var address = pk.ToAddress();
            Assert.Equal(
                ChainFx.Chain.GetNextTxNonce(address) - 1,
                // ReSharper disable once MethodHasAsyncOverload
                Fx.Index.GetLastNonceByAddress(address) ?? -1);
            Assert.Equal(
                ChainFx.Chain.GetNextTxNonce(address) - 1,
                await Fx.Index.GetLastNonceByAddressAsync(address) ?? -1);
        }

        // ReSharper disable once MethodHasAsyncOverload
        Assert.Null(Fx.Index.GetLastNonceByAddress(new Address()));
        Assert.Null(await Fx.Index.GetLastNonceByAddressAsync(new Address()));
    }

    [Fact]
    public async Task BlockHashToIndex()
    {
        for (var i = 0; i < ChainFx.Chain.Count; i++)
        {
            var inChain = ChainFx.Chain[i];
            // ReSharper disable once MethodHasAsyncOverload
            Assert.Equal(i, Fx.Index.BlockHashToIndex(inChain.Hash));
            Assert.Equal(i, await Fx.Index.BlockHashToIndexAsync(inChain.Hash));
        }

        Assert.Throws<IndexOutOfRangeException>(() => Fx.Index.BlockHashToIndex(new BlockHash()));
        await Assert.ThrowsAsync<IndexOutOfRangeException>(
            async () => await Fx.Index.BlockHashToIndexAsync(new BlockHash()));
    }

    [Fact]
    public async Task IndexToBlockHash()
    {
        for (var i = 0; i < ChainFx.Chain.Count; i++)
        {
            var inChain = ChainFx.Chain[i];
            // ReSharper disable once MethodHasAsyncOverload
            Assert.Equal(inChain.Hash, Fx.Index.IndexToBlockHash(i));
            Assert.Equal(inChain.Hash, await Fx.Index.IndexToBlockHashAsync(i));
        }

        Assert.Throws<IndexOutOfRangeException>(() => Fx.Index.IndexToBlockHash(long.MaxValue));
        await Assert.ThrowsAsync<IndexOutOfRangeException>(
            async () => await Fx.Index.IndexToBlockHashAsync(long.MaxValue));

        Assert.Equal(
            await Fx.Index.IndexToBlockHashAsync(Fx.Index.Tip.Index),
            await Fx.Index.IndexToBlockHashAsync(-1));
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetBlockHashes(bool offsetPresent, bool limitPresent, bool desc)
    {
        int? offset = offsetPresent ? ChainFx.BlockCount / 4 : null;
        int? limit = limitPresent ? ChainFx.BlockCount / 2 : null;
        int rangeEnd = limit is { } limitValue ? (offset ?? 0) + limitValue : ChainFx.BlockCount;
        var blocks = Enumerable.Range(0, (int)ChainFx.Chain.Count)
            .Select(i => ChainFx.Chain[i])
            .ToImmutableArray();
        blocks = desc ? blocks.Reverse().ToImmutableArray() : blocks;
        var inChain = Enumerable.Range(offset ?? 0, rangeEnd - (offset ?? 0))
            .Select(i => blocks[i])
            .ToImmutableArray();
        var indexed = Fx.Index.GetBlockHashesByOffset(offset, limit, desc).ToArray();
        Assert.Equal(
            indexed,
            await Fx.Index.GetBlockHashesByOffsetAsync(offset, limit, desc).ToArrayAsync());
        Assert.Equal(
            indexed,
            Fx.Index.GetBlockHashesByRange((offset ?? 0)..rangeEnd, desc));
        Assert.Equal(
            indexed,
            await Fx.Index.GetBlockHashesByRangeAsync((offset ?? 0)..rangeEnd, desc).ToArrayAsync());
        if (!desc)
        {
            Assert.Equal(
                indexed.Select(tuple => tuple.Hash),
                Fx.Index[(offset ?? 0)..rangeEnd]);
        }

        Assert.Equal(inChain.Length, indexed.Length);
        for (var i = 0; i < indexed.Length; i++)
        {
            Assert.Equal(inChain[i].Hash, indexed[i].Hash);
            Assert.Equal(inChain[i].Index, indexed[i].Index);
        }
    }

    [Fact]
    public async Task GetBlockHashesByRangeOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Fx.Index.GetBlockHashesByRange(..((int)Fx.Index.Tip.Index + 2)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await Fx.Index.GetBlockHashesByRangeAsync(..((int)Fx.Index.Tip.Index + 2)).ToArrayAsync());
    }

    [Theory]
    [InlineData(SpecialRangeKind.OmitStartEnd, false)]
    [InlineData(SpecialRangeKind.OmitStartEnd, true)]
    [InlineData(SpecialRangeKind.OmitEnd, false)]
    [InlineData(SpecialRangeKind.OmitEnd, true)]
    [InlineData(SpecialRangeKind.StartFromEnd, false)]
    [InlineData(SpecialRangeKind.StartFromEnd, true)]
    [InlineData(SpecialRangeKind.EndFromEnd, false)]
    [InlineData(SpecialRangeKind.EndFromEnd, true)]
    public async Task GetBlockHashesByRangeSpecial(SpecialRangeKind kind, bool desc)
    {
        var (special, regular) = GetSpecialRange(kind);
        var byRegular = Fx.Index.GetBlockHashesByRange(regular, desc).ToArray();
        var byRegularAsync = await Fx.Index.GetBlockHashesByRangeAsync(regular, desc).ToArrayAsync();
        var bySpecial = Fx.Index.GetBlockHashesByRange(special, desc).ToArray();
        var bySpecialAsync = await Fx.Index.GetBlockHashesByRangeAsync(special, desc).ToArrayAsync();
        Assert.Equal(byRegular, bySpecial);
        Assert.Equal(byRegularAsync, bySpecialAsync);
        Assert.Equal(byRegular, byRegularAsync);
        Assert.Equal(bySpecial, bySpecialAsync);
        if (!desc)
        {
            Assert.Equal(
                Fx.Index[regular],
                Fx.Index[special]);
        }
    }

    public (Range special, Range regular) GetSpecialRange(SpecialRangeKind kind)
    {
        switch (kind)
        {
            case SpecialRangeKind.OmitStartEnd:
                return (.., ..ChainFx.BlockCount);
            case SpecialRangeKind.OmitEnd:
                return ((ChainFx.BlockCount / 4).., (ChainFx.BlockCount / 4)..ChainFx.BlockCount);
            case SpecialRangeKind.StartFromEnd:
                return ChainFx.BlockCount < 4
                    ? (^0.., ChainFx.BlockCount..ChainFx.BlockCount)
                    : (
                    ^(ChainFx.BlockCount / 4 * 3)..,
                    (ChainFx.BlockCount / 4)..ChainFx.BlockCount);
            case SpecialRangeKind.EndFromEnd:
                return ChainFx.BlockCount < 4
                    ? (..^ChainFx.BlockCount, ..0)
                    : (..^(ChainFx.BlockCount / 4), ..(ChainFx.BlockCount / 4 * 3));
        }

        throw new ArgumentOutOfRangeException();
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetBlockHashesByMiner(bool offsetPresent, bool limitPresent, bool desc)
    {
        foreach (var pk in ChainFx.PrivateKeys)
        {
            var address = pk.ToAddress();
            var inChain = ChainFx.MinedBlocks[address].ToArray();
            inChain = desc ? inChain.Reverse().ToArray() : inChain;
            int? offset = offsetPresent ? inChain.Length / 4 : null;
            int? limit = limitPresent ? inChain.Length / 2 : null;
            inChain = inChain[
                (offset ?? 0)
                ..(limit is { } limitValue ? (offset ?? 0) + limitValue : inChain.Length)];
            var indexed = Fx.Index.GetBlockHashesByOffset(offset, limit, desc, address).ToArray();
            Assert.Equal(
                indexed,
                await Fx.Index.GetBlockHashesByOffsetAsync(
                    offset, limit, desc, address)
                    .ToArrayAsync());
            Assert.Equal(inChain.Length, indexed.Length);
            for (var i = 0; i < indexed.Length; i++)
            {
                Assert.Equal(inChain[i].Hash, indexed[i].Hash);
                Assert.Equal(inChain[i].Index, indexed[i].Index);
            }
        }
    }

    [Fact]
    public async Task GetContainedBlockHashByTxId()
    {
        for (var i = 0; i < ChainFx.Chain.Count; i++)
        {
            foreach (var txId in ChainFx.Chain[i].Transactions.Select(tx => tx.Id))
            {
                // ReSharper disable once MethodHasAsyncOverload
                var indexed = Fx.Index.GetContainedBlockHashByTxId(txId);
                Assert.Equal(ChainFx.Chain[i].Hash, indexed);
                Assert.Equal(indexed, await Fx.Index.GetContainedBlockHashByTxIdAsync(txId));
            }
        }

        Assert.Throws<IndexOutOfRangeException>(
            () => Fx.Index.GetContainedBlockHashByTxId(new TxId()));
        await Assert.ThrowsAsync<IndexOutOfRangeException>(
            async () => await Fx.Index.GetContainedBlockHashByTxIdAsync(new TxId()));
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetSignedTxIdsByAddress(bool offsetPresent, bool limitPresent, bool desc)
    {
        foreach (var pk in ChainFx.PrivateKeys)
        {
            var address = pk.ToAddress();
            var inChain = ChainFx.SignedTxs[address].ToArray();
            inChain = desc ? inChain.Reverse().ToArray() : inChain;
            int? offset = offsetPresent ? inChain.Length / 4 : null;
            int? limit = limitPresent ? inChain.Length / 2 : null;
            inChain = inChain[
                (offset ?? 0)
                ..(limit is { } limitValue ? (offset ?? 0) + limitValue : inChain.Length)];
            var indexed = Fx.Index.GetSignedTxIdsByAddress(address, offset, limit, desc).ToArray();
            Assert.Equal(inChain.Length, indexed.Length);
            Assert.Equal(
                indexed,
                await Fx.Index.GetSignedTxIdsByAddressAsync(address, offset, limit, desc)
                    .ToArrayAsync());
            for (var i = 0; i < indexed.Length; i++)
            {
                Assert.Equal(inChain[i].Id, indexed[i]);
            }
        }
    }


    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public async Task GetInvolvedTxIdsByAddress(bool offsetPresent, bool limitPresent, bool desc)
    {
        foreach (var pk in ChainFx.PrivateKeys)
        {
            var address = pk.ToAddress();
            var inChain = ChainFx.InvolvedTxs[address].ToArray();
            inChain = desc ? inChain.Reverse().ToArray() : inChain;
            int? offset = offsetPresent ? inChain.Length / 4 : null;
            int? limit = limitPresent ? inChain.Length / 2 : null;
            inChain = inChain[
                (offset ?? 0)
                ..(limit is { } limitValue ? (offset ?? 0) + limitValue : inChain.Length)];
            var indexed = Fx.Index.GetInvolvedTxIdsByAddress(address, offset, limit, desc).ToArray();
            Assert.Equal(inChain.Length, indexed.Length);
            Assert.Equal(
                indexed,
                await Fx.Index.GetInvolvedTxIdsByAddressAsync(
                    address, offset, limit, desc)
                    .ToArrayAsync());
            for (var i = 0; i < indexed.Length; i++)
            {
                Assert.Equal(inChain[i].Id, indexed[i]);
            }
        }
    }

    public static IEnumerable<object[]> BooleanPermutation(short count) =>
        Enumerable.Range(0, 1 << count)
            .Aggregate(
                ImmutableArray<object[]>.Empty,
                (arr, bitString) =>
                    arr.Add(
                        Enumerable.Range(0, count)
                            .Aggregate(
                                ImmutableArray<object>.Empty,
                                (arr, item) =>
                                {
                                    var newArr = arr.Add(bitString % 2 != 0);
                                    bitString >>= 1;
                                    return newArr;
                                }).ToArray()));

    public static IEnumerable<object[]> BooleanPermutation3() => BooleanPermutation(3);

    public enum SpecialRangeKind
    {
        OmitStartEnd,
        OmitEnd,
        StartFromEnd,
        EndFromEnd
    }
}
