using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bencodex;
using Bencodex.Types;
using Libplanet.Blocks;
using Libplanet.Store;
using Libplanet.Tx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An <see cref="IBlockChainIndex"/> object for testing.
/// </summary>
public class NoOpBlockChainIndex : BlockChainIndexBase
{
    private static readonly Codec Codec = new Codec();

    /// <summary>
    /// Create an instance of <see cref="IBlockChainIndex"/> for testing.
    /// </summary>
    public NoOpBlockChainIndex()
    {
    }

    /// <inheritdoc />
    public override long BlockHashToIndex(BlockHash hash) => 0L;

    /// <inheritdoc />
    public override async Task<long> BlockHashToIndexAsync(BlockHash hash) =>
        await Task.Run(() => BlockHashToIndex(hash)).ConfigureAwait(false);

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetSignedTxIdsByAddress(Address signer, int? offset, int? limit, bool desc) =>
        Enumerable.Empty<TxId>();

    /// <inheritdoc />
    public override IEnumerable<TxId>
        GetInvolvedTxIdsByAddress(Address address, int? offset, int? limit, bool desc) =>
        Enumerable.Empty<TxId>();

    /// <inheritdoc />
    public override long? GetLastNonceByAddress(Address address) => null;

    /// <inheritdoc />
    public override bool TryGetContainedBlockHashById(TxId txId, out BlockHash containedBlock)
    {
        containedBlock = default;
        return true;
    }

    protected override (long Index, BlockHash Hash)? GetTipImpl() => null;

    protected override async Task<(long Index, BlockHash Hash)?> GetTipAsyncImpl()
        => await Task.Run(GetTipImpl).ConfigureAwait(false);

    protected override BlockHash IndexToBlockHashImpl(long index) => default;

    protected override async Task<BlockHash> IndexToBlockHashAsyncImpl(long index)
        => await Task.Run(() => IndexToBlockHashImpl(index)).ConfigureAwait(false);

    protected override IEnumerable<(long Index, BlockHash Hash)>
        GetBlockHashesByOffsetImpl(int? offset, int? limit, bool desc, Address? miner) =>
    Enumerable.Empty<(long Index, BlockHash Hash)>();

    /// <inheritdoc />
    protected override void RecordBlockImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken)
    {
        var minerAddress = blockDigest.Miner.ByteArray.ToArray();
        var blockHash = blockDigest.Hash.ByteArray.ToArray();

        LongToBigEndianByteArray(blockDigest.Index).Concat(blockHash).ToArray().GetEnumerator();
        blockHash.Concat(LongToBigEndianByteArray(blockDigest.Index)).ToArray().GetEnumerator();
        minerAddress
            .Concat(LongToBigEndianByteArray(blockDigest.Index))
            .Concat(blockHash)
            .ToArray()
            .GetEnumerator();

        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            signerAddress
                .Concat(LongToBigEndianByteArray(tx.Nonce))
                .Concat(txId)
                .ToArray()
                .GetEnumerator();
            txId.Concat(blockHash).ToArray().GetEnumerator();
            if (tx.SystemAction is { } systemAction)
            {
                ShortToBigEndianByteArray((short)systemAction.GetValue<Integer>("type_id"))
                    .Concat(txId)
                    .ToArray()
                    .GetEnumerator();
            }

            foreach (var address in tx.UpdatedAddresses.Select(address => address.ByteArray))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    return;
                }

                address.Concat(txId).ToArray().GetEnumerator();
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    return;
                }

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                // Use IValue for string, as "abc" and "abcd" as raw byte strings overlap.
                Codec.Encode(typeId);
                Codec.Encode(typeId);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task RecordBlockAsyncImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken) =>
        await Task.Run(() => RecordBlockImpl(blockDigest, txs, context, stoppingToken));

    protected override IRecordBlockContext GetRecordBlockContext() => new NoOpRecordBlockContext();

    protected override void FinalizeRecordBlockContext(IRecordBlockContext context, bool commit)
    {
    }

    private static byte[] ShortToBigEndianByteArray(short val)
    {
        byte[] arr = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(arr);
        }

        return arr;
    }

    private static byte[] LongToBigEndianByteArray(long val)
    {
        byte[] arr = BitConverter.GetBytes(val);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(arr);
        }

        return arr;
    }
}
