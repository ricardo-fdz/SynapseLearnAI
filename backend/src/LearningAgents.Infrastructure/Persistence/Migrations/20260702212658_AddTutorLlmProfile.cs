using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningAgents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorLlmProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LlmProfile",
                table: "Tutors",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "gemini-default");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "ValueJson",
                value: "{\"temas\":[]}");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "ValueJson",
                value: "{\"activas\":[],\"resueltas\":[]}");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 5,
                column: "ValueJson",
                value: "{\"proyectos\":[]}");

            migrationBuilder.UpdateData(
                table: "Tutors",
                keyColumn: "Id",
                keyValue: 1,
                column: "LlmProfile",
                value: "gemini-default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LlmProfile",
                table: "Tutors");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 3,
                column: "ValueJson",
                value: "{}");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 4,
                column: "ValueJson",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 5,
                column: "ValueJson",
                value: "[]");
        }
    }
}
