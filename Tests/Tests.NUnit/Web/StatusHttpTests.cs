using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;

namespace Tests.NUnit.Web;

[TestFixture]
public class StatusHttpTests
{
    internal sealed class Factory(MessageReadinessState? state) : WebApplicationFactory<InvoicesApiController>
    {
        public Mock<IMessageReadiness> Readiness { get; } = new(MockBehavior.Strict);
        public Mock<IConnectionFactory> Connections { get; } = new(MockBehavior.Strict);
        public Mock<IMessagePublisherService> Publisher { get; } = new(MockBehavior.Strict);
        private readonly SymmetricSecurityKey _testKey = new(RandomNumberGenerator.GetBytes(32));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("InvoiceService", state.HasValue ? "SQLite" : "Mock");
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>("Bearer", options =>
                    options.TokenValidationParameters.IssuerSigningKey = _testKey);
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType == typeof(RmqStartupWorker)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IInvoiceService>();
                services.AddSingleton<IInvoiceService, MockInvoiceService>();
                if (state.HasValue)
                {
                    Readiness.SetupGet(r => r.State).Returns(state.Value);
                    services.RemoveAll<IMessageReadiness>();
                    services.AddSingleton(Readiness.Object);
                    services.RemoveAll<IConnectionFactory>();
                    services.AddSingleton(Connections.Object);
                    services.RemoveAll<IMessagePublisherService>();
                    services.AddSingleton(Publisher.Object);
                }
            });
        }

        public Task<HttpClient> Client(string? authentication = "Cookies")
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            if (authentication == "Cookies")
            {
                var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
                var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "status-test")], "Cookies")), new AuthenticationProperties(), "Cookies");
                client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
            }
            else if (authentication == "Bearer")
            {
                var token = new JwtSecurityToken(claims: [new Claim(ClaimTypes.Name, "status-test")],
                    expires: DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: new SigningCredentials(_testKey, SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            return Task.FromResult(client);
        }
    }

    [TestCase(MessageReadinessState.Connecting, false, "Connecting")]
    [TestCase(MessageReadinessState.Ready, true, "Ready")]
    [TestCase(MessageReadinessState.Unavailable, false, "Unavailable")]
    [TestCase(null, true, "NotRequired")]
    public async Task StatusUsesOneSnapshotWithoutMessagingWork(MessageReadinessState? state, bool canWrite, string rabbitMq)
    {
        await using var factory = new Factory(state);
        using var client = await factory.Client();
        using var response = await client.GetAsync("/api/status");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(json.EnumerateObject().Count(), Is.EqualTo(2));
        Assert.That(json.GetProperty("canWriteInvoices").GetBoolean(), Is.EqualTo(canWrite));
        Assert.That(json.GetProperty("rabbitMq").GetString(), Is.EqualTo(rabbitMq));
        if (state.HasValue) factory.Readiness.VerifyGet(r => r.State, Times.Once);
        else
        {
            Assert.That(factory.Services.GetService<IMessageReadiness>(), Is.Null);
            Assert.That(factory.Services.GetService<IMessagePublisherService>(), Is.Null);
            Assert.That(factory.Services.GetServices<IHostedService>().OfType<RmqStartupWorker>(), Is.Empty);
        }
        factory.Readiness.VerifyNoOtherCalls();
        factory.Connections.VerifyNoOtherCalls();
        factory.Publisher.VerifyNoOtherCalls();
    }

    [TestCase("Cookies", HttpStatusCode.OK)]
    [TestCase("Bearer", HttpStatusCode.OK)]
    [TestCase(null, HttpStatusCode.Unauthorized)]
    public async Task StatusAcceptsRealAuthenticationWithoutLoginRedirect(string? scheme, HttpStatusCode expected)
    {
        await using var factory = new Factory(null);
        using var client = await factory.Client(scheme);
        using var response = await client.GetAsync("/api/status");
        Assert.That(response.StatusCode, Is.EqualTo(expected));
        Assert.That(response.Headers.Location, Is.Null);
        if (scheme is null) Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("<html"));
        if (scheme == "Cookies")
        {
            using var existingApi = await client.GetAsync("/api/invoices");
            Assert.That(existingApi.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
    }
}
