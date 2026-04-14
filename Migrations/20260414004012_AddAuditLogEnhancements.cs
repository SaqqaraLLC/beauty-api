using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_EnterpriseAccounts_EnterpriseAccountId",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "EnterpriseAccountId",
                table: "AuditLogs",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ActorEmail",
                table: "AuditLogs",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "AuditLogs",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "AuditLogs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ResultCode",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_EnterpriseAccounts_EnterpriseAccountId",
                table: "AuditLogs",
                column: "EnterpriseAccountId",
                principalTable: "EnterpriseAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_EnterpriseAccounts_EnterpriseAccountId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ActorEmail",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ResultCode",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "EnterpriseAccountId",
                table: "AuditLogs",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_EnterpriseAccounts_EnterpriseAccountId",
                table: "AuditLogs",
                column: "EnterpriseAccountId",
                principalTable: "EnterpriseAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
