using Libplanet.Blocks;

namespace Libplanet.Explorer.Indexing;

/// <summary>
/// Representation of a single indexed block object.
/// </summary>
public readonly struct IndexedBlockItem
{
    private IndexedBlockItem(long index, BlockHash hash, Address miner)
    {
        Index = index;
        Hash = hash;
        Miner = miner;
    }

    /// <summary>
    /// The index of the block.
    /// </summary>
    public long Index { get; }

    /// <summary>
    /// The hash of the block.
    /// </summary>
    public BlockHash Hash { get; }

    /// <summary>
    /// The miner address of the block.
    /// </summary>
    public Address Miner { get; }

    internal static IndexedBlockItem? FromTuple((long Index, byte[]? Hash, byte[]? Miner) tuple)
    {
        return tuple.Hash is { } && tuple.Miner is { }
            ? new IndexedBlockItem(tuple.Index, new BlockHash(tuple.Hash), new Address(tuple.Miner))
            : null;
    }
}
