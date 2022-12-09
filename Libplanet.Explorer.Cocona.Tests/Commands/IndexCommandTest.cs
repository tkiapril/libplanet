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
            IndexCommand<SimpleAction>.LoadIndexFromUri($"rocksdb+file://{tempFileName}1");
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"{tempFileName}2"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"rocksdb://{tempFileName}3"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri($"rocksdb+://{tempFileName}4"));
            Assert.Throws<ArgumentException>(
                () => IndexCommand<SimpleAction>.LoadIndexFromUri(
                    $"rocksdb+foo://{tempFileName}5"));
        }
    }
}
