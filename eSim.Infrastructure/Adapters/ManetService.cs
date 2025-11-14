using eSim.Core.Constants;
using eSim.Core.Contracts;
using eSim.Core.Models;
using eSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eSim.Infrastructure.Adapters;

public class ManetService
{
    public HttpClient _http { get; }
    public AppDbContext _dbContext { get; }
    public ILogger<ManetService> _logger { get; }

    // Authentication
    private readonly string _accessCode;
    private readonly string _partnerId;
    private readonly string _partnerSuffix;

    // URLs
    private readonly string _packagesUrl;
    private readonly string _orderUrl;
    private readonly string _savePurchaseUrl;
    private readonly string _dataUsageUrl;
    private readonly string _webhookUrl;

    // Price Config
    private readonly decimal _retailPriceMultiplier;

    public ManetService(HttpClient http, AppDbContext dbContext, ILogger<ManetService> logger, IConfiguration cfg)
    {
        _http = http;
        _dbContext = dbContext;
        _logger = logger;

        // Authentication
        _accessCode = cfg["Manet:AccessCode"]!;
        _partnerId = cfg["Manet:PartnerId"]!;
        _partnerSuffix = cfg["Manet:PartnerSuffix"]!;

        // URLs
        _packagesUrl = cfg["Manet:PackagesUrl"]!;
        _orderUrl = cfg["Manet:OrderUrl"]!;
        _savePurchaseUrl = cfg["Manet:SavePurchase"]!;
        _dataUsageUrl = cfg["Manet:GetProfile"]!;
        _webhookUrl = cfg["App:PublicBaseUrl"] + cfg["Manet:WebhookUrl"]!;

        // Price Config
        _retailPriceMultiplier = decimal.Parse(cfg["Manet:RetailPriceMultiplier"]!);
    }

    // ---- Helpers ----
    public static EsimStatus MapManetStatus(string? status, bool orderError)
    {
        if (orderError) return EsimStatus.Error;

        return status?.ToUpperInvariant() switch
        {
            "NOT_INSTALLED" or "NOT_ACTIVE" => EsimStatus.Ready,      // Ready for installation
            "ACTIVE" => EsimStatus.Active,      // installed and consuming data
            "FINISHED" => EsimStatus.Consumed,     // all data consumed
            "EXPIRED" => EsimStatus.Expired,    // validity expired
            "REFUNDED" => EsimStatus.Cancelled,  // refunded
            _ => EsimStatus.Error
        };
    }

    private string AddPartnerSuffixToUrl(string url)
    {
        if (url.Contains("partner-suffix"))
            return url.Replace("partner-suffix", _partnerSuffix);
        else
            return url;
    }

