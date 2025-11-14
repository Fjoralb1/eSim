using DinkToPdf;
using DinkToPdf.Contracts;
using eSim.Core.Interfaces;
using eSim.Infrastructure.Adapters;
using eSim.Infrastructure.Data;
using eSim.Infrastructure.Identity;
using eSim.Infrastructure.Services;
using eSim.Infrastructure.Services.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

namespace eSim.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---- Serilog setup ----
        SerilogSetup(builder);

        // ---- CORS setup ----
        CorsPolicySetup(builder);

        // AppDbContext and Identity setup
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                builder.Configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly("eSim.Infrastructure")
        ));

        builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // JWT Authentication setup
        AddAuthentication(builder);

        // Self Created Services
        builder.Services.AddScoped<DistributorManager>();
        builder.Services.AddHttpClient<EsimAccessService>();
        builder.Services.AddHttpClient<ManetService>();
        builder.Services.AddScoped<IOrderWorkflow, OrderWorkflow>();
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IInvoiceService, InvoiceService>();
        builder.Services.AddScoped<IPayPalClient, PayPalServerSdkClient>();
        builder.Services.AddHostedService<UnpaidOrderCleanupWorker>();

        // Auto Generated Services
        builder.Services.AddScoped<IdentitySeeder>();
        builder.Services.AddSingleton<IConverter>(new SynchronizedConverter(new PdfTools()));
        builder.Services.AddMemoryCache();
        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        if (app.Environment.IsProduction())
        {
            app.UseExceptionHandler("/Error");
            app.UseHttpsRedirection();
            app.UseHsts();
        }

        // ?? Add this to serve your "EmailTemplates" folder
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmailTemplates")
            ),
            RequestPath = "/EmailTemplates" // this becomes the URL path prefix
        });

        app.UseSerilogRequestLogging();
        app.UseCors("CorsPolicy");
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapGet("/", () => Results.Ok("API is running"));

        await SeedAdmin(app);

        app.Run();
    }

    private static void CorsPolicySetup(WebApplicationBuilder builder)
    {
        // ---- CORS setup ----
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy", policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            });
        });
    }

    private static void SerilogSetup(WebApplicationBuilder builder)
    {
        // ---- Serilog setup ----
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                path: "Logs/app-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                shared: true
            )
            .CreateLogger();

        builder.Host.UseSerilog();
    }

    private static async Task SeedAdmin(WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var email = config["AdminSeed:Email"] ?? "admin@yourdomain.com";
        var pwd = config["AdminSeed:Password"] ?? "Admin!2345";
        await seeder.SeedAsync(email, pwd, "Super Admin");
    }

    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        // ------------- JWT AUTHENTICATION -------------
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Secret"]!);

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero // no delay on token expiration
            };
        });
    }
}