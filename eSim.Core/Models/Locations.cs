using System.ComponentModel.DataAnnotations;

namespace eSim.Core.Models
{
    public class Locations
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int Type { get; set; }
        public int Ordering { get; set; }
    }
}
