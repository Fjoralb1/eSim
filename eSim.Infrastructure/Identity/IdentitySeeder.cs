using eSim.Core.Constants;
using eSim.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace eSim.Infrastructure.Identity;

public class IdentitySeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public IdentitySeeder(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
    {
        _roleManager = roleManager;
        _userManager = userManager;
    }

    public async Task SeedAsync(string adminEmail, string adminPassword, string? adminFullName = null)
    {
        // Ensure roles
        foreach (var role in new[] { RoleNames.Admin, RoleNames.Reseller, RoleNames.Customer })
            if (!await _roleManager.Roles.AnyAsync(r => r.Name == role))
                await _roleManager.CreateAsync(new IdentityRole(role));

        // Ensure admin
        var admin = await _userManager.FindByEmailAsync(adminEmail);

        if (admin == null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = adminFullName ?? "Super Admin",
                CountryOfResidence = "Admin",
                FirstName = "Super",
                LastName = "Admin"
            };
            var create = await _userManager.CreateAsync(admin, adminPassword);
            if (!create.Succeeded) throw new Exception(string.Join("; ", create.Errors.Select(e => e.Description)));
        }

        // Ensure admin in Admin role
        if (!await _userManager.IsInRoleAsync(admin, RoleNames.Admin))
            await _userManager.AddToRoleAsync(admin, RoleNames.Admin);
    }
}