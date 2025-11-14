namespace eSim.API.DTOs
{
    public class OrderDTO
    {
        public required string OrderNo { get; set; }
        public required string PackageName { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal Amount { get; set; }
    }
}
