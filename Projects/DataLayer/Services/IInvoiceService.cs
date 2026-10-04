using DataLayer.Models;

namespace DataLayer.Services
{
    public interface IInvoiceService
    {
        Task<List<Invoice>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<Invoice?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
        Task UpdateAsync(Invoice invoice, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
