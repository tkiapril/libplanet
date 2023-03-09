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
            var minedBlocks = Fx.Chain.IterateBlocks()
                .Where(block => block.Miner == miner)
                .ToImmutableArray();
            await AssertBlocksQueryPermutation(minedBlocks, miner);
        }
    }

    private async Task AssertBlocksQueryPermutation(
        ImmutableArray<Block<PolymorphicAction<SimpleAction>>> blocksToTest,
        Address? miner)
    {
        var excludeEmptyTxs = (ImmutableArray<BlockHash> blockHashes) =>
        await AssertAgainstBlocksQuery(
            blockHashesToTest,
            false,
            null,
            null,
            excludeEmptyTxs,
            miner);
        var expected = blockHashesToTest.Reverse().ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            true,
            null,
            null,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest.Skip(blockHashesToTest.Length / 4).ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            false,
            blockHashesToTest.Length / 4,
            null,
            excludeEmptyTxs,
            miner);
        await AssertAgainstBlocksQuery(
            expected,
            false,
            blockHashesToTest.Length / 4 - blockHashesToTest.Length,
            null,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest.Reverse().Skip(blockHashesToTest.Length / 4).ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            true,
            blockHashesToTest.Length / 4,
            null,
            excludeEmptyTxs,
            miner);
        await AssertAgainstBlocksQuery(
            expected,
            true,
            blockHashesToTest.Length / 4 - blockHashesToTest.Length,
            null,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest.Take(blockHashesToTest.Length / 4).ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            false,
            null,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest
            .Reverse()
            .Take(blockHashesToTest.Length / 4)
            .ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            true,
            null,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest
            .Skip(blockHashesToTest.Length / 3)
            .Take(blockHashesToTest.Length / 4)
            .ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            false,
            blockHashesToTest.Length / 3,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
        await AssertAgainstBlocksQuery(
            expected,
            false,
            blockHashesToTest.Length / 3 - blockHashesToTest.Length,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
        expected = blockHashesToTest
            .Reverse()
            .Skip(blockHashesToTest.Length / 3)
            .Take(blockHashesToTest.Length / 4)
            .ToImmutableArray();
        await AssertAgainstBlocksQuery(
            expected,
            true,
            blockHashesToTest.Length / 3,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
        await AssertAgainstBlocksQuery(
            expected,
            true,
            blockHashesToTest.Length / 3 - blockHashesToTest.Length,
            blockHashesToTest.Length / 4,
            excludeEmptyTxs,
            miner);
    }

    private async Task AssertAgainstBlocksQuery(
        IReadOnlyList<BlockHash> expected,
        bool desc,
        int? offset,
        int? limit,
        bool excludeEmptyTxs,
        Address? miner)
    {
        var actual =
            await ExecuteBlocksQueryAsync(
                desc, offset, limit, excludeEmptyTxs, miner);
        try
        {
            Assert.Equal(expected.Count, actual.Length);
            foreach (var i in Enumerable.Range(0, actual.Length))
            {
                Assert.Equal(expected[i].ToString(), actual[i]);
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
