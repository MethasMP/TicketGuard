using BookingGuardian.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DevToolsController : ControllerBase
    {
        private readonly BookingDbContext _dbContext;

        public DevToolsController(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost("seed-test-anomaly")]
        public async Task<IActionResult> SeedTestAnomaly()
        {
            // 1. Create a Booking that matches the recovery criteria:
            // PaymentStatus = SUCCESS, BookingStatus = PENDING, PaymentAt < thresholdTime
            var booking = new Models.Booking
            {
                ReferenceNo = "DEV-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
                CustomerName = "Test User",
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
            var anomaly = new Models.Anomaly
            {
                BookingId = booking.Id,
                DetectedAt = DateTime.UtcNow,
                Status = "OPEN",
                Note = "Manual seed for testing Auto-Recovery"
            };

            _dbContext.Anomalies.Add(anomaly);
            await _dbContext.SaveChangesAsync();

            return Ok(new { 
                message = "Test data seeded successfully", 
                bookingId = booking.Id, 
                referenceNo = booking.ReferenceNo,
                anomalyId = anomaly.Id
            });
        }
    }
}
