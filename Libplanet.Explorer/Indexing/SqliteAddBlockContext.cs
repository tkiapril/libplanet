using System.Data;

namespace Libplanet.Explorer.Indexing;

public record SqliteAddBlockContext(IDbTransaction Transaction) : IAddBlockContext;
