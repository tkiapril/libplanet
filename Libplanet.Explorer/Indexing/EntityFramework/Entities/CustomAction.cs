using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Libplanet.Explorer.Indexing.EntityFramework.Entities;

internal class CustomAction
{
    [Key]
    public string TypeId { get; set; } = null!;

    public IEnumerable<Transaction> ContainedTransactions => null!;
}
