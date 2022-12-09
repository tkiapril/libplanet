using System;
using System.IO;
using Libplanet.Explorer.Cocona.Commands;
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
            IndexCommand.LoadIndexFromUri($"sqlite+file://{tempFileName}");
            IndexCommand.LoadIndexFromUri($"sqlite3+file://{tempFileName}");
            Assert.Throws<ArgumentException>(
                () => IndexCommand.LoadIndexFromUri($"{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand.LoadIndexFromUri($"sqlite://{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand.LoadIndexFromUri($"sqlite+://{tempFileName}"));
            Assert.Throws<SqliteException>(
                () => IndexCommand.LoadIndexFromUri($"sqlite+foo://{tempFileName}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand.LoadIndexFromUri($"sqlite2+file://{tempFileName}"));
        }
    }
}
