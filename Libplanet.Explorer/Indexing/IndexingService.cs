using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Blockchain;
using Microsoft.Extensions.Hosting;
using Nito.AsyncEx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An ASP.NET Core service that indexes blocks added to the given <see cref="BlockChain{T}"/>
/// instance to the provided <see cref="IBlockChainIndex"/> instance.
/// </summary>
/// <typeparam name="T">The <see cref="IAction"/> type of the blocks that will be indexed.
/// </typeparam>
public class IndexingService<T> : BackgroundService
    where T : IAction, new()
{
    private readonly IBlockChainIndex _index;
    private readonly BlockChain<T> _chain;

    /// <summary>
    /// Create an instance of the service that indexes blocks added to the <paramref name="chain"/>
    /// to the <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The index object that blocks will be indexed.</param>
    /// <param name="chain">The blockchain object that will be indexed by the
    /// <paramref name="index"/>.</param>
    public IndexingService(IBlockChainIndex index, BlockChain<T> chain)
    {
        _index = index;
        _chain = chain;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _index.SynchronizeForeverAsync(_chain, stoppingToken);
    }
}
