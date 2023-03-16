#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Execution;
using Libplanet.Action;
using Libplanet.Explorer.Queries;
using Xunit;
using static Libplanet.Explorer.Tests.GraphQLTestUtils;

namespace Libplanet.Explorer.Tests.Queries;

public class BlockQueryTest
{
    protected readonly GeneratedBlockChainFixture Fx;
    protected MockBlockChainContext<PolymorphicAction<SimpleAction>> Source;
    private readonly BlockQuery<PolymorphicAction<SimpleAction>> _queryGraph;

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
}
