using System.Security.Cryptography;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

namespace Tests.NUnit.Web;

[TestFixture]
public class InvoiceServiceStartupTests
{
    private const string ConnectionStringKey = "ConnectionStrings:InvoiceDb";
    private const string PasswordKey = "Database:Password";

    private sealed class Factory(string mode, string? blankKey = null) : WebApplicationFactory<InvoicesApiController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InvoiceService", mode);
            new TestAuthSettings().Apply(builder);
            // Both values are valid by default, so a failure can only come from the blanked key.
            builder.UseSetting(ConnectionStringKey, "Server=localhost,1433;Database=InvoiceDb;User Id=sa;TrustServerCertificate=True");
            builder.UseSetting(PasswordKey, Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
            if (blankKey is not null) builder.UseSetting(blankKey, "");
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType == typeof(RmqStartupWorker)).ToArray())
                    services.Remove(descriptor);
            });
        }
    }

    [TestCase(ConnectionStringKey)]
    [TestCase(PasswordKey)]
    public void SqlServer_MissingSettingFailsStartup(string key)
    {
        using var factory = new Factory("SqlServer", blankKey: key);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.That(error!.Message, Does.Contain($"'{key}'"));
    }

    [Test]
    public void SQLite_FailsStartup()
    {
        using var factory = new Factory("SQLite");
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.That(error!.Message, Does.Contain("'InvoiceService'"));
    }
}
