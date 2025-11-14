using System.Text.Json;

namespace eSim.Core.Contracts.WebHooks;

public sealed class EsimAccessWebhookEnvelope
{
    public string notifyType { get; set; } = ""; // ORDER_STATUS, SMDP_EVENT, ESIM_STATUS, DATA_USAGE, VALIDITY_USAGE, CHECK_HEALTH
    public JsonElement content { get; set; }     // differs per notifyType
}

public sealed class OrderStatusContent
{
    public string? orderNo { get; set; }
    public string? orderStatus { get; set; } // "GOT_RESOURCE" when ready
}

public sealed class EsimStatusContent
{
    public string? iccid { get; set; }
    public string? esimTranNo { get; set; }
    public string? esimStatus { get; set; } // IN_USE, USED_UP, USED_EXPIRED, UNUSED_EXPIRED, CANCEL, REVOKED
    public string? smdpStatus { get; set; } // ENABLED, DISABLED, RELEASED, DELETED, INSTALLATION
}

public sealed class CheckHealthContent
{
    public string? orderNo { get; set; }
    public string? orderStatus { get; set; }
}