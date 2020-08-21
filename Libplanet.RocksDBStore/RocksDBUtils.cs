#nullable enable
using RocksDbSharp;

namespace Libplanet.RocksDBStore
{
    internal static class RocksDBUtils
    {
        internal static RocksDb OpenRocksDb(
            DbOptions options, string dbPath, ColumnFamilies? columnFamilies = null)
        {
            return columnFamilies is null
                ? RocksDb.Open(options, dbPath)
                : RocksDb.Open(options, dbPath, columnFamilies);
        }
    }
}
