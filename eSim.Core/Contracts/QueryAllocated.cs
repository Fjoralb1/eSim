namespace eSim.Core.Contracts;

// Query allocated (returns allocated eSIMs once ready; if not ready, error code 200010)
public record QueryAllocatedResult(bool Ready, string? ErrorCode, string? ErrorMsg, IReadOnlyList<AllocatedEsim> Esims);
public record AllocatedEsim(string OrderNo, string Iccid, string EsimTransNo, string EsimStatus, string PackageCode, string PackageName, string LocationCode, DateTime CreationDate, string ActivationCode, string QrCodeUrl);