    private HttpRequestMessage NewPost(string url, object content)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, AddPartnerSuffixToUrl(url));
        req.Headers.Add("X-Api-Key", _accessCode);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(content), System.Text.Encoding.UTF8, "application/json");
        return req;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ===================== PACKAGES =====================
    public async Task<PackageListResult> GetPackagesAsync(string locationCode, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                partner_id = _partnerId,
                unlimited = 0
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
            var items = root?.json?.data ?? [];

            var packages = items.Where(x => x.coverages.Equals(locationCode, StringComparison.CurrentCultureIgnoreCase)).Select(p => new PackageDto(
                PackageCode: p.id,
                Name: p.name,
                Currency: "EUR",
                Price: Math.Round(p.price, 2),
                retailPrice: Math.Round(p.price * _retailPriceMultiplier, 2),
                Location: locationCode,
                DataMB: int.Parse(p.data) * 1024,
                DurationDays: int.Parse(p.duration),
                provider: p.provider,
                Unlimited: int.Parse(p.unlimited) == 1,
                dailyLimit: false
            )).OrderBy(x => x.DataMB)
            .ThenBy(x => x.DurationDays).ToList();

            _logger.LogInformation("Packages returned {Count}", items.Count);

            return new PackageListResult { Packages = packages };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in savePurchaseAsync Manet Distributor with error: {err}", ex.Message);
            throw;
        }
    }


    // ===================== ORDER =====================
    public async Task<CreateOrderResult> CreateOrderAsync(Order order, ApplicationUser user, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                partner_id = _partnerId,
                payment_intent_id = "wallet",
                cost = order.Price,
                wallet = "wallet",
                country = user.CountryOfResidence,
                provider = order.Provider,
                product = new { 
                    id = order.PackageCode, 
                    sold_price = order.Price, 
                    sold_currency = "EUR", 
                    activation_date = DateTime.Today.ToString("yyyy-MM-dd") 
                },
                subscriber = new { 
                    email = user.Email, 
                    first_name = user.FirstName, 
                    last_name = user.LastName, 
                    country_of_residence = user.CountryOfResidence, 
                    device = new { 
                        operating_system = "other", 
                        model = "undefined undefined", 
                        id = 1 
                    } 
                }
            };

            using var req = NewPost(_orderUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Order create failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new CreateOrderResult { Accepted = false, OrderNo = null, Message = $"HTTP {(int)res.StatusCode}" };
            }

            var root = JsonSerializer.Deserialize<OrderRoot>(body, JsonOpts);
            bool ok = root != null && string.IsNullOrEmpty(root?.error) && root!.json != null && root.json.data.status == "ACCEPTED";
            var orderNo = root?.json?.data.id;
            var msg = root?.error;

            _logger.LogInformation("Order create result accepted={Acc} orderNo={OrderNo}", ok, orderNo);
            return new CreateOrderResult { Accepted = ok, OrderNo = orderNo, Message = msg };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in CreateOrderAsync Manet Distributor with error: {err}", ex.Message);
            return new CreateOrderResult { Accepted = false, Message = ex.Message };
        }
    }

    public async Task<SavePurchaseResult> savePurchaseAsync(Order order, string orderId, ApplicationUser user, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                partner_id = _partnerId,
                payment_intent_id = "wallet",
                order_id = orderId,
                currency = "eur",
                cost = order.Price,
                email = user.Email,
                subscription_id = "subscription id not available",
                operation = "Purchase",
                purchase_country = user.CountryOfResidence,
                residence_country = user.CountryOfResidence,
                esim_activation_date = DateTime.Today.ToString("yyyy-MM-dd"),
                payment_method = "wallet",
                provider = order.Provider,
                url_webhook = _webhookUrl
            };

            using var req = NewPost(_savePurchaseUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Order create failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new SavePurchaseResult(false, $"Http error: {res.StatusCode}");
            }

            var root = JsonSerializer.Deserialize<PurchaseRoot>(body, JsonOpts);
            var ok = root != null && root.json != null && root.json.data != null && root.json.data.result;

            _logger.LogInformation("Order create result accepted={Acc}", ok);
            return new SavePurchaseResult(ok, root?.error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in savePurchaseAsync Manet Distributor with error: {err}", ex.Message);
            return new SavePurchaseResult(false, ex.Message);
        }
    }

    public async Task<DataUsageListResult> DataUsageAndStatus(string userEmail, CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                partner_id = _partnerId,
                email = userEmail
            };

            using var req = NewPost(_dataUsageUrl, payload);
            using var res = await _http.SendAsync(req, ct);

            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Order create failed HTTP {Status}: {Body}", res.StatusCode, body);
                return new DataUsageListResult();
            }

            var root = JsonSerializer.Deserialize<DataUsageRoot>(body, JsonOpts);
            var items = root.json?.data;

            var esim = await _dbContext.OrderItems.FirstOrDefaultAsync(x => x.EsimTransNo == items.sim_id, ct);

            if (esim != null)
            {
                esim.Status = MapManetStatus(items.status, false).ToString();
                await _dbContext.SaveChangesAsync(ct);
            }

            List<DataUsageDto> usage = [new DataUsageDto(items.sim_id, items.spent_balance, items.initial_balance)];
            return new DataUsageListResult { dataUsage = usage };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in GetDataUsageAsync Manet Distributor with error: {err}", ex.Message);
            throw;
        }
    }

    // ====== Packages ======
    private sealed class PackagesRoot
    {
        public string? error { get; set; }
        public int status { get; set; }
        public PackagesObj? json { get; set; }
    }
    private sealed class PackagesObj
    {
        public List<PackageItem>? data { get; set; }
    }

    private sealed class PackageItem
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string duration { get; set; } = "";
        public string data { get; set; } = "";
        public decimal price { get; set; }
        public string coverages { get; set; } = "";
        public string provider { get; set; } = "";
        public string unlimited { get; set; }
    }

    // Order
    private sealed class OrderRoot
    {
        public string? error { get; set; }
        public int status { get; set; }
        public OrderObj? json { get; set; }
    }

    private sealed class OrderObj
    {
        public OrderItems? data { get; set; }
    }

    private sealed class OrderItems
    {
        public string? id { get; set; }
        public string? status { get; set; } // ACCEPTED only equals success
        public string? created_date { get; set; }
    }

    // Save Purchase
    private sealed class PurchaseRoot
    {
        public string? error { get; set; }
        public int status { get; set; }
        public PurchaseObj? json { get; set; }
    }

    private sealed class PurchaseObj
    {
        public PurchaseItems? data { get; set; }
    }

    private sealed class PurchaseItems
    {
        public bool result { get; set; }
    }

    // Data Usage
    private sealed class DataUsageRoot
    {
        public string? error { get; set; }
        public int status { get; set; }
        public DataUsageObj? json { get; set; }
    }
    private sealed class DataUsageObj
    {
        public DataUsageItems? data { get; set; }
    }
    private sealed class DataUsageItems
    {
        public string? sim_id { get; set; }
        public int initial_balance { get; set; }
        public int spent_balance { get; set; }
        public string? status { get; set; }
    }
}