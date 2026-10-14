using DataLayer.Services;
using InvoiceCoreApp.Controllers;
using InvoiceCoreApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;

namespace Tests.NUnit.Web;

[TestFixture]
public class StatusControllerTests
{
    [TestCase(MessageReadinessState.Ready, MessageReadinessState.Unavailable)]
    [TestCase(MessageReadinessState.Unavailable, MessageReadinessState.Ready)]
    public void GetReadsCurrentReadinessAndPreservesCachedPageSnapshot(
        MessageReadinessState initialState, MessageReadinessState currentState)
    {
        var readiness = new Mock<IMessageReadiness>(MockBehavior.Strict);
        readiness.SetupGet(r => r.State).Returns(initialState);
        var status = new InvoiceWriteStatus(readiness.Object);
        var pageSnapshot = status.Snapshot;
        readiness.SetupGet(r => r.State).Returns(currentState);
        var controller = new StatusController(status)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = controller.Get();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var snapshot = ((OkObjectResult)result.Result!).Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Is.EqualTo(new InvoiceWriteSnapshot(
                    currentState == MessageReadinessState.Ready, currentState.ToString())));
            Assert.That(status.Snapshot, Is.SameAs(pageSnapshot));
            Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        }
        readiness.VerifyGet(r => r.State, Times.Exactly(2));
        readiness.VerifyNoOtherCalls();
    }
}
