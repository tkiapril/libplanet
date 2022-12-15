using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blocks;
using Libplanet.Store;
using Libplanet.Tx;
using Serilog;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// A base implementation of <see cref="IBlockChainIndex"/>.
/// </summary>
public abstract class BlockChainIndexBase : IBlockChainIndex
{
    private ILogger? _logger;

    private ILogger? _defaultLogger;

    private bool _isReady = false;

    /// <inheritdoc />
    public IndexedBlockItem Tip
    {
        get
        {
            EnsureReady();
            return GetTipImpl() ?? throw new IndexOutOfRangeException("The index is empty.");
        }
    }

    protected ILogger Logger
    {
        get
        {
            _defaultLogger ??= Log
                .ForContext<IBlockChainIndex>()
                .ForContext("Source", GetType().Name);
            return _logger ?? _defaultLogger;
        }
        set => _logger = _logger is null ? value : throw new InvalidOperationException(
            "The logger is already set.");
    }

    /// <inheritdoc />
    public async Task<IndexedBlockItem> GetTipAsync()
    {
        EnsureReady();
        return await GetTipAsyncImpl() ?? throw new IndexOutOfRangeException("The index is empty.");
    }

    /// <inheritdoc />
    public abstract IndexedBlockItem GetIndexedBlock(BlockHash hash);

    /// <inheritdoc />
    public IndexedBlockItem GetIndexedBlock(long index)
    {
        EnsureReady();
        return GetIndexedBlockImpl(index);
    }

    /// <inheritdoc />
    public abstract Task<IndexedBlockItem> GetIndexedBlockAsync(BlockHash hash);

    /// <inheritdoc />
    public async Task<IndexedBlockItem> GetIndexedBlockAsync(long index)
    {
        EnsureReady();
        return await GetIndexedBlockAsyncImpl(index);
    }

    /// <inheritdoc />
    public abstract IImmutableList<IndexedBlockItem>
        GetIndexedBlocks(Range indexRange, bool desc, Address? miner);

    /// <inheritdoc />
    public IImmutableList<IndexedBlockItem>
        GetIndexedBlocks(int? offset, int? limit, bool desc, Address? miner) =>
        GetIndexedBlocks(
            new Range(
                new Index(offset ?? 0),
                limit is { } limitValue
                    ? new Index((offset ?? 0) + limitValue)
                    : new Index(0, true)),
            desc,
            miner);

