using BookingGuardian.Data;
using BookingGuardian.Models;
using BookingGuardian.Services;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.BackgroundServices
{
    public class AnomalyDetectionJob : BackgroundService
    {
        private readonly ILogger<AnomalyDetectionJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public AnomalyDetectionJob(
            ILogger<AnomalyDetectionJob> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var intervalMinutes = _configuration.GetValue<int>("AnomalyDetection:IntervalMinutes", 5);
                
                try
                {
                    await DetectAndRecoverAnomaliesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in AnomalyDetectionJob");
                }

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
        }

        public async Task DetectAndRecoverAnomaliesAsync(CancellationToken stoppingToken = default)
        {
            var thresholdMinutes = _configuration.GetValue<int>("AnomalyDetection:ThresholdMinutes", 10);
            var autoRecoveryEnabled = _configuration.GetValue<bool>("AnomalyDetection:AutoRecoveryEnabled", true);

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();

            var thresholdTime = DateTime.UtcNow.AddMinutes(-thresholdMinutes);
            var runTime = DateTime.UtcNow;

            // Identify potential anomalies: SUCCESS payment, PENDING booking, older than threshold
            var stuckBookings = await dbContext.Bookings
                .Where(b => b.PaymentStatus == "SUCCESS" 
                         && b.BookingStatus == "PENDING"
                         && b.PaymentAt < thresholdTime)
                .Where(b => !dbContext.Anomalies.Any(a => a.BookingId == b.Id))
                .ToListAsync();

            var endpointHealthHistory = await dbContext.EndpointHealths
                .Where(e => e.CheckedAt <= runTime)
                .OrderByDescending(e => e.CheckedAt)
                .ToListAsync();

            var healthByEndpoint = endpointHealthHistory
                .GroupBy(e => e.Name)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).ToList());

            if (stuckBookings.Any())
            {
                _logger.LogInformation("Found {Count} stuck bookings. Processing...", stuckBookings.Count);

                foreach (var booking in stuckBookings)
                {
                    var anomaly = new Anomaly
                    {
                        BookingId = booking.Id,
                        DetectedAt = runTime,
                        DetectionRunAt = runTime,
                        Status = "OPEN",
                        EndpointHealthId = ResolveLinkedOutageHealthId(booking.PaymentAt ?? runTime, endpointHealthHistory)
                    };

                    await dbContext.Anomalies.AddAsync(anomaly);
                    await dbContext.SaveChangesAsync();
                }
            }

            // AUTO RECOVERY PHASE (Zero-Touch)
            // Circuit Breaker: If any critical service is DOWN (active outage), inhibit auto-recovery
            var latestHealthStatus = healthByEndpoint.Values
                .Select(h => h.OrderByDescending(x => x.CheckedAt).FirstOrDefault())
                .ToList();

            var activeOutages = latestHealthStatus.Where(h => h?.Status == "DOWN").ToList();
            bool inhibitRecovery = activeOutages.Any();

            if (inhibitRecovery && autoRecoveryEnabled)
            {
                _logger.LogWarning("Active outages detected ({Names}). Inhibiting Zero-Touch Recovery for this run.", 
                    string.Join(", ", activeOutages.Select(o => o?.Name)));
                autoRecoveryEnabled = false;
            }

            var openAnomaliesToRecover = new List<Anomaly>();
            
            if (autoRecoveryEnabled)
            {
                openAnomaliesToRecover = await dbContext.Anomalies
                    .Include(a => a.Booking)
                    .Where(a => a.Status == "OPEN" 
                             && a.Booking.PaymentStatus == "SUCCESS" 
                             && a.Booking.BookingStatus == "PENDING"
                             && a.Booking.PaymentAt < thresholdTime)
                    .ToListAsync();
            }

            if (openAnomaliesToRecover.Any())
            {
                _logger.LogInformation("Zero-Touch Recovery started for {Count} anomalies. Mode: Staggered (1s delay).", openAnomaliesToRecover.Count);

                int recoveredCount = 0;
                foreach (var anomaly in openAnomaliesToRecover)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Recovery process cancelled due to application shutdown.");
                        break;
                    }

                    try
                    {
                        // Staggered Recovery: Give the system 1 second of breathing room between each recovery
                        if (recoveredCount > 0)
                        {
                            await Task.Delay(1000, stoppingToken);
                        }

                        var note = "Auto-recovered by TicketGuard. Payment confirmed, but booking remained pending.";
                        var result = await bookingService.RecoverAnomalyAsync(
                            anomaly.Id,
                            note,
                            "SYSTEM_GUARDIAN",
                            "127.0.0.1",
                            "BookingGuardian/ZeroTouch");
                        if (result.Success)
                        {
                            recoveredCount++;
                            _logger.LogInformation("Successfully auto-recovered anomaly {AnomalyId} for booking {ReferenceNo}.", anomaly.Id, anomaly.Booking?.ReferenceNo);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to auto-recover anomaly {AnomalyId} for booking {ReferenceNo}: {Message}", anomaly.Id, anomaly.Booking?.ReferenceNo, result.Message);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Recovery process cancelled due to application shutdown.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled error while auto-recovering anomaly {AnomalyId}", anomaly.Id);
                    }
                }
            }

            if (stuckBookings.Any() || openAnomaliesToRecover.Any())
            {
                _logger.LogInformation("AnomalyDetectionJob run complete. New={NewCount}, Recovered={RecoverCount} RunAt={Time}", 
                    stuckBookings.Count, openAnomaliesToRecover.Count, runTime);
            }
            else
            {
                _logger.LogDebug("AnomalyDetectionJob run complete. No new issues found.");
            }
        }

        private static int? ResolveLinkedOutageHealthId(DateTime bookingPaymentAt, IEnumerable<EndpointHealth> endpointHealthHistory)
        {
            // Primary window: near the payment point, allowing delayed health checks.
            var primaryWindowStart = bookingPaymentAt.AddMinutes(-5);
            var primaryWindowEnd = bookingPaymentAt.AddMinutes(30);

            var downEndpoint = endpointHealthHistory
                .Where(e => e.Status == "DOWN"
                         && e.CheckedAt >= primaryWindowStart
                         && e.CheckedAt <= primaryWindowEnd)
                .OrderByDescending(e => e.CheckedAt)
                .FirstOrDefault();

            if (downEndpoint != null)
            {
                return downEndpoint.Id;
            }

            // Fallback: include outages that started earlier but still plausibly related.
            var fallbackWindowStart = bookingPaymentAt.AddMinutes(-30);
            var fallbackWindowEnd = bookingPaymentAt.AddMinutes(30);

            return endpointHealthHistory
                .Where(e => e.Status == "DOWN"
                         && e.CheckedAt >= fallbackWindowStart
                         && e.CheckedAt <= fallbackWindowEnd)
                .OrderByDescending(e => e.CheckedAt)
                .Select(e => (int?)e.Id)
                .FirstOrDefault();
        }
    }
}
