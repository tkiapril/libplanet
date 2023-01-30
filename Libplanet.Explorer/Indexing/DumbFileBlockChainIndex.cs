using System;
using System.Collections.Generic;
using System.IO;
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
public class DumbFileBlockChainIndex : BlockChainIndexBase
{
    private static readonly Codec Codec = new Codec();
    private readonly string _path;

    /// <summary>
    /// Create an instance of <see cref="IBlockChainIndex"/> for testing.
    /// </summary>
    /// <param name="path">The path containing the dumb file index database.</param>
    public DumbFileBlockChainIndex(string path)
    {
        _path = path;
        Directory.CreateDirectory(path);
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
        DumbFileRecordBlockContext files =
            context as DumbFileRecordBlockContext
            ?? (DumbFileRecordBlockContext)GetRecordBlockContext();

        void FinalizeContext()
        {
            if (context is not DumbFileRecordBlockContext)
            {
                FinalizeRecordBlockContext(files, true);
            }
            else
            {
                files.BlockHashToIndex.Flush();
                files.IndexToBlockHash.Flush();
                files.MinerToBlockIndex.Flush();
                files.SignerToTxId.Flush();
                files.InvolvedAddressToTxId.Flush();
                files.TxIdToContainedBlockHash.Flush();
                files.SystemActionTypeIdToTxId.Flush();
                files.CustomActionTypeIdToTxId.Flush();
                files.CustomActionTypeId.Flush();
            }
        }

        var minerAddress = blockDigest.Miner.ByteArray.ToArray();
        var blockHash = blockDigest.Hash.ByteArray.ToArray();

        files.IndexToBlockHash.Write(
            LongToBigEndianByteArray(blockDigest.Index).Concat(blockHash).ToArray());
        files.BlockHashToIndex.Write(
            blockHash.Concat(LongToBigEndianByteArray(blockDigest.Index)).ToArray());
        files.MinerToBlockIndex.Write(
            minerAddress
                .Concat(LongToBigEndianByteArray(blockDigest.Index))
                .Concat(blockHash)
                .ToArray());

        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                FinalizeContext();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            files.SignerToTxId.Write(
                    signerAddress
                        .Concat(LongToBigEndianByteArray(tx.Nonce))
                        .Concat(txId)
                        .ToArray());
            files.TxIdToContainedBlockHash.Write(txId.Concat(blockHash).ToArray());
            if (tx.SystemAction is { } systemAction)
            {
                files.SystemActionTypeIdToTxId.Write(
                    ShortToBigEndianByteArray((short)systemAction.GetValue<Integer>("type_id"))
                        .Concat(txId)
                        .ToArray());
            }

            foreach (var address in tx.UpdatedAddresses.Select(address => address.ByteArray))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    FinalizeContext();
                    return;
                }