    /// <inheritdoc />
    public abstract IImmutableList<IndexedTransactionItem>
        GetSignedTransactions(Address signer, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract IImmutableList<IndexedTransactionItem>
        GetInvolvedTransactions(Address address, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract long? AccountLastNonce(Address address);

    /// <inheritdoc />
    public IndexedBlockItem GetContainedBlock(TxId txId) =>
        TryGetContainedBlock(txId, out var containedBlock)
            ? containedBlock
            : throw new IndexOutOfRangeException(
                $"The txId {txId} does not exist in the index.");

    /// <inheritdoc />
    public abstract bool TryGetContainedBlock(TxId txId, out IndexedBlockItem containedBlock);

    /// <inheritdoc />
    void IBlockChainIndex.AddBlock<T>(
        Block<T> block, CancellationToken? stoppingToken) =>
        AddBlockImpl(block, null, stoppingToken);

    /// <inheritdoc />
    async Task IBlockChainIndex.AddBlockAsync<T>(
        Block<T> block, CancellationToken? stoppingToken) =>
        await AddBlockAsyncImpl(block, null, stoppingToken);

    void IBlockChainIndex.Bind<T>(BlockChain<T> chain, CancellationToken? stoppingToken)
    {
        ((IBlockChainIndex)this).Populate<T>(chain.Store, stoppingToken);
        chain.TipChanged += GetTipChangedHandler(chain, stoppingToken);
        MarkReady();
    }

    async Task IBlockChainIndex.BindAsync<T>(
        BlockChain<T> chain, CancellationToken? stoppingToken)
    {
        await ((IBlockChainIndex)this).PopulateAsync<T>(chain.Store, stoppingToken);
        chain.TipChanged += GetTipChangedHandler(chain, stoppingToken);
        MarkReady();
    }

    void IBlockChainIndex.Populate<T>(IStore store, CancellationToken? stoppingToken)
    {
        var indexTip = GetTipImpl();
        var indexTipIndex = indexTip?.Index ?? -1;
        var chainId = store.GetCanonicalChainId()
                      ?? throw new InvalidOperationException(
                          "The store does not contain a valid chain.");
        var chainTipIndex = store.CountIndex(chainId) - 1;

        if (indexTipIndex >= 0)
        {
            var indexHash = GetIndexedBlockImpl(0).Hash;
            using var chainIndexEnumerator =
                store.IterateIndexes(chainId, limit: 1).GetEnumerator();
            if (!chainIndexEnumerator.MoveNext())
            {
                throw new InvalidOperationException(
                    "The store does not contain a valid genesis block.");
            }

            var chainHash = chainIndexEnumerator.Current;
            if (!indexHash.Equals(chainHash))
            {
                throw new IndexMismatchException(0, indexHash, chainHash);
            }
        }

        if (indexTipIndex >= 1)
        {
            var indexTipHash = indexTip!.Value.Hash;
            var commonLatestIndex = Math.Min(indexTipIndex, chainTipIndex);
            using var chainIndexEnumerator =
                store.IterateIndexes(chainId, (int)commonLatestIndex, limit: 1).GetEnumerator();
            BlockHash? chainTipHash = chainIndexEnumerator.MoveNext()
                ? chainIndexEnumerator.Current
                : null;
            if (chainTipHash is not { } chainTipHashValue
                || !indexTipHash.Equals(chainTipHashValue))
            {
                throw new IndexMismatchException(
                    indexTipIndex, indexTipHash, chainTipHash);
            }
        }

        if (indexTipIndex == chainTipIndex)
        {
            Logger.Information("Index is up to date.");
            return;
        }

        if (indexTipIndex > chainTipIndex)
        {
            Logger.Information(
                "The height of the index is higher than the height of the blockchain. Index"
                + " preparation will proceed, but if a block of an existing height and a different"
                + $" hash is encountered, an {nameof(IndexMismatchException)} will be raised.");
            return;
        }

        Logger.Information("Index is out of date. Synchronizing...");

        long processedBlockCount = 0, totalBlocksToSync = chainTipIndex - indexTipIndex;

        using var indexEnumerator =
            store.IterateIndexes(chainId, (int)indexTipIndex + 1).GetEnumerator();
        var addBlockContext = GetAddBlockContext();
        while (indexEnumerator.MoveNext() && indexTipIndex + processedBlockCount < chainTipIndex)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Logger.Information("Index synchronization interrupted.");
                break;
            }

            AddBlockImpl(
                store.GetBlock<T>(indexEnumerator.Current),
                addBlockContext,
                stoppingToken);

            if (++processedBlockCount % 1000 == 0)
            {
                Logger.Information($"[{processedBlockCount}/{totalBlocksToSync}] processed.");
            }
        }

        FinalizeAddBlockContext(addBlockContext, true);

        Logger.Information($"{processedBlockCount} out of {totalBlocksToSync} blocks processed.");

        if (totalBlocksToSync == processedBlockCount)
        {
            Logger.Information("Finished synchronizing index.");
        }
    }

