using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using RabbitMQ.Client;

namespace Tests.NUnit.Web;

[TestFixture]
public class RmqRecoveryConfigurationTests
{
    private sealed class Factory : WebApplicationFactory<InvoicesApiController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InvoiceService", "SQLite");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RabbitMQ:ConnectionString"] = "amqp://localhost",
                    ["RabbitMQ:UserName"] = "",
                    ["RabbitMQ:UserPassword"] = ""
                }));
            builder.ConfigureTestServices(services =>
            {
                // Inspect the real factory registration without connecting or opening a database.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType == typeof(RmqStartupWorker)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IInvoiceService>();
                services.AddSingleton<IInvoiceService, MockInvoiceService>();
            });
        }
    }

    [Test]
    public async Task ConnectionFactory_DisablesAutomaticRecovery()
    {
        await using var application = new Factory();
        var factory = application.Services.GetRequiredService<IConnectionFactory>();
        Assert.That(factory, Is.TypeOf<ConnectionFactory>());
        Assert.That(((ConnectionFactory)factory).AutomaticRecoveryEnabled, Is.False);
    }
}