                files.InvolvedAddressToTxId.Write(address.Concat(txId).ToArray());
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    FinalizeContext();
                    return;
                }

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                // Use IValue for string, as "abc" and "abcd" as raw byte strings overlap.
                files.CustomActionTypeId.Write(Codec.Encode(typeId));
                files.CustomActionTypeIdToTxId.Write(
                    Codec.Encode(typeId).Concat(txId).ToArray());
            }
        }

        FinalizeContext();
    }

    /// <inheritdoc />
    protected override async Task RecordBlockAsyncImpl(
        BlockDigest blockDigest,
        IEnumerable<ITransaction> txs,
        IRecordBlockContext? context,
        CancellationToken? stoppingToken)
    {
        DumbFileRecordBlockContext files =
            context as DumbFileRecordBlockContext
            ?? (DumbFileRecordBlockContext)GetRecordBlockContext();

        void FinalizeContext()
        {
            if (context is not DumbFileRecordBlockContext)
            {
                FinalizeRecordBlockContext(files, true);
            }
            else
            {
                files.BlockHashToIndex.Flush();
                files.IndexToBlockHash.Flush();
                files.MinerToBlockIndex.Flush();
                files.SignerToTxId.Flush();
                files.InvolvedAddressToTxId.Flush();
                files.TxIdToContainedBlockHash.Flush();
                files.SystemActionTypeIdToTxId.Flush();
                files.CustomActionTypeIdToTxId.Flush();
                files.CustomActionTypeId.Flush();
            }
        }

        var minerAddress = blockDigest.Miner.ByteArray.ToArray();
        var blockHash = blockDigest.Hash.ByteArray.ToArray();

        await files.IndexToBlockHash.WriteAsync(
            LongToBigEndianByteArray(blockDigest.Index).Concat(blockHash).ToArray());
        await files.BlockHashToIndex.WriteAsync(
            blockHash.Concat(LongToBigEndianByteArray(blockDigest.Index)).ToArray());
        await files.MinerToBlockIndex.WriteAsync(
            minerAddress
                .Concat(LongToBigEndianByteArray(blockDigest.Index))
                .Concat(blockHash)
                .ToArray());

        foreach (var tx in txs)
        {
            if (stoppingToken?.IsCancellationRequested ?? false)
            {
                FinalizeContext();
                return;
            }

            var signerAddress = tx.Signer.ByteArray.ToArray();
            var txId = tx.Id.ByteArray.ToArray();
            await files.SignerToTxId.WriteAsync(
                    signerAddress
                        .Concat(LongToBigEndianByteArray(tx.Nonce))
                        .Concat(txId)
                        .ToArray());
            await files.TxIdToContainedBlockHash.WriteAsync(txId.Concat(blockHash).ToArray());
            if (tx.SystemAction is { } systemAction)
            {
                await files.SystemActionTypeIdToTxId.WriteAsync(
                    ShortToBigEndianByteArray((short)systemAction.GetValue<Integer>("type_id"))
                        .Concat(txId)
                        .ToArray());
            }

            foreach (var address in tx.UpdatedAddresses.Select(address => address.ByteArray))
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    FinalizeContext();
                    return;
                }

                await files.InvolvedAddressToTxId.WriteAsync(address.Concat(txId).ToArray());
            }

            if (tx.CustomActions is not { } customActions)
            {
                continue;
            }

            foreach (var customAction in customActions)
            {
                if (stoppingToken?.IsCancellationRequested ?? false)
                {
                    FinalizeContext();
                    return;
                }

                if (customAction is not Dictionary actionDict
                    || !actionDict.TryGetValue((Text)"type_id", out var typeId))
                {
                    continue;
                }

                // Use IValue for string, as "abc" and "abcd" as raw byte strings overlap.
                await files.CustomActionTypeId.WriteAsync(Codec.Encode(typeId));
                await files.CustomActionTypeIdToTxId.WriteAsync(
                    Codec.Encode(typeId).Concat(txId).ToArray());
            }
        }

        FinalizeContext();
    }

    protected override IRecordBlockContext GetRecordBlockContext() =>
        new DumbFileRecordBlockContext(
            File.OpenWrite(Path.Combine(_path, "BlockHashToIndex.txt")),
            File.OpenWrite(Path.Combine(_path, "IndexToBlockHash.txt")),
            File.OpenWrite(Path.Combine(_path, "MinerToBlockIndex.txt")),
            File.OpenWrite(Path.Combine(_path, "SignerToTxId.txt")),
            File.OpenWrite(Path.Combine(_path, "InvolvedAddressToTxId.txt")),
            File.OpenWrite(Path.Combine(_path, "TxIdToContainedBlockHash.txt")),
            File.OpenWrite(Path.Combine(_path, "SystemActionTypeIdToTxId.txt")),
            File.OpenWrite(Path.Combine(_path, "CustomActionTypeIdToTxId.txt")),
            File.OpenWrite(Path.Combine(_path, "CustomActionTypeId.txt")));

    protected override void FinalizeRecordBlockContext(IRecordBlockContext context, bool commit)
    {
        if (context is not DumbFileRecordBlockContext noOpContext)
        {
            throw new ArgumentException(
                $"Received an unsupported {nameof(IRecordBlockContext)}: {context.GetType()}");
        }

        noOpContext.BlockHashToIndex.Flush();
        noOpContext.BlockHashToIndex.Close();
        noOpContext.BlockHashToIndex.Dispose();
        noOpContext.IndexToBlockHash.Flush();
        noOpContext.IndexToBlockHash.Close();
        noOpContext.IndexToBlockHash.Dispose();
        noOpContext.MinerToBlockIndex.Flush();
        noOpContext.MinerToBlockIndex.Close();
        noOpContext.MinerToBlockIndex.Dispose();
        noOpContext.SignerToTxId.Flush();
        noOpContext.SignerToTxId.Close();
        noOpContext.SignerToTxId.Dispose();
        noOpContext.InvolvedAddressToTxId.Flush();
        noOpContext.InvolvedAddressToTxId.Close();
        noOpContext.InvolvedAddressToTxId.Dispose();
        noOpContext.TxIdToContainedBlockHash.Flush();
        noOpContext.TxIdToContainedBlockHash.Close();
        noOpContext.TxIdToContainedBlockHash.Dispose();
        noOpContext.SystemActionTypeIdToTxId.Flush();
        noOpContext.SystemActionTypeIdToTxId.Close();
        noOpContext.SystemActionTypeIdToTxId.Dispose();
        noOpContext.CustomActionTypeIdToTxId.Flush();
        noOpContext.CustomActionTypeIdToTxId.Close();
        noOpContext.CustomActionTypeIdToTxId.Dispose();
        noOpContext.CustomActionTypeId.Flush();
        noOpContext.CustomActionTypeId.Close();
        noOpContext.CustomActionTypeId.Dispose();
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
