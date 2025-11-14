namespace eSim.Core.Contracts.WebHooks;

using System.Text.Json.Serialization;

public sealed class ManetWebhookEnvelope
{
    [JsonPropertyName("order_error")]
    public bool OrderError { get; set; } // false = OK, true = error

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("customer_firstname")]
    public string? CustomerFirstName { get; set; }

    [JsonPropertyName("customer_lastname")]
    public string? CustomerLastName { get; set; }

    [JsonPropertyName("customer_country")]
    public string? CustomerCountry { get; set; }

    [JsonPropertyName("customer_email")]
    public string? CustomerEmail { get; set; }

    [JsonPropertyName("plan_name")]
    public string? PlanName { get; set; }

    [JsonPropertyName("sim_id")]
    public string? SimId { get; set; }

    [JsonPropertyName("available_data")]
    public long? AvailableData { get; set; }

    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("start_date")]
    public DateTimeOffset? StartDate { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("qrcode")]
    public string? QrCodeUrl { get; set; }

    [JsonPropertyName("lpa")]
    public string? Lpa { get; set; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; set; }

    [JsonPropertyName("payment_intent_id")]
    public string? PaymentIntentId { get; set; }

    [JsonPropertyName("customer_pref_language")]
    public string? CustomerPreferredLanguage { get; set; }
}
