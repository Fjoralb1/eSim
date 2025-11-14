using eSim.Core.Models;

namespace eSim.Core.Interfaces;
public interface IPayPalClient
{
    Task<string> CreateOrderAsync(decimal amount, string referenceId, string billingLocationCode, CancellationToken ct);

    Task<(string CaptureId, decimal CapturedAmount)> CaptureOrderAsync(string orderId, CancellationToken ct);

    Task<string> RefundCaptureAsync(string captureId, decimal amount, string? reason = null, CancellationToken ct = default);
}