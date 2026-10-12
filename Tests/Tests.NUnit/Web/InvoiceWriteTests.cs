using DataLayer.Models;
using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Pages.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Moq;
using NUnit.Framework;

namespace Tests.NUnit.Web;

[TestFixture]
public class InvoiceWriteTests
{
    private const string Detail = "Invoice changes are unavailable while messaging is not ready.";
    private readonly Invoice invoice = new()
    {
        Id = 7, Description = "Submitted", Supplier = "Supplier", DueDate = new DateTime(2030, 4, 5),
        InvoiceLines = [new() { Id = 8, Description = "First", Price = 12.5, Quantity = 3 },
            new() { Id = 9, Description = "Second", Price = 4, Quantity = 2 }]
    };

    private Mock<IInvoiceService> Service(string operation, CancellationToken token, Exception? error = null)
    {
        var service = new Mock<IInvoiceService>(MockBehavior.Strict);
        var setup = operation switch
        {
            "POST" => service.Setup(s => s.AddAsync(invoice, token)),
            "PUT" => service.Setup(s => s.UpdateAsync(invoice, token)),
            _ => service.Setup(s => s.DeleteAsync(invoice.Id, token))
        };
        if (error == null) setup.Returns(Task.CompletedTask);
        else setup.ThrowsAsync(error);
        return service;
    }

    private async Task<IActionResult> Write(InvoicesApiController controller, string operation, CancellationToken token) =>
        operation switch
        {
            "POST" => (await controller.Post(invoice, token)).Result!,
            "PUT" => await controller.Put(invoice.Id, invoice, token),
            _ => await controller.Delete(invoice.Id, token)
        };

