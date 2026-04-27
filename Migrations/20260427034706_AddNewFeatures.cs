using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNewFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "BillingTier",
                table: "EnterpriseAccounts",
                newName: "PlanTier");

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "EnterpriseAccounts",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ContractAmount",
                table: "EnterpriseAccounts",
                type: "decimal(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractRenewalDate",
                table: "EnterpriseAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractStartDate",
                table: "EnterpriseAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxMonthlyBookings",
                table: "EnterpriseAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SeatLimit",
                table: "EnterpriseAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ArtistSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    TrialStartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TrialEndDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubscriptionStartDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextBillingDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastBilledDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastBilledAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistSubscriptions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EnterpriseContractHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EnterpriseAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
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
                        name: "FK_EnterpriseContractHistories_EnterpriseAccounts_EnterpriseAcc~",
                        column: x => x.EnterpriseAccountId,
                        principalTable: "EnterpriseAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SubmittedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpenseDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceiptUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewNotes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ServiceRequiredProducts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ServiceId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SalePriceCents = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequiredProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequiredProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRequiredProducts_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StreamFlags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StreamId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FlagConfidence = table.Column<double>(type: "double", nullable: false),
                    FlaggedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedByName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewNotes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FlaggedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StreamFlags_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "StreamId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseAccounts_ContractRenewalDate",
                table: "EnterpriseAccounts",
                column: "ContractRenewalDate");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_NextBillingDate",
                table: "ArtistSubscriptions",
                column: "NextBillingDate");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_Status",
                table: "ArtistSubscriptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_UserId",
                table: "ArtistSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseContractHistories_EffectiveDate",
                table: "EnterpriseContractHistories",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseContractHistories_EnterpriseAccountId",
                table: "EnterpriseContractHistories",
                column: "EnterpriseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Category",
                table: "Expenses",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseDate",
                table: "Expenses",
                column: "ExpenseDate");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Status",
                table: "Expenses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SubmittedByUserId",
                table: "Expenses",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequiredProducts_ProductId",
                table: "ServiceRequiredProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequiredProducts_ServiceId_IsActive",
                table: "ServiceRequiredProducts",
                columns: new[] { "ServiceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_StreamFlags_FlaggedAt",
                table: "StreamFlags",
                column: "FlaggedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StreamFlags_Status",
                table: "StreamFlags",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StreamFlags_StreamId",
                table: "StreamFlags",
                column: "StreamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistSubscriptions");

            migrationBuilder.DropTable(
                name: "EnterpriseContractHistories");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "ServiceRequiredProducts");

            migrationBuilder.DropTable(
                name: "StreamFlags");

            migrationBuilder.DropIndex(
                name: "IX_EnterpriseAccounts_ContractRenewalDate",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "ContractAmount",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "ContractRenewalDate",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "ContractStartDate",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "MaxMonthlyBookings",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "SeatLimit",
                table: "EnterpriseAccounts");

            migrationBuilder.RenameColumn(
                name: "PlanTier",
                table: "EnterpriseAccounts",
                newName: "BillingTier");
        }
    }
}
