using eSim.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eSim.Core.Interfaces
{
    public interface IInvoiceService
    {
        Task<byte[]> GenerateInvoicePdfAsync(Order order, string userName, string userEmail, CancellationToken ct);
    }
}
