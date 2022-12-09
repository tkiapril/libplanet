using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Libplanet.Explorer.Indexing.EntityFramework.Entities;

internal class Block
{
    [Key]
    [Column(TypeName = "binary(32)")]
    public byte[] Hash { get; set; } = null!;

    public long Index { get; set; }

    public IEnumerable<Transaction> Transactions => null!;

    public Account Miner { get; set; } = null!;
}
