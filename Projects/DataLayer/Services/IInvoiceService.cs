using DataLayer.Models;

namespace DataLayer.Services
{
    public interface IInvoiceService
    {
        Task<List<Invoice>> GetAllAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Keyset page ordered by Id. Returns invoices with Id &gt; afterId; DueDate filter is dueFrom inclusive, dueTo exclusive.
        /// </summary>
        Task<List<InvoiceSummaryDto>> GetPageAsync(int? afterId, int size, string? supplier, DateTime? dueFrom,
            DateTime? dueTo, CancellationToken cancellationToken = default);
        Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
        Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
