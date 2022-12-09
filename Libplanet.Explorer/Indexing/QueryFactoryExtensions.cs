using System.Data;
using System.Threading.Tasks;
using Dapper;
using SqlKata.Execution;

namespace Libplanet.Explorer.Indexing;

internal static class QueryFactoryExtensions
{
    internal static async Task<int> InsertOrIgnoreAsync(
        this QueryFactory queryFactory,
        string table,
        object data,
        IDbTransaction? transaction = null)
    {
        var query = queryFactory.Compiler.Compile(queryFactory.Query(table).AsInsert(data));
        return await queryFactory.Connection.ExecuteAsync(
            "INSERT OR IGNORE " + string.Join(" ", query.Sql.Split(" ")[1..]),
            query.NamedBindings,
            transaction);
    }

    internal static int InsertOrIgnore(
        this QueryFactory queryFactory,
        string table,
        object data,
        IDbTransaction? transaction = null)
    {
        var query = queryFactory.Compiler.Compile(queryFactory.Query(table).AsInsert(data));
        return queryFactory.Connection.Execute(
            "INSERT OR IGNORE " + string.Join(" ", query.Sql.Split(" ")[1..]),
            query.NamedBindings,
            transaction);
    }
}
