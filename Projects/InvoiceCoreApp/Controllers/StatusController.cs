using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceCoreApp.Controllers;

[ApiController]
[Route("api/status")]
[Authorize(AuthenticationSchemes = "InvoiceStatus")]
public sealed class StatusController(InvoiceWriteStatus status) : ControllerBase
{
    [HttpGet]
    public ActionResult<InvoiceWriteSnapshot> Get()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(status.ReadSnapshot());
    }
}
