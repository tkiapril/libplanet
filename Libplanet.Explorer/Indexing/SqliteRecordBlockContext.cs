using System.Data;

namespace Libplanet.Explorer.Indexing;

public record SqliteRecordBlockContext(IDbTransaction Transaction) : IRecordBlockContext;
