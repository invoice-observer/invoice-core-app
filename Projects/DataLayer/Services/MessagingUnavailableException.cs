namespace DataLayer.Services;

public sealed class MessagingUnavailableException() : InvalidOperationException(
    "Invoice changes are unavailable while messaging is not ready.");
