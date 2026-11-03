using System.Text.RegularExpressions;
using DataLayer.Services;
using NUnit.Framework;

namespace Tests.NUnit.Web;

[TestFixture]
public class StatusRenderingTests
{
    [TestCase(MessageReadinessState.Connecting, false, "Connecting", "amber")]
    [TestCase(MessageReadinessState.Ready, true, "Ready", "green")]
    [TestCase(MessageReadinessState.Unavailable, false, "Unavailable", "red")]
    [TestCase(null, true, "not required", "neutral")]
    public async Task InitialStatusAndAllChangeControlsAgree(MessageReadinessState? state, bool enabled, string text, string tone)
    {
        await using var factory = new StatusHttpTests.Factory(state);
        using var client = await factory.Client();
        foreach (var path in new[] { "/Invoices", "/Invoices/Create" })
        {
            var html = await client.GetStringAsync(path);
            Assert.That(html, Does.Contain("id=\"messaging-status\""));
            Assert.That(html, Does.Contain($"status-{tone}"));
            Assert.That(html, Does.Contain($"RabbitMQ {text}"));
            Assert.That(html, Does.Contain("/js/messaging-status.js"));
            var controls = Regex.Matches(html, "<(?:a|button)\\b[^>]*data-invoice-write[^>]*>").Select(m => m.Value).ToArray();
            Assert.That(controls.Length, Is.GreaterThanOrEqualTo(path.EndsWith("Create") ? 3 : 2));
            foreach (var control in controls)
            {
                if (control.StartsWith("<button"))
                    Assert.That(Regex.IsMatch(control, "\\sdisabled(?:=|\\s|>)"), Is.EqualTo(!enabled));
                else
                {
                    Assert.That(Regex.IsMatch(control, "\\shref="), Is.EqualTo(enabled));
                    Assert.That(control, Does.Contain("data-write-href=\"/Invoices/Create\""));
                    if (!enabled)
                    {
                        Assert.That(control, Does.Contain("aria-disabled=\"true\""));
                        Assert.That(control, Does.Contain("tabindex=\"-1\""));
                    }
                }
            }
            Assert.That(html, Does.Match(path.EndsWith("Create")
                ? "<a[^>]*href=\"/Invoices\"[^>]*>Cancel</a>"
                : "<a[^>]*href=\"/Invoices/Details[^\"]*\"[^>]*>Details</a>"));
        }
        if (state.HasValue) factory.Readiness.VerifyGet(r => r.State, Moq.Times.Exactly(2));
    }

    [Test]
    public async Task AnonymousPageHasNoStatusOrPolling()
    {
        await using var factory = new StatusHttpTests.Factory(null);
        using var client = await factory.Client(null);
        var html = await client.GetStringAsync("/Login");
        Assert.That(html, Does.Not.Contain("id=\"messaging-status\""));
        Assert.That(html, Does.Not.Contain("/js/messaging-status.js"));
    }
}
