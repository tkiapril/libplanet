using System;
using System.IO;
using Libplanet.Explorer.Cocona.Commands;
using Libplanet.Explorer.Tests.Indexing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Libplanet.Explorer.Cocona.Tests.Commands
{
    public class IndexCommandTest
    {
        [Fact]
        public void LoadIndexFromUri()
        {
            var tempFileName = Path.GetTempFileName();
            IndexCommand<SimpleAction>.LoadIndexFromUri($"sqlite+file://{tempFileName}");
            IndexCommand<SimpleAction>.LoadIndexFromUri($"sqlite3+file://{tempFileName}");
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"sqlite://{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"sqlite+://{tempFileName}"));
            Assert.Throws<SqliteException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"sqlite+foo://{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"sqlite2+file://{tempFileName}"));
        }
    }
}
