using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using BookingGuardian.Models;
using Microsoft.AspNetCore.Authorization;

namespace BookingGuardian.Controllers;

[AllowAnonymous]
[Authorize(Policy = "SupportOrAdmin")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly Data.BookingDbContext _dbContext;

    public HomeController(ILogger<HomeController> logger, Data.BookingDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [AllowAnonymous]
    public async Task<IActionResult> SeedTestAnomaly()
    {
        // 1. Create a Booking that matches the recovery criteria:
        // PaymentStatus = SUCCESS, BookingStatus = PENDING, PaymentAt < thresholdTime
        var booking = new Booking
        {
            ReferenceNo = "DEV-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
            CustomerName = "Test User " + DateTime.UtcNow.ToString("HH:mm:ss"),
            Route = "Bangkok - Chiang Mai",
            OperatorName = "Green Bus",
            PassengerCount = 1,
            TravelDate = DateTime.UtcNow.AddDays(7),
            Amount = 550.00m,
            PaymentStatus = "SUCCESS",
            BookingStatus = "PENDING",
            PaymentAt = DateTime.UtcNow.AddMinutes(-5), // Older than 1 minute threshold
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync();

        // 2. Create an Anomaly record for this booking
        var anomaly = new Anomaly
        {
            BookingId = booking.Id,
            DetectedAt = DateTime.UtcNow,
            Status = "OPEN",
            Note = "Manual seed for testing Auto-Recovery"
        };

        _dbContext.Anomalies.Add(anomaly);
        await _dbContext.SaveChangesAsync();

        return Content($"SUCCESS: Seeded Booking {booking.ReferenceNo} and Anomaly {anomaly.Id}. Wait ~1 min for TicketGuard to recover.");
    }
}
