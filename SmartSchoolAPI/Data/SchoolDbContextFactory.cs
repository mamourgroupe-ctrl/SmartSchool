using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartSchoolAPI.Data;

/// <summary>
/// Enables migration generation without starting the web host or reading runtime secrets.
/// </summary>
public sealed class SchoolDbContextFactory : IDesignTimeDbContextFactory<SchoolDbContext>
{
    public SchoolDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchoolDbContext>()
            .UseSqlite("Data Source=smartschool.db")
            .Options;

        return new SchoolDbContext(options);
    }
}
