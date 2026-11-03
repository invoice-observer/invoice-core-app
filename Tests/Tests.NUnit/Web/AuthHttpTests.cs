using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

namespace Tests.NUnit.Web;

[TestFixture]
public class AuthHttpTests
{
    private sealed class Factory(TestAuthSettings auth, string? blankKey = null, string? jwtKey = null)
        : WebApplicationFactory<InvoicesApiController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InvoiceService", "Mock");
            auth.Apply(builder);
            if (blankKey is not null) builder.UseSetting(blankKey, "");
            if (jwtKey is not null) builder.UseSetting("Jwt:Key", jwtKey);
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType == typeof(RmqStartupWorker)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IInvoiceService>();
                services.AddSingleton<IInvoiceService, MockInvoiceService>();
            });
        }

        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
    }

    [Test]
    public async Task ConfiguredCredentialsIssueTokenAcceptedByApi()
    {
        var auth = new TestAuthSettings();
        await using var factory = new Factory(auth);
        using var client = factory.Client();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = auth.UserName, password = auth.Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invoices = await client.GetAsync("/api/invoices");
        Assert.That(invoices.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task WrongCredentialsAreRejected(bool rightUser, bool rightPassword)
    {
        var auth = new TestAuthSettings();
        await using var factory = new Factory(auth);
        using var client = factory.Client();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = rightUser ? auth.UserName : "someone-else", password = rightPassword ? auth.Password : "wrong"
        });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("Auth:UserName")]
    [TestCase("Auth:Password")]
    [TestCase("Jwt:Key")]
    public void MissingSettingFailsStartup(string key)
    {
        using var factory = new Factory(new TestAuthSettings(), blankKey: key);
        var error = Assert.Throws<InvalidOperationException>(() => factory.Client());
        Assert.That(error!.Message, Does.Contain($"'{key}'"));
    }

    [Test]
    public void ShortJwtKeyFailsStartup()
    {
        using var factory = new Factory(new TestAuthSettings(), jwtKey: new string('k', 31));
        var error = Assert.Throws<InvalidOperationException>(() => factory.Client());
        Assert.That(error!.Message, Does.Contain("'Jwt:Key'").And.Contain("32 bytes"));
    }
}
