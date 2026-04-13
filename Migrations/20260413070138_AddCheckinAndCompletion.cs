using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckinAndCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArtistCheckedIn",
                table: "CompanyBookingArtistSlots",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArtistCheckedInAt",
                table: "CompanyBookingArtistSlots",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ArtistCheckedIn",
                table: "Bookings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArtistCheckedInAt",
                table: "Bookings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Reminder24hSent",
                table: "Bookings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Reminder2hSent",
                table: "Bookings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceCompleted",
                table: "Bookings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceCompletedAt",
                table: "Bookings",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtistCheckedIn",
                table: "CompanyBookingArtistSlots");

            migrationBuilder.DropColumn(
                name: "ArtistCheckedInAt",
                table: "CompanyBookingArtistSlots");

            migrationBuilder.DropColumn(
                name: "ArtistCheckedIn",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ArtistCheckedInAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Reminder24hSent",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Reminder2hSent",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ServiceCompleted",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ServiceCompletedAt",
                table: "Bookings");
        }
    }
}
