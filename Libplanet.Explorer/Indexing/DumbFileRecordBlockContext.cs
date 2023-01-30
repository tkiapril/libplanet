using System.Data;
using System.IO;

namespace Libplanet.Explorer.Indexing;

public record DumbFileRecordBlockContext(
    FileStream BlockHashToIndex,
    FileStream IndexToBlockHash,
    FileStream MinerToBlockIndex,
    FileStream SignerToTxId,
    FileStream InvolvedAddressToTxId,
    FileStream TxIdToContainedBlockHash,
    FileStream SystemActionTypeIdToTxId,
    FileStream CustomActionTypeIdToTxId,
    FileStream CustomActionTypeId
) : IRecordBlockContext;
