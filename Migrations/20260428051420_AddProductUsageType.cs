using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUsageType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsageType",
                table: "ServiceRequiredProducts",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequiredProducts_ServiceId_UsageType",
                table: "ServiceRequiredProducts",
                columns: new[] { "ServiceId", "UsageType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceRequiredProducts_ServiceId_UsageType",
                table: "ServiceRequiredProducts");

            migrationBuilder.DropColumn(
                name: "UsageType",
                table: "ServiceRequiredProducts");
        }
    }
}
