using Microsoft.AspNetCore.Identity;

namespace eSim.Infrastructure.Data;
public class ApplicationUser : IdentityUser
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string FullName { get; set; }
    public required string CountryOfResidence { get; set; }
}