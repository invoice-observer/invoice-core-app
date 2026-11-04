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
        // Accepts only the exact names (any case); rejects numbers, lists, whitespace and SQLite.
        public static InvoiceServiceMode Parse(string? value) =>
            Enum.GetValues<InvoiceServiceMode>()
                .Cast<InvoiceServiceMode?>()
                .FirstOrDefault(mode => string.Equals(mode.ToString(), value, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Invalid configuration value '{value}' for 'InvoiceService'. " +
                $"Allowed values: {string.Join(", ", Enum.GetNames<InvoiceServiceMode>())}.");
    }
}
