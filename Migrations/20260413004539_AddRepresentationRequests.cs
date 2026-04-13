using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRepresentationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepresentationRequests",
                columns: table => new
                {
                    RepresentationRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AgentProfileId = table.Column<int>(type: "int", nullable: false),
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResponseNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepresentationRequests", x => x.RepresentationRequestId);
                    table.ForeignKey(
                        name: "FK_RepresentationRequests_AgentProfiles_AgentProfileId",
                        column: x => x.AgentProfileId,
                        principalTable: "AgentProfiles",
                        principalColumn: "AgentProfileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepresentationRequests_ArtistProfiles_ArtistProfileId",
                        column: x => x.ArtistProfileId,
                        principalTable: "ArtistProfiles",
                        principalColumn: "ArtistProfileId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRequests_AgentProfileId",
                table: "RepresentationRequests",
                column: "AgentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRequests_AgentProfileId_ArtistProfileId",
                table: "RepresentationRequests",
                columns: new[] { "AgentProfileId", "ArtistProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRequests_ArtistProfileId",
                table: "RepresentationRequests",
                column: "ArtistProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RepresentationRequests_Status",
                table: "RepresentationRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepresentationRequests");
        }
    }
}
