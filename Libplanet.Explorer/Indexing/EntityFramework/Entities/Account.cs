using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Libplanet.Explorer.Indexing.EntityFramework.Entities;

internal class Account
{
    [Key]
    [Column(TypeName = "binary(20)")]
    public byte[] Address { get; set; } = null!;

    public IEnumerable<Transaction> InvolvedTransactions => null!;

    public IEnumerable<Transaction> SignedTransactions => null!;

    public IEnumerable<Block> MinedBlocks => null!;
}
