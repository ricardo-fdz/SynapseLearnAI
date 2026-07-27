using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LearningAgents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Tutors",
                columns: new[] { "Id", "CreatedAtUtc", "Description", "GeminiModel", "Name", "SystemPromptContent", "UpdatedAtUtc" },
                values: new object[] { 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "Tutor de ejemplo para aprendizaje guiado de programacion.", "gemini-2.5-flash", "Programming Tutor", "Actua como un tutor de programacion socratico y enfocado en aprendizaje real.", new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "MemoryEntries",
                columns: new[] { "Id", "CreatedAtUtc", "Key", "SchemaVersion", "TutorId", "UpdatedAtUtc", "ValueJson" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "memoria_sesion", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "{}" },
                    { 2, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "perfil_estudiante", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "{}" },
                    { 3, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "mapa_dominio", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "{}" },
                    { 4, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "lagunas_o_errores", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "[]" },
                    { 5, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "historial_actividades", 1, 1, new DateTime(2026, 6, 24, 0, 0, 0, 0, DateTimeKind.Utc), "[]" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "MemoryEntries",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Tutors",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
