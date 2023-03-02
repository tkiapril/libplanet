using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <inheritdoc />
    public (long Index, BlockHash Hash) Tip
        => GetTipImpl() ?? throw new IndexOutOfRangeException("The index is empty.");

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
    public async Task<(long Index, BlockHash Hash)> GetTipAsync() =>
        await GetTipAsyncImpl() ?? throw new IndexOutOfRangeException("The index is empty.");

    /// <inheritdoc />
    public abstract long BlockHashToIndex(BlockHash hash);

    /// <inheritdoc />
    public abstract Task<long> BlockHashToIndexAsync(BlockHash hash);

    /// <inheritdoc />
    public abstract BlockHash IndexToBlockHash(long index);

    /// <inheritdoc />
    public abstract Task<BlockHash> IndexToBlockHashAsync(long index);

    /// <inheritdoc />
    public IEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByRange(Range indexRange, bool desc, Address? miner)
    {
        var (offset, limit) = indexRange.GetOffsetAndLength((int)(Tip.Index + 1 & int.MaxValue));
        return GetBlockHashesByOffset(offset, limit, desc, miner);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByRangeAsync(Range indexRange, bool desc, Address? miner)
    {
        var (offset, limit) = indexRange.GetOffsetAndLength((int)(Tip.Index + 1 & int.MaxValue));
        await foreach (var item in GetBlockHashesByOffsetAsync(offset, limit, desc, miner))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public abstract IEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByOffset(int? offset, int? limit, bool desc, Address? miner);

    /// <inheritdoc />
    public abstract IAsyncEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByOffsetAsync(int? offset, int? limit, bool desc, Address? miner);

    /// <inheritdoc />
    public abstract IEnumerable<TxId>
        GetSignedTxIdsByAddress(Address signer, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TxId>
        GetSignedTxIdsByAddressAsync(Address signer, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract IEnumerable<TxId>
        GetInvolvedTxIdsByAddress(Address address, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract IAsyncEnumerable<TxId>
        GetInvolvedTxIdsByAddressAsync(Address address, int? offset, int? limit, bool desc);

    /// <inheritdoc />
    public abstract long? GetLastNonceByAddress(Address address);

    /// <inheritdoc />
    public abstract Task<long?> GetLastNonceByAddressAsync(Address address);

    /// <inheritdoc />
    public BlockHash GetContainedBlockHashByTxId(TxId txId) =>
        TryGetContainedBlockHashById(txId, out var containedBlock)
            ? containedBlock
            : throw new IndexOutOfRangeException(
                $"The txId {txId} does not exist in the index.");

    /// <inheritdoc />
    public async Task<BlockHash> GetContainedBlockHashByTxIdAsync(TxId txId) =>
        await TryGetContainedBlockHashByIdAsync(txId)
        ?? throw new IndexOutOfRangeException(
            $"The txId {txId} does not exist in the index.");

    /// <inheritdoc />
    public abstract bool TryGetContainedBlockHashById(TxId txId, out BlockHash containedBlock);

    /// <inheritdoc />
    public abstract Task<BlockHash?> TryGetContainedBlockHashByIdAsync(TxId txId);

    /// <inheritdoc />
    void IBlockChainIndex.Index(
        BlockDigest blockDigest, IEnumerable<ITransaction> txs, CancellationToken stoppingToken) =>
        IndexImpl(blockDigest, txs, null, stoppingToken);

    /// <inheritdoc />
    async Task IBlockChainIndex.IndexAsync(
        BlockDigest blockDigest, IEnumerable<ITransaction> txs, CancellationToken stoppingToken) =>
        await IndexAsyncImpl(blockDigest, txs, null, stoppingToken);

    async Task IBlockChainIndex.SynchronizeForeverAsync<T>(
        IStore store, TimeSpan pollInterval, CancellationToken stoppingToken)
    {
        while (true)
        {
            await ((IBlockChainIndex)this).SynchronizeAsync<T>(store, stoppingToken)
                .ConfigureAwait(false);
            await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
            stoppingToken.ThrowIfCancellationRequested();
        }
    }

    async Task IBlockChainIndex.SynchronizeAsync<T>(IStore store, CancellationToken stoppingToken)
    {
        var indexTip = await GetTipAsyncImpl();
        var indexTipIndex = indexTip?.Index ?? -1;
        var chainId = store.GetCanonicalChainId()
                      ?? throw new InvalidOperationException(
                          "The store does not contain a valid chain.");
        var chainTipIndex = store.CountIndex(chainId) - 1;

        if (indexTipIndex >= 0)
        {
            var indexHash = await IndexToBlockHashAsync(0);
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

        long processedBlockCount = 0,
            totalBlocksToSync = chainTipIndex - indexTipIndex,
            intervalBlockCount = 0,
            intervalTxCount = 0;
        var populateStart = DateTimeOffset.Now;
        var intervalStart = populateStart;

        using var indexEnumerator =
            store.IterateIndexes(chainId, (int)indexTipIndex + 1).GetEnumerator();
        var addBlockContext = GetIndexingContext();
        while (indexEnumerator.MoveNext() && indexTipIndex + processedBlockCount < chainTipIndex)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                Logger.Information("Index synchronization interrupted.");
                break;
            }

            var blockDigest = store.GetBlockDigest(indexEnumerator.Current)!.Value;
            var txs = blockDigest.TxIds.Select(
                txId => (ITransaction)store.GetTransaction<T>(new TxId(txId.ToArray()))).ToArray();
            await IndexAsyncImpl(blockDigest, txs, addBlockContext, stoppingToken);
            intervalTxCount += txs.Length;
            ++processedBlockCount;
            ++intervalBlockCount;

            var now = DateTimeOffset.Now;
            var interval = (now - intervalStart).TotalSeconds;
            if (interval > 10)
            {
                var totalElapsedSec = (now - populateStart).TotalSeconds;
                var currentRate = intervalBlockCount / interval;
                var totalRate = processedBlockCount / totalElapsedSec;
                var txRate = intervalTxCount / interval;
                var elapsedStr = FormatSeconds((int)totalElapsedSec);
                var eta = FormatSeconds(
                    (int)TimeSpan.FromSeconds(
                            (chainTipIndex - indexTipIndex - processedBlockCount) / currentRate)
                        .TotalSeconds);

                Logger.Information(
                    $"Height #{blockDigest.Index} of {chainTipIndex},"
                    + $" {totalBlocksToSync - processedBlockCount} to go"
                    + $" ({(float)(indexTipIndex + processedBlockCount) / chainTipIndex * 100:F1}%"
                    + $" synced), session: {processedBlockCount}/{totalBlocksToSync},"
                    + $" current: {(int)currentRate}blk/s, total: {(int)totalRate}blk/s,"
                    + $" txrate: {(int)txRate}tx/s,"
                    + $" tx density: {intervalTxCount / intervalBlockCount}tx/blk,"
                    + $" elapsed: {elapsedStr}, eta: {eta}");
                intervalStart = now;
                intervalBlockCount = 0;
                intervalTxCount = 0;
            }
        }

        await CommitIndexingContextAsync(addBlockContext);

        Logger.Information(
            $"{processedBlockCount} out of {totalBlocksToSync} blocks processed," +
            $" elapsed: {FormatSeconds((int)(DateTimeOffset.Now - populateStart).TotalSeconds)}");

        if (totalBlocksToSync == processedBlockCount)
        {
            Logger.Information("Finished synchronizing index.");
        }
    }

    protected abstract (long Index, BlockHash Hash)? GetTipImpl();

    protected abstract Task<(long Index, BlockHash Hash)?> GetTipAsyncImpl();

    protected abstract void IndexImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IIndexingContext? context,
        CancellationToken token);

    protected abstract Task IndexAsyncImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IIndexingContext? context,
        CancellationToken token);

    /// <summary>
    /// Get a context that can be consumed by <see cref="IBlockChainIndex.Index"/> and
    /// <see cref="IBlockChainIndex.IndexAsync"/> for batch processing.
    /// </summary>
    /// <returns>A context that can be consumed by <see cref="IBlockChainIndex.Index"/>.
    /// </returns>
    protected abstract IIndexingContext GetIndexingContext();

    /// <summary>
    /// Commits the data gathered in the context gained from <see cref="GetIndexingContext"/>.
    /// </summary>
    /// <param name="context">A context gained from <see cref="GetIndexingContext"/>.</param>
    protected abstract void CommitIndexingContext(IIndexingContext context);

    /// <inheritdoc cref="CommitIndexingContext"/>
    protected abstract Task CommitIndexingContextAsync(IIndexingContext context);

    private static string FormatSeconds(int seconds)
    {
        var minutes = seconds / 60;
        seconds %= 60;
        var hours = minutes / 60;
        minutes %= 60;
        return hours > 0
            ? $"{hours}h{minutes}m{seconds}s"
            : minutes > 0
                ? $"{minutes}m{seconds}s"
                : $"{seconds}s";
    }
}
