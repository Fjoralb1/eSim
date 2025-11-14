using eSim.Core.Interfaces;
using eSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using eSim.Core.Models;
using Microsoft.Extensions.Logging;
using eSim.Core.Constants;
using eSim.Core.Contracts;

namespace eSim.Infrastructure.Services;

public sealed class OrderWorkflow : IOrderWorkflow
{
    private readonly AppDbContext _db;
    private readonly IPayPalClient _paypal;
    private readonly DistributorManager _distributor;
    private readonly ILogger<OrderWorkflow> _log;

    public OrderWorkflow(AppDbContext db, IPayPalClient paypal, DistributorManager distributor, ILogger<OrderWorkflow> log)
    {
        _db = db; _paypal = paypal; _log = log;
        _distributor = distributor;
    }

    public async Task<string> StartCheckoutAsync(OrderPackageRequest request, string userId, CancellationToken ct)
    {
        int distributorId = _db.Distributors.SingleOrDefault(s => s.IsActive)!.DistributorId;

        if (request.locationCode.Equals("AL", StringComparison.OrdinalIgnoreCase) && _distributor._manetService._isActiveDistributor)
            distributorId = (int)DistributorEnum.Manet;

        var order = new Order
        {
            UserId = userId.ToString(),
            Status = OrderStatus.PendingPayment,
            PeriodNum = request.periodNum,
            Count = request.count,
            Price = request.price,
            RPrice = request.retailPrice,
            CreatedAt = DateTime.UtcNow,
            PackageName = request.packageName,
            PackageCode = request.packageCode,
            DistributorId = distributorId,
            LocationCode = request.locationCode,
            Provider = request.provider
        };

        await _db.Orders.AddAsync(order, ct);
        await _db.SaveChangesAsync(ct);

        var reference = $"ORD-{order.Id:N}";
        decimal CalcAmount = request.retailPrice * request.count * (request.periodNum > 0 ? request.periodNum : 1);
        var paypalOrderId = await _paypal.CreateOrderAsync(CalcAmount, reference, request.billingLocationCode, ct);
        order.PaypalOrderId = paypalOrderId;
        await _db.SaveChangesAsync(ct);

        return paypalOrderId;
    }

    public async Task CaptureAndPlaceOrderAsync(string paypalOrderId, CancellationToken ct)
    {
        Order? order = await _db.Orders.FirstOrDefaultAsync(o => o.PaypalOrderId == paypalOrderId, ct);

        if (order == null)
        {
            _log.LogWarning("Order with this paypalOrderId: {OrderId} doesnt exist", paypalOrderId);
            return;
        }

        decimal CalcAmount = order.RPrice * order.Count * (order.PeriodNum > 0 ? order.PeriodNum : 1);

        try
        {
            var (captureId, capturedAmount) = await _paypal.CaptureOrderAsync(paypalOrderId, ct);

            if (capturedAmount != CalcAmount)
                _log.LogWarning("PayPal capture mismatch: expected {A}={B}", CalcAmount, capturedAmount);

            order.PaypalCaptureId = captureId;
            order.Status = OrderStatus.PaymentConfirmed;

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PayPal capture failed for Order {O}", order.Id);
            order.Status = OrderStatus.Failed;
            order.FailureReason = "Payment capture failed";
            order.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            if (order.Status == OrderStatus.Failed)
            {
                throw new Exception("Payment capture failed");
            }

            order.Status = OrderStatus.DistributorOrdering;
            await _db.SaveChangesAsync(ct);

            var user = await _db.Users.FindAsync([order.UserId], cancellationToken: ct)
                ?? throw new InvalidOperationException("User not found");

            await _distributor.OrderPackageAsync(order.Id, user, ct);

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Distributor order failed; refunding PayPal…");
            if (!string.IsNullOrEmpty(order.PaypalCaptureId))
                await _paypal.RefundCaptureAsync(order.PaypalCaptureId, CalcAmount, ct: ct);

            order.Status = OrderStatus.Refunded;
            await _db.SaveChangesAsync(ct);
        }
    }
}