    async Task IBlockChainIndex.PopulateAsync<T>(IStore store, CancellationToken? stoppingToken)
    {
        var indexTip = await GetTipAsyncImpl();
        var indexTipIndex = indexTip?.Index ?? -1;
        var chainId = store.GetCanonicalChainId()
                      ?? throw new InvalidOperationException(
                          "The store does not contain a valid chain.");
        var chainTipIndex = store.CountIndex(chainId) - 1;

        if (indexTipIndex >= 0)
        {
            var indexHash = (await GetIndexedBlockAsyncImpl(0)).Hash;
            using var chainIndexEnumerator =
                store.IterateIndexes(chainId, limit: 1).GetEnumerator();
            if (!chainIndexEnumerator.MoveNext())
            {
                throw new InvalidOperationException(
                    "The store does not contain a valid genesis block.");
            }

            var chainHash = chainIndexEnumerator.Current;
            if (!indexHash.Equals(chainHash))
            {
                throw new IndexMismatchException(0, indexHash, chainHash);
            }
        }

        if (indexTipIndex >= 1)
        {
            var indexTipHash = indexTip!.Value.Hash;
            var commonLatestIndex = Math.Min(indexTipIndex, chainTipIndex);
            using var chainIndexEnumerator =
                store.IterateIndexes(chainId, (int)commonLatestIndex, limit: 1).GetEnumerator();
            BlockHash? chainTipHash = chainIndexEnumerator.MoveNext()
                ? chainIndexEnumerator.Current
                : null;
            if (chainTipHash is not { } chainTipHashValue
                || !indexTipHash.Equals(chainTipHashValue))
            {
                throw new IndexMismatchException(
                    indexTipIndex, indexTipHash, chainTipHash);
            }
        }

        if (indexTipIndex == chainTipIndex)
        {
            Logger.Information("Index is up to date.");
            return;
        }

        if (indexTipIndex > chainTipIndex)
        {
            Logger.Information(
                "The height of the index is higher than the height of the blockchain. Index"
                + " preparation will proceed, but if a block of an existing height and a different"
                + $" hash is encountered, an {nameof(IndexMismatchException)} will be raised.");
            return;
        }

        Logger.Information("Index is out of date. Synchronizing...");

        long processedBlockCount = 0, totalBlocksToSync = chainTipIndex - indexTipIndex;

        using var indexEnumerator =
            store.IterateIndexes(chainId, (int)indexTipIndex + 1).GetEnumerator();
        var addBlockContext = GetAddBlockContext();
        while (indexEnumerator.MoveNext() && indexTipIndex + processedBlockCount < chainTipIndex)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Logger.Information("Index synchronization interrupted.");
                break;
            }

            await AddBlockAsyncImpl(
                store.GetBlock<T>(indexEnumerator.Current),
                addBlockContext,
                stoppingToken);

            if (++processedBlockCount % 1000 == 0)
            {
                Logger.Information($"[{processedBlockCount}/{totalBlocksToSync}] processed.");
            }
        }

        FinalizeAddBlockContext(addBlockContext, true);

        Logger.Information($"{processedBlockCount} out of {totalBlocksToSync} blocks processed.");

        if (totalBlocksToSync == processedBlockCount)
        {
            Logger.Information("Finished synchronizing index.");
        }
    }

    protected abstract IndexedBlockItem? GetTipImpl();

    protected abstract Task<IndexedBlockItem?> GetTipAsyncImpl();

    protected abstract IndexedBlockItem GetIndexedBlockImpl(long index);

    protected abstract Task<IndexedBlockItem> GetIndexedBlockAsyncImpl(long index);

    protected abstract void AddBlockImpl<T>(
        Block<T> block, IAddBlockContext? context, CancellationToken? token)
        where T : IAction, new();

    protected abstract Task AddBlockAsyncImpl<T>(
        Block<T> block, IAddBlockContext? context, CancellationToken? token)
        where T : IAction, new();

    /// <summary>
    /// Get a context that can be consumed by <see cref="AddBlock{T}"/> and
    /// <see cref="AddBlockAsync{T}"/> (e.g. <see cref="System.Data.IDbTransaction"/> for batch
    /// processing.
    /// </summary>
    /// <returns>A context that can be consumed by <see cref="AddBlock{T}"/>.</returns>
    protected abstract IAddBlockContext GetAddBlockContext();

    /// <summary>
    /// Finalizes the data for a context gained from <see cref="GetAddBlockContext"/>.
    /// </summary>
    /// <param name="context">A context gained from <see cref="GetAddBlockContext"/>.</param>
    /// <param name="commit">If true, commit the data, and if false, discard the data.</param>
    protected abstract void FinalizeAddBlockContext(IAddBlockContext context, bool commit);

    protected void EnsureReady()
    {
        if (!_isReady)
        {
            throw new IndexNotReadyException();
        }
    }

    private void MarkReady()
    {
        if (!_isReady)
        {
            _isReady = true;
        }
        else
        {
            throw new InvalidOperationException(
                $"Something went wrong: {GetType()}.{nameof(MarkReady)}() has been called more than"
                + " once.");
        }
    }

    private EventHandler<(Block<T> OldTip, Block<T> NewTip)> GetTipChangedHandler<T>(
        BlockChain<T> chain, CancellationToken? stoppingToken = null)
        where T : IAction, new() =>
        (_, e) =>
        {
            var addBlockContext = GetAddBlockContext();
            for (var i = e.OldTip.Index + 1; i <= e.NewTip.Index; i++)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    break;
                }

                AddBlockImpl(chain[i], addBlockContext, stoppingToken);
            }

            FinalizeAddBlockContext(addBlockContext, true);
        };
}
