namespace eSim.Core.Contracts;

public class DataUsageListResult
{
    public List<DataUsageDto> dataUsage { get; set; }
};

public record DataUsageDto(string EsimTransNo, long dataUsage, long totalData);
