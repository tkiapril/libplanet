using System;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.Sys;
using Libplanet.Blocks;
using Libplanet.Explorer.Indexing.EntityFramework;
using Libplanet.Tx;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlKata.Compilers;
using SqlKata.Execution;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An <see cref="IBlockChainIndex"/> object that uses Sqlite 3 as the backend.
/// </summary>
public class SqliteBlockChainIndex : BlockChainIndexBase
{
    /// <summary>
    /// Create an instance of <see cref="IBlockChainIndex"/> that uses Sqlite 3 as the backend.
    /// </summary>
    /// <param name="connection">The connection to a Sqlite 3 database.</param>
    public SqliteBlockChainIndex(SqliteConnection connection)
    {
        if (connection.State == ConnectionState.Closed)
        {
            connection.Open();
        }

        var contextForMigration = new SqliteBlockChainIndexEfContext(connection);
        if (contextForMigration.Database.GetPendingMigrations().Any())
        {
            contextForMigration.Database.Migrate();
        }

        contextForMigration.Dispose();
        Db = new QueryFactory(connection, new SqliteCompiler());
    }

    private QueryFactory Db { get; }

    /// <inheritdoc />
    public override IndexedBlockItem GetIndexedBlock(BlockHash hash)
    {
        EnsureReady();
        return IndexedBlockItem.FromTuple(
                   Db.Query("Blocks")
                       .Select("Index", "Hash", "MinerAddress")
                       .Where("Hash", hash.ByteArray.ToArray())
                       .FirstOrDefault<(long, byte[], byte[])>())
               ?? throw new IndexOutOfRangeException(
                   $"The hash {hash} does not exist in the index.");
    }

    /// <inheritdoc />
    public override async Task<IndexedBlockItem> GetIndexedBlockAsync(BlockHash hash)
    {
        EnsureReady();
        return IndexedBlockItem.FromTuple(
            await Db.Query("Blocks")
                .Select("Index", "Hash", "MinerAddress")
                .Where("Hash", hash.ByteArray.ToArray())
                .FirstOrDefaultAsync<(long, byte[], byte[])>())
               ?? throw new IndexOutOfRangeException(
                   $"The hash {hash} does not exist in the index.");
    }

    /// <inheritdoc />
    public override IImmutableList<IndexedBlockItem>
        GetIndexedBlocks(Range indexRange, bool desc, Address? miner)
    {
        EnsureReady();
        var (offset, limit) = indexRange.GetOffsetAndLength((int)((Tip.Index + 1) & int.MaxValue));
        if (limit == 0)
        {
            return ImmutableArray<IndexedBlockItem>.Empty;
        }

        var query = Db.Query("Blocks")
            .Select("Index", "Hash", "MinerAddress")
            .Limit(limit)
            .Offset(offset);
        query = miner is { } minerValue
            ? query.Where("MinerAddress", minerValue.ByteArray.ToArray())
            : query;
        query = desc ? query.OrderByDesc("Index") : query.OrderBy("Index");
        return query.Get<(long, byte[], byte[])>()
            .Select(result => IndexedBlockItem.FromTuple(result)!.Value)
            .ToImmutableArray();
    }

    /// <inheritdoc />
    public override IImmutableList<IndexedTransactionItem>
        GetSignedTransactions(Address signer, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        if (limit == 0)
        {
            return ImmutableArray<IndexedTransactionItem>.Empty;
        }

        var query = Db.Query("Transactions")
            .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
            .Select(
                "Transactions.Id",
                "Transactions.SystemActionTypeId",
                "Transactions.SignerAddress",
                "Transactions.BlockHash")
            .Where("Transactions.SignerAddress", signer.ByteArray.ToArray());
        query = desc
            ? query.OrderByDesc("Blocks.Index", "Transactions.Id")
            : query.OrderBy("Blocks.Index", "Transactions.Id");
        query = limit is { } limitVal ? query.Limit(limitVal) : query;
        query = offset is { } offsetVal ? query.Offset(offsetVal) : query;
        return query.Get<(byte[], short?, byte[], byte[])>()
            .Select(result => IndexedTransactionItem.FromTuple(result)!.Value)
            .ToImmutableArray();
    }

