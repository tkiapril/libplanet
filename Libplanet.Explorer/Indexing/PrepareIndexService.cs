using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Blockchain;
using Microsoft.Extensions.Hosting;
using Nito.AsyncEx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An ASP.NET Core service that prepares the provided <see cref="IBlockChainIndex"/> instance.
/// </summary>
/// <typeparam name="T">The <see cref="IAction"/> type of the blocks that will be indexed.
/// </typeparam>
public class PrepareIndexService<T> : BackgroundService
    where T : IAction, new()
{
    private readonly IBlockChainIndex _index;
    private readonly BlockChain<T> _chain;
    private readonly AsyncManualResetEvent _done;

    /// <summary>
    /// Create an instance of service that prepares the provided <paramref name="index"/>.
    /// Pass an <see cref="AsyncManualResetEvent"/> and wait for it in other services that will be
    /// modifying the <paramref name="chain"/> status.
    /// </summary>
    /// <param name="index">The index object that will be prepared.</param>
    /// <param name="chain">The blockchain object that will be indexed by the
    /// <paramref name="index"/>.</param>
    /// <param name="done">An event that will signal when the preparing is done.</param>
    public PrepareIndexService(
        IBlockChainIndex index, BlockChain<T> chain, AsyncManualResetEvent done)
    {
        _index = index;
        _chain = chain;
        _done = done;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _index.PrepareAsync(_chain, stoppingToken);
        _done.Set();
    }
}
