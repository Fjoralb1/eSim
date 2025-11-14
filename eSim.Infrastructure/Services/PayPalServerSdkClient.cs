using System;
using System.Globalization;
using System.Text.Json;
using eSim.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaypalServerSdk.Standard;
using PaypalServerSdk.Standard.Authentication;
using PaypalServerSdk.Standard.Exceptions;
using PaypalServerSdk.Standard.Models;

namespace eSim.Infrastructure.Services;

public sealed class PayPalServerSdkClient : IPayPalClient
{
    private readonly PaypalServerSdkClient _client;
    private readonly string _defaultCurrency;
    private readonly ILogger<PayPalServerSdkClient> _log;
    private readonly bool _isProduction;

    public PayPalServerSdkClient(Microsoft.Extensions.Configuration.IConfiguration cfg, ILogger<PayPalServerSdkClient> log)
    {
        _log = log;
        _defaultCurrency = cfg["PayPal:ReturnCurrency"] ?? "EUR";

        var envString = cfg["PayPal:Environment"] ?? "Sandbox";
        _isProduction = envString.Equals("Production", StringComparison.OrdinalIgnoreCase);

        var env = _isProduction
            ? PaypalServerSdk.Standard.Environment.Production
            : PaypalServerSdk.Standard.Environment.Sandbox;

        var clientId = cfg["PayPal:ClientId"];
        var clientSecret = cfg["PayPal:ClientSecret"];

        // Validate configuration
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("PayPal credentials are not configured properly.");
        }

        _client = new PaypalServerSdkClient.Builder()
            .ClientCredentialsAuth(new ClientCredentialsAuthModel.Builder(
                clientId, clientSecret
            ).Build())
            .Environment(env)
            .Build();

