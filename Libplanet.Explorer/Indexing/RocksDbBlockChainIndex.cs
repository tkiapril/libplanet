using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bencodex;
using Bencodex.Types;
using Libplanet.Blocks;
using Libplanet.Store;
using Libplanet.Tx;
using RocksDbSharp;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An <see cref="IBlockChainIndex"/> object that uses RocksDB as the backend.
/// </summary>
public class RocksDbBlockChainIndex : BlockChainIndexBase
{
    private static readonly byte[] BlockHashToIndexPrefix = { (byte)'b' };
    private static readonly byte[] IndexToBlockHashPrefix = { (byte)'i' };
    private static readonly byte[] MinerToBlockIndexPrefix = { (byte)'m' };
    private static readonly byte[] SignerToTxIdPrefix = { (byte)'s' };
    private static readonly byte[] InvolvedAddressToTxIdPrefix = { (byte)'I' };
    private static readonly byte[] TxIdToContainedBlockHashPrefix = { (byte)'t' };
    private static readonly byte[] SystemActionTypeIdToTxIdPrefix = { (byte)'S' };
    private static readonly byte[] CustomActionTypeIdToTxIdPrefix = { (byte)'C' };
    private static readonly byte[] CustomActionTypeIdPrefix = { (byte)'c' };
    private static readonly Codec Codec = new Codec();
    private readonly RocksDb _db;

    /// <summary>
    /// Create an instance of <see cref="IBlockChainIndex"/> that uses RocksDB as the backend.
    /// </summary>
    /// <param name="path">The path containing the RocksDB index database.</param>
    public RocksDbBlockChainIndex(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        path = Path.GetFullPath(path);

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        var rocksDbOption = new DbOptions()
            .SetCreateIfMissing();

        _db = RocksDb.Open(rocksDbOption, path);
    }

    /// <inheritdoc />
    public override long BlockHashToIndex(BlockHash hash)
    {
        EnsureReady();
        return _db.Get(
            BlockHashToIndexPrefix.Concat(hash.ByteArray).ToArray()) is { } arr
            ? LittleEndianByteArrayToLong(arr)
            : throw new IndexOutOfRangeException(
                $"The hash {hash} does not exist in the index.");
    }

    /// <inheritdoc />
    public override async Task<long> BlockHashToIndexAsync(BlockHash hash) =>
        await Task.Run(() => BlockHashToIndex(hash)).ConfigureAwait(false);

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetSignedTxIdsByAddress(Address signer, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        return IteratePrefix(
                offset, limit, desc, SignerToTxIdPrefix.Concat(signer.ByteArray).ToArray())
            .Select(kv => new TxId(kv.Value));
    }

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetInvolvedTxIdsByAddress(Address address, int? offset, int? limit, bool desc)
    {
        EnsureReady();
        return IteratePrefix(
                offset,
                limit,
                desc,
                InvolvedAddressToTxIdPrefix.Concat(address.ByteArray).ToArray())
            .Select(kv => new TxId(kv.Value));
    }

    /// <inheritdoc />
    public override long? GetLastNonceByAddress(Address address)
    {
        EnsureReady();
        using var iter = IteratePrefix(
                0, 1, true, SignerToTxIdPrefix.Concat(address.ByteArray).ToArray())
            .Select(kv => LittleEndianByteArrayToLong(kv.Key)).GetEnumerator();
        return iter.MoveNext()
            ? iter.Current
            : null;
    }

    /// <inheritdoc />
    public override bool TryGetContainedBlockHashById(TxId txId, out BlockHash containedBlock)
    {
        EnsureReady();
        containedBlock = default;
        var bytes =
            _db.Get(TxIdToContainedBlockHashPrefix.Concat(txId.ByteArray).ToArray());
        if (bytes is not { })
        {
            return false;
        }

        containedBlock = new BlockHash(bytes);
        return true;
    }

    protected override (long Index, BlockHash Hash)? GetTipImpl()
    {
        using var iter = GetBlockHashesByOffsetImpl(0, 1, true, null).GetEnumerator();
        return iter.MoveNext()
            ? iter.Current
            : null;
    }

    protected override async Task<(long Index, BlockHash Hash)?> GetTipAsyncImpl()
        => await Task.Run(GetTipImpl).ConfigureAwait(false);

