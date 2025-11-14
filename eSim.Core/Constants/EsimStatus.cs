namespace eSim.Core.Constants;

public enum EsimStatus
{
    Ready = 1,       // eSIM allocated / got resource
    Active = 2,       // Installed and active
    Consumed = 3,      // Data fully consumed
    Expired = 4,     // Plan validity expired
    Cancelled = 5,   // Cancelled before activation, refunded
    Error = 6
}