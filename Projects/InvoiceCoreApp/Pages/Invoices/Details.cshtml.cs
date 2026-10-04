using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceCoreApp.Pages.Invoices
{
    public class DetailsModel(IInvoiceService service) : PageModel
    {
        public Invoice? Invoice { get; set; }

        public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
        {
            Invoice = await service.GetByIdAsync(id, cancellationToken);
            if (Invoice == null)
                return NotFound();
            return Page();
        }
    }
}