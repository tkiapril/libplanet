using System.IO;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Explorer.Indexing;

namespace Libplanet.Explorer.Tests.Indexing;

public class RocksDbBlockChainIndexFixture<T>: BlockChainIndexFixture<T>
    where T : IAction, new()
{
    public RocksDbBlockChainIndexFixture(BlockChain<T> chain)
        : base(
            chain,
            new RocksDbBlockChainIndex(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())))
    {
    }

    public override IBlockChainIndex CreateEphemeralIndexInstance() =>
        new RocksDbBlockChainIndex(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
}
