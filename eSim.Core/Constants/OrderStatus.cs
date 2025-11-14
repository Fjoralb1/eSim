namespace eSim.Core.Constants;
public enum OrderStatus
{
    PendingPayment = 0,
    PaymentConfirmed = 1,
    DistributorOrdering = 2,
    AwaitingProvisioning = 3,
    Completed = 4,
    Refunded = 5,
    Failed = 6
}