using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceCoreApp.Pages.Invoices
{
    public class IndexModel(IInvoiceService service) : PageModel
    {
        public List<Invoice> Invoices { get; set; } = [];

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            Invoices = await service.GetAllAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
        {
            try
            {
                await service.DeleteAsync(id, cancellationToken);
            }
            catch (MessagingUnavailableException exception)
            {
                Invoices = await service.GetAllAsync(cancellationToken);
                ModelState.AddModelError(string.Empty, exception.Message);
                return Page();
            }
            return RedirectToPage();
        }
    }
}
