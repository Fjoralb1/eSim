using eSim.Core.Constants;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace eSim.Core.Models;

public class Order
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? DistributorOrderNo { get; set; }
    public string? PaypalOrderId { get; set; }
    public string? PaypalCaptureId { get; set; }
    public string? UserId { get; set; }
    public string? PackageName { get; set; }
    public string? PackageCode { get; set; }
    public int Count { get; set; }
    [Column(TypeName = "decimal(18, 2)")]
    public decimal Price { get; set; }
    [Column(TypeName = "decimal(18, 2)")]
    public decimal RPrice { get; set; }
    public int PeriodNum { get; set; }
    public DateTime CreatedAt { get; set; }
    public int DistributorId { get; set; }
    public string? Provider { get; set; }
    public string? LocationCode { get; set; }
    public OrderStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}