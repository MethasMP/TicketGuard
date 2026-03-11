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
builder.Services.Configure<SmsServiceOptions>(builder.Configuration.GetSection("SmsService"));
builder.Services.Configure<MonthlyReportOptions>(builder.Configuration.GetSection("MonthlyReport"));
builder.Services.AddHttpClient<ISmsNotificationService, SmsNotificationService>();
builder.Services.AddHttpClient<IPaymentGatewayService, PaymentGatewayService>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("SupportOrAdmin", policy => policy.RequireRole("Admin", "Supporter"));
});

// JWT Configuration
var jwtSecret = builder.Configuration["JWT_SECRET"] 
    ?? builder.Configuration["JwtSettings:Secret"];

if (!string.IsNullOrEmpty(jwtSecret))
{
    var key = Encoding.ASCII.GetBytes(jwtSecret);
    builder.Services.AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(x =>
    {
        x.RequireHttpsMetadata = false;
        x.SaveToken = true;
        x.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false
        };
        x.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["JWT_TOKEN"];
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                // Skip default 401 response and redirect to login if it's a browser request
                if (context.Request.Headers["Accept"].ToString().Contains("text/html"))
                {
                    context.HandleResponse();
                    context.Response.Redirect("/Account/Login");
                }
                return Task.CompletedTask;
            }
        };
    });
}
else
{
    // Fallback if JWT secret is missing during local dev
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(x => {
        x.Events = new JwtBearerEvents {
            OnChallenge = context => {
                if (context.Request.Headers["Accept"].ToString().Contains("text/html")) {
                    context.HandleResponse();
                    context.Response.Redirect("/Account/Login");
                }
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
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
builder.Services.AddHostedService<BookingGuardian.BackgroundServices.AnomalyDetectionJob>();
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

app.Run();
