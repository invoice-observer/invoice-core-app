using System.Diagnostics;
using NUnit.Framework;

namespace Tests.NUnit.Web;

[TestFixture]
public class StatusPollingTests
{
    [Test]
    public async Task BrowserScriptWithControlledHttpAndTimers()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "InvoiceCoreApp.sln"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        using var process = Process.Start(new ProcessStartInfo("node")
        {
            ArgumentList = { "--test", "Tests/Tests.NUnit/Web/messaging-status.test.cjs" },
            WorkingDirectory = root!.FullName,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.That(process.ExitCode, Is.Zero, await output + await error);
    }
}
