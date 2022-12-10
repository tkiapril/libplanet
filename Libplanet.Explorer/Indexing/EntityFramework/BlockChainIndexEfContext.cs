using System.Linq;
using Libplanet.Explorer.Indexing.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;

namespace Libplanet.Explorer.Indexing.EntityFramework;

/// <summary>
/// A class to provide EF Core context for blockchain index.
/// </summary>
internal abstract class BlockChainIndexEfContext : DbContext
{
    internal DbSet<Block> Blocks => Set<Block>();

    internal DbSet<Transaction> Transactions => Set<Transaction>();

    internal DbSet<Account> Accounts => Set<Account>();

    internal DbSet<CustomAction> CustomActions => Set<CustomAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>()
            .HasMany(account => account.InvolvedTransactions)
            .WithMany(transaction => transaction.UpdatedAddresses);

        modelBuilder.Entity<Account>()
            .HasMany(account => account.SignedTransactions)
            .WithOne(transaction => transaction.Signer)
            .IsRequired();

        modelBuilder.Entity<Account>()
            .HasMany(account => account.MinedBlocks)
            .WithOne(block => block.Miner)
            .IsRequired();

        modelBuilder.Entity<Block>()
            .HasIndex(block => block.Index)
            .IsUnique();

        modelBuilder.Entity<Transaction>()
            .HasOne(tx => tx.Signer)
            .WithMany(account => account.SignedTransactions)
            .IsRequired();

        var cascadedFks = modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);

        foreach (var fk in cascadedFks)
        {
            fk.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
