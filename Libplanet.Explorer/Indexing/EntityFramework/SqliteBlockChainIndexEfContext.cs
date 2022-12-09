using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Libplanet.Explorer.Indexing.EntityFramework;

/// <summary>
/// A class that provides the EF Core context using Sqlite 3 for blockchain index.
/// Currently only used for database migration purposes.
/// </summary>
internal class SqliteBlockChainIndexEfContext : BlockChainIndexEfContext
{
    internal SqliteBlockChainIndexEfContext(DbConnection connection)
    {
        Connection = connection;
    }

    private DbConnection Connection { get; }

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(Connection);
}
