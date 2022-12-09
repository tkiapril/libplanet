using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Explorer.Indexing;

namespace Libplanet.Explorer.Tests.Indexing;

public abstract class BlockChainIndexFixture<T> : IBlockChainIndexFixture<T>
    where T : IAction, new()
{
    public IBlockChainIndex Index { get; }

    protected BlockChainIndexFixture(BlockChain<T> chain, IBlockChainIndex index)
    {
        Index = index;
        Index.Prepare(chain);
    }

    public abstract IBlockChainIndex CreateEphemeralIndexInstance();
}
