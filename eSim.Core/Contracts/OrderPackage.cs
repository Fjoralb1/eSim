using eSim.Core.Models;

namespace eSim.Core.Contracts
{
    // ===== Orders =====
    public class CreateOrderResult
    {
        public bool Accepted { get; set; }
        public string? OrderNo { get; set; }
        public string? Message { get; set; }
    };

    public record SavePurchaseResult(bool success, string? message);
    public record OrderPackageResponse(bool success);

    // Requests
    public record OrderPackageRequest(string packageCode, string packageName, int count, decimal price, 
        decimal retailPrice, int periodNum, string provider, 
        string locationCode, string billingLocationCode);

    // Get Orders per user
    public record GetOrdersPerUser(IReadOnlyList<Order> Orders);

    // Get Order Items per order 
    public class GetOrderItemsPerOrder
    {
        public List<OrderItemFull>? Orders { get; set; }
    }
    public class OrderItemFull
    {
        public string? Iccid { get; set; }
        public string? ActivationCode { get; set; }
        public long TotalData { get; set; }
        public long UsedData { get; set; }
        public string? QrCodeUrl { get; set; }
        public string? Status { get; set; }
    }
}
