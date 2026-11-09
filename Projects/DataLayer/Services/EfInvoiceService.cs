using DataLayer.Models;
using Microsoft.EntityFrameworkCore;

namespace DataLayer.Services
{
    public class EfInvoiceService : IInvoiceService
    {
        private readonly InvoiceDbContext _db;
        private readonly IMessagePublisherService _messagePublisher;
        private readonly IMessageReadiness _messageReadiness;

        public EfInvoiceService(InvoiceDbContext db, IMessagePublisherService messagePublisher,
            IMessageReadiness messageReadiness)
        {
            _db = db;
            _messagePublisher = messagePublisher;
            _messageReadiness = messageReadiness;
        }

        public async Task<List<Invoice>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Invoices.Include(i => i.InvoiceLines).ToListAsync(cancellationToken);
        }

        public async Task<List<InvoiceSummaryDto>> GetPageAsync(int? afterId, int size, string? supplier,
            DateTime? dueFrom, DateTime? dueTo, CancellationToken cancellationToken = default)
        {
            var query = _db.Invoices.AsNoTracking();
            if (afterId is { } after) query = query.Where(i => i.Id > after);
            if (!string.IsNullOrEmpty(supplier)) query = query.Where(i => i.Supplier == supplier);
            if (dueFrom is { } from) query = query.Where(i => i.DueDate >= from);
            if (dueTo is { } to) query = query.Where(i => i.DueDate < to);
            return await query.OrderBy(i => i.Id).Take(size)
                .Select(i => new InvoiceSummaryDto(i.Id, i.Supplier, i.DueDate, i.Description,
                    i.InvoiceLines.Sum(l => l.Price * l.Quantity)))
                .ToListAsync(cancellationToken);
        }

        public async Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _db.Invoices.Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        }

        public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            EnsureMessagingReady();
            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(cancellationToken);
            
            await _messagePublisher.PublishInvoiceEventAsync("added", invoice, cancellationToken);
        }

        public async Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default)
        {
            EnsureMessagingReady();
            _db.Invoices.Update(invoice);
            await _db.SaveChangesAsync(cancellationToken);
            
            await _messagePublisher.PublishInvoiceEventAsync("updated", invoice, cancellationToken);
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            EnsureMessagingReady();
            var invoice = await _db.Invoices.Include(i => i.InvoiceLines)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (invoice != null)
            {
                _db.InvoiceLines.RemoveRange(invoice.InvoiceLines);
                _db.Invoices.Remove(invoice);
                await _db.SaveChangesAsync(cancellationToken);
                
                await _messagePublisher.PublishInvoiceEventAsync("deleted", new { Id = id }, cancellationToken);
            }
        }

        private void EnsureMessagingReady()
        {
            if (!_messageReadiness.CanWriteInvoices)
                throw new MessagingUnavailableException();
        }
    }
}
