using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Bencodex.Types;
using Libplanet.Action.Sys;
using Libplanet.Blocks;
using Libplanet.Explorer.Indexing;
using Libplanet.Tx;
using Xunit;
using Random = System.Random;

namespace Libplanet.Explorer.Tests.Indexing;

public abstract class BlockChainIndexTest
{
    protected const int BlockCount = 20;

    protected const int MaxTxCount = 20;

    protected abstract IBlockChainIndexFixture<SimpleAction> Fx { get; }

    protected GeneratedBlockChainFixture ChainFx { get; }

    protected Random RandomGenerator { get; }

    protected BlockChainIndexTest()
    {
        RandomGenerator = new Random();
        ChainFx = new GeneratedBlockChainFixture(RandomGenerator.Next(), BlockCount, MaxTxCount);
    }

    [Fact]
    public async void Prepare()
    {
        var unpreparedIndex = Fx.CreateEphemeralIndexInstance();
        Assert.Throws<IndexNotReadyException>(() => unpreparedIndex.Tip);
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.AccountLastNonce(new Address()));
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.GetContainedBlock(new TxId()));
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.GetIndexedBlock(new BlockHash()));
        Assert.Throws<IndexNotReadyException>(() => unpreparedIndex.GetIndexedBlock(0));
        Assert.Throws<IndexNotReadyException>(() => unpreparedIndex.GetIndexedBlocks());
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.GetInvolvedTransactions(new Address()));
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.GetSignedTransactions(new Address()));
        await Assert.ThrowsAsync<IndexNotReadyException>(
            async () => await unpreparedIndex.GetTipAsync());
        await Assert.ThrowsAsync<IndexNotReadyException>(
            async () => await unpreparedIndex.GetIndexedBlockAsync(new BlockHash()));
        await Assert.ThrowsAsync<IndexNotReadyException>(
            async () => await unpreparedIndex.GetIndexedBlockAsync(0));
        Assert.Throws<IndexNotReadyException>(
            () => unpreparedIndex.TryGetContainedBlock(new TxId(), out _));
        Assert.Throws<IndexNotReadyException>(() => unpreparedIndex.AddBlock(ChainFx.Chain[0]));
        await Assert.ThrowsAsync<IndexNotReadyException>(
            async () => await unpreparedIndex.AddBlockAsync(ChainFx.Chain[0]));

        // ReSharper disable once MethodHasAsyncOverload
        unpreparedIndex.Bind(ChainFx.Chain);
        var populatedIndex = Fx.CreateEphemeralIndexInstance();
        await populatedIndex.BindAsync(ChainFx.Chain, CancellationToken.None);

        var forkedChain = ChainFx.Chain.Fork(ChainFx.Chain.Tip.PreviousHash!.Value);
        await forkedChain.MineBlock(ChainFx.PrivateKeys[0]);
        // ReSharper disable once MethodHasAsyncOverload
        populatedIndex.AddBlock(ChainFx.Chain.Tip);
        await populatedIndex.AddBlockAsync(ChainFx.Chain.Tip);
        Assert.Throws<IndexMismatchException>(
            () => populatedIndex.AddBlock(forkedChain.Tip));
        await Assert.ThrowsAsync<IndexMismatchException>(
            async () => await populatedIndex.AddBlockAsync(forkedChain.Tip));
    }

    [Fact]
    public async void Tip()
    {
        var tip = await Fx.Index.GetTipAsync();
        Assert.Equal(tip, Fx.Index.Tip);
        Assert.Equal(ChainFx.Chain.Tip.Hash, tip.Hash);
        Assert.Equal(ChainFx.Chain.Tip.Index, tip.Index);
        Assert.Equal(ChainFx.Chain.Tip.Miner, tip.Miner);
    }

    [Fact]
    public void AccountLastNonce()
    {
        foreach (var pk in ChainFx.PrivateKeys)
        {
            var address = pk.ToAddress();
            Assert.Equal(ChainFx.Chain.GetNextTxNonce(address) - 1, Fx.Index.AccountLastNonce(address));
        }

        Assert.Null(Fx.Index.AccountLastNonce(new Address()));
    }

    [Fact]
    public async void GetIndexedBlock()
    {
        for (var i = 0; i < ChainFx.Chain.Count; i++)
        {
            var inChain = ChainFx.Chain[i];
            var indexed = await Fx.Index.GetIndexedBlockAsync(i);
            // ReSharper disable MethodHasAsyncOverload
            Assert.Equal(indexed, Fx.Index.GetIndexedBlock(i));
            Assert.Equal(indexed, Fx.Index.GetIndexedBlock(inChain.Hash));
            // ReSharper restore MethodHasAsyncOverload
            Assert.Equal(indexed, await Fx.Index.GetIndexedBlockAsync(inChain.Hash));
            Assert.Equal(inChain.Hash, indexed.Hash);
            Assert.Equal(inChain.Index, indexed.Index);
            Assert.Equal(inChain.Miner, indexed.Miner);
        }

        Assert.Throws<IndexOutOfRangeException>(() => Fx.Index.GetIndexedBlock(new BlockHash()));
        await Assert.ThrowsAsync<IndexOutOfRangeException>(
            async () => await Fx.Index.GetIndexedBlockAsync(new BlockHash()));

        Assert.Throws<IndexOutOfRangeException>(() => Fx.Index.GetIndexedBlock(long.MaxValue));
        await Assert.ThrowsAsync<IndexOutOfRangeException>(
            async () => await Fx.Index.GetIndexedBlockAsync(long.MaxValue));

        Assert.Equal(
            await Fx.Index.GetIndexedBlockAsync(Fx.Index.Tip.Index),
            await Fx.Index.GetIndexedBlockAsync(-1));
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public void GetIndexedBlocks(bool offsetPresent, bool limitPresent, bool desc)
    {
        int? offset = offsetPresent ? BlockCount / 4 : null;
        int? limit = limitPresent ? BlockCount / 2 : null;
        int rangeEnd = limit is { } limitValue ? (offset ?? 0) + limitValue : BlockCount;
        var blocks = Enumerable.Range(0, (int)ChainFx.Chain.Count)
            .Select(i => ChainFx.Chain[i])
            .ToImmutableArray();
        blocks = desc ? blocks.Reverse().ToImmutableArray() : blocks;
        var inChain = Enumerable.Range(offset ?? 0, rangeEnd - (offset ?? 0))
            .Select(i => blocks[i])
            .ToImmutableArray();
        var indexed = Fx.Index.GetIndexedBlocks(offset, limit, desc);
        Assert.Equal(
            indexed,
            Fx.Index.GetIndexedBlocks(
                (offset ?? 0)..rangeEnd,
                desc));
        Assert.Equal(inChain.Length, indexed.Count);
        for (var i = 0; i < indexed.Count; i++)
        {
            Assert.Equal(inChain[i].Hash, indexed[i].Hash);
            Assert.Equal(inChain[i].Index, indexed[i].Index);
            Assert.Equal(inChain[i].Miner, indexed[i].Miner);
        }
    }

    [Theory]
    [MemberData(nameof(SpecialRanges))]
    public void GetIndexedBlocksRangeSpecial(Range special, Range regular, bool desc) =>
        Assert.Equal(Fx.Index.GetIndexedBlocks(regular, desc), Fx.Index.GetIndexedBlocks(special, desc));

    public static IEnumerable<object[]> SpecialRanges =>
        new[]
        {
            new object[] { .., ..BlockCount },
            new object[] { (BlockCount / 4).., (BlockCount / 4)..BlockCount },
            new object[] { ^(BlockCount / 4 * 3).., (BlockCount / 4)..BlockCount },
            new object[] { ..^(BlockCount / 4), 0..(BlockCount / 4 * 3) },
        }
            .Aggregate(ImmutableArray<object[]>.Empty, (arr, item) =>
                arr
                    .Add(item.ToImmutableArray().Add(true).ToArray())
                    .Add(item.ToImmutableArray().Add(false).ToArray()));


    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public void GetIndexedBlocksWithMiner(bool offsetPresent, bool limitPresent, bool desc)
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
            var indexed = Fx.Index.GetIndexedBlocks(offset, limit, desc, address);
            Assert.Equal(inChain.Length, indexed.Count);
            for (var i = 0; i < indexed.Count; i++)
            {
                Assert.Equal(inChain[i].Hash, indexed[i].Hash);
                Assert.Equal(inChain[i].Index, indexed[i].Index);
                Assert.Equal(inChain[i].Miner, indexed[i].Miner);
            }
        }
    }

    [Fact]
    public void GetContainedBlock()
    {
        for (var i = 0; i < ChainFx.Chain.Count; i++)
        {
            foreach (var txId in ChainFx.Chain[i].Transactions.Select(tx => tx.Id))
            {
                var indexed = Fx.Index.GetContainedBlock(txId);
                Assert.Equal(ChainFx.Chain[i].Hash, indexed.Hash);
                Assert.Equal(ChainFx.Chain[i].Index, indexed.Index);
                Assert.Equal(ChainFx.Chain[i].Miner, indexed.Miner);
            }
        }

        Assert.Throws<IndexOutOfRangeException>(() => Fx.Index.GetContainedBlock(new TxId()));
    }

    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public void GetSignedTransactions(bool offsetPresent, bool limitPresent, bool desc)
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
            var indexed = Fx.Index.GetSignedTransactions(address, offset, limit, desc);
            Assert.Equal(inChain.Length, indexed.Count);
            for (var i = 0; i < indexed.Count; i++)
            {
                Assert.Equal(inChain[i].Id, indexed[i].Id);
                Assert.Equal(inChain[i].Signer, indexed[i].Signer);
                Assert.Equal(
                    inChain[i].SystemAction is { } systemAction
                        ? (Integer)Registry.Serialize(systemAction)["type_id"]
                    : null,
                    indexed[i].SystemActionTypeId);
            }
        }
    }


    [Theory]
    [MemberData(nameof(BooleanPermutation3))]
    public void GetInvolvedTransactions(bool offsetPresent, bool limitPresent, bool desc)
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
            var indexed = Fx.Index.GetInvolvedTransactions(address, offset, limit, desc);
            Assert.Equal(inChain.Length, indexed.Count);
            for (var i = 0; i < indexed.Count; i++)
            {
                Assert.Equal(inChain[i].Id, indexed[i].Id);
                Assert.Equal(inChain[i].Signer, indexed[i].Signer);
                Assert.Equal(
                    inChain[i].SystemAction is { } systemAction
                        ? (Integer)Registry.Serialize(systemAction)["type_id"]
                        : null,
                    indexed[i].SystemActionTypeId);
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
}
