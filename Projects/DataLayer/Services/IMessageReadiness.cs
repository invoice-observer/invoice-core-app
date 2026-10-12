namespace DataLayer.Services;

public enum MessageReadinessState
{
    Connecting,
    Ready,
    Unavailable
}

public interface IMessageReadiness
{
    MessageReadinessState State { get; }
    bool CanWriteInvoices { get; }
}