    /// <inheritdoc />
    public override IImmutableList<IndexedTransactionItem>
        GetInvolvedTransactions(Address address, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        if (limit == 0)
        {
            return ImmutableArray<IndexedTransactionItem>.Empty;
        }

        var query = Db.Query("Transactions")
            .Join(
                "AccountTransaction",
                "AccountTransaction.InvolvedTransactionsId",
                "Transactions.Id")
            .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
            .Select(
                "Transactions.Id",
                "Transactions.SystemActionTypeId",
                "Transactions.SignerAddress",
                "Transactions.BlockHash")
            .Where("AccountTransaction.UpdatedAddressesAddress", address.ByteArray.ToArray());
        query = desc
            ? query.OrderByDesc("Blocks.Index", "Transactions.Id")
            : query.OrderBy("Blocks.Index", "Transactions.Id");
        query = limit is { } limitVal ? query.Limit(limitVal) : query;
        query = offset is { } offsetVal ? query.Offset(offsetVal) : query;
        return query.Get<(byte[], short?, byte[], byte[])>()
            .Select(result => IndexedTransactionItem.FromTuple(result)!.Value)
            .ToImmutableArray();
    }

    /// <inheritdoc />
    public override long? AccountLastNonce(Address address)
    {
        EnsureReady();
        return Db.Query("Accounts")
            .Select("LastNonce")
            .Where("Address", address.ByteArray.ToArray())
            .FirstOrDefault<long?>();
    }

    /// <inheritdoc />
    public override bool TryGetContainedBlock(TxId txId, out IndexedBlockItem containedBlock)
    {
        EnsureReady();
        containedBlock = default;
        var item = IndexedBlockItem.FromTuple(
            Db.Query("Transactions")
                .Select("Blocks.Index", "Blocks.Hash", "Blocks.MinerAddress")
                .Where("Transactions.Id", txId.ByteArray.ToArray())
                .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
                .FirstOrDefault<(long, byte[], byte[])>());
        if (item is not { } itemValue)
        {
            return false;
        }

        containedBlock = itemValue;
        return true;
    }

    /// <inheritdoc />
    protected override IndexedBlockItem? GetTipImpl() =>
        IndexedBlockItem.FromTuple(
            Db.Query("Blocks")
                .Select("Index", "Hash", "MinerAddress")
                .OrderByDesc("Index")
                .FirstOrDefault<(long, byte[], byte[])>());

    /// <inheritdoc />
    protected override async Task<IndexedBlockItem?> GetTipAsyncImpl() =>
        IndexedBlockItem.FromTuple(
            await Db.Query("Blocks")
                .Select("Index", "Hash", "MinerAddress")
                .OrderByDesc("Index")
                .FirstOrDefaultAsync<(long, byte[], byte[])>());

    /// <inheritdoc />
    protected override IndexedBlockItem GetIndexedBlockImpl(long index) =>
        IndexedBlockItem.FromTuple(
            Db.Query("Blocks")
                .Select("Index", "Hash", "MinerAddress")
                .Where("Index", index >= 0 ? index : (GetTipImpl()?.Index ?? 0) + index + 1)
                .FirstOrDefault<(long, byte[], byte[])>())
        ?? throw new IndexOutOfRangeException($"The block #{index} does not exist in the index.");

    /// <inheritdoc />
    protected override async Task<IndexedBlockItem> GetIndexedBlockAsyncImpl(long index) =>
        IndexedBlockItem.FromTuple(
            await Db.Query("Blocks")
                .Select("Index", "Hash", "MinerAddress")
                .Where(
                    "Index",
                    index >= 0 ? index : ((await GetTipAsyncImpl())?.Index ?? 0) + index + 1)
                .FirstOrDefaultAsync<(long, byte[], byte[])>())
        ?? throw new IndexOutOfRangeException($"The block #{index} does not exist in the index.");

