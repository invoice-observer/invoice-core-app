using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceCoreApp.Pages.Invoices
{
    public class CreateModel(IInvoiceService service) : PageModel
    {
        [BindProperty] public Invoice Invoice { get; set; } = new() { InvoiceLines = [new InvoiceLine()] };

        public void OnGet()
        {
            var formatted = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");
            Invoice = new Invoice
            {
                Description = $"Description-{formatted}",
                DueDate = DateTime.Now.Date + TimeSpan.FromDays(4),
                Supplier = $"Supplier-{formatted}",
                InvoiceLines = [new InvoiceLine{Description = $"Line-{formatted}", Quantity = 1}]
            };
        }
        public IActionResult OnPostAddLine()
        {
            var formatted = DateTime.Now.ToString("yyyyMMdd-HHmmss.fff");
            Invoice.InvoiceLines.Add(new InvoiceLine{Description = $"Line-{formatted}", Quantity = 1});
            ModelState.Clear();
            return Page();
        }

        public IActionResult OnPostRemoveLine(int index)
        {
            if (Invoice.InvoiceLines.Count > index)
                Invoice.InvoiceLines.RemoveAt(index);
            if (Invoice.InvoiceLines.Count == 0)
                Invoice.InvoiceLines.Add(new InvoiceLine());
            ModelState.Clear();
            return Page();
        }

        public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
                return Page();
            await service.AddAsync(Invoice, cancellationToken);
            return RedirectToPage("Index");
        }
    }
}