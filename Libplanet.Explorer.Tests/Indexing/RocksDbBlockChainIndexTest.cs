using Microsoft.Data.Sqlite;

namespace Libplanet.Explorer.Tests.Indexing;

public class RocksDbBlockChainIndexTest: BlockChainIndexTest
{
    public RocksDbBlockChainIndexTest()
    {
        Fx = new RocksDbBlockChainIndexFixture<SimpleAction>(ChainFx.Chain);
    }

    protected override RocksDbBlockChainIndexFixture<SimpleAction> Fx { get; }
}