    protected override BlockHash IndexToBlockHashImpl(long index)
    {
        return _db.Get(
            IndexToBlockHashPrefix.Concat(
                LongToLittleEndianByteArray(
                    index >= 0 ? index : (GetTipImpl()?.Index ?? 0) + index + 1)).ToArray())
            is { } arr
            ? new BlockHash(arr)
            : throw new IndexOutOfRangeException(
                $"The block #{index} does not exist in the index.");
    }

    protected override async Task<BlockHash> IndexToBlockHashAsyncImpl(long index)
        => await Task.Run(() => IndexToBlockHashImpl(index)).ConfigureAwait(false);

    protected override IEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByOffsetImpl(int? offset, int? limit, bool desc, Address? miner)
    {
        if (miner is { } minerVal)
        {
            return IteratePrefix(
                    offset,
                    limit,
                    desc,
                    MinerToBlockIndexPrefix.Concat(minerVal.ByteArray).ToArray())
                .Select(
                    kv => (
                        LittleEndianByteArrayToLong(kv.Value[..8]),
                        new BlockHash(kv.Value[8..40])));
        }

        return IteratePrefix(offset, limit, desc, IndexToBlockHashPrefix)
            .Select(kv => (LittleEndianByteArrayToLong(kv.Key), new BlockHash(kv.Value)));
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
        var indexToBlockHashKey = IndexToBlockHashPrefix
            .Concat(LongToLittleEndianByteArray(blockDigest.Index)).ToArray();

        var writeBatch = new WriteBatch();
        if (_db.Get(indexToBlockHashKey) is { } existingHash)
        {
            writeBatch.Dispose();
            if (new BlockHash(existingHash).Equals(blockDigest.Hash))
            {
                return;
            }

            throw new IndexMismatchException(
                blockDigest.Index, GetTipImpl()!.Value.Hash, blockDigest.Hash);
        }

        writeBatch.Put(indexToBlockHashKey, blockHash);
        writeBatch.Put(
            BlockHashToIndexPrefix.Concat(blockHash).ToArray(),
            LongToLittleEndianByteArray(blockDigest.Index));
        writeBatch.Put(
            GetNextOrdinalKey(MinerToBlockIndexPrefix.Concat(minerAddress).ToArray()),
            LongToLittleEndianByteArray(blockDigest.Index).Concat(blockHash).ToArray());

        var systemActionTypeIdOrdinalMemos = ImmutableDictionary<string, long>.Empty;
        var involvedAddressOrdinalMemos = ImmutableDictionary<string, long>.Empty;
        var customActionTypeIdToTxIdOrdinalMemos = ImmutableDictionary<string, long>.Empty;
        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                writeBatch.Dispose();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            var signerToTxIdKey = SignerToTxIdPrefix
                .Concat(signerAddress)
                .Concat(LongToLittleEndianByteArray(tx.Nonce)).ToArray();
            if (_db.Get(signerToTxIdKey) is { })
            {
                continue;
            }

            writeBatch.Put(signerToTxIdKey, txId);
            writeBatch.Put(TxIdToContainedBlockHashPrefix.Concat(txId).ToArray(), blockHash);
            if (tx.SystemAction is { } systemAction)
            {
                var systemActionTypeIdPrefix = SystemActionTypeIdToTxIdPrefix
                    .Concat(
                        ShortToLittleEndianByteArray(
                            (short)systemAction.GetValue<Integer>("type_id")))
                    .ToArray();
                PutOrdinalWithMemo(
                    ref writeBatch,
                    systemActionTypeIdPrefix,
                    txId,
                    ref systemActionTypeIdOrdinalMemos);
            }

            foreach (var address in tx.UpdatedAddresses.Select(address => address.ByteArray))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    writeBatch.Dispose();
                    return;
                }

