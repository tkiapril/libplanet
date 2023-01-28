using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blocks;
using Libplanet.Store;
using Libplanet.Tx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// An interface that provides indexing to Libplanet.Explorer.
/// </summary>
public interface IBlockChainIndex
{
    /// <summary>
    /// The height and the <see cref="BlockHash"/> of the most recently indexed
    /// <see cref="Block{T}"/>.
    /// </summary>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index is empty.</exception>
    (long Index, BlockHash Hash) Tip { get; }

    /// <inheritdoc cref="IndexToBlockHash"/>
    BlockHash this[long index] => IndexToBlockHash(index);

    /// <inheritdoc cref="GetBlockHashesByRange"/>
    IEnumerable<BlockHash> this[Range indexRange] =>
        GetBlockHashesByRange(indexRange).Select(tuple => tuple.Hash);

    /// <summary>
    /// Get the height and the <see cref="BlockHash"/> of the most recently indexed
    /// <see cref="Block{T}"/>.
    /// </summary>
    /// <returns>A <see cref="ValueTuple{Long,BlockHash}"/> that contains the height and the
    /// <see cref="BlockHash"/> of the most recently indexed <see cref="Block{T}"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index is empty.</exception>
    Task<(long Index, BlockHash Hash)> GetTipAsync();

    /// <summary>
    /// Get the indexed height of the <see cref="Block{T}"/> with the given <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The <see cref="BlockHash"/> of the desired indexed
    /// <see cref="Block{T}"/>.</param>
    /// <returns>The height of the block with the <paramref name="hash"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the <see cref="Block{T}"/> with the
    /// given <paramref name="hash"/> is not indexed yet.</exception>
    long BlockHashToIndex(BlockHash hash);

    /// <inheritdoc cref="BlockHashToIndex"/>
    Task<long> BlockHashToIndexAsync(BlockHash hash);

    /// <summary>
    /// Gets the indexed <see cref="BlockHash"/> of the <see cref="Block{T}"/> at
    /// <paramref name="index"/> height.
    /// </summary>
    /// <param name="index">The height of the desired indexed <see cref="Block{T}"/>.</param>
    /// <returns>The indexed <see cref="BlockHash"/> of the <see cref="Block{T}"/> at
    /// <paramref name="index"/> height.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index does not contain the
    /// <see cref="Block{T}"/> at the given <paramref name="index"/>.</exception>
    BlockHash IndexToBlockHash(long index);

    /// <inheritdoc cref="IndexToBlockHash"/>
    Task<BlockHash> IndexToBlockHashAsync(long index);

