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
/// A base implementation of IBlockChainIndex.
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
    public void AddBlock<T>(Block<T> block, CancellationToken? stoppingToken)
        where T : IAction, new()
    {
        EnsureReady();
        AddBlockImpl(block, stoppingToken);
    }

    /// <inheritdoc />
    public async Task AddBlockAsync<T>(Block<T> block, CancellationToken? stoppingToken)
        where T : IAction, new()
    {
        EnsureReady();
        await AddBlockAsyncImpl(block, stoppingToken);
    }

    void IBlockChainIndex.Prepare<T>(BlockChain<T> chain, CancellationToken? stoppingToken)
    {
        ((IBlockChainIndex)this).Populate<T>(chain.Store, stoppingToken);
        chain.TipChanged += GetTipChangedHandler(chain, stoppingToken);
        MarkReady();
    }

    async Task IBlockChainIndex.PrepareAsync<T>(
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
        var indexTipHash = indexTip?.Hash ?? null;
        var chainId = store.GetCanonicalChainId()
                      ?? throw new InvalidOperationException(
                          "The store does not contain a valid chain.");

        if (indexTipIndex >= 0)
        {
            var indexHash = GetIndexedBlockImpl(0).Hash;
            var chainHash = store.IndexBlockHash(chainId, 0)
                            ?? throw new InvalidOperationException(
                                "The store does not contain a valid genesis block.");
            if (!indexHash.Equals(chainHash))
            {
                throw new IndexMismatchException(0, indexHash, chainHash);
            }
        }

        if (indexTipIndex >= 1)
        {
            var chainHash = store.IndexBlockHash(chainId, indexTipIndex)
                            ?? throw new IndexMismatchException(
                                indexTipIndex, indexTipHash!.Value, null);
            if (!indexTipHash!.Value.Equals(chainHash))
            {
                throw new IndexMismatchException(indexTipIndex, indexTipHash.Value, chainHash);
            }
        }

        var chainTipIndex = store.CountIndex(chainId) - 1;

        Logger.Information(
            "Index is "
            + (
                indexTipIndex == chainTipIndex
                    ? "up to date."
                    : "out of date. Synchronizing..."));

        var totalBlocksToSync = chainTipIndex - indexTipIndex;

        for (var i = indexTipIndex + 1; i <= chainTipIndex; i++)
        {
            var processedBlockCount = i - indexTipIndex - 1;
            if (processedBlockCount % 100 == 0)
            {
                Logger.Information($"[{processedBlockCount}/{totalBlocksToSync}] processed.");
            }

            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Logger.Information("Index synchronization interrupted.");
                return;
            }

            AddBlockImpl(store.GetBlock<T>(store.IndexBlockHash(chainId, i)!.Value), stoppingToken);
        }

        if (indexTipIndex >= chainTipIndex)
        {
            return;
        }

        Logger.Information($"[{totalBlocksToSync}/{totalBlocksToSync}] processed.");
        Logger.Information("Finished synchronizing index.");
    }

    async Task IBlockChainIndex.PopulateAsync<T>(IStore store, CancellationToken? stoppingToken)
    {
        var indexTip = await GetTipAsyncImpl();
        var indexTipIndex = indexTip?.Index ?? -1;
        var indexTipHash = indexTip?.Hash ?? null;
        var chainId = store.GetCanonicalChainId()
                      ?? throw new InvalidOperationException(
                          "The store does not contain a valid chain.");

        if (indexTipIndex >= 0)
        {
            var indexHash = (await GetIndexedBlockAsyncImpl(0)).Hash;
            var chainHash = store.IndexBlockHash(chainId, 0)
                            ?? throw new InvalidOperationException(
                                "The store does not contain a valid genesis block.");
            if (!indexHash.Equals(chainHash))
            {
                throw new IndexMismatchException(0, indexHash, chainHash);
            }
        }

        if (indexTipIndex >= 1)
        {
            var chainHash = store.IndexBlockHash(chainId, indexTipIndex)
                            ?? throw new IndexMismatchException(
                                indexTipIndex, indexTipHash!.Value, null);
            if (!indexTipHash!.Value.Equals(chainHash))
            {
                throw new IndexMismatchException(indexTipIndex, indexTipHash.Value, chainHash);
            }
        }

        var chainTipIndex = store.CountIndex(chainId) - 1;

        Logger.Information(
            "Index is "
            + (
                indexTipIndex == chainTipIndex
                    ? "up to date."
                    : "out of date. Synchronizing..."));

        var totalBlocksToSync = chainTipIndex - indexTipIndex;

        for (var i = indexTipIndex + 1; i <= chainTipIndex; i++)
        {
            var processedBlockCount = i - indexTipIndex - 1;
            if (processedBlockCount % 100 == 0)
            {
                Logger.Information($"[{processedBlockCount}/{totalBlocksToSync}] processed.");
            }

            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Logger.Information("Index synchronization interrupted.");
                return;
            }

            await AddBlockAsyncImpl(
                store.GetBlock<T>(store.IndexBlockHash(chainId, i)!.Value), stoppingToken);
        }

        if (indexTipIndex >= chainTipIndex)
        {
            return;
        }

        Logger.Information($"[{totalBlocksToSync}/{totalBlocksToSync}] processed.");
        Logger.Information("Finished synchronizing index.");
    }

    protected abstract IndexedBlockItem? GetTipImpl();

    protected abstract Task<IndexedBlockItem?> GetTipAsyncImpl();

    protected abstract IndexedBlockItem GetIndexedBlockImpl(long index);

    protected abstract Task<IndexedBlockItem> GetIndexedBlockAsyncImpl(long index);

    protected abstract Task AddBlockAsyncImpl<T>(Block<T> block, CancellationToken? token)
        where T : IAction, new();

    protected abstract void AddBlockImpl<T>(Block<T> block, CancellationToken? token)
        where T : IAction, new();

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
            for (var i = e.OldTip.Index + 1; i <= e.NewTip.Index; i++)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    return;
                }

                AddBlock(chain[i], stoppingToken);
            }
        };
}
