using DotNetEnv;
using GeekBackend.Data.Data;
using GeekBackend.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL environment variable is not set. Add it to .env locally or as a Railway variable in production.");

// Railway blocks IPv6 egress. Resolve the pooler hostname to its IPv4 address so
// Npgsql opens an IPv4 socket instead of trying IPv6 first and failing.
if (connectionString.Contains("Host="))
{
    var parts = connectionString.Split(';');
    var hostIdx = Array.FindIndex(parts, p => p.TrimStart().StartsWith("Host=", StringComparison.OrdinalIgnoreCase));
    if (hostIdx >= 0)
    {
        var hostname = parts[hostIdx].Split('=', 2)[1].Trim();
        var ipv4 = System.Net.Dns.GetHostAddresses(hostname, System.Net.Sockets.AddressFamily.InterNetwork).FirstOrDefault();
        if (ipv4 != null)
        {
            parts[hostIdx] = $"Host={ipv4}";
            connectionString = string.Join(';', parts);
            Console.WriteLine($"[INFO] Resolved {hostname} → {ipv4} (IPv4 forced for Railway)");
        }
    }
}

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
builder.Services.AddScoped<IUseCaseRepository, UseCaseRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors();
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
