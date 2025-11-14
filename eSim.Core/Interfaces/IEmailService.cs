using eSim.Core.Models;

namespace eSim.Core.Interfaces;

public interface IEmailService
{
    Task SendEsimEmailAsync(string toEmail, string userName, Order order, OrderItem item, byte[] invoicePdfBytes, CancellationToken ct);
    Task SendPasswordResetEmailAsync(string toEmail, string userName, string resetUrl, CancellationToken ct);
    Task SendEmailConfirmationAsync(string toEmail, string userName, string confirmationLink, CancellationToken ct);
    Task SendSupportContactEmailAsync(ContactRequest contactRequest, CancellationToken ct);
}