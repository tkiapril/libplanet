// <auto-generated />
using System;
using Libplanet.Explorer.Indexing.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Libplanet.Explorer.Migrations
{
    [DbContext(typeof(SqliteBlockChainIndexEfContext))]
    [Migration("20221208082107_initial")]
    partial class initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.0");

            modelBuilder.Entity("AccountTransaction", b =>
                {
                    b.Property<byte[]>("InvolvedTransactionsId")
                        .HasColumnType("binary(32)");

                    b.Property<byte[]>("UpdatedAddressesAddress")
                        .HasColumnType("binary(20)");

                    b.HasKey("InvolvedTransactionsId", "UpdatedAddressesAddress");

                    b.HasIndex("UpdatedAddressesAddress");

                    b.ToTable("AccountTransaction");
                });

            modelBuilder.Entity("CustomActionTransaction", b =>
                {
                    b.Property<byte[]>("ContainedTransactionsId")
                        .HasColumnType("binary(32)");

                    b.Property<string>("CustomActionsTypeId")
                        .HasColumnType("TEXT");

                    b.HasKey("ContainedTransactionsId", "CustomActionsTypeId");

                    b.HasIndex("CustomActionsTypeId");

                    b.ToTable("CustomActionTransaction");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Account", b =>
                {
                    b.Property<byte[]>("Address")
                        .HasColumnType("binary(20)");

                    b.Property<long?>("LastNonce")
                        .HasColumnType("INTEGER");

                    b.HasKey("Address");

                    b.ToTable("Accounts");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Block", b =>
                {
                    b.Property<byte[]>("Hash")
                        .HasColumnType("binary(32)");

                    b.Property<long>("Index")
                        .HasColumnType("INTEGER");

                    b.Property<byte[]>("MinerAddress")
                        .IsRequired()
                        .HasColumnType("binary(20)");

                    b.HasKey("Hash");

                    b.HasIndex("MinerAddress");

                    b.ToTable("Blocks");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.CustomAction", b =>
                {
                    b.Property<string>("TypeId")
                        .HasColumnType("TEXT");

                    b.HasKey("TypeId");

                    b.ToTable("CustomActions");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Transaction", b =>
                {
                    b.Property<byte[]>("Id")
                        .HasColumnType("binary(32)");

                    b.Property<byte[]>("BlockHash")
                        .IsRequired()
                        .HasColumnType("binary(32)");

                    b.Property<byte[]>("SignerAddress")
                        .IsRequired()
                        .HasColumnType("binary(20)");

                    b.Property<short?>("SystemActionTypeId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("BlockHash");

                    b.HasIndex("SignerAddress");

                    b.ToTable("Transactions");
                });

            modelBuilder.Entity("AccountTransaction", b =>
                {
                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Transaction", null)
                        .WithMany()
                        .HasForeignKey("InvolvedTransactionsId")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();

                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Account", null)
                        .WithMany()
                        .HasForeignKey("UpdatedAddressesAddress")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();
                });

            modelBuilder.Entity("CustomActionTransaction", b =>
                {
                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Transaction", null)
                        .WithMany()
                        .HasForeignKey("ContainedTransactionsId")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();

                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.CustomAction", null)
                        .WithMany()
                        .HasForeignKey("CustomActionsTypeId")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Block", b =>
                {
                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Account", "Miner")
                        .WithMany("MinedBlocks")
                        .HasForeignKey("MinerAddress")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();

                    b.Navigation("Miner");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Transaction", b =>
                {
                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Block", "Block")
                        .WithMany("Transactions")
                        .HasForeignKey("BlockHash")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();

                    b.HasOne("Libplanet.Explorer.Indexing.EntityFramework.Entities.Account", "Signer")
                        .WithMany("SignedTransactions")
                        .HasForeignKey("SignerAddress")
                        .OnDelete(DeleteBehavior.NoAction)
                        .IsRequired();

                    b.Navigation("Block");

                    b.Navigation("Signer");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Account", b =>
                {
                    b.Navigation("MinedBlocks");

                    b.Navigation("SignedTransactions");
                });

            modelBuilder.Entity("Libplanet.Explorer.Indexing.EntityFramework.Entities.Block", b =>
                {
                    b.Navigation("Transactions");
                });
#pragma warning restore 612, 618
        }
    }
}
