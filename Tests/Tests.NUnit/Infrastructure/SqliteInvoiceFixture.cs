using DataLayer;
using DataLayer.Models;
using DataLayer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Data.Common;

namespace Tests.NUnit.Infrastructure;

public sealed class SqliteInvoiceFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly ServiceProvider _services;
    public InvoiceDbContext Db { get; }
    public Mock<IMessagePublisherService> Publisher { get; } = new();
    public Mock<IMessageReadiness> Readiness { get; } = new();
    public MessageReadinessState State { get; set; } = MessageReadinessState.Ready;
    public SqLiteInvoiceService Service { get; }
    public CommandCounter Commands { get; } = new();

    public SqliteInvoiceFixture()
    {
        _connection.Open();
        Db = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>()
            .UseSqlite(_connection).AddInterceptors(Commands).Options);
        Readiness.SetupGet(r => r.State).Returns(() => State);
        Readiness.SetupGet(r => r.CanWriteInvoices).Returns(() => State == MessageReadinessState.Ready);
        _services = new ServiceCollection().AddSingleton(Db).AddSingleton(Publisher.Object)
            .AddSingleton(Readiness.Object).BuildServiceProvider();
        Service = ActivatorUtilities.CreateInstance<SqLiteInvoiceService>(_services);
    }

    public InvoiceDbContext CreateContext() => new(new DbContextOptionsBuilder<InvoiceDbContext>()
        .UseSqlite(_connection).Options);

    public async Task SeedAsync()
    {
        await using var db = CreateContext();
        db.Invoices.Add(CreateInvoice());
        await db.SaveChangesAsync();
    }

    public static Invoice CreateInvoice(string description = "original") => new()
    {
        Id = 1, Description = description, Supplier = "Supplier", DueDate = new DateTime(2026, 10, 12),
        InvoiceLines = [new InvoiceLine { Id = 1, Description = description, Price = 12.5, Quantity = 2 }]
    };

    public async Task<string> SnapshotAsync()
    {
        await using var db = CreateContext();
        var invoices = await db.Invoices.Include(i => i.InvoiceLines).OrderBy(i => i.Id).ToListAsync();
        var lines = await db.InvoiceLines.OrderBy(l => l.Id).ToListAsync();
        return System.Text.Json.JsonSerializer.Serialize(new { invoices, lines });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public sealed class CommandCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