    /// <inheritdoc />
    protected override void AddBlockImpl<T>(
        Block<T> block, IAddBlockContext? context, CancellationToken? stoppingToken)
    {
        var minerAddress = block.Miner.ByteArray.ToArray();
        var blockHash = block.Hash.ByteArray.ToArray();

        var scope
            = ((SqliteAddBlockContext?)context)?.Transaction ?? Db.Connection.BeginTransaction();
        Db.Statement($"SAVEPOINT \"{block.Hash.ToString()}\"", scope);
        void Rollback()
        {
            Db.Statement($"ROLLBACK TO \"{block.Hash.ToString()}\"", scope);
            if (context is null)
            {
                scope.Rollback();
                scope.Dispose();
            }
        }

        Db.InsertOrIgnore("Accounts", new { Address = minerAddress }, scope);
        try
        {
            Db.Query("Blocks")
                .Insert(
                    new
                    {
                        Hash = blockHash,
                        Index = block.Index,
                        MinerAddress = minerAddress,
                    },
                    scope);
        }
        catch (SqliteException e)
        {
            Rollback();
            if (e.SqliteExtendedErrorCode != 1555 && e.SqliteExtendedErrorCode != 2067)
            {
                throw;
            }

            var index = Db.Query("Blocks")
                .Select("Index")
                .Where(new { Hash = block.Hash.ByteArray.ToArray(), Index = block.Index })
                .FirstOrDefault<long?>();

            if (index is not null)
            {
                return;
            }

            throw new IndexMismatchException(
                block.Index, GetTipImpl()!.Value.Hash, block.Hash);
        }

        var txNonces = ImmutableDictionary<Address, long>.Empty;

        foreach (var tx in block.Transactions)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Rollback();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            short? systemActionTypeId = tx.SystemAction is { } systemAction
                ? (short)Registry.Serialize(systemAction).GetValue<Integer>("type_id")
                : null;

            Db.Statement($"SAVEPOINT \"{tx.Id.ToString()}\"", scope);
            Db.InsertOrIgnore("Accounts", new { Address = signerAddress }, scope);
            try
            {
                Db.Query("Transactions")
                    .Insert(
                        new
                        {
                            Id = txId,
                            SystemActionTypeId = systemActionTypeId,
                            SignerAddress = signerAddress,
                            BlockHash = blockHash,
                        },
                        scope);
            }
            catch (SqliteException e)
            {
                Db.Statement($"ROLLBACK TO \"{tx.Id.ToString()}\"", scope);
                if (e.SqliteExtendedErrorCode != 1555 && e.SqliteExtendedErrorCode != 2067)
                {
                    Rollback();
                    throw;
                }

                continue;
            }

            if (!txNonces.TryGetValue(tx.Signer, out var nonce) || tx.Nonce > nonce)
            {
                txNonces = txNonces.SetItem(tx.Signer, tx.Nonce);
            }

            foreach (var address
                     in tx.UpdatedAddresses.Select(address => address.ByteArray.ToArray()))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    Rollback();
                    return;
                }

