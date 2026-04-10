using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentProfiles",
                columns: table => new
                {
                    AgentProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AgencyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bio = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AverageRating = table.Column<double>(type: "double", nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    RosterCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpecialtiesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WebsiteUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentProfiles", x => x.AgentProfileId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApprovalHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TargetUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PerformedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalHistories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ArtistProfiles",
                columns: table => new
                {
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Specialty = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bio = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    City = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Country = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AverageRating = table.Column<double>(type: "double", nullable: false),
                    ReviewCount = table.Column<int>(type: "int", nullable: false),
                    BookingCount = table.Column<int>(type: "int", nullable: false),
                    AgencyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WebsiteUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpecialtiesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HourlyRate = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistProfiles", x => x.ArtistProfileId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EventType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    ActionUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    ReviewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    ReviewerUserId = table.Column<string>(type: "varchar(450)", maxLength: 450, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewerRole = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewerAvatarUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectEntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectEntityId = table.Column<int>(type: "int", nullable: false),
                    SubjectName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(3000)", maxLength: 3000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsVerifiedBooking = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.ReviewId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AgentRosterEntries",
                columns: table => new
                {
                    RosterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AgentProfileId = table.Column<int>(type: "int", nullable: false),
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRosterEntries", x => x.RosterId);
                    table.ForeignKey(
                        name: "FK_AgentRosterEntries_AgentProfiles_AgentProfileId",
                        column: x => x.AgentProfileId,
                        principalTable: "AgentProfiles",
                        principalColumn: "AgentProfileId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentRosterEntries_ArtistProfiles_ArtistProfileId",
                        column: x => x.ArtistProfileId,
                        principalTable: "ArtistProfiles",
                        principalColumn: "ArtistProfileId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AvailabilityBlocks",
                columns: table => new
                {
                    BlockId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    EndTime = table.Column<TimeOnly>(type: "time(6)", nullable: true),
                    IsAvailable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityBlocks", x => x.BlockId);
                    table.ForeignKey(
                        name: "FK_AvailabilityBlocks_ArtistProfiles_ArtistProfileId",
                        column: x => x.ArtistProfileId,
                        principalTable: "ArtistProfiles",
                        principalColumn: "ArtistProfileId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeaturedSlots",
                columns: table => new
                {
                    SlotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false),
                    SlotType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayPosition = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeaturedSlots", x => x.SlotId);
                    table.ForeignKey(
                        name: "FK_FeaturedSlots_ArtistProfiles_ArtistProfileId",
                        column: x => x.ArtistProfileId,
                        principalTable: "ArtistProfiles",
                        principalColumn: "ArtistProfileId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Streams",
                columns: table => new
                {
                    StreamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArtistProfileId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ThumbnailUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsLive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ViewerCount = table.Column<int>(type: "int", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TagsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.StreamId);
                    table.ForeignKey(
                        name: "FK_Streams_ArtistProfiles_ArtistProfileId",
                        column: x => x.ArtistProfileId,
                        principalTable: "ArtistProfiles",
                        principalColumn: "ArtistProfileId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AgentProfiles_Status",
                table: "AgentProfiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgentProfiles_UserId",
                table: "AgentProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRosterEntries_AgentProfileId",
                table: "AgentRosterEntries",
                column: "AgentProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRosterEntries_AgentProfileId_ArtistProfileId",
                table: "AgentRosterEntries",
                columns: new[] { "AgentProfileId", "ArtistProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRosterEntries_ArtistProfileId",
                table: "AgentRosterEntries",
                column: "ArtistProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProfiles_IsActive",
                table: "ArtistProfiles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProfiles_IsVerified",
                table: "ArtistProfiles",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProfiles_UserId",
                table: "ArtistProfiles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityBlocks_ArtistProfileId_Date",
                table: "AvailabilityBlocks",
                columns: new[] { "ArtistProfileId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedSlots_ArtistProfileId",
                table: "FeaturedSlots",
                column: "ArtistProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedSlots_DisplayPosition",
                table: "FeaturedSlots",
                column: "DisplayPosition");

            migrationBuilder.CreateIndex(
                name: "IX_FeaturedSlots_IsActive",
                table: "FeaturedSlots",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ReviewerUserId",
                table: "Reviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_Status",
                table: "Reviews",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_SubjectEntityType_SubjectEntityId",
                table: "Reviews",
                columns: new[] { "SubjectEntityType", "SubjectEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Streams_ArtistProfileId",
                table: "Streams",
                column: "ArtistProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_IsActive",
                table: "Streams",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Streams_IsLive",
                table: "Streams",
                column: "IsLive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRosterEntries");

            migrationBuilder.DropTable(
                name: "ApprovalHistories");

            migrationBuilder.DropTable(
                name: "AvailabilityBlocks");

            migrationBuilder.DropTable(
                name: "FeaturedSlots");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "Streams");

            migrationBuilder.DropTable(
                name: "AgentProfiles");

            migrationBuilder.DropTable(
                name: "ArtistProfiles");
        }
    }
}
