using BookingGuardian.Data;
using BookingGuardian.Helpers;
using BookingGuardian.BackgroundServices;
using BookingGuardian.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options => 
{
    options.HeaderName = "RequestVerificationToken";
});
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IMonthlyPdfReportService, MonthlyPdfReportService>();
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();
builder.Services.AddSingleton<ISystemModeService, SystemModeService>();
builder.Services.Configure<SmsServiceOptions>(builder.Configuration.GetSection("SmsService"));
builder.Services.Configure<MonthlyReportOptions>(builder.Configuration.GetSection("MonthlyReport"));
builder.Services.AddHttpClient<ISmsNotificationService, SmsNotificationService>();
builder.Services.AddHttpClient<IPaymentGatewayService, PaymentGatewayService>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("SupportOrAdmin", policy => policy.RequireRole("Admin", "Supporter"));
});

// TicketGuard Identity Root (JWT)
var secretKey = builder.Configuration["JWT_SECRET"] 
               ?? builder.Configuration["JwtSettings:Secret"];

if (string.IsNullOrEmpty(secretKey)) {
    secretKey = "TicketGuard_Default_Dev_Secret_Unsafe_Placeholder";
}

var keyBytes = Encoding.ASCII.GetBytes(secretKey);

builder.Services.AddAuthentication(options => {
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => {
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.FromMinutes(5)
    };
    options.Events = new JwtBearerEvents {
        OnMessageReceived = context => {
            context.Token = context.Request.Cookies["JWT_TOKEN"];
            return Task.CompletedTask;
        },
        OnChallenge = context => {
            if (context.Request.Headers["Accept"].ToString().Contains("text/html")) {
                context.HandleResponse();
                context.Response.Redirect("/Account/Login");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.Cookie.Name = "TicketGuard_Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.Path = "/";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
});

// Database Configuration
var connectionString = builder.Configuration["DB_CONNECTION_STRING"] 
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<BookingDbContext>(options =>
        options.UseMySql(
            connectionString,
            new MySqlServerVersion(new Version(8, 0, 36)),
            mysqlOptions => mysqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));
}

builder.Services.AddHealthChecks();

// Background Services
builder.Services.AddSingleton<AnomalyDetectionJob>();
builder.Services.AddHostedService<AnomalyDetectionJob>(sp => sp.GetRequiredService<AnomalyDetectionJob>());
builder.Services.AddHostedService<BookingGuardian.BackgroundServices.EndpointHealthCheckJob>();
builder.Services.AddHostedService<MonthlyReportEmailJob>();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var connectSrc = app.Environment.IsDevelopment()
        ? "connect-src 'self' https://cdn.jsdelivr.net https://unpkg.com ws://localhost:* wss://localhost:*;"
        : "connect-src 'self' https://cdn.jsdelivr.net https://unpkg.com;";

    var csp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://unpkg.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "frame-ancestors 'none'; " +
        connectSrc;

    context.Response.Headers["Content-Security-Policy"] = csp;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/healthz");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Resilient Database Update & Seeding
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try 
    {
        // 1. Repair Migration History if tables exist but history is missing
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
                `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
                `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
                PRIMARY KEY (`MigrationId`)
            ) CHARACTER SET=utf8mb4;
        ");

        // Use a more reliable way to check if tables exist
        bool hasBookings = false;
        using (var command = context.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'bookings'";
            await context.Database.OpenConnectionAsync();
            var result = await command.ExecuteScalarAsync();
            hasBookings = Convert.ToInt32(result) > 0;
            await context.Database.CloseConnectionAsync();
        }

        if (hasBookings) 
        {
             // Fail-safe: Ensure CustomerEmail column exists regardless of migration history
             using (var command = context.Database.GetDbConnection().CreateCommand())
             {
                 command.CommandText = @"
                    SELECT COUNT(*) FROM information_schema.columns 
                    WHERE table_schema = DATABASE() 
                    AND table_name = 'bookings' 
                    AND column_name IN ('customer_email', 'CustomerEmail')";
                 
                 await context.Database.OpenConnectionAsync();
                 var columnExists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
                 if (!columnExists)
                 {
                     command.CommandText = "ALTER TABLE bookings ADD COLUMN CustomerEmail varchar(100) NULL";
                     await command.ExecuteNonQueryAsync();
                     logger.LogInformation("Repair: added missing 'CustomerEmail' column to 'bookings' table.");
                 }
                 await context.Database.CloseConnectionAsync();
             }

             await context.Database.ExecuteSqlRawAsync(@"
                INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) 
                VALUES ('20260315140327_InitialCreate', '8.0.0');
            ");
        }

        // 2. Now run pending migrations
        await context.Database.MigrateAsync();
        
        // 3. Seed Data
        await context.SeedAsync();
        
        logger.LogInformation("TicketGuard Core Engine: Database Synced & Ready.");
    }
    catch (Exception ex)
    {
        // If it still fails with "Table already exists", we probably have a mismatch. 
        // We'll try to baseline the history and let the user continue.
        if (ex.InnerException?.Message.Contains("already exists") == true || ex.Message.Contains("already exists"))
        {
            logger.LogWarning("Tables already exist. Attempting to baseline migration history.");
            await context.Database.ExecuteSqlRawAsync(@"
                INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) 
                VALUES ('20260315140327_InitialCreate', '8.0.0'),
                       ('20260315140354_AddCustomerEmailToExistingTable', '8.0.0');
            ");
        }
        else if (ex.Message.Contains("Duplicate column name"))
        {
            await context.Database.ExecuteSqlRawAsync(@"
                INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) 
                VALUES ('20260315140354_AddCustomerEmailToExistingTable', '8.0.0');
            ");
        }
        else 
        {
            logger.LogError(ex, "Database initialization encounterd an issue. Trying to proceed anyway.");
        }
    }
}

app.Run();
