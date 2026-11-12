namespace Academy.Payments.Infrastructure;

/// <summary>No money moves. Never treat this as a production payment integration.</summary>
public sealed class FakePaymentProvider
{
    public Task CaptureAsync(long amountMinor, string currency, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amountMinor <= 0 || currency != "USD")
            throw new InvalidOperationException("Simulated payment provider rejected the request.");
        return Task.CompletedTask;
    }
}
