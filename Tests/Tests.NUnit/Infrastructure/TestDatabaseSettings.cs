using System.Security.Cryptography;
using DataLayer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests.NUnit.Infrastructure;

// Valid SqlServer settings for test hosts; nothing connects. Random password: none is committed.
// UseSetting, not ConfigureAppConfiguration: Program.cs reads these values before Build().
public static class TestDatabaseSettings
{
    public const string ConnectionStringKey = "ConnectionStrings:InvoiceDb";
    public const string PasswordKey = "Database:Password";

    public static void Apply(IWebHostBuilder builder)
    {
        builder.UseSetting(ConnectionStringKey, "Server=localhost,1433;Database=InvoiceDb;User Id=sa;TrustServerCertificate=True");
        builder.UseSetting(PasswordKey, Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
    }

    // Program.cs runs EnsureCreated at startup; keep it off the unreachable SQL Server.
    public static void UseInMemoryDatabase(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<InvoiceDbContext>>();
        // EF keeps the provider choice in IDbContextOptionsConfiguration<InvoiceDbContext>, which is not public API.
        foreach (var descriptor in services.Where(d => d.ServiceType.IsGenericType
                     && d.ServiceType.GenericTypeArguments.SequenceEqual([typeof(InvoiceDbContext)])
                     && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")).ToArray())
            services.Remove(descriptor);
        services.AddDbContext<InvoiceDbContext>(options => options.UseInMemoryDatabase("DB-" + Guid.NewGuid()));
    }
}
