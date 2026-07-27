using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LearningAgents.Infrastructure.Persistence;

public sealed class LearningAgentsDbContextFactory : IDesignTimeDbContextFactory<LearningAgentsDbContext>
{
    public LearningAgentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LearningAgentsDbContext>();
        optionsBuilder.UseSqlite("Data Source=learning-agents.db");

        return new LearningAgentsDbContext(optionsBuilder.Options);
    }
}
