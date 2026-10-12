using DataLayer.Models;
using DataLayer.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using Tests.NUnit.Infrastructure;

namespace Tests.NUnit.Services;

[TestFixture]
public class InvoiceServiceReadinessTests
{
    private static Task WriteAsync(SqliteInvoiceFixture fixture, string operation, CancellationToken token = default) =>
        operation switch
        {
            "added" => fixture.Service.AddAsync(new Invoice
            {
                Description = "added", InvoiceLines = [new InvoiceLine { Description = "new line" }]
            }, token),
            "updated" => fixture.Service.UpdateAsync(SqliteInvoiceFixture.CreateInvoice("updated"), token),
            "deleted" => fixture.Service.DeleteAsync(1, token),
            _ => fixture.Service.DeleteAsync(999, token)
        };

    [Test]
    public async Task NonReadyWriteLeavesDatabaseAndTrackerUntouched(
        [Values("added", "updated", "deleted", "missing")] string operation,
        [Values(MessageReadinessState.Connecting, MessageReadinessState.Unavailable)] MessageReadinessState state)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        fixture.State = state;
        var before = await fixture.SnapshotAsync();
        var reads = fixture.Commands.Reads;

        await Assert.ThrowsAsync<MessagingUnavailableException>(() => WriteAsync(fixture, operation));

        Assert.That(fixture.Commands.Reads, Is.EqualTo(reads), "Rejection must precede queries");
        Assert.That(fixture.Db.ChangeTracker.Entries(), Is.Empty);
        Assert.That(await fixture.SnapshotAsync(), Is.EqualTo(before));
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReadsRemainAvailable(
        [Values(MessageReadinessState.Connecting, MessageReadinessState.Unavailable)] MessageReadinessState state)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        fixture.State = state;
        Assert.That(await fixture.Service.GetAllAsync(), Has.Count.EqualTo(1));
        var invoice = await fixture.Service.GetByIdAsync(1);
        Assert.That(invoice?.InvoiceLines, Has.Count.EqualTo(1));
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReadyWritePersistsAndPublishesOnce([Values("added", "updated", "deleted")] string operation)
    {
        await using var fixture = new SqliteInvoiceFixture();
        if (operation != "added") await fixture.SeedAsync();
        using var cancellation = new CancellationTokenSource();
        object? payload = null;
        fixture.Publisher.Setup(p => p.PublishInvoiceEventAsync(operation, It.IsAny<object>(), cancellation.Token))
            .Callback<string, object, CancellationToken>((_, data, _) => payload = data).Returns(Task.CompletedTask);

        await WriteAsync(fixture, operation, cancellation.Token);

        await using var verification = fixture.CreateContext();
        var invoices = await verification.Invoices.Include(i => i.InvoiceLines).ToListAsync();
        if (operation == "deleted")
        {
            Assert.That(invoices, Is.Empty);
            Assert.That(await verification.InvoiceLines.CountAsync(), Is.Zero);
            Assert.That(payload?.GetType().GetProperty("Id")?.GetValue(payload), Is.EqualTo(1));
        }
        else
        {
            Assert.That(invoices, Has.Count.EqualTo(1));
            Assert.That(invoices[0].Description, Is.EqualTo(operation));
            Assert.That(invoices[0].InvoiceLines.Single().Description,
                Is.EqualTo(operation == "added" ? "new line" : "updated"));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(payload),
                Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(invoices[0])));
        }
        fixture.Publisher.Verify(p => p.PublishInvoiceEventAsync(operation, It.IsAny<object>(), cancellation.Token), Times.Once);
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SameServiceChecksReadinessOnEveryWrite([Values("added", "updated", "deleted")] string operation)
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        fixture.State = MessageReadinessState.Unavailable;
        await Assert.ThrowsAsync<MessagingUnavailableException>(() => WriteAsync(fixture, operation));
        fixture.Publisher.VerifyNoOtherCalls();
        fixture.State = MessageReadinessState.Ready;
        await WriteAsync(fixture, operation);
        fixture.Publisher.Verify(p => p.PublishInvoiceEventAsync(operation, It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.State = MessageReadinessState.Unavailable;
        await Assert.ThrowsAsync<MessagingUnavailableException>(() => WriteAsync(fixture, operation));
        fixture.Publisher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ReadyMissingDeletionIsNoOp()
    {
        await using var fixture = new SqliteInvoiceFixture();
        await fixture.SeedAsync();
        var before = await fixture.SnapshotAsync();
        await fixture.Service.DeleteAsync(999);
        Assert.That(await fixture.SnapshotAsync(), Is.EqualTo(before));
        Assert.That(fixture.Db.ChangeTracker.Entries(), Is.Empty);
        fixture.Publisher.VerifyNoOtherCalls();
    }
}
