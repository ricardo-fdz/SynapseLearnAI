using LearningAgents.Application;
using LearningAgents.Infrastructure;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
const string AngularCorsPolicy = "AngularLocalhost";

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=learning-agents.db";

builder.Services.AddDbContext<LearningAgentsDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularCorsPolicy, policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200", "http://localhost", "http://localhost:80"];
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
app.UseCors(AngularCorsPolicy);
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.Run();
