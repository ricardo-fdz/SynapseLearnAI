using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningAgents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoadmapData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MemoryEntries",
                columns: new[] { "Id", "CreatedAtUtc", "Key", "SchemaVersion", "TutorId", "UpdatedAtUtc", "ValueJson" },
                values: new object[] { 100, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "roadmap", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "{\"roadmaps\":[]}" });

            migrationBuilder.Sql(
                "INSERT INTO \"MemoryEntries\" (\"Key\", \"ValueJson\", \"SchemaVersion\", \"TutorId\", \"CreatedAtUtc\", \"UpdatedAtUtc\") " +
                "SELECT 'roadmap', '{\"roadmaps\":[]}', 1, \"Id\", datetime('now'), datetime('now') FROM \"Tutors\" " +
                "WHERE \"Id\" NOT IN (SELECT \"TutorId\" FROM \"MemoryEntries\" WHERE \"Key\" = 'roadmap')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"MemoryEntries\" WHERE \"Key\" = 'roadmap'");
        }
    }
}
