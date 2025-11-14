using eSim.Core.Constants;
using eSim.Core.Contracts;
using eSim.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eSim.Infrastructure.Adapters;

public sealed class EsimAccessService
{
    private readonly HttpClient _http;
    private readonly ILogger<EsimAccessService> _logger;
    private readonly string _accessCode;

    // URLs
    private readonly string _packagesUrl;
    private readonly string _orderUrl;
    private readonly string _queryUrl;
    private readonly string _locationsUrl;
    private readonly string _dataUsage;

    public EsimAccessService(HttpClient http, ILogger<EsimAccessService> logger, IConfiguration cfg)
    {
        _http = http;
        _logger = logger;

        _accessCode = cfg["EsimAccess:AccessCode"]!;
        _packagesUrl = cfg["EsimAccess:PackagesUrl"]!;
        _orderUrl = cfg["EsimAccess:OrderUrl"]!;
        _queryUrl = cfg["EsimAccess:QueryUrl"]!;
        _locationsUrl = cfg["EsimAccess:LocationsUrl"]!;
        _dataUsage = cfg["EsimAccess:DataUsage"]!;
    }

    // ---- Helpers ----

    public static EsimStatus MapEsimAccessStatus(string? esimStatus) => esimStatus switch
    {
        "GOT_RESOURCE" => EsimStatus.Ready,
        "IN_USE" => EsimStatus.Active,
        "USED_UP" => EsimStatus.Consumed,
        "USED_EXPIRED" or "UNUSED_EXPIRED" => EsimStatus.Expired,
        "CANCEL" => EsimStatus.Cancelled,
        _ => EsimStatus.Error
    };

