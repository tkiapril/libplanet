using System;
using System.Collections.Immutable;
using System.Security.Cryptography;
using Libplanet.Common;
using Libplanet.Store.Trie;

namespace Libplanet.Store
{
    /// <summary>
    /// An interface to store states.
    /// </summary>
    public interface IStateStore : IDisposable
    {
        /// <summary>
        /// Gets the state root trie of the <paramref name="stateRootHash"/> from the state store.
        /// </summary>
        /// <param name="stateRootHash">The state root hash of the state root trie to get.
        /// If <see langword="null"/> is passed the empty state root trie is returned.</param>
        /// <returns>The state root trie of the <paramref name="stateRootHash"/>.
        /// If <see langword="null"/> is passed the empty state root trie is returned.</returns>
        ITrie GetStateRoot(HashDigest<SHA256>? stateRootHash);

        /// <summary>
        /// Prunes the states no more used from the state store.
        /// </summary>
        /// <param name="survivingStateRootHashes">The state root hashes <em>not</em> to prune.
        /// These state root hashes are guaranteed to survive after pruning.</param>
        void PruneStates(IImmutableSet<HashDigest<SHA256>> survivingStateRootHashes);

        /// <summary>
        /// Commits given <paramref name="trie"/> to storage.
        /// Returned <see cref="ITrie"/> must be identical to the one obtained from
        /// <see cref="GetStateRoot"/> with resulting <see cref="ITrie"/>'s
        /// <see cref="ITrie.Hash"/>.
        /// </summary>
        /// <param name="trie">The <see cref="ITrie"/> to commit.</param>
        /// <returns>The committed <see cref="ITrie"/>.</returns>
        /// <remarks>
        /// Given <paramref name="trie"/> must have originated from the same instance
        /// (or with an instance with the same reference to an <see cref="IKeyValueStore"/>).
        /// Otherwise, this is not guaranteed to work properly.
        /// </remarks>
        /// <seealso cref="GetStateRoot"/>
        ITrie Commit(ITrie trie);
    }
}
