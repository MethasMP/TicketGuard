using BookingGuardian.BackgroundServices;
using BookingGuardian.Data;
using BookingGuardian.Models;
using BookingGuardian.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace BookingGuardian.Tests
{
    public class AnomalyDetectionTests
    {
        private readonly BookingDbContext _dbContext;
        private readonly AnomalyDetectionJob _job;
        private readonly ServiceProvider _serviceProvider;
        private readonly Mock<IBookingService> _bookingServiceMock;
        private readonly Mock<IPaymentGatewayService> _paymentGatewayMock;

        public AnomalyDetectionTests()
        {
            var options = new DbContextOptionsBuilder<BookingDbContext>()
                 .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                 .Options;
            
            _dbContext = new BookingDbContext(options);
            _bookingServiceMock = new Mock<IBookingService>();
            _paymentGatewayMock = new Mock<IPaymentGatewayService>();

            var services = new ServiceCollection();
            services.AddSingleton(_dbContext);
            services.AddSingleton(_bookingServiceMock.Object);
            services.AddSingleton(_paymentGatewayMock.Object);
            _serviceProvider = services.BuildServiceProvider();

            var loggerMock = new Mock<ILogger<AnomalyDetectionJob>>();
            var configMock = new Mock<IConfiguration>();
            
            // Fix: Mock values correctly based on C# GetValue expectations
            configMock.Setup(c => c.GetSection("AnomalyDetection:ThresholdMinutes").Value).Returns("10");
            configMock.Setup(c => c.GetSection("AnomalyDetection:IntervalMinutes").Value).Returns("5");
            configMock.Setup(c => c.GetSection("AnomalyDetection:AutoRecoveryEnabled").Value).Returns("true");

            _bookingServiceMock.Setup(b => b.RecoverAnomalyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new AnomalyResponse { Success = true, Message = "OK" });

            _job = new AnomalyDetectionJob(loggerMock.Object, _serviceProvider, configMock.Object);
        }

        [Fact]
        public async Task DetectAnomalies_ShouldFlag_WhenPaymentSuccessAndBookingPending()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "STUCK-1",
                CustomerName = "Stuck",
                Route = "R",
                OperatorName = "O",
                Amount = 10,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20) // Older than 10m threshold
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            // Act
            await _job.DetectAndRecoverAnomaliesAsync();

            // Assert
            var anomaly = await _dbContext.Anomalies.FirstOrDefaultAsync(a => a.BookingId == booking.Id);
            Assert.NotNull(anomaly);
            Assert.Equal("OPEN", anomaly.Status);
        }

        [Fact]
        public async Task DetectAnomalies_ShouldNotFlag_WhenBookingAlreadyConfirmed()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "CONFIRMED-1",
                CustomerName = "C",
                Route = "R",
                OperatorName = "O",
                Amount = 10,
                BookingStatus = "CONFIRMED",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            // Act
            await _job.DetectAndRecoverAnomaliesAsync();

            // Assert
            var anomaly = await _dbContext.Anomalies.FirstOrDefaultAsync(a => a.BookingId == booking.Id);
            Assert.Null(anomaly);
        }

        [Fact]
        public async Task DetectAnomalies_ShouldNotDuplicate_WhenAnomalyAlreadyExists()
        {
            // Arrange
            var booking = new Booking
            {
                ReferenceNo = "EXISTING-1",
                CustomerName = "E",
                Route = "R",
                OperatorName = "O",
                Amount = 10,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            var existingAnomaly = new Anomaly
            {
                BookingId = booking.Id,
                Status = "OPEN",
                DetectedAt = DateTime.UtcNow.AddMinutes(-5)
            };
            await _dbContext.Anomalies.AddAsync(existingAnomaly);
            await _dbContext.SaveChangesAsync();

            // Act
            await _job.DetectAndRecoverAnomaliesAsync();

            // Assert
            var count = await _dbContext.Anomalies.CountAsync(a => a.BookingId == booking.Id);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task DetectAnomalies_ShouldLinkEndpointHealth_WhenEndpointWasDownAtPaymentTime()
        {
            var downHealth = new EndpointHealth
            {
                Name = "Payment Gateway",
                Url = "https://payment.local/health",
                Status = "DOWN",
                HttpCode = 503,
                CheckedAt = DateTime.UtcNow.AddMinutes(-30)
            };
            await _dbContext.EndpointHealths.AddAsync(downHealth);
            await _dbContext.SaveChangesAsync();

            var booking = new Booking
            {
                ReferenceNo = "OUTAGE-1",
                CustomerName = "Outage User",
                Route = "R",
                OperatorName = "O",
                Amount = 10,
                BookingStatus = "PENDING",
                PaymentStatus = "SUCCESS",
                PaymentAt = DateTime.UtcNow.AddMinutes(-20)
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            await _job.DetectAndRecoverAnomaliesAsync();

            var anomaly = await _dbContext.Anomalies.FirstOrDefaultAsync(a => a.BookingId == booking.Id);
            Assert.NotNull(anomaly);
            Assert.Equal(downHealth.Id, anomaly!.EndpointHealthId);
        }
    }
}
