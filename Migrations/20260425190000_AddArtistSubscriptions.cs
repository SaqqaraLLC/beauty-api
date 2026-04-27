using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArtistSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtistSubscriptions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Trialing")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MonthlyAmount = table.Column<decimal>(type: "decimal(10,2)", nullable: false, defaultValue: 19.00m),
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

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_UserId",
                table: "ArtistSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_Status",
                table: "ArtistSubscriptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistSubscriptions_NextBillingDate",
                table: "ArtistSubscriptions",
                column: "NextBillingDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ArtistSubscriptions");
        }
    }
}
