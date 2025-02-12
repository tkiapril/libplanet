#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Types;
using Libplanet.Action.State;
using Libplanet.Blockchain;
using Libplanet.Crypto;
using Libplanet.Explorer.GraphTypes;
using Libplanet.Explorer.Indexing;
using Libplanet.Explorer.Interfaces;
using Libplanet.Explorer.Store;
using Libplanet.Store;
using Libplanet.Types.Blocks;
using Libplanet.Types.Tx;

namespace Libplanet.Explorer.Queries
{
    public class ExplorerQuery : ObjectGraphType
    {
        public ExplorerQuery(IBlockChainContext chainContext)
        {
            ChainContext = chainContext;
            Field<BlockQuery>("blockQuery", resolve: context => new { });
            Field<TransactionQuery>("transactionQuery", resolve: context => new { });
            Field<StateQuery>("stateQuery", resolve: context => (
                (IBlockChainStates)chainContext.BlockChain,
                chainContext.BlockChain.Policy));
            Field<NonNullGraphType<NodeStateType>>(
                "nodeState",
                resolve: context => chainContext
            );

            Name = "ExplorerQuery";
        }

        private static IBlockChainContext ChainContext { get; set; }

        private static BlockChain Chain => ChainContext.BlockChain;

        private static IStore Store => ChainContext.Store;

        private static IBlockChainIndex Index => ChainContext.Index;

        internal static IEnumerable<Block> ListBlocks(
            bool desc,
            long offset,
            long? limit,
            bool excludeEmptyTxs,
            Address? miner)
        {
            Block tip = Chain.Tip;
            long tipIndex = tip.Index;
            IStore store = ChainContext.Store;

            if (desc)
            {
                if (offset < 0)
                {
                    offset = tipIndex + offset + 1;
                }
                else
                {
                    offset = tipIndex - offset + 1 - (limit ?? 0);
                }
            }
            else
            {
                if (offset < 0)
                {
                    offset = tipIndex + offset + 1;
                }
            }

            var indexList = store.IterateIndexes(
                    Chain.Id,
                    (int)offset,
                    limit == null ? null : (int)limit)
                .Select((value, i) => new { i, value } );

            if (desc)
            {
                indexList = indexList.Reverse();
            }

            foreach (var index in indexList)
            {
                var block = store.GetBlock(index.value);
                bool isMinerValid = miner is null || miner == block.Miner;
                bool isTxValid = !excludeEmptyTxs || block.Transactions.Any();
                if (!isMinerValid || !isTxValid)
                {
                    continue;
                }

                yield return block;
            }
        }

        internal static IEnumerable<Transaction> ListTransactions(
            Address? signer, Address? involved, bool desc, long offset, int? limit)
        {
            Block tip = Chain.Tip;
            long tipIndex = tip.Index;

            if (offset < 0)
            {
                offset = tipIndex + offset + 1;
            }

            if (tipIndex < offset || offset < 0)
            {
                yield break;
            }

            if (Store is IRichStore richStore)
            {
                IEnumerable<TxId> txIds;
                if (!(signer is null))
                {
                    txIds = richStore
                        .IterateSignerReferences(
                            (Address)signer, desc, (int)offset, limit ?? int.MaxValue);
                }
                else if (!(involved is null))
                {
                    txIds = richStore
                        .IterateUpdatedAddressReferences(
                            (Address)involved, desc, (int)offset, limit ?? int.MaxValue);
                }
                else
                {
                    txIds = richStore
                        .IterateTxReferences(null, desc, (int)offset, limit ?? int.MaxValue)
                        .Select(r => r.Item1);
                }

                var txs = txIds.Select(txId => Chain.GetTransaction(txId));
                foreach (var tx in txs)
                {
                    yield return tx;
                }

                yield break;
            }

            Block block = Chain[desc ? tipIndex - offset : offset];

            while (!(block is null) && (limit is null || limit > 0))
            {
                foreach (var tx in desc ? block.Transactions.Reverse() : block.Transactions)
                {
                    if (IsValidTransaction(tx, signer, involved))
                    {
                        yield return tx;
                        limit--;
                        if (limit <= 0)
                        {
                            break;
                        }
                    }
                }

                block = GetNextBlock(block, desc);
            }
        }

        internal static IEnumerable<Transaction> ListStagedTransactions(
            Address? signer, Address? involved, bool desc, int offset, int? limit)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"{nameof(ListStagedTransactions)} doesn't support negative offset.");
            }

            var stagedTxs = Chain.StagePolicy.Iterate(Chain)
                .Where(tx => IsValidTransaction(tx, signer, involved))
                .Skip(offset);

            stagedTxs = desc ? stagedTxs.OrderByDescending(tx => tx.Timestamp)
                : stagedTxs.OrderBy(tx => tx.Timestamp);

            stagedTxs = stagedTxs.TakeWhile((tx, index) => limit is null || index < limit);

            return stagedTxs;
        }

        internal static Block GetBlockByHash(BlockHash hash) => Store.GetBlock(hash);

        internal static Block GetBlockByIndex(long index) => Chain[index];

        internal static Transaction GetTransaction(TxId id) => Chain.GetTransaction(id);

        private static Block GetNextBlock(Block block, bool desc)
        {
            if (desc && block.PreviousHash is { } prev)
            {
                return Chain[prev];
            }
            else if (!desc && block != Chain.Tip)
            {
                return Chain[block.Index + 1];
            }

            return null;
        }

        private static bool IsValidTransaction(
            Transaction tx,
            Address? signer,
            Address? involved)
        {
            if (signer is { } signerVal)
            {
                return tx.Signer.Equals(signerVal);
            }

            if (involved is { } involvedVal)
            {
                return tx.UpdatedAddresses.Contains(involvedVal);
            }

            return true;
        }
    }
}
