using eSim.Core.Constants;
using eSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eSim.Infrastructure.Services.Workers;

public sealed class UnpaidOrderCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<UnpaidOrderCleanupWorker> _log;
    private readonly IConfiguration _cfg;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public UnpaidOrderCleanupWorker(IServiceProvider sp, ILogger<UnpaidOrderCleanupWorker> log, IConfiguration cfg)
    {
        _sp = sp; _log = log; _cfg = cfg;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMin = int.Parse(_cfg["Orders:CleanupIntervalMinutes"]!);
        var interval = TimeSpan.FromMinutes(intervalMin);

        _log.LogInformation("UnpaidOrderCleanupWorker started. Interval={Interval} minutes", intervalMin);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _gate.WaitAsync(stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unpaid order cleanup sweep failed.");
            }
            finally
            {
                if (_gate.CurrentCount == 0) _gate.Release();
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var timeoutMin = int.Parse(_cfg["Orders:PendingPaymentTimeoutMinutes"]!);
        var cutoffUtc = DateTime.UtcNow.AddMinutes(-timeoutMin);

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Only touch orders stuck in PendingPayment and older than cutoff
        var stale = await db.Orders
            .Where(o => o.Status == OrderStatus.PendingPayment && o.CreatedAt <= cutoffUtc)
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            _log.LogDebug("No stale pending-payment orders found (cutoff {Cutoff}).", cutoffUtc);
            return;
        }

        foreach (var o in stale)
        {
            o.Status = OrderStatus.Failed; // 6
            o.FailureReason = "Payment not completed within time window.";
            o.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _log.LogInformation("Marked {Count} orders as Failed due to payment timeout (>{Minutes} min).", stale.Count, timeoutMin);
    }
}