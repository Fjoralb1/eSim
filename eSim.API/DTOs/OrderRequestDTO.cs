namespace eSim.API.DTOs;

public class OrderRequestDTO
{
    public required string PackageCode { get; set; }
    public required string PackageName { get; set; }
    public int Count { get; set; }
    public decimal Price { get; set; }
    public decimal RetailPrice { get; set; }
    public int PeriodNum { get; set; }
    public required string Provider { get; set; }
    public required string LocationCode { get; set; }
}