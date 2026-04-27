using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseYearlyExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Alter EnterpriseAccounts ───────────────────────────────────────
            // Rename BillingTier → PlanTier
            migrationBuilder.RenameColumn(
                name:  "BillingTier",
                table: "EnterpriseAccounts",
                newName: "PlanTier");

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "EnterpriseAccounts",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SeatLimit",
                table: "EnterpriseAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxMonthlyBookings",
                table: "EnterpriseAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractAmount",
                table: "EnterpriseAccounts",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractStartDate",
                table: "EnterpriseAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractRenewalDate",
                table: "EnterpriseAccounts",
                type: "datetime(6)",
                nullable: true);

            // ── Create EnterpriseContractHistories ────────────────────────────
            migrationBuilder.CreateTable(
                name: "EnterpriseContractHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EnterpriseAccountId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangeType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousTier = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewTier = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousSeatLimit = table.Column<int>(type: "int", nullable: false),
                    NewSeatLimit = table.Column<int>(type: "int", nullable: false),
                    PreviousMaxMonthlyBookings = table.Column<int>(type: "int", nullable: false),
                    NewMaxMonthlyBookings = table.Column<int>(type: "int", nullable: false),
                    PreviousBillingCycle = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewBillingCycle = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousContractAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    NewContractAmount = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseContractHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseContractHistories_EnterpriseAccounts_EnterpriseAccountId",
                        column: x => x.EnterpriseAccountId,
                        principalTable: "EnterpriseAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseContractHistories_EnterpriseAccountId",
                table: "EnterpriseContractHistories",
                column: "EnterpriseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseContractHistories_EffectiveDate",
                table: "EnterpriseContractHistories",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseAccounts_ContractRenewalDate",
                table: "EnterpriseAccounts",
                column: "ContractRenewalDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EnterpriseContractHistories");

            migrationBuilder.DropIndex(
                name: "IX_EnterpriseAccounts_ContractRenewalDate",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(name: "BillingCycle",         table: "EnterpriseAccounts");
            migrationBuilder.DropColumn(name: "SeatLimit",            table: "EnterpriseAccounts");
            migrationBuilder.DropColumn(name: "MaxMonthlyBookings",   table: "EnterpriseAccounts");
            migrationBuilder.DropColumn(name: "ContractAmount",       table: "EnterpriseAccounts");
            migrationBuilder.DropColumn(name: "ContractStartDate",    table: "EnterpriseAccounts");
            migrationBuilder.DropColumn(name: "ContractRenewalDate",  table: "EnterpriseAccounts");

            migrationBuilder.RenameColumn(
                name:  "PlanTier",
                table: "EnterpriseAccounts",
                newName: "BillingTier");
        }
    }
}