    private HttpRequestMessage NewPost(string url, object content)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("RT-AccessCode", _accessCode);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(content), System.Text.Encoding.UTF8, "application/json");
        return req;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static decimal Scaled(int v) => Math.Round(v / 10000m, 2); // 4-decimal scale

    // ===================== PACKAGES =====================
    public async Task<PackageListResult> GetPackagesAsync(string locationCode, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                locationCode = locationCode.ToUpper(),
                type = "",
                packageCode = "",
                slug = "",
                iccid = ""
            };

            using var req = NewPost(_packagesUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Packages call failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new PackageListResult();
            }

            var root = JsonSerializer.Deserialize<PackagesRoot>(body, JsonOpts);
            var items = root?.obj?.packageList ?? [];

            var packages = items.Where(x => x.locationCode.Equals(locationCode, StringComparison.CurrentCultureIgnoreCase)).Select(p => new PackageDto(
                PackageCode: p.packageCode,
                Name: p.name.Replace("/", " "),
                Currency: "EUR",
                Price: Scaled(p.price),
                retailPrice: Scaled(p.retailPrice),
                Location: p.locationCode,
                DataMB: (int)(p.volume / (1024 * 1024)),
                DurationDays: p.duration,
                provider: null,
                Unlimited: false,
                dailyLimit: p.dataType != 1
            )).OrderBy(x => x.DataMB)
            .ThenBy(x => x.DurationDays).ToList();

            _logger.LogInformation("Packages returned {Count}", packages.Count);

            return new PackageListResult { Packages = packages };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetPackagesAsync EsimAccess Distributor with error: {err}", ex.Message);
            throw;
        }
    }

    // ===================== ORDER =====================
    public async Task<CreateOrderResult> CreateOrderAsync(Order order, CancellationToken ct)
    {
        try
        {
            object packageInfo = new
            {
                packageCode = order.PackageCode,
                count = order.Count,
                price = (int)(order.Price * 10000),
                periodNum = order.PeriodNum
            };

            decimal Amount = order.Price * order.Count * (order.PeriodNum > 0 ? order.PeriodNum : 1);

            var payload = new
            {
                transactionId = Guid.NewGuid(),
                packageInfoList = new List<object>
                {
                    packageInfo
                },
                amount = (long)(Amount * 10000)
            };

            _logger.LogInformation("Order Payload={payload} packageInfo={packageI}", payload, packageInfo);

            using var req = NewPost(_orderUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Order create failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new CreateOrderResult { Accepted = false, OrderNo = null, Message = $"HTTP {(int)res.StatusCode}" };
            }

            var root = JsonSerializer.Deserialize<OrderCreateRoot>(body, JsonOpts);
            var ok = root?.success == true && !string.IsNullOrWhiteSpace(root?.obj?.orderNo);
            var orderNo = root?.obj?.orderNo;
            var msg = root?.errorMsg;

            _logger.LogInformation("Order create result accepted={Acc} orderNo={OrderNo} errorMsg={error}", ok, orderNo, msg);
            return new CreateOrderResult { Accepted = ok, OrderNo = orderNo, Message = msg };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreateOrderAsync EsimAccess Distributor with error: {err}", ex.Message);
            return new CreateOrderResult { Accepted = false, OrderNo = null, Message = ex.Message };
        }
    }

    // ===================== QUERY ALLOCATED =====================
    public async Task<QueryAllocatedResult> QueryAllocatedAsync(string? OrderNo, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                orderNo = OrderNo,
                pager = new { pageNum = 1, pageSize = 20 }
            };

            using var req = NewPost(_queryUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Query allocated failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new QueryAllocatedResult(false, "HTTP", $"HTTP {(int)res.StatusCode}", []);
            }

            var root = JsonSerializer.Deserialize<QueryRoot>(body, JsonOpts);

            // Not ready yet
            if (root?.success == false && root?.errorCode == "200010")
            {
                _logger.LogInformation("Order {OrderNo} not ready yet (200010)", OrderNo);
                return new QueryAllocatedResult(false, root.errorCode, root.errorMsg, []);
            }

            var list = root?.obj?.esimList ?? [];
            var package = list.FirstOrDefault()?.packageList?.FirstOrDefault()!;

            var esims = list.Select(e => new AllocatedEsim(
                OrderNo: e.orderNo ?? "",
                Iccid: e.iccid,
                EsimTransNo: e.esimTranNo ?? "",
                EsimStatus: MapEsimAccessStatus(e.esimStatus).ToString(),
                PackageCode: package.packageCode ?? "",
                LocationCode: package.locationCode ?? "",
                CreationDate: DateTime.Parse(package.createTime!),
                ActivationCode: e.ac,
                QrCodeUrl: e.qrCodeUrl,
                PackageName: package.packageName ?? ""
            )).ToList();

            var ready = esims.Count > 0;
            _logger.LogInformation("Order {OrderNo} query ready={Ready} profiles={Count}", OrderNo, ready, esims.Count);

            return new QueryAllocatedResult(ready, root?.errorCode, root?.errorMsg, esims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in QueryAllocatedAsync EsimAccess Distributor with error: {err}", ex.Message);
            throw;
        }
    }

    public async Task<GetLocationsResponse> GetAllLocationsAsync(CancellationToken ct)
    {
        try
        {
            using var locReq = NewPost(_locationsUrl, new { });
            using var locRes = await _http.SendAsync(locReq, ct);
            var res = await locRes.Content.ReadAsStringAsync(ct);

            if (!locRes.IsSuccessStatusCode)
            {
                _logger.LogWarning("Locations HTTP {Status}: {Body}", locRes.StatusCode, res);
                return new GetLocationsResponse(false, res, []);
            }

            var root = JsonSerializer.Deserialize<LocationRoot>(res, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var items = root?.obj?.locationList ?? [];
            var locationsList = items.Select(e => new LocationData
            (
                code: e.code ?? "",
                name: e.name ?? "",
                type: e.type,
                SubLocations: e.subLocationList?.Select(s => new SubLocations(s.code ?? "", s.name ?? "")).ToList() ?? []
            )).ToList();

            return new GetLocationsResponse(root?.success == true, root?.errorMsg, locationsList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetAllLocationsAsync with error: {err}", ex.Message);
            throw;
        }
    }

    public async Task<DataUsageListResult> GetDataUsageAsync(IEnumerable<string> orderItemsNo, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                esimTranNoList = orderItemsNo.ToList(),
            };

            using var dataUsgReq = NewPost(_dataUsage, payload);
            using var dataUsageRes = await _http.SendAsync(dataUsgReq, ct);
            var res = await dataUsageRes.Content.ReadAsStringAsync(ct);

            if (!dataUsageRes.IsSuccessStatusCode)
            {
                _logger.LogWarning("Data Usage HTTP {Status}: {Body}", dataUsageRes.StatusCode, res);
                return new DataUsageListResult();
            }

            var root = JsonSerializer.Deserialize<DataUsageRoot>(res, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var items = root?.obj?.esimUsageList!.ToList();
            var usage = items!.Select(x => new DataUsageDto(x.esimTranNo!, x.dataUsage, x.totalData)).ToList();

            return new DataUsageListResult { dataUsage = usage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetDataUsageAsync with error: {err}", ex.Message);
            throw;
        }
    }


    private sealed class PackagesRoot
    {
        public bool success { get; set; }
        public string? errorCode { get; set; }
        public string? errorMsg { get; set; }
        public PackagesObj? obj { get; set; }
    }
    private sealed class PackagesObj
    {
        public List<PackageItem>? packageList { get; set; }
    }

    public sealed class PackageItem
    {
        public string packageCode { get; set; } = "";
        public string name { get; set; } = "";
        public int price { get; set; }
        public long volume { get; set; }
        public int duration { get; set; }
        public string locationCode { get; set; } = "";
        public int retailPrice { get; set; }
        public int dataType { get; set; }
    }

    private sealed class OrderCreateRoot
    {
        public bool success { get; set; }
        public string? errorCode { get; set; }
        public string? errorMsg { get; set; }
        public OrderCreateObj? obj { get; set; }
    }
    private sealed class OrderCreateObj
    {
        public string? orderNo { get; set; }
    }

    private sealed class QueryRoot
    {
        public bool success { get; set; }
        public string? errorCode { get; set; }
        public string? errorMsg { get; set; }
        public QueryObj? obj { get; set; }
    }
    private sealed class QueryObj
    {
        public List<EsimItem>? esimList { get; set; }
    }
    private sealed class EsimItem
    {
        public required string esimTranNo { get; set; }
        public required string orderNo { get; set; }
        public required string iccid { get; set; }
        public required string ac { get; set; }
        public required string qrCodeUrl { get; set; }
        public string? esimStatus { get; set; }
        public List<EsimPackageItem>? packageList { get; set; }
    }
    private sealed class EsimPackageItem
    {
        public string? packageCode { get; set; }
        public string? packageName { get; set; }
        public string? locationCode { get; set; }
        public string? createTime { get; set; }
    }

    // Locations
    private sealed class LocationRoot
    {
        public bool success { get; set; }
        public string? errorCode { get; set; }
        public string? errorMsg { get; set; }
        public LocationObj? obj { get; set; }
    }
    private sealed class LocationObj
    {
        public List<LocationItem>? locationList { get; set; }
    }
    private sealed class LocationItem
    {
        public string? code { get; set; }
        public string? name { get; set; }
        public int type { get; set; } // 1=Country, 2=Region
        public List<SubLoc>? subLocationList { get; set; }
    }
    private sealed class SubLoc
    {
        public string? code { get; set; }
        public string? name { get; set; }
    }

    // Data Usage
    private sealed class DataUsageRoot
    {
        public bool success { get; set; }
        public string? errorCode { get; set; }
        public string? errorMsg { get; set; }
        public DataUsageObj? obj { get; set; }
    }
    private sealed class DataUsageObj
    {
        public List<DataUsageItem>? esimUsageList { get; set; }
    }
    private sealed class DataUsageItem
    {
        public string? esimTranNo { get; set; }
        public long dataUsage { get; set; }
        public long totalData { get; set; }
        public string? lastUpdateTime { get; set; }
    }
}