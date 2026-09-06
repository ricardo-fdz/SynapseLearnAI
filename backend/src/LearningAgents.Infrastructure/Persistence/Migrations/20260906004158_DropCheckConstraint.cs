using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningAgents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MemoryEntries_Key",
                table: "MemoryEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_MemoryEntries_Key",
                table: "MemoryEntries",
                sql: "Key IN ('memoria_sesion', 'perfil_estudiante', 'mapa_dominio', 'lagunas_o_errores', 'historial_actividades')");
        }
    }
}
