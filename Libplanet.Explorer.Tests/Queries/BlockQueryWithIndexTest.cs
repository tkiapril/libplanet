using Libplanet.Action;
using Libplanet.Explorer.Queries;

namespace Libplanet.Explorer.Tests.Queries;

public class BlockQueryWithIndexTest : BlockQueryTest
{
    public BlockQueryWithIndexTest()
    {
        Source = new MockBlockChainContextWithIndex<PolymorphicAction<SimpleAction>>(Fx.Chain);
        var _ = new ExplorerQuery<PolymorphicAction<SimpleAction>>(Source);
    }
}
