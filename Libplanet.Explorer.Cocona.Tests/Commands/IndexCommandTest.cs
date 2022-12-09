using System;
using System.IO;
using Libplanet.Explorer.Cocona.Commands;
using Libplanet.Explorer.Tests.Indexing;
using Xunit;

namespace Libplanet.Explorer.Cocona.Tests.Commands
{
    public class IndexCommandTest
    {
        [Fact]
        public void LoadIndexFromUri()
        {
            var tempFileName = Path.GetTempFileName();
            File.Delete(tempFileName);
            Directory.CreateDirectory(tempFileName);
            IndexCommand<SimpleAction>.LoadIndexFromUri(
                $"rocksdb+file://{Path.Combine(tempFileName, "success")}");
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"{Path.Combine(tempFileName, "no-scheme")}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"rocksdb://{Path.Combine(tempFileName, "no-transport")}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"rocksdb+://{Path.Combine(tempFileName, "empty-transport")}"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"rocksdb+foo://{Path.Combine(tempFileName, "unknown-transport")}"));
        }
    }
}