    [TestCase("POST")][TestCase("PUT")][TestCase("DELETE")]
    public async Task RestRejection(string operation)
    {
        using var cts = new CancellationTokenSource();
        var service = Service(operation, cts.Token, new MessagingUnavailableException());
        var result = await Write(new(service.Object), operation, cts.Token);
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(503));
        Assert.That(response.Value, Is.InstanceOf<ProblemDetails>());
        var problem = (ProblemDetails)response.Value!;
        Assert.That(problem.Status, Is.EqualTo(503));
        Assert.That(problem.Title, Is.EqualTo("Invoice changes unavailable"));
        Assert.That(problem.Detail, Is.EqualTo(Detail));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [TestCase("POST", 201)][TestCase("PUT", 204)][TestCase("DELETE", 204)]
    public async Task RestSuccess(string operation, int status)
    {
        using var cts = new CancellationTokenSource();
        var service = Service(operation, cts.Token);
        var result = await Write(new(service.Object), operation, cts.Token);
        Assert.That(result is ObjectResult obj ? obj.StatusCode : ((StatusCodeResult)result).StatusCode, Is.EqualTo(status));
        if (result is CreatedAtActionResult created)
        {
            Assert.That(created.Value, Is.SameAs(invoice));
            Assert.That(created.ActionName, Is.EqualTo("Get"));
            Assert.That(created.RouteValues!["id"], Is.EqualTo(invoice.Id));
        }
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task MismatchedPutDoesNotCallService()
    {
        var service = new Mock<IInvoiceService>(MockBehavior.Strict);
        Assert.That(await new InvoicesApiController(service.Object).Put(99, invoice, default), Is.TypeOf<BadRequestResult>());
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetsRemainUnchanged()
    {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IInvoiceService>(MockBehavior.Strict);
        List<Invoice> list = [invoice];
        service.Setup(s => s.GetAllAsync(cts.Token)).ReturnsAsync(list);
        service.Setup(s => s.GetByIdAsync(7, cts.Token)).ReturnsAsync(invoice);
        service.Setup(s => s.GetByIdAsync(99, cts.Token)).ReturnsAsync((Invoice?)null);
        var controller = new InvoicesApiController(service.Object);
        Assert.That(((OkObjectResult)(await controller.GetAll(cts.Token)).Result!).Value, Is.SameAs(list));
        Assert.That(((OkObjectResult)(await controller.Get(7, cts.Token)).Result!).Value, Is.SameAs(invoice));
        Assert.That((await controller.Get(99, cts.Token)).Result, Is.TypeOf<NotFoundResult>());
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task OtherErrorsPropagate([Values("POST", "PUT", "DELETE", "Save", "Delete")] string operation,
        [Values(false, true)] bool cancelled)
    {
        using var cts = new CancellationTokenSource();
        if (cancelled) cts.Cancel();
        Exception error = cancelled ? new OperationCanceledException(cts.Token) : new InvalidOperationException("publish failed");
        var service = Service(operation == "Save" ? "POST" : operation, cts.Token, error);
        async Task Invoke()
        {
            if (operation == "Save") await new CreateModel(service.Object) { Invoice = invoice }.OnPostSaveAsync(cts.Token);
            else if (operation == "Delete") await new IndexModel(service.Object).OnPostDeleteAsync(7, cts.Token);
            else await Write(new(service.Object), operation, cts.Token);
        }
        var thrown = await Assert.ThrowsAsync(error.GetType(), Invoke);
        Assert.That(thrown, Is.SameAs(error));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SaveRejectionPreservesSubmissionAndModelState()
    {
        using var cts = new CancellationTokenSource();
        var service = Service("POST", cts.Token, new MessagingUnavailableException());
        var page = new CreateModel(service.Object) { Invoice = invoice };
        page.ModelState.SetModelValue("Invoice.Description", "Attempted", "Attempted");
        page.ModelState.MarkFieldValid("Invoice.Description");
        var entry = page.ModelState["Invoice.Description"];
        var before = System.Text.Json.JsonSerializer.Serialize(invoice);
        Assert.That(await page.OnPostSaveAsync(cts.Token), Is.TypeOf<PageResult>());
        Assert.That(page.Invoice, Is.SameAs(invoice));
        Assert.That(System.Text.Json.JsonSerializer.Serialize(page.Invoice), Is.EqualTo(before));
        Assert.That(page.ModelState["Invoice.Description"], Is.SameAs(entry));
        Assert.That(entry!.AttemptedValue, Is.EqualTo("Attempted"));
        Assert.That(page.ModelState[string.Empty]!.Errors.Single().ErrorMessage, Is.EqualTo(Detail));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DeleteRejectionReloadsList()
    {
        using var cts = new CancellationTokenSource();
        var service = Service("DELETE", cts.Token, new MessagingUnavailableException());
        List<Invoice> list = [invoice];
        service.Setup(s => s.GetAllAsync(cts.Token)).ReturnsAsync(list);
        var page = new IndexModel(service.Object);
        Assert.That(await page.OnPostDeleteAsync(7, cts.Token), Is.TypeOf<PageResult>());
        Assert.That(page.Invoices, Is.SameAs(list));
        Assert.That(page.ModelState[string.Empty]!.Errors.Single().ErrorMessage, Is.EqualTo(Detail));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [TestCase(true)][TestCase(false)]
    public async Task RazorSuccessRedirects(bool save)
    {
        using var cts = new CancellationTokenSource();
        var service = Service(save ? "POST" : "DELETE", cts.Token);
        var result = save
            ? await new CreateModel(service.Object) { Invoice = invoice }.OnPostSaveAsync(cts.Token)
            : await new IndexModel(service.Object).OnPostDeleteAsync(7, cts.Token);
        Assert.That(result, Is.TypeOf<RedirectToPageResult>());
        Assert.That(((RedirectToPageResult)result).PageName, Is.EqualTo(save ? "Index" : null));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task InvalidSavePreservesErrorsWithoutCallingService()
    {
        var service = new Mock<IInvoiceService>(MockBehavior.Strict);
        var page = new CreateModel(service.Object) { Invoice = invoice };
        page.ModelState.AddModelError("Invoice.Description", "Existing validation error");
        Assert.That(await page.OnPostSaveAsync(default), Is.TypeOf<PageResult>());
        Assert.That(page.Invoice, Is.SameAs(invoice));
        Assert.That(page.ModelState["Invoice.Description"]!.Errors.Single().ErrorMessage,
            Is.EqualTo("Existing validation error"));
        service.VerifyNoOtherCalls();
    }

    [Test]
    public async Task GetDoesNotTranslateMessagingExceptions()
    {
        var service = new Mock<IInvoiceService>(MockBehavior.Strict);
        service.Setup(s => s.GetAllAsync(default)).ThrowsAsync(new MessagingUnavailableException());
        service.Setup(s => s.GetByIdAsync(7, default)).ThrowsAsync(new MessagingUnavailableException());
        var controller = new InvoicesApiController(service.Object);
        await Assert.ThrowsAsync<MessagingUnavailableException>(async () => await controller.GetAll(default));
        await Assert.ThrowsAsync<MessagingUnavailableException>(async () => await controller.Get(7, default));
        service.VerifyAll();
        service.VerifyNoOtherCalls();
    }
}
