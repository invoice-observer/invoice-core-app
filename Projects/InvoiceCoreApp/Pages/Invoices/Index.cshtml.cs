using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceCoreApp.Pages.Invoices
{
    public class IndexModel(IInvoiceService service) : PageModel
    {
        private const int PageSize = 50;

        public List<InvoiceSummaryDto> Invoices { get; set; } = [];
        public int? NextAfterId { get; private set; }

        [BindProperty(SupportsGet = true)] public int? AfterId { get; set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadPageAsync(cancellationToken);
        }

        // Reads one extra row to know whether a next page exists.
        private async Task LoadPageAsync(CancellationToken cancellationToken)
        {
            var rows = await service.GetPageAsync(AfterId, PageSize + 1, null, null, null, cancellationToken);
            if (rows.Count > PageSize)
            {
                rows.RemoveAt(PageSize);
                NextAfterId = rows[^1].Id;
            }
            Invoices = rows;
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
        {
            try
            {
                await service.DeleteAsync(id, cancellationToken);
            }
            catch (MessagingUnavailableException exception)
            {
                await LoadPageAsync(cancellationToken);
                ModelState.AddModelError(string.Empty, exception.Message);
                return Page();
            }
            return RedirectToPage(new { AfterId });
        }
    }
}
