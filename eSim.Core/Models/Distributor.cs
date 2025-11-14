using System.ComponentModel.DataAnnotations;

namespace eSim.Core.Models;

public class Distributor
{
    [Key]
    public int DistributorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}