        _log.LogInformation("✅ PayPal SDK initialized. Environment={Env}, Currency={Currency}",
            envString, _defaultCurrency);
    }

    public async Task<string> CreateOrderAsync(
        decimal amount,
        string referenceId,
        string billingLocationCode,
        CancellationToken ct)
    {
        // Validation
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        if (string.IsNullOrWhiteSpace(referenceId))
            throw new ArgumentException("Reference ID is required.", nameof(referenceId));

        if (string.IsNullOrWhiteSpace(billingLocationCode))
            throw new ArgumentException("Billing location code is required.", nameof(billingLocationCode));

        try
        {
            _log.LogInformation(
                "🟦 [PayPal] Creating order. RefId={RefId}, Amount={Amount} {Currency}, Country={Country}",
                referenceId, amount, _defaultCurrency, billingLocationCode);

            var orders = _client.OrdersController;

            var req = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Capture,
                PurchaseUnits =
                [
                    new()
                    {
                        ReferenceId = referenceId,
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = _defaultCurrency,
                            MValue = amount.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    }
                ],
                PaymentSource = new PaymentSource
                {
                    Paypal = new PaypalWallet
                    {
                        ExperienceContext = new PaypalWalletExperienceContext
                        {
                            ShippingPreference = PaypalWalletContextShippingPreference.NoShipping,
                            BrandName = "MobySim" // Your brand name
                        }
                    }
                },
                Payer = new Payer
                {
                    Address = new Address
                    {
                        CountryCode = billingLocationCode
                    }
                }
            };

            var created = await orders.CreateOrderAsync(new CreateOrderInput
            {
                Body = req,
                PaypalRequestId = referenceId, // Idempotency key
                Prefer = "return=minimal"
            }, ct);

            if (created?.Data?.Id == null)
            {
                _log.LogError("❌ [PayPal] Order creation returned null or empty ID. RefId={RefId}", referenceId);
                throw new InvalidOperationException("PayPal did not return an order ID.");
            }

            _log.LogInformation("✅ [PayPal] Order created. OrderId={OrderId}, RefId={RefId}",
                created.Data.Id, referenceId);

            return created.Data.Id;
        }
        catch (ApiException apiEx)
        {
            // PayPal SDK specific exception
            _log.LogError(apiEx,
                "❌ [PayPal] API error creating order. RefId={RefId}, StatusCode={StatusCode}, Body={ResponseBody}",
                referenceId, apiEx.ResponseCode, apiEx.HttpContext?.Response?.Body);
            throw new InvalidOperationException($"PayPal API error: {apiEx.Message}", apiEx);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "❌ [PayPal] Unexpected error creating order. RefId={RefId}", referenceId);
            throw;
        }
    }

    public async Task<(string CaptureId, decimal CapturedAmount)> CaptureOrderAsync(
        string orderId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("Order ID is required.", nameof(orderId));

        try
        {
            _log.LogInformation("🟧 [PayPal] Capturing order. OrderId={OrderId}", orderId);

            var captured = await _client.OrdersController.CaptureOrderAsync(
                new CaptureOrderInput
                {
                    Id = orderId,
                    Prefer = "return=representation"
                },
                ct
            );

            if (captured?.Data == null)
            {
                _log.LogError("❌ [PayPal] Null response when capturing order. OrderId={OrderId}", orderId);
                throw new InvalidOperationException("PayPal returned null response.");
            }

            // Extract capture details
            var capture = captured.Data.PurchaseUnits?
                .Where(pu => pu?.Payments?.Captures != null)
                .SelectMany(pu => pu.Payments.Captures)
                .FirstOrDefault(c => c?.Status != null);

            if (capture == null)
            {
                var orderStatus = captured.Data.Status?.ToString() ?? "UNKNOWN";
                _log.LogWarning(
                    "⚠️ [PayPal] No capture found. OrderId={OrderId}, OrderStatus={Status}",
                    orderId, orderStatus);

                // Log full response for debugging (only in non-production)
                if (!_isProduction)
                {
                    var raw = JsonSerializer.Serialize(captured.Data);
                    _log.LogDebug("PayPal Response: {Response}", raw);
                }

                throw new InvalidOperationException(
                    $"No capture found. Order status: {orderStatus}");
            }

            var captureId = capture.Id;
            var captureStatus = capture.Status?.ToString() ?? "UNKNOWN";

            if (string.IsNullOrWhiteSpace(captureId))
            {
                _log.LogError("❌ [PayPal] Missing capture ID. OrderId={OrderId}", orderId);
                throw new InvalidOperationException("Capture ID is missing.");
            }

            var amountStr = capture.Amount?.MValue;
            if (string.IsNullOrWhiteSpace(amountStr))
            {
                _log.LogError("❌ [PayPal] Missing capture amount. CaptureId={CaptureId}", captureId);
                throw new InvalidOperationException("Capture amount is missing.");
            }

            if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                _log.LogError(
                    "❌ [PayPal] Invalid amount format. CaptureId={CaptureId}, Amount={Amount}",
                    captureId, amountStr);
                throw new FormatException($"Cannot parse amount: {amountStr}");
            }

            var currency = capture.Amount?.CurrencyCode ?? _defaultCurrency;

            _log.LogInformation(
                "✅ [PayPal] Order captured. CaptureId={CaptureId}, Amount={Amount} {Currency}, Status={Status}",
                captureId, amount, currency, captureStatus);

            return (captureId, amount);
        }
        catch (ApiException apiEx)
        {
            _log.LogError(apiEx,
                "❌ [PayPal] API error capturing order. OrderId={OrderId}, StatusCode={StatusCode}",
                orderId, apiEx.ResponseCode);
            throw new InvalidOperationException($"PayPal API error: {apiEx.Message}", apiEx);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("⚠️ [PayPal] Capture cancelled. OrderId={OrderId}", orderId);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "❌ [PayPal] Failed to capture order. OrderId={OrderId}", orderId);
            throw;
        }
    }

    public async Task<string> RefundCaptureAsync(
        string captureId,
        decimal amount,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(captureId))
            throw new ArgumentException("Capture ID is required.", nameof(captureId));

        if (amount <= 0)
            throw new ArgumentException("Refund amount must be greater than zero.", nameof(amount));

        try
        {
            _log.LogWarning(
                "🔁 [PayPal] Refunding capture. CaptureId={CaptureId}, Amount={Amount} {Currency}, Reason={Reason}",
                captureId, amount, _defaultCurrency, reason ?? "N/A");

            var payments = _client.PaymentsController;

            var body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = _defaultCurrency,
                    MValue = amount.ToString("F2", CultureInfo.InvariantCulture)
                },
                NoteToPayer = reason // Optional note
            };

            var refundResponse = await payments.RefundCapturedPaymentAsync(
                new RefundCapturedPaymentInput
                {
                    CaptureId = captureId,
                    Body = body,
                    Prefer = "return=representation"
                },
                ct);

            var refundId = refundResponse?.Data?.Id;
            var refundStatus = refundResponse?.Data?.Status?.ToString() ?? "UNKNOWN";

            _log.LogInformation(
                "✅ [PayPal] Refund processed. RefundId={RefundId}, CaptureId={CaptureId}, Status={Status}",
                refundId, captureId, refundStatus);

            return refundId ?? string.Empty;
        }
        catch (ApiException apiEx)
        {
            _log.LogError(apiEx,
                "❌ [PayPal] API error refunding capture. CaptureId={CaptureId}, StatusCode={StatusCode}",
                captureId, apiEx.ResponseCode);
            throw new InvalidOperationException($"PayPal refund failed: {apiEx.Message}", apiEx);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "❌ [PayPal] Failed to refund capture. CaptureId={CaptureId}", captureId);
            throw;
        }
    }
}