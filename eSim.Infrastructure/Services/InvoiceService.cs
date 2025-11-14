using DinkToPdf;
using DinkToPdf.Contracts;
using eSim.Core.Interfaces;
using eSim.Core.Models;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Net;
using System.Text;

namespace eSim.Infrastructure.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly string _templatesPath;
        private readonly IConverter _converter;
        private readonly IConfiguration cfg;

        public InvoiceService(IConverter converter, IConfiguration configuration)
        {
            _templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmailTemplates");
            _converter = converter;
            cfg = configuration;
        }

        public async Task<byte[]> GenerateInvoicePdfAsync(Order order, string userName, string userEmail, CancellationToken ct)
        {
            var templatePath = Path.Combine(_templatesPath, "InvoiceTemplate.html");
            var html = await File.ReadAllTextAsync(templatePath, ct);

            // Build items table rows
            var rows = new StringBuilder();

            decimal CalcAmount = order.RPrice * order.Count * (order.PeriodNum > 0 ? order.PeriodNum : 1);

            // HTML-encode text values to avoid layout breaks / XSS in emails
            string name = WebUtility.HtmlEncode(order.PackageName!);

            rows.AppendLine($@"
                  <tr>
                    <td>{order.Count}</td>
                    <td>{name}</td>
                    <td>€{order.RPrice:F2}</td>
                    <td>€{CalcAmount:F2}</td>
                  </tr>");

            html = html.Replace("{{LogoBase64}}", await AddLogo(ct))
                .Replace("{{CompanyName}}", WebUtility.HtmlEncode(cfg["CompanyInformation:Name"]))
                .Replace("{{CustomerName}}", WebUtility.HtmlEncode(userName))
                .Replace("{{CustomerEmail}}", WebUtility.HtmlEncode(userEmail))
                .Replace("{{CustomerEmail}}", WebUtility.HtmlEncode(userEmail))
                .Replace("{{OrderNumber}}", order.DistributorOrderNo)
                .Replace("{{OrderTotal}}", CalcAmount.ToString("F2"))
                .Replace("{{OrderDate}}", order.CreatedAt.ToString("dd MMM yyyy, HH:mm"))
                .Replace("{{ItemsRows}}", rows.ToString())
                .Replace("{{SupportEmail}}", cfg["CompanyInformation:Email"]);

            // Generate PDF bytes (no temp file)
            var doc = new HtmlToPdfDocument
            {
                GlobalSettings = new GlobalSettings
                {
                    PaperSize = PaperKind.A4,
                    Orientation = Orientation.Portrait,
                    Out = null,
                    Margins = new MarginSettings { Top = 10, Right = 10, Bottom = 10, Left = 10 }
                },
                Objects =
                {
                    new ObjectSettings
                    {
                        HtmlContent = html,
                        WebSettings = new WebSettings
                        {
                            DefaultEncoding = "utf-8",
                            LoadImages = true
                        }
                    }
                }
            };

            var pdfBytes = _converter.Convert(doc);
            return pdfBytes;
        }

        private async Task<string> AddLogo(CancellationToken ct)
        {
            var pngPath = Path.Combine(_templatesPath, "Assets", "Images", "mobysim_logo2.png");
            var bytes = await File.ReadAllBytesAsync(pngPath, ct);
            var b64 = Convert.ToBase64String(bytes);

            return $"data:image/png;base64,{b64}";
        }
    }
}