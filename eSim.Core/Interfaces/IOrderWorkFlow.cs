using eSim.Core.Contracts;
namespace eSim.Core.Interfaces;
public interface IOrderWorkflow
{
    Task<string> StartCheckoutAsync(OrderPackageRequest request, string userId, CancellationToken ct);
    Task CaptureAndPlaceOrderAsync(string paypalOrderId, CancellationToken ct);
}