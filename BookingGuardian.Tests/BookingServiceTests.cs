using BookingGuardian.Data;
using BookingGuardian.Models;
using BookingGuardian.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BookingGuardian.Tests
{
    public class BookingServiceTests
    {
        private readonly BookingDbContext _dbContext;
        private readonly BookingService _bookingService;

        public BookingServiceTests()
        {
            var options = new DbContextOptionsBuilder<BookingDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            
            _dbContext = new BookingDbContext(options);
            var loggerMock = new Mock<ILogger<BookingService>>();
            var smsMock = new Mock<ISmsNotificationService>();
            smsMock.Setup(s => s.SendRecoveryNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new SmsNotificationResult { Attempted = false, Success = true });
            _bookingService = new BookingService(_dbContext, loggerMock.Object, smsMock.Object);
        }

        [Fact]
        public async Task RecoverBooking_ShouldSucceed_WhenPaymentSuccessAndStatusOpen()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "REF-OK",
                CustomerName = "Test",
                Route = "R",
                OperatorName = "O",
                Amount = 100,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            var anomaly = new Anomaly
            {
                BookingId = booking.Id,
                Status = "OPEN",
                DetectionRunAt = DateTime.UtcNow
            };
            await _dbContext.Anomalies.AddAsync(anomaly);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _bookingService.RecoverAnomalyAsync(anomaly.Id, "Reason for recovery test", "tester@agent.com", "127.0.0.1", "TestAgent");

            // Assert
            Assert.True(result.Success);
            Assert.Equal("CONFIRMED", booking.BookingStatus);
            Assert.Equal("RESOLVED", anomaly.Status);
        }

        [Fact]
        public async Task RecoverBooking_ShouldFail_WhenPaymentNotSuccess()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "REF-FAIL",
                CustomerName = "Test",
                Route = "R",
                OperatorName = "O",
                Amount = 100,
                BookingStatus = "PENDING",
                PaymentStatus = "PENDING", // Not SUCCESS
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            var anomaly = new Anomaly
            {
                BookingId = booking.Id,
                Status = "OPEN",
                DetectionRunAt = DateTime.UtcNow
            };
            await _dbContext.Anomalies.AddAsync(anomaly);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _bookingService.RecoverAnomalyAsync(anomaly.Id, "Reason for recovery test", "tester@agent.com", null, null);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(400, result.StatusCode);
            Assert.Contains("payment status is not SUCCESS", result.Message);
        }

        [Fact]
        public async Task RecoverBooking_ShouldFail_WhenAlreadyResolved()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "REF-ALREADY",
                CustomerName = "Test",
                Route = "R",
                OperatorName = "O",
                Amount = 100,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            var anomaly = new Anomaly
            {
                BookingId = booking.Id,
                Status = "RESOLVED",
                DetectionRunAt = DateTime.UtcNow
            };
            await _dbContext.Anomalies.AddAsync(anomaly);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _bookingService.RecoverAnomalyAsync(anomaly.Id, "Reason for recovery test", "tester@agent.com", null, null);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(409, result.StatusCode);
            Assert.Contains("already RESOLVED", result.Message);
        }

        [Fact]
        public async Task RecoverBooking_ShouldFail_WhenNoteIsTooShort()
        {
            // Act
            var result = await _bookingService.RecoverAnomalyAsync(1, "short", "tester@agent.com", null, null);

            // Assert
            Assert.False(result.Success);
            Assert.Equal(422, result.StatusCode); // Verify 422 as per PRD
            Assert.Contains("at least 10 characters", result.Message);
        }

        [Fact]
        public async Task RecoverBooking_ShouldWriteAuditLog()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "REF-AUDIT",
                CustomerName = "Test",
                Route = "R",
                OperatorName = "O",
                Amount = 100,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            var anomaly = new Anomaly
            {
                BookingId = booking.Id,
                Status = "OPEN",
                DetectionRunAt = DateTime.UtcNow
            };
            await _dbContext.Anomalies.AddAsync(anomaly);
            await _dbContext.SaveChangesAsync();

            // Act
            await _bookingService.RecoverAnomalyAsync(anomaly.Id, "Reason for recovery test", "tester@agent.com", "1.1.1.1", "Chrome");

            // Assert
            var log = await _dbContext.AuditLogs.FirstOrDefaultAsync(l => l.EntityId == booking.Id && l.Action == "RECOVER");
            Assert.NotNull(log);
            Assert.Equal("tester@agent.com", log.PerformedBy);
            Assert.Equal("1.1.1.1", log.IpAddress);
        }

        [Fact]
        public async Task BulkRecover_ShouldRecoverAllAtomically_AndWriteAuditPerBooking()
        {
            var booking1 = new Booking
            {
                ReferenceNo = "BULK-OK-1",
                CustomerName = "One",
                Route = "R",
                OperatorName = "O",
                Amount = 120,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            var booking2 = new Booking
            {
                ReferenceNo = "BULK-OK-2",
                CustomerName = "Two",
                Route = "R",
                OperatorName = "O",
                Amount = 220,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-25)
            };
            await _dbContext.Bookings.AddRangeAsync(booking1, booking2);
            await _dbContext.SaveChangesAsync();

            var anomaly1 = new Anomaly { BookingId = booking1.Id, Status = "OPEN", DetectionRunAt = DateTime.UtcNow };
            var anomaly2 = new Anomaly { BookingId = booking2.Id, Status = "OPEN", DetectionRunAt = DateTime.UtcNow };
            await _dbContext.Anomalies.AddRangeAsync(anomaly1, anomaly2);
            await _dbContext.SaveChangesAsync();

            var result = await _bookingService.BulkRecoverAnomaliesAsync(
                new List<int> { anomaly1.Id, anomaly2.Id },
                "Bulk recovery reason",
                "tester@agent.com",
                "127.0.0.1",
                "TestAgent");

            Assert.True(result.Success);
            Assert.Equal(2, result.AffectedCount);
            Assert.Equal("CONFIRMED", booking1.BookingStatus);
            Assert.Equal("CONFIRMED", booking2.BookingStatus);
            Assert.Equal("RESOLVED", anomaly1.Status);
            Assert.Equal("RESOLVED", anomaly2.Status);

            var auditCount = await _dbContext.AuditLogs.CountAsync(a => a.Action == "RECOVER");
            Assert.Equal(2, auditCount);
        }

        [Fact]
        public async Task BulkRecover_ShouldRollback_WhenAnyBookingPaymentIsInvalid()
        {
            var booking1 = new Booking
            {
                ReferenceNo = "BULK-FAIL-1",
                CustomerName = "One",
                Route = "R",
                OperatorName = "O",
                Amount = 120,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            var booking2 = new Booking
            {
                ReferenceNo = "BULK-FAIL-2",
                CustomerName = "Two",
                Route = "R",
                OperatorName = "O",
                Amount = 220,
                BookingStatus = "PENDING",
                PaymentStatus = "FAILED",
                PaymentAt = DateTime.UtcNow.AddMinutes(-25)
            };
            await _dbContext.Bookings.AddRangeAsync(booking1, booking2);
            await _dbContext.SaveChangesAsync();

            var anomaly1 = new Anomaly { BookingId = booking1.Id, Status = "OPEN", DetectionRunAt = DateTime.UtcNow };
            var anomaly2 = new Anomaly { BookingId = booking2.Id, Status = "OPEN", DetectionRunAt = DateTime.UtcNow };
            await _dbContext.Anomalies.AddRangeAsync(anomaly1, anomaly2);
            await _dbContext.SaveChangesAsync();

            var result = await _bookingService.BulkRecoverAnomaliesAsync(
                new List<int> { anomaly1.Id, anomaly2.Id },
                "Bulk recovery reason",
                "tester@agent.com",
                "127.0.0.1",
                "TestAgent");

            Assert.False(result.Success);
            Assert.Equal("PENDING", booking1.BookingStatus);
            Assert.Equal("PENDING", booking2.BookingStatus);
            Assert.Equal("OPEN", anomaly1.Status);
            Assert.Equal("OPEN", anomaly2.Status);
            Assert.Equal(0, await _dbContext.AuditLogs.CountAsync(a => a.Action == "RECOVER"));
        }
    }
}