    /// <summary>
    /// Get the height and the <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/>s in the
    /// given <paramref name="indexRange"/>.
    /// </summary>
    /// <param name="indexRange">The range of <see cref="Block{T}"/> height to look up.</param>
    /// <param name="desc">Whether to look up the index in the descending order.</param>
    /// <param name="miner">The miner of the block, if filtering by the miner is desired.</param>
    /// <returns>The height and the <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/>s
    /// in the given <paramref name="indexRange"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IEnumerable<(long Index, BlockHash Hash)> GetBlockHashesByRange(
        Range indexRange, bool desc = false, Address? miner = null);

    /// <summary>
    /// Get the height and the <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/>s from
    /// the <paramref name="offset"/>, at most <paramref name="limit"/> number.
    /// </summary>
    /// <param name="offset">The starting index.</param>
    /// <param name="limit">The upper limit of <see cref="Block{T}"/>s to look up.</param>
    /// <param name="desc">Whether to look up the index in the descending order.</param>
    /// <param name="miner">The miner of the block, if filtering by the miner is desired.</param>
    /// <returns>The height and the <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/>s
    /// starting at <paramref name="offset"/> index and at most <paramref name="limit"/> number.
    /// </returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IEnumerable<(long Index, BlockHash Hash)> GetBlockHashesByOffset(
        int? offset = null, int? limit = null, bool desc = false, Address? miner = null);

    /// <summary>
    /// Get the <see cref="TxId"/> of the indexed <see cref="Transaction{T}"/>s from the
    /// <paramref name="offset"/> that was signed by the <paramref name="signer"/>, at most
    /// <paramref name="limit"/> number.
    /// </summary>
    /// <param name="signer">The signer of the <see cref="Transaction{T}"/>.</param>
    /// <param name="offset">the starting index.</param>
    /// <param name="limit">The upper limit of <see cref="TxId"/>s to return.</param>
    /// <param name="desc">Whether to look up the <see cref="TxId"/>s in the descending order.
    /// </param>
    /// <returns>The <see cref="TxId"/> of the indexed <see cref="Transaction{T}"/>s signed by
    /// the <paramref name="signer"/> starting at <paramref name="offset"/> index and at most
    /// <paramref name="limit"/> number.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IEnumerable<TxId> GetSignedTxIdsByAddress(
            Address signer, int? offset = null, int? limit = null, bool desc = false);

    /// <summary>
    /// Get the <see cref="TxId"/> of the indexed <see cref="Transaction{T}"/>s from the
    /// <paramref name="offset"/> that involves the <paramref name="address"/>, at most
    /// <paramref name="limit"/> number.
    /// </summary>
    /// <param name="address">The address that is recorded as involved in the
    /// <see cref="Transaction{T}"/>.</param>
    /// <param name="offset">The starting index.</param>
    /// <param name="limit">The upper limit of <see cref="TxId"/>s to return.</param>
    /// <param name="desc">Whether to look up the <see cref="TxId"/>s in the descending order.
    /// </param>
    /// <returns>The <see cref="TxId"/> of the indexed <see cref="Transaction{T}"/>s involving the
    /// <paramref name="address"/> starting at <paramref name="offset"/> index and at most
    /// <paramref name="limit"/> number.
    /// </returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IEnumerable<TxId> GetInvolvedTxIdsByAddress(
        Address address, int? offset = null, int? limit = null, bool desc = false);

    /// <summary>
    /// Get the <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/> that contains the
    /// <see cref="Transaction{T}"/> with the <paramref name="txId"/>.
    /// </summary>
    /// <param name="txId">The <see cref="TxId"/> of the <see cref="Transaction{T}"/> to look up the
    /// containing <see cref="Block{T}"/>.</param>
    /// <returns>The <see cref="BlockHash"/> of the indexed <see cref="Block{T}"/> that contains the
    /// <see cref="Transaction{T}"/> with the <paramref name="txId"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the <paramref name="txId"/> does not
    /// exist in the index.</exception>
    BlockHash GetContainedBlockHashByTxId(TxId txId);

    /// <summary>
    /// Attempt to get the <see cref="BlockHash"/> of the indexed block that contains the
    /// <see cref="Transaction{T}"/> with the <paramref name="txId"/>, and return whether if the
    /// lookup was successful.
    /// </summary>
    /// <param name="txId">The <see cref="TxId"/> of the <see cref="Transaction{T}"/> to find the
    /// containing <see cref="Block{T}"/>.</param>
    /// <param name="containedBlock">The <see cref="BlockHash"/> of the indexed block that contains
    /// the <see cref="Transaction{T}"/> with the <paramref name="txId"/>, if it exists.</param>
    /// <returns>Whether the retrieval was successful.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    bool TryGetContainedBlockHashById(TxId txId, out BlockHash containedBlock);

    /// <summary>
    /// Get the last tx nonce of the <paramref name="address"/> that was indexed.
    /// </summary>
    /// <param name="address">The address to retrieve the tx nonce.</param>
    /// <returns>The last tx nonce of the <paramref name="address"/> that was indexed.</returns>
    /// <remarks>This method does not return the tx nonces of <see cref="Transaction{T}"/>s that
    /// are currently staged.</remarks>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    long? GetLastNonceByAddress(Address address);

    /// <summary>
    /// Record the metadata of a <see cref="Block{T}"/> corresponding to the given
    /// <paramref name="blockDigest"/> to the index.
    /// </summary>
    /// <param name="blockDigest">The block digest object to record to the index.</param>
    /// <param name="txs">An <see cref="IEnumerable{T}"/> containing the <see cref="ITransaction"/>
    /// instances corresponding to <see cref="BlockDigest.TxIds"/> of given
    /// <paramref name="blockDigest"/>.</param>
    /// <param name="token">A token to mark the cancellation of processing.</param>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexMismatchException">Thrown if the index already has seen a block in
    /// the height of the given block, but the hash of the indexed block and the given block is
    /// different.</exception>
    internal void RecordBlock(
        BlockDigest blockDigest, IEnumerable<ITransaction> txs, CancellationToken? token = null);

    /// <inheritdoc cref="RecordBlock"/>
    internal Task RecordBlockAsync(
        BlockDigest blockDigest, IEnumerable<ITransaction> txs, CancellationToken? token = null);

    internal void Bind<T>(BlockChain<T> chain, CancellationToken? stoppingToken = null)
        where T : IAction, new();

    internal Task BindAsync<T>(BlockChain<T> chain, CancellationToken? stoppingToken = null)
        where T : IAction, new();

    internal void Populate<T>(IStore store, CancellationToken? stoppingToken = null)
        where T : IAction, new();

    internal Task PopulateAsync<T>(IStore store, CancellationToken? stoppingToken = null)
        where T : IAction, new();
}
