using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Explorer.Indexing;
using Microsoft.Data.Sqlite;

namespace Libplanet.Explorer.Tests.Indexing;

public class SqliteBlockChainIndexFixture<T>: BlockChainIndexFixture<T>
    where T : IAction, new()
{
    public SqliteBlockChainIndexFixture(BlockChain<T> chain, SqliteConnection connection)
        : base(chain, new SqliteBlockChainIndex(connection))
    {
        DbConnection = connection;
    }

    public SqliteConnection DbConnection { get; set; }

    public override IBlockChainIndex CreateEphemeralIndexInstance() =>
        new SqliteBlockChainIndex(new SqliteConnection("Data source=:memory:"));
}
