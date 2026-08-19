using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSchoolAPI.Data;
using SmartSchoolAPI.Models;
using SmartSchoolAPI.Security;

namespace SmartSchoolAPI.Tests;

public sealed class IntegrationTestFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "integration-test-key-012345678901234567890123456789";
    public const string JwtIssuer = "SmartSchoolAPI";
    public const string JwtAudience = "SmartSchoolClients";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("SMARTSCHOOL_JWT_KEY", JwtKey);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.ConfigureServices(services =>
        {
            _connection.Open();
            services.RemoveAll<DbContextOptions<SchoolDbContext>>();
            services.RemoveAll<SchoolDbContext>();
            services.AddSingleton(_connection);
            services.AddDbContext<SchoolDbContext>(options => options.UseSqlite(_connection));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<SchoolDbContext>().Database.EnsureCreated();
        });
    }

    public void SeedUser(string username, string password, string role, int? userId = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        if (!db.Users.Any(u => u.Username == username))
        {
            var user = new User { Username = username, PasswordHash = PasswordService.Hash(password), Role = role };
            db.Users.Add(user);
            db.SaveChanges();
        }
    }

    public int SeedStudent(string username, string password, string firstName, string lastName)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchoolDbContext>();
        var user = new User { Username = username, PasswordHash = PasswordService.Hash(password), Role = SmartSchoolAPI.Authorization.RoleNames.Student };
        db.Users.Add(user);
        db.SaveChanges();
        var student = new Student { UserId = user.UserId, FirstName = firstName, LastName = lastName };
        db.Students.Add(student);
        db.SaveChanges();
        return user.UserId;
    }

    public List<AuditLog> AuditLogs()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SchoolDbContext>().AuditLogs.AsNoTracking().ToList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _connection.Dispose();
        base.Dispose(disposing);
    }
}
