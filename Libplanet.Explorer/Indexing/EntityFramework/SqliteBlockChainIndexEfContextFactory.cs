using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace Libplanet.Explorer.Indexing.EntityFramework;

/// <summary>
/// Used for design-time generation of migrations.
/// </summary>
internal class SqliteBlockChainIndexEfContextFactory :
    IDesignTimeDbContextFactory<SqliteBlockChainIndexEfContext>
{
    public SqliteBlockChainIndexEfContext CreateDbContext(string[] args) =>
        new(new SqliteConnection("Data Source=:memory:"));
}
