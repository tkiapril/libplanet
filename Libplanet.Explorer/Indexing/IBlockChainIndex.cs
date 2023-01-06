using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// Tip of the indexed <see cref="Block{T}"/>s.
    /// </summary>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index is empty.</exception>
    IndexedBlockItem Tip { get; }

    /// <inheritdoc cref="GetIndexedBlock(long)"/>
    IndexedBlockItem this[long index] => GetIndexedBlock(index);

    /// <inheritdoc cref="GetIndexedBlock(Libplanet.Blocks.BlockHash)"/>
    IndexedBlockItem this[BlockHash hash] => GetIndexedBlock(hash);

    /// <inheritdoc cref="GetIndexedBlocks(System.Range,bool,Libplanet.Address?)"/>
    IImmutableList<IndexedBlockItem> this[Range indexRange] => GetIndexedBlocks(indexRange);

    /// <summary>
    /// Get the tip of the indexed <see cref="Block{T}"/>s.
    /// </summary>
    /// <returns>The tip of the indexed <see cref="Block{T}"/>s.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index is empty.</exception>
    Task<IndexedBlockItem> GetTipAsync();

    /// <summary>
    /// Get the indexed block with the given <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The <see cref="BlockHash"/> of the desired <see cref="Block{T}"/>.
    /// </param>
    /// <returns>The indexed block with the <paramref name="hash"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index does not contain the
    /// <see cref="Block{T}"/> with the given <paramref name="hash"/>.</exception>
    IndexedBlockItem GetIndexedBlock(BlockHash hash);

    /// <summary>
    /// Gets the indexed block at <paramref name="index"/> height.
    /// </summary>
    /// <param name="index">The height of the desired indexed <see cref="Block{T}"/>.</param>
    /// <returns>The indexed <see cref="Block{T}"/> at <paramref name="index"/> height.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the index does not contain the
    /// <see cref="Block{T}"/> at the given <paramref name="index"/>.</exception>
    IndexedBlockItem GetIndexedBlock(long index);

    /// <inheritdoc cref="GetIndexedBlock(Libplanet.Blocks.BlockHash)"/>
    Task<IndexedBlockItem> GetIndexedBlockAsync(BlockHash hash);

    /// <inheritdoc cref="GetIndexedBlock(long)"/>
    Task<IndexedBlockItem> GetIndexedBlockAsync(long index);

    /// <summary>
    /// Get the indexed <see cref="Block{T}"/>s in the given <paramref name="indexRange"/>.
    /// </summary>
    /// <param name="indexRange">The range of index to get the blocks from.</param>
    /// <param name="desc">Whether to return the <see cref="Block{T}"/>s in the descending order.
    /// </param>
    /// <param name="miner">The miner of the block, if desired.</param>
    /// <returns>The indexed <see cref="Block{T}"/>s in the given <paramref name="indexRange"/>.
    /// </returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IImmutableList<IndexedBlockItem> GetIndexedBlocks(
        Range indexRange, bool desc = false, Address? miner = null);

    /// <summary>
    /// Get at most <paramref name="limit"/> number of indexed <see cref="Block{T}"/>s from the
    /// <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">The starting index.</param>
    /// <param name="limit">The upper limit of <see cref="Block{T}"/>s to return.</param>
    /// <param name="desc">Whether to return the <see cref="Block{T}"/>s in the descending order.
    /// </param>
    /// <param name="miner">The miner of the block, if desired.</param>
    /// <returns>The indexed <see cref="Block{T}"/>s starting at <paramref name="offset"/> index
    /// and at most <paramref name="limit"/> number.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IImmutableList<IndexedBlockItem> GetIndexedBlocks(
        int? offset = null, int? limit = null, bool desc = false, Address? miner = null);

    /// <summary>
    /// Get at most <paramref name="limit"/> number of indexed <see cref="Transaction{T}"/>s from
    /// the <paramref name="offset"/> that was signed by the <paramref name="signer"/>.
    /// </summary>
    /// <param name="signer">The signer of the <see cref="Transaction{T}"/>.</param>
    /// <param name="offset">the starting index.</param>
    /// <param name="limit">The upper limit of <see cref="Transaction{T}"/>s to return.</param>
    /// <param name="desc">Whether to return the <see cref="Transaction{T}"/>s in the descending
    /// order.</param>
    /// <returns>The indexed <see cref="Transaction{T}"/>s signed by <paramref name="signer"/>
    /// starting at <paramref name="offset"/> index and at most <paramref name="limit"/> number.
    /// </returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IImmutableList<IndexedTransactionItem>
        GetSignedTransactions(
            Address signer, int? offset = null, int? limit = null, bool desc = false);

    /// <summary>
    /// Get at most <paramref name="limit"/> number of indexed <see cref="Transaction{T}"/>s from
    /// the <paramref name="offset"/> that involves the <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The address that is recorded as involved in the
    /// <see cref="Transaction{T}"/>.</param>
    /// <param name="offset">The starting index.</param>
    /// <param name="limit">The upper limit of <see cref="Transaction{T}"/>s to return.</param>
    /// <param name="desc">Whether to return the <see cref="Transaction{T}"/>s in the descending
    /// order.</param>
    /// <returns>The indexed <see cref="Transaction{T}"/>s involving the <paramref name="address"/>
    /// starting at <paramref name="offset"/> index and at most <paramref name="limit"/> number.
    /// </returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    IImmutableList<IndexedTransactionItem>
        GetInvolvedTransactions(
            Address address, int? offset = null, int? limit = null, bool desc = false);

    /// <summary>
    /// Get the indexed block that contains the <see cref="Transaction{T}"/> with the
    /// <paramref name="txId"/>.
    /// </summary>
    /// <param name="txId">The id of the <see cref="Transaction{T}"/> to find the containing
    /// <see cref="Block{T}"/>.</param>
    /// <returns>The indexed block that contains the <see cref="Transaction{T}"/> with the
    /// <paramref name="txId"/>.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if the <paramref name="txId"/> does not
    /// exist in the index.</exception>
    IndexedBlockItem GetContainedBlock(TxId txId);

    /// <summary>
    /// Attempt to get the indexed block that contains the <see cref="Transaction{T}"/> with the
    /// <paramref name="txId"/>, and return whether if it was successful.
    /// </summary>
    /// <param name="txId">The id of the <see cref="Transaction{T}"/> to find the containing
    /// <see cref="Block{T}"/>.</param>
    /// <param name="containedBlock">The indexed block that contains the
    /// <see cref="Transaction{T}"/> with the <paramref name="txId"/>, if it exists.</param>
    /// <returns>Whether the retrieval succeeded.</returns>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    bool TryGetContainedBlock(TxId txId, out IndexedBlockItem containedBlock);

    /// <summary>
    /// Get the last tx nonce of the <paramref name="address"/> that was recorded in the
    /// <see cref="BlockChain{T}"/>.
    /// </summary>
    /// <param name="address">The address to retrieve the tx nonce.</param>
    /// <returns>The last tx nonce of the <paramref name="address"/> that was recorded in the
    /// <see cref="BlockChain{T}"/>.</returns>
    /// <remarks>This method does not return the tx nonces of <see cref="Transaction{T}"/>s that
    /// are currently staged.</remarks>
    /// <exception cref="IndexNotReadyException">Thrown if the index is not ready.</exception>
    long? AccountLastNonce(Address address);

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
    internal void AddBlock(
        BlockDigest blockDigest, IEnumerable<ITransaction> txs, CancellationToken? token = null);

    /// <inheritdoc cref="AddBlock"/>
    internal Task AddBlockAsync(
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
