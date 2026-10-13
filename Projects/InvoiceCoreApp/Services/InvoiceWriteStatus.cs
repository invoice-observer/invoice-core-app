using DataLayer.Services;

namespace InvoiceCoreApp.Services;

public sealed record InvoiceWriteSnapshot(bool CanWriteInvoices, string RabbitMq);

// Scoped so the layout and page controls use the same readiness observation.
public sealed class InvoiceWriteStatus(IMessageReadiness? readiness = null)
{
    private InvoiceWriteSnapshot? _snapshot;
    public InvoiceWriteSnapshot Snapshot => _snapshot ??= ReadSnapshot();

    private InvoiceWriteSnapshot ReadSnapshot()
    {
        if (readiness is null) return new(true, "NotRequired");
        var state = readiness.State;
        return new(state == MessageReadinessState.Ready, state.ToString());
    }
}
