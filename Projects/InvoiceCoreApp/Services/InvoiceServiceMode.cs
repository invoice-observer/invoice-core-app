namespace InvoiceCoreApp.Services;

public enum InvoiceServiceMode
{
    SqlServer,
    Mock
}

public static class InvoiceServiceModeExtensions
{
    extension(InvoiceServiceMode)
    {
        // Stub: implementation pending.
        public static InvoiceServiceMode Parse(string? value) => throw new NotImplementedException();
    }
}
