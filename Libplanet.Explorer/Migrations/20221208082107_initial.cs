using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Libplanet.Explorer.Migrations
{
    /// <inheritdoc />
    public partial class initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Address = table.Column<byte[]>(type: "binary(20)", nullable: false),
                    LastNonce = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Address);
                });

            migrationBuilder.CreateTable(
                name: "CustomActions",
                columns: table => new
                {
                    TypeId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomActions", x => x.TypeId);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    Index = table.Column<long>(type: "INTEGER", nullable: false),
                    MinerAddress = table.Column<byte[]>(type: "binary(20)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_Blocks_Accounts_MinerAddress",
                        column: x => x.MinerAddress,
                        principalTable: "Accounts",
                        principalColumn: "Address");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    SystemActionTypeId = table.Column<short>(type: "INTEGER", nullable: true),
                    SignerAddress = table.Column<byte[]>(type: "binary(20)", nullable: false),
                    BlockHash = table.Column<byte[]>(type: "binary(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_SignerAddress",
                        column: x => x.SignerAddress,
                        principalTable: "Accounts",
                        principalColumn: "Address");
                    table.ForeignKey(
                        name: "FK_Transactions_Blocks_BlockHash",
                        column: x => x.BlockHash,
                        principalTable: "Blocks",
                        principalColumn: "Hash");
                });

            migrationBuilder.CreateTable(
                name: "AccountTransaction",
                columns: table => new
                {
                    InvolvedTransactionsId = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    UpdatedAddressesAddress = table.Column<byte[]>(type: "binary(20)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTransaction", x => new { x.InvolvedTransactionsId, x.UpdatedAddressesAddress });
                    table.ForeignKey(
                        name: "FK_AccountTransaction_Accounts_UpdatedAddressesAddress",
                        column: x => x.UpdatedAddressesAddress,
                        principalTable: "Accounts",
                        principalColumn: "Address");
                    table.ForeignKey(
                        name: "FK_AccountTransaction_Transactions_InvolvedTransactionsId",
                        column: x => x.InvolvedTransactionsId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomActionTransaction",
                columns: table => new
                {
                    ContainedTransactionsId = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CustomActionsTypeId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomActionTransaction", x => new { x.ContainedTransactionsId, x.CustomActionsTypeId });
                    table.ForeignKey(
                        name: "FK_CustomActionTransaction_CustomActions_CustomActionsTypeId",
                        column: x => x.CustomActionsTypeId,
                        principalTable: "CustomActions",
                        principalColumn: "TypeId");
                    table.ForeignKey(
                        name: "FK_CustomActionTransaction_Transactions_ContainedTransactionsId",
                        column: x => x.ContainedTransactionsId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountTransaction_UpdatedAddressesAddress",
                table: "AccountTransaction",
                column: "UpdatedAddressesAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_MinerAddress",
                table: "Blocks",
                column: "MinerAddress");

            migrationBuilder.CreateIndex(
                name: "IX_CustomActionTransaction_CustomActionsTypeId",
                table: "CustomActionTransaction",
                column: "CustomActionsTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BlockHash",
                table: "Transactions",
                column: "BlockHash");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SignerAddress",
                table: "Transactions",
                column: "SignerAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountTransaction");

            migrationBuilder.DropTable(
                name: "CustomActionTransaction");

            migrationBuilder.DropTable(
                name: "CustomActions");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
