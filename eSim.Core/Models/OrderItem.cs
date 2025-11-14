using System.ComponentModel.DataAnnotations;

namespace eSim.Core.Models
{
    public class OrderItem
    {
        [Key]
        public Guid OrderLineId { get; set; }
        [Required]
        public string? OrderNo { get; set; }
        public string? PackageName { get; set; }
        public string? Iccid { get; set; }
        public string? EsimTransNo { get; set; }
        public string? Status { get; set; }
        public string? QrCodeUrl { get; set; }
        public string? ActivationCode { get; set; }
    }
}