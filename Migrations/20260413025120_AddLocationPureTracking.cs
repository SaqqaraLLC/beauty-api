using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationPureTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                table: "Locations",
                type: "varchar(450)",
                maxLength: 450,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PureAccountActivatedAt",
                table: "Locations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PureAccountStatus",
                table: "Locations",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "PureFirstOrderPlacedAt",
                table: "Locations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OwnerUserId",
                table: "Locations",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_PureAccountStatus",
                table: "Locations",
                column: "PureAccountStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Locations_OwnerUserId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_PureAccountStatus",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PureAccountActivatedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PureAccountStatus",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PureFirstOrderPlacedAt",
                table: "Locations");
        }
    }
}