                Db.InsertOrIgnore(
                    "Accounts", new { Address = address, }, scope);
                Db.Query("AccountTransaction")
                    .Insert(
                        new
                        {
                            InvolvedTransactionsId = txId,
                            UpdatedAddressesAddress = address,
                        },
                        scope);
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    Rollback();
                    return;
                }

                if (ActionTypeAttribute.ValueOf(customAction.GetType()) is not
                    { } typeId)
                {
                    continue;
                }

                Db.InsertOrIgnore(
                    "CustomActions",
                    new
                    {
                        TypeId = typeId,
                    },
                    scope);
                Db.Query("CustomActionTransaction")
                    .Insert(
                        new
                        {
                            ContainedTransactionsId = txId,
                            CustomActionsTypeId = typeId,
                        },
                        scope);
            }

            Db.Statement($"RELEASE \"{tx.Id.ToString()}\"", scope);
        }

        foreach (var nonce in txNonces)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Rollback();
                return;
            }

            Db.Query("Accounts")
                .Where(new { Address = nonce.Key.ByteArray.ToArray() })
                .Update(
                    new { LastNonce = nonce.Value },
                    scope);
        }

        Db.Statement($"RELEASE \"{block.Hash.ToString()}\"", scope);
        if (context is null)
        {
            scope.Commit();
            scope.Dispose();
        }
    }

    /// <inheritdoc />
    protected override async Task AddBlockAsyncImpl<T>(
        Block<T> block, IAddBlockContext? context, CancellationToken? stoppingToken)
    {
        var minerAddress = block.Miner.ByteArray.ToArray();
        var blockHash = block.Hash.ByteArray.ToArray();

        var scope
            = ((SqliteAddBlockContext?)context)?.Transaction ?? Db.Connection.BeginTransaction();
        await Db.StatementAsync($"SAVEPOINT \"{block.Hash.ToString()}\"", scope);
        async Task Rollback()
        {
            await Db.StatementAsync($"ROLLBACK TO \"{block.Hash.ToString()}\"", scope);
            if (context is null)
            {
                scope.Rollback();
                scope.Dispose();
            }
        }

        await Db.InsertOrIgnoreAsync("Accounts", new { Address = minerAddress }, scope);
        try
        {
            await Db.Query("Blocks")
                .InsertAsync(
                    new
                    {
                        Hash = blockHash,
                        Index = block.Index,
                        MinerAddress = minerAddress,
                    },
                    scope);
        }
        catch (SqliteException e)
        {
            await Rollback();
            if (e.SqliteExtendedErrorCode != 1555 && e.SqliteExtendedErrorCode != 2067)
            {
                throw;
            }

            var index = await Db.Query("Blocks")
                .Select("Index")
                .Where(new { Hash = block.Hash.ByteArray.ToArray(), Index = block.Index })
                .FirstOrDefaultAsync<long?>();

            if (index is not null)
            {
                return;
            }

            throw new IndexMismatchException(
                block.Index, (await GetTipAsyncImpl())!.Value.Hash, block.Hash);
        }

        var txNonces = ImmutableDictionary<Address, long>.Empty;

        foreach (var tx in block.Transactions)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                await Rollback();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            short? systemActionTypeId = tx.SystemAction is { } systemAction
                ? (short)Registry.Serialize(systemAction).GetValue<Integer>("type_id")
                : null;

            await Db.StatementAsync($"SAVEPOINT \"{tx.Id.ToString()}\"", scope);
            await Db.InsertOrIgnoreAsync("Accounts", new { Address = signerAddress }, scope);
            try
            {
                await Db.Query("Transactions")
                    .InsertAsync(
                        new
                        {
                            Id = txId,
                            SystemActionTypeId = systemActionTypeId,
                            SignerAddress = signerAddress,
                            BlockHash = blockHash,
                        },
                        scope);
            }
            catch (SqliteException e)
            {
                await Db.StatementAsync($"ROLLBACK TO \"{tx.Id.ToString()}\"", scope);
                if (e.SqliteExtendedErrorCode != 1555 && e.SqliteExtendedErrorCode != 2067)
                {
                    await Rollback();
                    throw;
                }

                continue;
            }

            if (!txNonces.TryGetValue(tx.Signer, out var nonce) || tx.Nonce > nonce)
            {
                txNonces = txNonces.SetItem(tx.Signer, tx.Nonce);
            }

            foreach (var address
                     in tx.UpdatedAddresses.Select(address => address.ByteArray.ToArray()))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    await Rollback();
                    return;
                }

                await Db.InsertOrIgnoreAsync(
                    "Accounts", new { Address = address, }, scope);
                await Db.Query("AccountTransaction")
                    .InsertAsync(
                        new
                        {
                            InvolvedTransactionsId = txId,
                            UpdatedAddressesAddress = address,
                        },
                        scope);
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    await Rollback();
                    return;
                }

                if (ActionTypeAttribute.ValueOf(customAction.GetType()) is not { } typeId)
                {
                    continue;
                }

                await Db.InsertOrIgnoreAsync(
                    "CustomActions",
                    new
                    {
                        TypeId = typeId,
                    },
                    scope);
                await Db.Query("CustomActionTransaction")
                    .InsertAsync(
                        new
                        {
                            ContainedTransactionsId = txId,
                            CustomActionsTypeId = typeId,
                        },
                        scope);
            }

            await Db.StatementAsync($"RELEASE \"{tx.Id.ToString()}\"", scope);
        }

        foreach (var nonce in txNonces)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                await Rollback();
                return;
            }

            await Db.Query("Accounts")
                .Where(new { Address = nonce.Key.ByteArray.ToArray() })
                .UpdateAsync(
                    new { LastNonce = nonce.Value },
                    scope);
        }

        await Db.StatementAsync($"RELEASE \"{block.Hash.ToString()}\"", scope);
        if (context is null)
        {
            scope.Commit();
            scope.Dispose();
        }
    }

    protected override IAddBlockContext GetAddBlockContext() =>
        new SqliteAddBlockContext(Db.Connection.BeginTransaction());

    protected override void FinalizeAddBlockContext(IAddBlockContext context, bool commit)
    {
        if (context is not SqliteAddBlockContext sqliteContext)
        {
            throw new ArgumentException(
                $"Received an unsupported {nameof(IAddBlockContext)}: {context.GetType()}");
        }

        if (commit)
        {
            sqliteContext.Transaction.Commit();
        }
        else
        {
            sqliteContext.Transaction.Rollback();
        }

        sqliteContext.Transaction.Dispose();
    }
}