                PutOrdinalWithMemo(
                    ref writeBatch,
                    InvolvedAddressToTxIdPrefix.Concat(address).ToArray(),
                    txId,
                    ref involvedAddressOrdinalMemos);
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    writeBatch.Dispose();
                    return;
                }

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                // Use IValue for string, as "abc" and "abcd" as raw byte strings overlap.
                writeBatch.Put(
                    CustomActionTypeIdPrefix.Concat(Codec.Encode(typeId)).ToArray(),
                    Array.Empty<byte>());
                PutOrdinalWithMemo(
                    ref writeBatch,
                    CustomActionTypeIdToTxIdPrefix.Concat(Codec.Encode(typeId)).ToArray(),
                    txId,
                    ref customActionTypeIdToTxIdOrdinalMemos);
            }
        }

        _db.Write(writeBatch);
        writeBatch.Dispose();
    }

    /// <inheritdoc />
    protected override async Task RecordBlockAsyncImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken) =>
        await Task.Run(() => RecordBlockImpl(blockDigest, txs, context, stoppingToken));

    protected override IRecordBlockContext GetRecordBlockContext() =>
        new RocksDbRecordBlockContext();

    protected override void FinalizeRecordBlockContext(IRecordBlockContext context, bool commit)
    {
        if (context is not RocksDbRecordBlockContext sqliteContext)
        {
            throw new ArgumentException(
                $"Received an unsupported {nameof(IRecordBlockContext)}: {context.GetType()}");
        }
    }

    private static byte[] ShortToLittleEndianByteArray(short val)
    {
        byte[] arr = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(arr);
        }

        return arr;
    }

    private static byte[] LongToLittleEndianByteArray(long val)
    {
        byte[] arr = BitConverter.GetBytes(val);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(arr);
        }

        return arr;
    }

    private static long LittleEndianByteArrayToLong(byte[] val)
    {
        var len = val.Length;
        if (len != 8)
        {
            throw new ArgumentException(
                $"a byte array of size 8 must be provided, but the size of given array was {len}.",
                nameof(val));
        }

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(val);
        }

        return BitConverter.ToInt64(val);
    }

    private IEnumerable<(byte[] Key, byte[] Value)>
        IteratePrefix(int? offset, int? limit, bool desc, byte[] prefix)
    {
        if (limit == 0)
        {
            yield break;
        }

        Iterator iter = _db.NewIterator().Seek(prefix);
        if (!iter.Valid() || !iter.Key().StartsWith(prefix))
        {
            yield break;
        }

        if (desc)
        {
            byte[] upper = new byte[iter.Key().Length - prefix.Length];
            Array.Fill(upper, byte.MaxValue);
            iter.Dispose();
            iter = _db.NewIterator().SeekForPrev(prefix.Concat(upper).ToArray());
        }

        byte[] key;
        Func<long> GetAdvancer()
        {
            long count = 0;
            System.Action advance = desc
                ? () => iter.Prev()
                : () => iter.Next();
            return () =>
            {
                advance();
                return ++count;
            };
        }

        var advance = GetAdvancer();
        for (var i = 0; i < offset; ++i)
        {
            advance();
        }

        for (long count = 0L;
             iter.Valid()
             && (key = iter.Key()).StartsWith(prefix)
             && (limit is not { } || count < (offset ?? 0) + limit);
             count = advance())
        {
            yield return (key[prefix.Length..], iter.Value());
        }

        iter.Dispose();
    }

    private long GetNextOrdinal(byte[] prefix)
    {
        using Iterator iter = _db.NewIterator().Seek(prefix);
        if (!iter.Valid() || !iter.Key().StartsWith(prefix))
        {
            return 0L;
        }

        byte[] upper = new byte[iter.Key().Length - prefix.Length];
        Array.Fill(upper, byte.MaxValue);
        using Iterator lastIter = _db.NewIterator().SeekForPrev(prefix.Concat(upper).ToArray());
        return LittleEndianByteArrayToLong(lastIter.Key()[prefix.Length..]) + 1;
    }

    private byte[] GetNextOrdinalKey(byte[] prefix)
    {
        long? memo = null;
        return GetNextOrdinalKey(prefix, ref memo);
    }

    private byte[] GetNextOrdinalKey(byte[] prefix, ref long? memo) =>
        prefix.Concat(
            LongToLittleEndianByteArray(
                (long)(memo = memo is { } memoVal
                    ? ++memoVal
                    : (memo = GetNextOrdinal(prefix)).Value))).ToArray();

    private void PutOrdinalWithMemo(
        ref WriteBatch writeBatch,
        byte[] prefix,
        byte[] value,
        ref ImmutableDictionary<string, long> memos)
    {
        long? memo = memos.TryGetValue(
            Convert.ToBase64String(prefix),
            out var memoValue)
            ? memoValue
            : null;
        writeBatch.Put(
            GetNextOrdinalKey(prefix, ref memo),
            value);
        memos = memos.SetItem(
            Convert.ToBase64String(prefix),
            memo!.Value);
    }
}
