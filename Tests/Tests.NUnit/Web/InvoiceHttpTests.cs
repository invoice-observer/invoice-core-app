using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

namespace Tests.NUnit.Web;

[TestFixture]
public class InvoiceHttpTests
{
    private const string Detail = "Invoice changes are unavailable while messaging is not ready.";

    private sealed class Factory(SqliteInvoiceFixture fixture) : WebApplicationFactory<InvoicesApiController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InvoiceService", "Mock");
            builder.ConfigureTestServices(services =>
            {
                // Never start messaging or resolve the application's database in HTTP tests.
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType == typeof(RmqStartupWorker)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IInvoiceService>();
                services.AddSingleton<IInvoiceService>(fixture.Service);
                services.AddTransient<TestAuthentication>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.SchemeMap["Bearer"].HandlerType = typeof(TestAuthentication);
                    options.SchemeMap["Cookies"].HandlerType = typeof(TestAuthentication);
                });
            });
        }

        public HttpClient Client(bool authenticated = true)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            if (authenticated) client.DefaultRequestHeaders.Add("X-Test-User", "invoice-test");
            return client;
        }
    }

    public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers.ContainsKey("X-Test-User")
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                    new ClaimsIdentity([new Claim(ClaimTypes.Name, "invoice-test")], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }

    [TestCase("POST")][TestCase("PUT")][TestCase("DELETE")]
    public async Task DirectApiWriteSerializes503AndLeavesDatabaseUntouched(string method)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        fixture.State = MessageReadinessState.Unavailable;
        var before = await fixture.SnapshotAsync();
        await using var factory = new Factory(fixture);
        using var client = factory.Client();
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/invoices" : "/api/invoices/1")
        {
            Content = method == "DELETE" ? null : JsonContent.Create(SqliteInvoiceFixture.CreateInvoice("changed"))
        };
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("status").GetInt32(), Is.EqualTo(503));
        Assert.That(json.RootElement.GetProperty("title").GetString(), Is.EqualTo("Invoice changes unavailable"));
        Assert.That(json.RootElement.GetProperty("detail").GetString(), Is.EqualTo(Detail));
        Assert.That(await fixture.SnapshotAsync(), Is.EqualTo(before));
        fixture.Publisher.VerifyNoOtherCalls();
    }

    private static async Task<string> AntiforgeryToken(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.That(match.Success, Is.True, "Get the real token; the client retains its matching cookie.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [TestCase(true)][TestCase(false)]
    public async Task DirectRazorPostRendersRejectionWithValidAntiforgery(bool save)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        fixture.State = MessageReadinessState.Unavailable;
        var before = await fixture.SnapshotAsync();
        await using var factory = new Factory(fixture);
        using var client = factory.Client();
        var path = save ? "/Invoices/Create" : "/Invoices";
        var token = await AntiforgeryToken(client, path);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Invoice.Description"] = "Submitted invoice", ["Invoice.Supplier"] = "Submitted supplier",
            ["Invoice.DueDate"] = "2030-04-05",
            ["Invoice.InvoiceLines[0].Description"] = "Submitted first line",
            ["Invoice.InvoiceLines[0].Price"] = "12.50", ["Invoice.InvoiceLines[0].Quantity"] = "3",
            ["Invoice.InvoiceLines[1].Description"] = "Submitted second line",
            ["Invoice.InvoiceLines[1].Price"] = "4.00", ["Invoice.InvoiceLines[1].Quantity"] = "2"
        };
        using var response = await client.PostAsync(path + (save ? "?handler=Save" : "?handler=Delete&id=1"), new FormUrlEncodedContent(fields));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(html, Does.Contain("validation-summary-errors"));
        Assert.That(html, Does.Contain(Detail));
        if (save)
        {
            foreach (var field in fields.Where(f => f.Key.StartsWith("Invoice.")))
            {
                var input = Regex.Matches(html, "<input[^>]*>").Select(m => WebUtility.HtmlDecode(m.Value))
                    .Single(tag => tag.Contains($"name=\"{field.Key}\""));
                Assert.That(input, Does.Contain($"value=\"{field.Value}\""), field.Key);
            }
        }
        else Assert.That(html, Does.Contain("original"));
        Assert.That(await fixture.SnapshotAsync(), Is.EqualTo(before));
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ApiValidationStillRejectsInvalidInput()
    {
        await using var fixture = new SqliteInvoiceFixture();
        fixture.State = MessageReadinessState.Unavailable;
        await using var factory = new Factory(fixture);
        using var client = factory.Client();
        using var response = await client.PostAsJsonAsync("/api/invoices", new
        {
            Description = new string('x', 201), Supplier = "Supplier", DueDate = "2030-04-05"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(json.RootElement.GetProperty("errors").TryGetProperty("Description", out _), Is.True);
        Assert.That(await fixture.Service.GetAllAsync(), Is.Empty);
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AuthenticationAndAntiforgeryRemainRequired()
    {
        await using var fixture = new SqliteInvoiceFixture();
        await using var factory = new Factory(fixture);
        using var anonymous = factory.Client(false);
        using var unauthorized = await anonymous.PostAsJsonAsync("/api/invoices", SqliteInvoiceFixture.CreateInvoice());
        Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        using var client = factory.Client();
        using var noToken = await client.PostAsync("/Invoices/Create?handler=Save", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Invoice.Description"] = "must not save" }));
        Assert.That(noToken.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await fixture.Service.GetAllAsync(), Is.Empty);
        fixture.Publisher.VerifyNoOtherCalls();
    }
}
