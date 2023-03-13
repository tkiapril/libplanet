#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Execution;
using Libplanet.Action;
using Libplanet.Blocks;
using Libplanet.Explorer.Queries;
using Xunit;
using static Libplanet.Explorer.Tests.GraphQLTestUtils;

namespace Libplanet.Explorer.Tests.Queries;

public class BlockQueryTest
{
    protected readonly GeneratedBlockChainFixture Fx;
    protected MockBlockChainContext<PolymorphicAction<SimpleAction>> Source;
    private BlockQuery<PolymorphicAction<SimpleAction>> _queryGraph;

    public BlockQueryTest()
    {
        Fx = new GeneratedBlockChainFixture(
            new System.Random().Next(),
            txActionsForPrefixBlocks:
            ImmutableArray<ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>>
                .Empty
                .Add(ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>.Empty),
            txActionsForSuffixBlocks:
            ImmutableArray<ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>>
                .Empty
                .Add(ImmutableArray<ImmutableArray<PolymorphicAction<SimpleAction>>>.Empty));
        Source = new MockBlockChainContext<PolymorphicAction<SimpleAction>>(Fx.Chain);
        var _ = new ExplorerQuery<PolymorphicAction<SimpleAction>>(Source);
        _queryGraph = new BlockQuery<PolymorphicAction<SimpleAction>>();
    }

