using System.Net.Mail;
using System.Net.Mime;
using eSim.Core.Models;
using eSim.Core.Interfaces;
using QRCoder;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Web;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using Azure.Core;

namespace eSim.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly string _templatesPath;
        public IConfiguration _cfg { get; }
        private readonly string EmailFrom;
        private readonly string SmtpClient;
        private readonly string SmtpUser;
        private readonly string SmtpPassword;
        public readonly string PrivacyUrl;
        public readonly string TermsUrl;

        public EmailService(IConfiguration configuration)
        {
            _cfg = configuration;

            _templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmailTemplates");
            EmailFrom = _cfg["Smtp:EmailFrom"]!;
            SmtpClient = _cfg["Smtp:SmtpClient"]!;
            SmtpUser = _cfg["Smtp:SmtpUser"]!;
            SmtpPassword = _cfg["Smtp:SmtpPass"]!;
            PrivacyUrl = _cfg["App:PublicBaseUrl"] + _cfg["CompanyInformation:PrivacyUrl"];
            TermsUrl = _cfg["App:PublicBaseUrl"] + _cfg["CompanyInformation:TermsUrl"];
        }

        public async Task SendEsimEmailAsync(string toEmail, string userName, Order order, OrderItem item, byte[] invoicePdfBytes, CancellationToken ct)
        {
            var template = await File.ReadAllTextAsync(Path.Combine(_templatesPath, "EsimReadyEmail.html"), ct);

            decimal CalcAmount = order.RPrice * order.Count * (order.PeriodNum > 0 ? order.PeriodNum : 1);

            var body = template
            .Replace("{{CustomerName}}", userName)
            .Replace("{{OrderNumber}}", order.DistributorOrderNo)
            .Replace("{{OrderDate}}", order.CreatedAt.ToString("dd MMM yyyy"))
            .Replace("{{PaymentMethod}}", "PayPal")
            .Replace("{{PackageName}}", order.PackageName)
            .Replace("{{OrderTotal}}", CalcAmount.ToString("F2"))
            .Replace("{{WebsiteTermsUrl}}", TermsUrl)
            .Replace("{{PrivacyPolicyUrl}}", PrivacyUrl);

            using var message = new MailMessage(EmailFrom, toEmail)
            {
                Subject = $"Your eSIM {item.PackageName} is ready",
                IsBodyHtml = true,
                Body = body
            };

            var qrPath = await GenerateQrCode(item.ActivationCode!, item.EsimTransNo!);

            var images = new Dictionary<string, string>
            {
                { "email_banner", Path.Combine(_templatesPath, "Assets", "Images", "Main_header.png") },
                { "footer_logo", Path.Combine(_templatesPath, "Assets", "Images", "mobysim_logo.png") },
                { "qrcode", qrPath }
            };

            var alternateView = CreateHtmlViewWithMultipleImages(body, images);
            message.AlternateViews.Add(alternateView);

            var qrAttachment = new Attachment(qrPath, MediaTypeNames.Image.Png) { Name = "esim-qr.png" };
            message.Attachments.Add(qrAttachment);

            if (invoicePdfBytes != null)
            {
                var ms = new MemoryStream(invoicePdfBytes); // keep open until after Send
                var attachment = new Attachment(ms, $"invoice_{item.OrderNo}.pdf", MediaTypeNames.Application.Pdf);
                attachment.ContentDisposition!.Inline = false;   // set true if you want inline
                attachment.ContentDisposition.FileName = $"invoice_{item.OrderNo}.pdf";
                message.Attachments.Add(attachment);
            }

            using var smtp = new SmtpClient(SmtpClient)
            {
                Credentials = !string.IsNullOrEmpty(SmtpUser)
                ? new NetworkCredential(SmtpUser, SmtpPassword)
                : CredentialCache.DefaultNetworkCredentials,
                EnableSsl = bool.Parse(_cfg["Smtp:EnableSsl"]!),
                Port = int.Parse(_cfg["Smtp:Port"]!)
            };

            await smtp.SendMailAsync(message, ct);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetUrl, CancellationToken ct = default)
        {
            var template = await File.ReadAllTextAsync(Path.Combine(_templatesPath, "PasswordResetEmail.html"), ct);

            var body = template
                .Replace("{{CustomerName}}", userName)
                .Replace("{{ResetUrl}}", resetUrl)
                .Replace("{{WebsiteTermsUrl}}", TermsUrl)
                .Replace("{{PrivacyPolicyUrl}}", PrivacyUrl);

            using var message = new MailMessage(EmailFrom, toEmail)
            {
                Subject = "Reset your MobySim password",
                IsBodyHtml = true,
                Body = body
            };

            var images = new Dictionary<string, string>
            {
                { "email_banner", Path.Combine(_templatesPath, "Assets", "Images", "EmailHeader.png") },
                { "footer_logo", Path.Combine(_templatesPath, "Assets", "Images", "mobysim_logo.png") }
            };

            var alternateView = CreateHtmlViewWithMultipleImages(body, images);
            message.AlternateViews.Add(alternateView);

            using var smtp = new SmtpClient(SmtpClient)
            {
                Credentials = !string.IsNullOrEmpty(SmtpUser)
                ? new NetworkCredential(SmtpUser, SmtpPassword)
                : CredentialCache.DefaultNetworkCredentials,
                EnableSsl = bool.Parse(_cfg["Smtp:EnableSsl"]!),
                Port = int.Parse(_cfg["Smtp:Port"]!)
            };

            await smtp.SendMailAsync(message, ct);
        }

        public async Task SendEmailConfirmationAsync(string toEmail, string userName, string confirmationLink, CancellationToken ct)
        {
            var template = await File.ReadAllTextAsync(Path.Combine(_templatesPath, "ConfirmEmail.html"), ct);

            var body = template
                .Replace("{{CustomerName}}", userName)
                .Replace("{{ConfirmUrl}}", confirmationLink)
                .Replace("{{WebsiteTermsUrl}}", TermsUrl)
                .Replace("{{PrivacyPolicyUrl}}", PrivacyUrl);

            var message = new MailMessage(EmailFrom, toEmail)
            {
                Subject = "Confirm your MobySim account",
                IsBodyHtml = true,
                Body = body
            };

            var images = new Dictionary<string, string>
            {
                { "email_banner", Path.Combine(_templatesPath, "Assets", "Images", "EmailHeader.png") },
                { "footer_logo", Path.Combine(_templatesPath, "Assets", "Images", "mobysim_logo.png") }
            };

            var alternateView = CreateHtmlViewWithMultipleImages(body, images);
            message.AlternateViews.Add(alternateView);

            using var smtp = new SmtpClient(SmtpClient)
            {
                Credentials = !string.IsNullOrEmpty(SmtpUser)
                ? new NetworkCredential(SmtpUser, SmtpPassword)
                : CredentialCache.DefaultNetworkCredentials,
                EnableSsl = bool.Parse(_cfg["Smtp:EnableSsl"]!),
                Port = int.Parse(_cfg["Smtp:Port"]!)
            };

            await smtp.SendMailAsync(message, ct);
        }

        public async Task SendSupportContactEmailAsync(ContactRequest req, CancellationToken ct = default)
        {
            // Build subject + HTML safely
            string subject = $"[Contact] {req.topic ?? "General"} ({req.topickey}) - {req.name}";
            static string enc(string? s) => HttpUtility.HtmlEncode(s ?? string.Empty);

            string html =
                $"<p><b>Name:</b> {enc(req.name)}</p>" +
                $"<p><b>Email:</b> {enc(req.email)}</p>" +
                $"<p><b>Phone:</b> {enc(req.phone)}</p>" +
                $"<p><b>Topic:</b> {enc(req.topic)} ({enc(req.topickey)})</p>" +
                $"<p><b>Message:</b><br/>{enc(req.message).Replace("\n", "<br/>")}</p>";

            using var msg = new MailMessage(EmailFrom, _cfg["CompanyInformation:Email"]!)
            {
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };

            // Make replies go to the customer
            if (!string.IsNullOrWhiteSpace(req.email))
                msg.ReplyToList.Add(new MailAddress(req.email, req.name));

            using var smtp = new SmtpClient(SmtpClient)
            {
                Credentials = !string.IsNullOrEmpty(SmtpUser)
                ? new NetworkCredential(SmtpUser, SmtpPassword)
                : CredentialCache.DefaultNetworkCredentials,
                EnableSsl = bool.Parse(_cfg["Smtp:EnableSsl"]!),
                Port = int.Parse(_cfg["Smtp:Port"]!)
            };

            await smtp.SendMailAsync(msg, ct);
        }

        public static AlternateView CreateHtmlViewWithMultipleImages(string htmlBody, Dictionary<string, string> images)
        {
            var view = AlternateView.CreateAlternateViewFromString(
                htmlBody,
                Encoding.UTF8,
                "text/html"
            );

            foreach (var image in images)
            {
                string contentId = image.Key;
                string imagePath = image.Value;

                if (File.Exists(imagePath))
                {
                    var resource = new LinkedResource(imagePath, "image/png")
                    {
                        ContentId = contentId,
                        TransferEncoding = TransferEncoding.Base64
                    };
                    resource.ContentType.Name = Path.GetFileName(imagePath);
                    view.LinkedResources.Add(resource);
                }
            }

            return view;
        }

        public static async Task<string> GenerateQrCode(string lpaContent, string esimTranNo)
        {
            // Create a generator
            using var qrGenerator = new QRCodeGenerator();

            // Generate QR code data
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(lpaContent, QRCodeGenerator.ECCLevel.Q);

            // Render QR code as bitmap
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrBytes = qrCode.GetGraphic(20);

            // Save to file (optional, if you want to attach as image file)
            var filePath = Path.Combine(Path.GetTempPath(), $"{esimTranNo}.png");
            await File.WriteAllBytesAsync(filePath, qrBytes);

            return filePath;
        }
    }
}