using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Libplanet.Explorer.Indexing.EntityFramework.Entities;

internal class Transaction
{
    [Key]
    [Column(TypeName = "binary(32)")]
    public byte[] Id { get; set; } = null!;

    public short? SystemActionTypeId { get; set; }

    public IEnumerable<CustomAction> CustomActions { get; set; } = null!;

    public Account Signer { get; set; } = null!;

    public IEnumerable<Account> UpdatedAddresses => null!;

    public byte[] BlockHash { get; set; } = null!;

    public Block Block { get; set; } = null!;
}