    [Fact]
    public async Task BlockByIndex()
    {
        foreach (var i in Fx.Chain.IterateBlocks())
        {
            ExecutionResult result = await ExecuteQueryAsync(@$"
            {{
                block(index: {i.Index})
                {{
                    hash
                }}
             }}
            ", _queryGraph, source: Source);
            Assert.Null(result.Errors);
            ExecutionNode resultData = Assert.IsAssignableFrom<ExecutionNode>(result.Data);
            IDictionary<string, object> resultDict =
                Assert.IsAssignableFrom<IDictionary<string, object>>(resultData.ToValue());
            Assert.Equal(
                i.Hash.ToString(),
                ((IDictionary<string, object>)resultDict["block"])["hash"]);
        }
    }

    [Fact]
    public async Task Blocks()
    {
        var allBlocks = Fx.Chain.IterateBlocks().ToImmutableArray();
        await AssertBlocksQueryPermutation(allBlocks, null);
        foreach (var miner in Fx.PrivateKeys.Select(pk => pk.ToAddress()))
        {
            await AssertBlocksQueryPermutation(allBlocks, miner);
        }
    }

    private async Task AssertBlocksQueryPermutation(
        ImmutableArray<Block<PolymorphicAction<SimpleAction>>> blocksToTest,
        Address? miner)
    {
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            null,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            null,
            null,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            null,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            null,
            null,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 4,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 4,
            null,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 4 - blocksToTest.Length,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 4 - blocksToTest.Length,
            null,
            true,
            miner);
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 4,
                null,
                false,
                miner),
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 4 - blocksToTest.Length,
                null,
                false,
                miner));
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 4,
                null,
                true,
                miner),
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 4 - blocksToTest.Length,
                null,
                true,
                miner));
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 4,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 4,
            null,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 4 - blocksToTest.Length,
            null,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 4 - blocksToTest.Length,
            null,
            true,
            miner);
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 4,
                null,
                false,
                miner),
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 4 - blocksToTest.Length,
                null,
                false,
                miner));
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 4,
                null,
                true,
                miner),
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 4 - blocksToTest.Length,
                null,
                true,
                miner));
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            null,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            null,
            blocksToTest.Length / 4,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            null,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            null,
            blocksToTest.Length / 4,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 3,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 3,
            blocksToTest.Length / 4,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 3 - blocksToTest.Length,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            false,
            blocksToTest.Length / 3 - blocksToTest.Length,
            blocksToTest.Length / 4,
            true,
            miner);
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 3,
                blocksToTest.Length / 4,
                false,
                miner),
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 3 - blocksToTest.Length,
                blocksToTest.Length / 4,
                false,
                miner));
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 3,
                blocksToTest.Length / 4,
                true,
                miner),
            await ExecuteBlocksQueryAsync(
                false,
                blocksToTest.Length / 3 - blocksToTest.Length,
                blocksToTest.Length / 4,
                true,
                miner));
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 3,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 3,
            blocksToTest.Length / 4,
            true,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 3 - blocksToTest.Length,
            blocksToTest.Length / 4,
            false,
            miner);
        await AssertAgainstBlocksQuery(
            blocksToTest,
            true,
            blocksToTest.Length / 3 - blocksToTest.Length,
            blocksToTest.Length / 4,
            true,
            miner);
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 3,
                blocksToTest.Length / 4,
                false,
                miner),
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 3 - blocksToTest.Length,
                blocksToTest.Length / 4,
                false,
                miner));
        Assert.Equal<IEnumerable<string>>(
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 3,
                blocksToTest.Length / 4,
                true,
                miner),
            await ExecuteBlocksQueryAsync(
                true,
                blocksToTest.Length / 3 - blocksToTest.Length,
                blocksToTest.Length / 4,
                true,
                miner));
    }

    private async Task AssertAgainstBlocksQuery(
        ImmutableArray<Block<PolymorphicAction<SimpleAction>>> blocksToTest,
        bool desc,
        int? offset,
        int? limit,
        bool excludeEmptyTxs,
        Address? miner)
    {
        IEnumerable<Block<PolymorphicAction<SimpleAction>>> blocks = blocksToTest;
        if (desc)
        {
            blocks = blocks.Reverse();
        }

        if (offset is { } offsetVal)
        {
            offsetVal = offsetVal >= 0 ? offsetVal : blocksToTest.Length + offsetVal;
            blocks = blocks.Skip(offsetVal);
        }

        if (limit is { } limitVal)
        {
            blocks = blocks.Take(limitVal);
        }

        if (excludeEmptyTxs)
        {
            blocks = blocks.Where(block => block.Transactions.Any());
        }

        if (miner is { } minerVal)
        {
            blocks = blocks.Where(block => block.Miner.Equals(minerVal));
        }

        var expected = blocks.ToImmutableArray();

        var actual =
            await ExecuteBlocksQueryAsync(
                desc, offset, limit, excludeEmptyTxs, miner);
        try
        {
            Assert.Equal(expected.Length, actual.Length);
            foreach (var i in Enumerable.Range(0, actual.Length))
            {
                Assert.Equal(expected[i].Hash.ToString(), actual[i]);
            }
        }
        catch (Exception)
        {
            throw;
        }
    }

    private async Task<ImmutableArray<string>>
        ExecuteBlocksQueryAsync(
            bool desc,
            int? offset,
            int? limit,
            bool excludeEmptyTxs,
            Address? miner)
    {
        ExecutionResult result = await ExecuteQueryAsync(@$"
        {{
            blocks(
                desc: {(desc ? "true" : "false")},
                offset: {offset ?? 0}
                {(limit is { } limitVal ? $"limit: {limitVal}" : "")}
                excludeEmptyTxs: {(excludeEmptyTxs ? "true" : "false")},
                {(miner is { } minerVal ? @$"miner: ""{minerVal}""" : "")}
            )
            {{
                hash
            }}
         }}
        ", _queryGraph, source: Source);
        Assert.Null(result.Errors);
        ExecutionNode resultData = Assert.IsAssignableFrom<ExecutionNode>(result.Data);
        IDictionary<string, object> resultDict =
            Assert.IsAssignableFrom<IDictionary<string, object>>(resultData.ToValue());
        return ((IReadOnlyList<object>)resultDict["blocks"])
            .Select(txData => (string)((IDictionary<string, object>)txData)["hash"])
            .ToImmutableArray();
    }
}
