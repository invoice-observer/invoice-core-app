using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading;

namespace InvoiceCoreApp.Controllers
{
    [ApiController]
    [Route("api/invoices")]
    public class InvoicesApiController(IInvoiceService service) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<List<Invoice>>> GetAll(CancellationToken cancellationToken)
            => Ok(await service.GetAllAsync(cancellationToken));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Invoice>> Get(int id, CancellationToken cancellationToken)
        {
            var invoice = await service.GetByIdAsync(id, cancellationToken);
            return invoice == null ? NotFound() : Ok(invoice);
        }

        [HttpPost]
        public async Task<ActionResult<Invoice>> Post([FromBody] Invoice invoice, CancellationToken cancellationToken)
        {
            await service.AddAsync(invoice, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Put(int id, [FromBody] Invoice invoice, CancellationToken cancellationToken)
        {
            if (id != invoice.Id) return BadRequest();
            await service.UpdateAsync(invoice, cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            await service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
