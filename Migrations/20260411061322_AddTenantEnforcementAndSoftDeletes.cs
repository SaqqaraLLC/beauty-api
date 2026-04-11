using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantEnforcementAndSoftDeletes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_EnterpriseAccounts_EnterpriseAccountId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_IsActive",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_EnterpriseUsers_EnterpriseAccountId_Email",
                table: "EnterpriseUsers");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TargetType_TargetId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_LocationId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "EnterpriseUsers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "EnterpriseUsers");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "AuditLogs");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "Locations",
                newName: "IsDeleted");

            migrationBuilder.RenameColumn(
                name: "LastLoginAt",
                table: "EnterpriseUsers",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "SuspendedAt",
                table: "EnterpriseAccounts",
                newName: "DeletedAt");

            migrationBuilder.RenameColumn(
                name: "LegalName",
                table: "EnterpriseAccounts",
                newName: "Name");

            migrationBuilder.AlterColumn<Guid>(
                name: "EnterpriseAccountId",
                table: "Locations",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Locations",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Locations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EnterpriseUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "EnterpriseClients",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EnterpriseClients",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EnterpriseAccounts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TargetEntity",
                table: "AuditLogs",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId1",
                table: "AspNetUsers",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_LocationId1",
                table: "AspNetUsers",
                column: "LocationId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId1",
                table: "AspNetUsers",
                column: "LocationId1",
                principalTable: "Locations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_EnterpriseAccounts_EnterpriseAccountId",
                table: "Locations",
                column: "EnterpriseAccountId",
                principalTable: "EnterpriseAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId1",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_EnterpriseAccounts_EnterpriseAccountId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_LocationId1",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EnterpriseUsers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "EnterpriseClients");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EnterpriseClients");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EnterpriseAccounts");

            migrationBuilder.DropColumn(
                name: "TargetEntity",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "LocationId1",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Locations",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "EnterpriseUsers",
                newName: "LastLoginAt");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "EnterpriseAccounts",
                newName: "LegalName");

            migrationBuilder.RenameColumn(
                name: "DeletedAt",
                table: "EnterpriseAccounts",
                newName: "SuspendedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "EnterpriseAccountId",
                table: "Locations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "Locations",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Locations",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Locations",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Locations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Locations",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Locations",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Locations",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "EnterpriseUsers",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "EnterpriseUsers",
                type: "varchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAt",
                table: "EnterpriseAccounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "EnterpriseAccounts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TargetId",
                table: "AuditLogs",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "AuditLogs",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive",
                table: "Locations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseUsers_EnterpriseAccountId_Email",
                table: "EnterpriseUsers",
                columns: new[] { "EnterpriseAccountId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TargetType_TargetId",
                table: "AuditLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_LocationId",
                table: "AspNetUsers",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Locations_LocationId",
                table: "AspNetUsers",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_EnterpriseAccounts_EnterpriseAccountId",
                table: "Locations",
                column: "EnterpriseAccountId",
                principalTable: "EnterpriseAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
