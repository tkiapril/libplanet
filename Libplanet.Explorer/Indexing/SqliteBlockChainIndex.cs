using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Blocks;
using Libplanet.Explorer.Indexing.EntityFramework;
using Libplanet.Store;
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
    public override long BlockHashToIndex(BlockHash hash)
    {
        EnsureReady();
        return Db.Query("Blocks")
                   .Select("Index")
                   .Where("Hash", hash.ByteArray.ToArray())
                   .FirstOrDefault<long?>()
               ?? throw new IndexOutOfRangeException(
                   $"The hash {hash} does not exist in the index.");
    }

    /// <inheritdoc />
    public override async Task<long> BlockHashToIndexAsync(BlockHash hash)
    {
        EnsureReady();
        return await Db.Query("Blocks")
                   .Select("Index", "Hash", "MinerAddress")
                   .Where("Hash", hash.ByteArray.ToArray())
                   .FirstOrDefaultAsync<long?>()
               ?? throw new IndexOutOfRangeException(
                   $"The hash {hash} does not exist in the index.");
    }

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetSignedTxIdsByAddress(Address signer, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        if (limit == 0)
        {
            return Enumerable.Empty<TxId>();
        }

        var query = Db.Query("Transactions")
            .Select("Id")
            .Where("SignerAddress", signer.ByteArray.ToArray());
        query = desc
            ? query.OrderByDesc("Nonce")
            : query.OrderBy("Nonce");
        query = limit is { } limitVal ? query.Limit(limitVal) : query;
        query = offset is { } offsetVal ? query.Offset(offsetVal) : query;
        return query.Get<byte[]>()
            .Select(result => new TxId(result));
    }

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetInvolvedTxIdsByAddress(Address address, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        if (limit == 0)
        {
            return Enumerable.Empty<TxId>();
        }

        var query = Db.Query("Transactions")
            .Join(
                "AccountTransaction",
                "AccountTransaction.InvolvedTransactionsId",
                "Transactions.Id")
            .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
            .Select("Transactions.Id")
            .Where("AccountTransaction.UpdatedAddressesAddress", address.ByteArray.ToArray());
        query = desc
            ? query.OrderByDesc("Blocks.Index", "Transactions.Id")
            : query.OrderBy("Blocks.Index", "Transactions.Id");
        query = limit is { } limitVal ? query.Limit(limitVal) : query;
        query = offset is { } offsetVal ? query.Offset(offsetVal) : query;
        return query.Get<byte[]>()
            .Select(result => new TxId(result));
    }

    /// <inheritdoc />
    public override long? GetLastNonceByAddress(Address address)
    {
        EnsureReady();
        return Db.Query("Transactions")
            .Select("Nonce")
            .Where("SignerAddress", address.ByteArray.ToArray())
            .OrderByDesc("Nonce")
            .FirstOrDefault<long?>();
    }

    /// <inheritdoc />
    public override bool TryGetContainedBlockHashById(TxId txId, out BlockHash containedBlock)
    {
        EnsureReady();
        containedBlock = default;
        var item = Db.Query("Transactions")
            .Select("Blocks.Hash")
            .Where("Transactions.Id", txId.ByteArray.ToArray())
            .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
            .FirstOrDefault<byte[]>();
        if (item is not { })
        {
            return false;
        }

        containedBlock = new BlockHash(item);
        return true;
    }

    protected override (long Index, BlockHash Hash)? GetTipImpl()
    {
        (long? Index, byte[]? Hash) tipData = Db.Query("Blocks")
            .Select("Index", "Hash")
            .OrderByDesc("Index")
            .FirstOrDefault<(long?, byte[])>();
        return tipData is { Index: { } index, Hash: { } hash }
            ? (index, new BlockHash(hash))
            : null;
    }

    protected override async Task<(long Index, BlockHash Hash)?> GetTipAsyncImpl()
    {
        (long? Index, byte[]? Hash) tipData = await Db.Query("Blocks")
            .Select("Index", "Hash")
            .OrderByDesc("Index")
            .FirstOrDefaultAsync<(long?, byte[])>();
        return tipData is { Index: { } index, Hash: { } hash }
            ? (index, new BlockHash(hash))
            : null;
    }

    protected override BlockHash IndexToBlockHashImpl(long index)
    {
        var blockHashBytes = Db.Query("Blocks")
            .Select("Hash")
            .Where("Index", index >= 0 ? index : (GetTipImpl()?.Index ?? 0) + index + 1)
            .FirstOrDefault<byte[]>();
        return blockHashBytes is { }
            ? new BlockHash(blockHashBytes)
            : throw new IndexOutOfRangeException(
                $"The block #{index} does not exist in the index.");
    }

    protected override async Task<BlockHash> IndexToBlockHashAsyncImpl(long index)
    {
        var blockHashBytes = await Db.Query("Blocks")
            .Select("Hash")
            .Where(
                "Index",
                index >= 0 ? index : ((await GetTipAsyncImpl())?.Index ?? 0) + index + 1)
            .FirstOrDefaultAsync<byte[]>();
        return blockHashBytes is { }
            ? new BlockHash(blockHashBytes)
            : throw new IndexOutOfRangeException(
                $"The block #{index} does not exist in the index.");
    }

    protected override IEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByOffsetImpl(int? offset, int? limit, bool desc, Address? miner)
    {
        if (limit == 0)
        {
            return Enumerable.Empty<(long Index, BlockHash Hash)>();
        }

        var query = Db.Query("Blocks")
            .Select("Index", "Hash")
            .Offset(offset ?? 0);
        query = limit is { } limitVal
            ? query.Limit(limitVal)
            : query;
        query = miner is { } minerVal
            ? query.Where("MinerAddress", minerVal.ByteArray.ToArray())
            : query;
        query = desc ? query.OrderByDesc("Index") : query.OrderBy("Index");
        return query.Get<(long, byte[])>()
            .Select(result => (result.Item1, new BlockHash(result.Item2)));
    }

    /// <inheritdoc />
    protected override void RecordBlockImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken)
    {
        var minerAddress = blockDigest.Miner.ByteArray.ToArray();
        var blockHash = blockDigest.Hash.ByteArray.ToArray();

        var scope
            = ((SqliteRecordBlockContext?)context)?.Transaction ?? Db.Connection.BeginTransaction();
        Db.Statement($"SAVEPOINT \"{blockDigest.Hash.ToString()}\"", scope);
        void Rollback()
        {
            Db.Statement($"ROLLBACK TO \"{blockDigest.Hash.ToString()}\"", scope);
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
                        Index = blockDigest.Index,
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
                .Where(
                    new { Hash = blockDigest.Hash.ByteArray.ToArray(), Index = blockDigest.Index })
                .FirstOrDefault<long?>();

            if (index is not null)
            {
                return;
            }

            throw new IndexMismatchException(
                blockDigest.Index, GetTipImpl()!.Value.Hash, blockDigest.Hash);
        }

        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                Rollback();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            short? systemActionTypeId = tx.SystemAction is { } systemAction
                ? (short)systemAction.GetValue<Integer>("type_id")
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
                            Nonce = tx.Nonce,
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

                var (existingIndex, existingBlockHash) = Db.Query("Transactions")
                    .Select("Blocks.Index", "Blocks.Hash")
                    .Where("Transactions.Id", txId)
                    .Join("Blocks", "Blocks.Hash", "Transactions.BlockHash")
                    .FirstOrDefault<(long, byte[])>(scope);
                File.AppendAllText(
                    "tx-collision-log.txt",
                    $"{{\"txid\": \"{tx.Id.ToString()}\", "
                    + $"\"incident_block_index\": {blockDigest.Index}, "
                    + $"\"incident_blockhash\": \"{blockDigest.Hash.ToString()}\", "
                    + $"\"existing_block_index\": {existingIndex}, "
                    + $"\"existing_blockhash\": \"{new BlockHash(existingBlockHash).ToString()}\""
                    + "},");
                continue;
            }

            foreach (var address
                     in tx.UpdatedAddresses.Select(address => address.ByteArray.ToArray()))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    Rollback();
                    return;
                }

                Db.InsertOrIgnore("Accounts", new { Address = address }, scope);
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

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                Db.InsertOrIgnore(
                    "CustomActions",
                    new
                    {
                        TypeId = (string)(Text)typeId,
                    },
                    scope);
                Db.Query("CustomActionTransaction")
                    .Insert(
                        new
                        {
                            ContainedTransactionsId = txId,
                            CustomActionsTypeId = (string)(Text)typeId,
                        },
                        scope);
            }

            Db.Statement($"RELEASE \"{tx.Id.ToString()}\"", scope);
        }

        Db.Statement($"RELEASE \"{blockDigest.Hash.ToString()}\"", scope);
        if (context is null)
        {
            scope.Commit();
            scope.Dispose();
        }
    }

    /// <inheritdoc />
    protected override async Task RecordBlockAsyncImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken)
    {
        var minerAddress = blockDigest.Miner.ByteArray.ToArray();
        var blockHash = blockDigest.Hash.ByteArray.ToArray();

        var scope
            = ((SqliteRecordBlockContext?)context)?.Transaction ?? Db.Connection.BeginTransaction();
        await Db.StatementAsync($"SAVEPOINT \"{blockDigest.Hash.ToString()}\"", scope);
        async Task Rollback()
        {
            await Db.StatementAsync($"ROLLBACK TO \"{blockDigest.Hash.ToString()}\"", scope);
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
                        Index = blockDigest.Index,
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
                .Where(
                    new { Hash = blockDigest.Hash.ByteArray.ToArray(), Index = blockDigest.Index })
                .FirstOrDefaultAsync<long?>();

            if (index is not null)
            {
                return;
            }

            throw new IndexMismatchException(
                blockDigest.Index, (await GetTipAsyncImpl())!.Value.Hash, blockDigest.Hash);
        }

        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                await Rollback();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            short? systemActionTypeId = tx.SystemAction is { } systemAction
                ? (short)systemAction.GetValue<Integer>("type_id")
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
                            Nonce = tx.Nonce,
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

            foreach (var address
                     in tx.UpdatedAddresses.Select(address => address.ByteArray.ToArray()))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    await Rollback();
                    return;
                }

                await Db.InsertOrIgnoreAsync("Accounts", new { Address = address, }, scope);
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

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                await Db.InsertOrIgnoreAsync(
                    "CustomActions",
                    new
                    {
                        TypeId = (string)(Text)typeId,
                    },
                    scope);
                await Db.Query("CustomActionTransaction")
                    .InsertAsync(
                        new
                        {
                            ContainedTransactionsId = txId,
                            CustomActionsTypeId = (string)(Text)typeId,
                        },
                        scope);
            }

            await Db.StatementAsync($"RELEASE \"{tx.Id.ToString()}\"", scope);
        }

        await Db.StatementAsync($"RELEASE \"{blockDigest.Hash.ToString()}\"", scope);
        if (context is null)
        {
            scope.Commit();
            scope.Dispose();
        }
    }

    protected override IRecordBlockContext GetRecordBlockContext() =>
        new SqliteRecordBlockContext(Db.Connection.BeginTransaction());

    protected override void FinalizeRecordBlockContext(IRecordBlockContext context, bool commit)
    {
        if (context is not SqliteRecordBlockContext sqliteContext)
        {
            throw new ArgumentException(
                $"Received an unsupported {nameof(IRecordBlockContext)}: {context.GetType()}");
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
