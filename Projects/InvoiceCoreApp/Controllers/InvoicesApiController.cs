using DataLayer.Models;
using DataLayer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceCoreApp.Controllers
{
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("api/invoices")]
    public class InvoicesApiController(IInvoiceService service) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<List<Invoice>>> GetAll(CancellationToken cancellationToken)
            => Ok(await service.GetAllAsync(cancellationToken));

        private const int MaxPageSize = 200;

        [HttpGet("page")]
        public async Task<ActionResult<List<InvoiceSummaryDto>>> GetPage(int? afterId, string? supplier,
            DateTime? dueFrom, DateTime? dueTo, CancellationToken cancellationToken, int size = 50)
        {
            if (size is < 1 or > MaxPageSize) return BadRequest($"size must be between 1 and {MaxPageSize}.");
            return Ok(await service.GetPageAsync(afterId, size, supplier, dueFrom, dueTo, cancellationToken));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Invoice>> Get(int id, CancellationToken cancellationToken)
        {
            var invoice = await service.GetByIdAsync(id, cancellationToken);
            return invoice == null ? NotFound() : Ok(invoice);
        }

        [HttpPost]
        public async Task<ActionResult<Invoice>> Post([FromBody] Invoice invoice, CancellationToken cancellationToken)
        {
            try
            {
                await service.AddAsync(invoice, cancellationToken);
            }
            catch (MessagingUnavailableException exception)
            {
                return ChangesUnavailable(exception);
            }
            return CreatedAtAction(nameof(Get), new { id = invoice.Id }, invoice);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Put(int id, [FromBody] Invoice invoice, CancellationToken cancellationToken)
        {
            if (id != invoice.Id) return BadRequest();
            try
            {
                await service.UpdateAsync(invoice, cancellationToken);
            }
            catch (MessagingUnavailableException exception)
            {
                return ChangesUnavailable(exception);
            }
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            try
            {
                await service.DeleteAsync(id, cancellationToken);
            }
            catch (MessagingUnavailableException exception)
            {
                return ChangesUnavailable(exception);
            }
            return NoContent();
        }

        private static ObjectResult ChangesUnavailable(MessagingUnavailableException exception) => new(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Invoice changes unavailable",
            Detail = exception.Message
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
    }
}
