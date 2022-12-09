using Microsoft.Data.Sqlite;

namespace Libplanet.Explorer.Tests.Indexing;

public class SqliteBlockChainIndexTest: BlockChainIndexTest
{
    public SqliteBlockChainIndexTest()
    {
        Fx = new SqliteBlockChainIndexFixture<SimpleAction>(
            ChainFx.Chain,
            new SqliteConnection("Data Source=:memory:"));
    }

    protected override SqliteBlockChainIndexFixture<SimpleAction> Fx { get; }
}
