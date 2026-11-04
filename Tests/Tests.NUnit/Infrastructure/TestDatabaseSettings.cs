using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

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
}
