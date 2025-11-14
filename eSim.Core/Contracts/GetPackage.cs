namespace eSim.Core.Contracts
{
    //Responses
    public record GetPackagesResponse(IReadOnlyList<PackageData> packages);
    public record PackageData(
        string packageCode,
        string name,
        string currencyCode,
        decimal price,
        decimal retailPrice,
        decimal dataMB,
        int duration,
        string? location,
        bool unlimited,
        bool dailyLimit,
        string provider
    );

    // ===== Packages =====
    public class PackageListResult 
    { 
        public IReadOnlyList<PackageDto>? Packages { get; set; } 
    }
    public record PackageDto(
        string PackageCode,
        string Name,
        string Currency,
        decimal Price,
        decimal retailPrice,
        string Location,
        decimal DataMB,
        int DurationDays,
        string? provider,
        bool Unlimited,
        bool dailyLimit
    );
}