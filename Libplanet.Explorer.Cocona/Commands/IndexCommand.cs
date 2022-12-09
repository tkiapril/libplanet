using System;
using Cocona;
using Libplanet.Action;
using Libplanet.Explorer.Indexing;
using Libplanet.Extensions.Cocona;
using Libplanet.Store;
using Microsoft.Data.Sqlite;

namespace Libplanet.Explorer.Cocona.Commands
{
    public class IndexCommand
    {
        private const string StoreArgumentDescription =
            "The URI that represents the backend of an " + nameof(IStore) + " object."
            + " <store-type>://<store-path> (e.g., rocksdb+file:///path/to/store)";

        private const string IndexArgumentDescription =
            "The URI that represents the backend of an " + nameof(IBlockChainIndex) + " object."
            + " <index-type>://<index-path (e.g., sqlite+file:///path/to/store)";

        [Command(
            Description = "Populates an index database for use with libplanet explorer services.")]
        public void Populate<T>(
            [Argument("STORE", Description = StoreArgumentDescription)]
            string storeUri,
            [Argument("INDEX", Description = IndexArgumentDescription)]
            string indexUri
            )
            where T : IAction, new() =>
            LoadIndexFromUri(indexUri).Populate<T>(Utils.LoadStoreFromUri(storeUri));

        internal static IBlockChainIndex LoadIndexFromUri(string uriString)
        {
            // Adapted from Libplanet.Extensions.Cocona.Utils.LoadStoreFromUri().
            // TODO: Cocona supports .NET's TypeConverter protocol for instantiating objects
            // from CLI options/arguments.  We'd better to implement it for IStore, and simply
            // use IStore as the option/argument types rather than taking them as strings.
            var uri = new Uri(uriString);

            var protocol = uri.Scheme.Split('+')[0];
            var transport = string.Join('+', uri.Scheme.Split('+')[1..]);

            if (string.IsNullOrWhiteSpace(transport))
            {
                throw new ArgumentException(
                    $"The index URI scheme must contain a transport (e.g. sqlite+file://).",
                    nameof(uriString)
                );
            }

            if (protocol is "sqlite" or "sqlite3")
            {
                return new SqliteBlockChainIndex(
                    new SqliteConnection(
                        $"Data Source={string.Join('+', uri.ToString().Split('+')[1..])}"));
            }

            throw new ArgumentException(
                $"The index URI scheme {uri.Scheme}:// is not supported.",
                nameof(uriString)
            );
        }
    }
}
