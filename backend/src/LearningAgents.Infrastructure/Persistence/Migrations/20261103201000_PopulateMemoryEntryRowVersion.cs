using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearningAgents.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PopulateMemoryEntryRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"MemoryEntries\" SET \"RowVersion\" = randomblob(16) WHERE \"RowVersion\" IS NULL OR length(\"RowVersion\") = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
