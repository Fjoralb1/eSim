using eSim.Core.Constants;
using eSim.Core.Contracts;
using eSim.Core.Contracts.WebHooks;
using eSim.Core.Interfaces;
using eSim.Core.Models;
using eSim.Infrastructure.Adapters;
using eSim.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace eSim.Infrastructure.Services
{
    public class DistributorManager
    {
        private AppDbContext _dbContext { get; }
        private EsimAccessService _eSimAccessService { get; }
        private ManetService _manetService { get; }
        public IEmailService _emailService { get; }
        public UserManager<ApplicationUser> _users { get; }
        public IPayPalClient _payPalClient { get; }
        public IInvoiceService _invoiceService { get; }
        public ILogger<DistributorManager> _logger { get; }

        public DistributorManager(
            AppDbContext context,
            EsimAccessService eSimAccessService,
            ManetService manetService,
            UserManager<ApplicationUser> users,
            IPayPalClient payPalClient,
            IInvoiceService invoiceService,
            IEmailService emailService,
            ILogger<DistributorManager> logger)
        {
            _dbContext = context;
            _eSimAccessService = eSimAccessService;
            _manetService = manetService;
            _users = users;
            _payPalClient = payPalClient;
            _invoiceService = invoiceService;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<GetLocationsResponse> GetAllLocations(CancellationToken ct)
        {
            try
            {
                int distributorId = _dbContext.Distributors.SingleOrDefault(s => s.IsActive)!.DistributorId;

                return distributorId switch
                {
                    (int)DistributorEnum.EsimAcess => await _eSimAccessService.GetAllLocationsAsync(ct),
                    _ => new GetLocationsResponse(false, "", []),
                };
            }
            catch
            {
                throw;
            }
        }

        public async Task<GetPackagesResponse> GetPackagesAsync(string locationCode, CancellationToken ct)
        {
            try
            {
                int distributorId = _dbContext.Distributors.SingleOrDefault(s => s.IsActive)!.DistributorId;

                if (locationCode.Equals("AL", StringComparison.OrdinalIgnoreCase))
                    distributorId = (int)DistributorEnum.Manet;

                var filteredPackages = new PackageListResult();

                switch (distributorId)
                {
                    case (int)DistributorEnum.EsimAcess:
                        filteredPackages = await _eSimAccessService.GetPackagesAsync(locationCode, ct);

                        break;

                    case (int)DistributorEnum.Manet:
                        filteredPackages = await _manetService.GetPackagesAsync(locationCode, ct);

                        break;
                }

                if (filteredPackages == null || filteredPackages.Packages == null || !filteredPackages.Packages.Any())
                {
                    return new GetPackagesResponse([]);
                }

                IReadOnlyList<PackageData> packageData = [.. filteredPackages.Packages.Where(x => x.Unlimited || x.DataMB >= 1024).Select(p => new PackageData(
                            packageCode: p.PackageCode,
                            name: p.Name,
                            currencyCode: p.Currency,
                            price: p.Price,
                            retailPrice: p.retailPrice,
                            dataMB: p.DataMB,
                            duration: p.DurationDays,
                            location: p.Location,
                            unlimited: p.Unlimited,
                            dailyLimit: p.dailyLimit,
                            provider: p.provider!
                        ))];

                return new GetPackagesResponse(packageData);
            }
            catch
            {
                throw;
            }
        }

        public async Task OrderPackageAsync(Guid orderId, ApplicationUser user, CancellationToken ct)
        {
            var apiResponse = new CreateOrderResult();

            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new Exception("Order not found");

            try
            {
                switch (order.DistributorId)
                {
                    case (int)DistributorEnum.EsimAcess:
                        apiResponse = await _eSimAccessService.CreateOrderAsync(order, ct);

                        if (!apiResponse.Accepted && !string.IsNullOrEmpty(apiResponse.OrderNo))
                            throw new Exception($"Order creating failed with error: {apiResponse!.Message}");

                        break;

                    case (int)DistributorEnum.Manet:
                        apiResponse = await _manetService.CreateOrderAsync(order, user, ct);

                        if (apiResponse.Accepted && !string.IsNullOrEmpty(apiResponse.OrderNo))
                        {
                            // Call SavePurchase Api
                            var savePurchaseRes = await _manetService.savePurchaseAsync(order, apiResponse.OrderNo, user, ct);
                            if (!savePurchaseRes.success)
                                throw new Exception($"Order creating failed with error: {savePurchaseRes.message}");

                            break;
                        }
                        else
                            throw new Exception($"Order creating failed with error: {apiResponse.Message}");
                }


                order.DistributorOrderNo = apiResponse.OrderNo;
                order.Status = OrderStatus.AwaitingProvisioning;
            }
            catch (Exception ex)
            {
                throw new Exception($"Order creating failed with error: {ex.Message}");
            }
        }

        public async Task<GetOrdersPerUser> GetOrdersForUserAsync(string userid, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("GetOrders with userId: {userId}", userid);

                var response = new List<Order>();
                var user = await _users.FindByIdAsync(userid) ?? throw new Exception("User not found");

                if (await _users.IsInRoleAsync(user, RoleNames.Admin))
                    response = await _dbContext.Orders.Where(x => !string.IsNullOrEmpty(x.DistributorOrderNo)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
                else
                    response = await _dbContext.Orders.Where(x => x.UserId == userid && !string.IsNullOrEmpty(x.DistributorOrderNo) && x.Status != OrderStatus.Failed).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

                return new GetOrdersPerUser(response);
            }
            catch
            {
                throw;
            }
        }

        public async Task<GetOrderItemsPerOrder> GetOrderItemsAsync(string orderNo, string userId, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("GetOrderItems with orderNo: {OrderNo}", orderNo);

                GetOrderItemsPerOrder finalResult = new();
                DataUsageListResult apiRes = new();

                var user = await _users.FindByIdAsync(userId);
                var distributorId = _dbContext.Orders.FirstOrDefault(x => x.DistributorOrderNo == orderNo)?.DistributorId;
                var orderItems = await _dbContext.OrderItems.Where(x => x.OrderNo == orderNo).ToListAsync(ct);

                switch (distributorId)
                {
                    case (int?)DistributorEnum.EsimAcess:
                        apiRes = await _eSimAccessService.GetDataUsageAsync(orderItems.Select(x => x.EsimTransNo)!, ct);

                        break;
                    case (int?)DistributorEnum.Manet:
                        apiRes = await _manetService.DataUsageAndStatus(user!.Email!, ct);

                        break;
                }

                if (apiRes == null || apiRes.dataUsage.Count == 0)
                {
                    finalResult.Orders = [.. orderItems.Select(x => new OrderItemFull
                    {
                        ActivationCode = x.ActivationCode,
                        Iccid = x.Iccid,
                        QrCodeUrl = x.QrCodeUrl,
                        Status = x.Status,
                        TotalData = 0,
                        UsedData = 0
                    })];
                }
                else
                {
                    finalResult.Orders = [.. from o in orderItems
                                        join m in apiRes.dataUsage on o.EsimTransNo equals m.EsimTransNo
                                        select new OrderItemFull
                                        {
                                            Iccid = o.Iccid,
                                            ActivationCode = o.ActivationCode,
                                            QrCodeUrl = o.QrCodeUrl,
                                            TotalData = m.totalData,
                                            UsedData = m.dataUsage,
                                            Status = o.Status
                                        }];
                }

                return finalResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetOrderItems for orderNo: {OrderNo} failed with error: {err}", orderNo, ex.Message);
                return new GetOrderItemsPerOrder()
                {
                    Orders = []
                };
            }
        }

        public async Task<byte[]?> GetInvoiceForOrder(string orderNo, ApplicationUser? loggedInUser, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation("GetInvoiceForOrder with orderNo: {OrderNo}", orderNo);

                var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.DistributorOrderNo == orderNo, ct);

                var pdf = await _invoiceService.GenerateInvoicePdfAsync(order!, loggedInUser!.FullName, loggedInUser.Email!, ct);

                return pdf;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("GetInvoiceForOrder for orderNo: {OrderNo} failed with error: {error}", orderNo, ex.Message);
                return null;
            }
        }

        public async Task HandleEsimAccessWebhookAsync(string notifyType, JsonElement content, CancellationToken ct)
        {
            switch ((notifyType ?? "").ToUpperInvariant())
            {
                case "ORDER_STATUS":
                    {
                        var dto = content.Deserialize<OrderStatusContent>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        var order = await _dbContext.Orders.SingleOrDefaultAsync(o => o.DistributorOrderNo == dto.orderNo, ct);

                        _logger.LogInformation("ORDER_STATUS: orderNo={OrderNo}, status={Status}", dto.orderNo, dto.orderStatus);

                        try
                        {
                            if (dto.orderStatus == "GOT_RESOURCE")
                            {
                                var result = await _eSimAccessService.QueryAllocatedAsync(dto.orderNo, ct);

                                if (result != null && result.Ready && result.Esims.Count > 0)
                                {
                                    var user = await _users.FindByIdAsync(order!.UserId!);

                                    foreach (var item in result.Esims)
                                    {
                                        var orderItem = new OrderItem
                                        {
                                            OrderNo = item.OrderNo,
                                            EsimTransNo = item.EsimTransNo,
                                            QrCodeUrl = item.QrCodeUrl,
                                            ActivationCode = item.ActivationCode,
                                            Iccid = item.Iccid,
                                            PackageName = item.PackageName,
                                            Status = item.EsimStatus
                                        };

                                        // Persist each line item to DB
                                        await _dbContext.OrderItems.AddAsync(orderItem, ct);

                                        var pdfGenerated = await _invoiceService.GenerateInvoicePdfAsync(order, user!.FullName, user.Email!, ct);

                                        await _emailService.SendEsimEmailAsync(
                                            toEmail: user!.Email!,
                                            userName: user.FullName,
                                            order: order,
                                            item: orderItem,
                                            invoicePdfBytes: pdfGenerated!,
                                            ct: ct);
                                    }

                                    await _dbContext.SaveChangesAsync(ct);
                                }
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                        catch (Exception ex)
                        {
                            //await _payPalClient.RefundCaptureAsync(order!.PaypalCaptureId!, order.Amount, ct);
                            order!.Status = OrderStatus.Refunded;
                            await _dbContext.SaveChangesAsync(ct);
                            _logger.LogWarning("Failed to provision order: {OrderNo} with error: {ErrorMsg}", dto.orderNo, ex.Message);
                        }

                        break;
                    }
                case "ESIM_STATUS":
                    {
                        var dto = content.Deserialize<EsimStatusContent>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        var orderItem = await _dbContext.OrderItems.SingleOrDefaultAsync(oi => oi.EsimTransNo == dto.esimTranNo && oi.Iccid == dto.iccid, ct);
                        _logger.LogInformation("ESIM_STATUS: iccid={Iccid}, esimTranNo={EsimTranNo}, esimStatus={EsimStatus}, smdpStatus={SmdpStatus}",
                            dto.iccid, dto.esimTranNo, dto.esimStatus, dto.smdpStatus);
                        if (orderItem != null)
                        {
                            orderItem.Status = EsimAccessService.MapEsimAccessStatus(dto.esimStatus).ToString() ?? orderItem.Status;
                            await _dbContext.SaveChangesAsync(ct);
                        }
                        break;
                    }
                case "CHECK_HEALTH":
                    {
                        var dto = content.Deserialize<CheckHealthContent>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        _logger.LogInformation("CHECK_HEALTH: orderNo={OrderNo}, status={Status}", dto.orderNo, dto.orderStatus);

                        await _dbContext.Orders.AddAsync(new Order
                        {
                            CreatedAt = DateTime.Now,
                            DistributorOrderNo = dto.orderNo,
                            Status = OrderStatus.Completed,
                            LocationCode = "test"
                        }, ct);

                        await _dbContext.SaveChangesAsync(ct);
                        break;
                    }
                default:
                    _logger.LogWarning("Unknown notifyType '{NotifyType}'.", notifyType);
                    break;
            }

            await Task.CompletedTask;
        }

        public async Task HandleManetWebhookAsync(ManetWebhookEnvelope request, CancellationToken ct)
        {
            Order order = new();

            try
            {
                if (request != null && !request.OrderError)
                {
                    order = await _dbContext.Orders.SingleOrDefaultAsync(o => o.DistributorOrderNo == request.OrderId, ct)
                        ?? throw new Exception("Order not found");

                    var orderItem = new OrderItem
                    {
                        OrderNo = request.OrderId,
                        ActivationCode = request.Lpa,
                        EsimTransNo = request.SimId,
                        Iccid = request.SimId,
                        PackageName = request.PlanName,
                        QrCodeUrl = request.QrCodeUrl,
                        Status = EsimStatus.Ready.ToString()
                    };

                    await _dbContext.OrderItems.AddAsync(orderItem, ct);
                    await _dbContext.SaveChangesAsync(ct);

                    // Send esim information via Email
                    string CustomerFullName = request.CustomerFirstName + " " + request.CustomerLastName;

                    var pdfGenerated = await _invoiceService.GenerateInvoicePdfAsync(order, CustomerFullName, request.CustomerEmail!, ct);

                    await _emailService.SendEsimEmailAsync(
                        toEmail: request.CustomerEmail!,
                        userName: CustomerFullName,
                        order: order,
                        item: orderItem,
                        invoicePdfBytes: pdfGenerated!,
                        ct: ct);
                }
                else
                {
                    _logger.LogWarning("Failed to create profile for order: {ID}", order.DistributorOrderNo);
                    throw new Exception();
                }
            }
            catch
            {
                decimal Amount = order.RPrice * order.Count * (order.PeriodNum > 0 ? order.PeriodNum : 1);
                var res = await _payPalClient.RefundCaptureAsync(order!.PaypalCaptureId!, Amount, ct: ct);
                order.Status = OrderStatus.Refunded;
                await _dbContext.SaveChangesAsync(ct);
                _logger.LogWarning("Failed to provision order: {OrderNo}", order.DistributorOrderNo);
            }

            await Task.CompletedTask;
        }
    }
}