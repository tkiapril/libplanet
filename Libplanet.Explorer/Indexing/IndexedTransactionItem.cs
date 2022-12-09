using Libplanet.Action.Sys;
using Libplanet.Blocks;
using Libplanet.Tx;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// Representation of an single indexed transaction object.
/// </summary>
public struct IndexedTransactionItem
{
    private IndexedTransactionItem(
        TxId id, short? systemActionTypeId, Address signer, BlockHash containedBlockHash)
    {
        Id = id;
        SystemActionTypeId = systemActionTypeId;
        Signer = signer;
        ContainedBlockHash = containedBlockHash;
    }

    /// <summary>
    /// The id of the transaction.
    /// </summary>
    public TxId Id { get; }

    /// <summary>
    /// The type id of system action, if this transaction contains one.
    /// </summary>
    public short? SystemActionTypeId { get; }

    /// <summary>
    /// The signer of the transaction.
    /// </summary>
    public Address Signer { get; }

    /// <summary>
    /// The hash of the block that contains this transaction.
    /// </summary>
    public BlockHash ContainedBlockHash { get; }

    internal static IndexedTransactionItem? FromTuple(
        (byte[]? Id, short? SystemActionTypeId, byte[]? Signer, byte[]? ContainedBlockHash) tuple)
    {
        return tuple.Id is { } && tuple.Signer is { } && tuple.ContainedBlockHash is { }
            ? new IndexedTransactionItem(
                new TxId(tuple.Id),
                tuple.SystemActionTypeId,
                new Address(tuple.Signer),
                new BlockHash(tuple.ContainedBlockHash))
            : null;
    }
}
