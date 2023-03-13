using Libplanet.Action;
using Libplanet.Explorer.Queries;

namespace Libplanet.Explorer.Tests.Queries;

public class TransactionQueryGeneratedWithIndexTest : TransactionQueryGeneratedTest
{
    public TransactionQueryGeneratedWithIndexTest()
    {
        Source = new MockBlockChainContextWithIndex<PolymorphicAction<SimpleAction>>(Fx.Chain);
        var _ = new ExplorerQuery<PolymorphicAction<SimpleAction>>(Source);
        QueryGraph = new TransactionQuery<PolymorphicAction<SimpleAction>>(Source);
    }
}
