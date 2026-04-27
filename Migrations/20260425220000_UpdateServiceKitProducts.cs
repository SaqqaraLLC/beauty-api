using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateServiceKitProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Products are now full units the customer buys and keeps.
            // Replace decimal MinimumQuantity + Unit with int Quantity + int? SalePriceCents.
            migrationBuilder.DropColumn(name: "MinimumQuantity", table: "ServiceRequiredProducts");
            migrationBuilder.DropColumn(name: "Unit",            table: "ServiceRequiredProducts");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "ServiceRequiredProducts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SalePriceCents",
                table: "ServiceRequiredProducts",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Quantity",      table: "ServiceRequiredProducts");
            migrationBuilder.DropColumn(name: "SalePriceCents", table: "ServiceRequiredProducts");

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumQuantity",
                table: "ServiceRequiredProducts",
                type: "decimal(10,3)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "ServiceRequiredProducts",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
