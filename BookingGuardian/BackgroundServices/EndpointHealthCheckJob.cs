using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.BackgroundServices
{
    public class EndpointHealthCheckJob : BackgroundService
    {
        private readonly ILogger<EndpointHealthCheckJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpClient _httpClient;
        private readonly List<EndpointConfig> _endpoints;
        private readonly int _intervalMinutes;

        public EndpointHealthCheckJob(
            ILogger<EndpointHealthCheckJob> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            
            _endpoints = configuration.GetSection("HealthCheck:Endpoints").Get<List<EndpointConfig>>() 
                         ?? new List<EndpointConfig>();
            _intervalMinutes = configuration.GetValue<int>("HealthCheck:IntervalMinutes", 5);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("EndpointHealthCheckJob started. Interval: {Interval}m", _intervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckHealthAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in EndpointHealthCheckJob");
                }

                await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
            }
        }

        private async Task CheckHealthAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

            foreach (var config in _endpoints)
            {
                // To avoid redundant data from accidental double-runs or quick restarts, 
                // check if we just recorded this endpoint less than a minute ago.
                var lastRecord = await dbContext.EndpointHealths
                    .Where(h => h.Name == config.Name)
                    .OrderByDescending(h => h.CheckedAt)
                    .FirstOrDefaultAsync();

                if (lastRecord != null && (DateTime.UtcNow - lastRecord.CheckedAt).TotalMinutes < 1)
                {
                    _logger.LogDebug("Skipping health check for {Name}, last check was too recent.", config.Name);
                    continue;
                }

                var healthRecord = new EndpointHealth
                {
                    Name = config.Name,
                    Url = config.Url,
                    CheckedAt = DateTime.UtcNow
                };

                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var response = await _httpClient.GetAsync(config.Url);
                    watch.Stop();
                    
                    healthRecord.ResponseMs = (int)watch.ElapsedMilliseconds;
                    healthRecord.HttpCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        healthRecord.Status = healthRecord.ResponseMs < 2000 ? "UP" : "DEGRADED";
                    }
                    else
                    {
                        healthRecord.Status = "DOWN";
                    }
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    _logger.LogWarning("Endpoint health check failed. Name={Name} Url={Url} Error={Error}", 
                        config.Name, config.Url, ex.Message);
                    healthRecord.Status = "DOWN";
                    healthRecord.ResponseMs = null;
                    healthRecord.HttpCode = null;
                }

                if (lastRecord != null)
                {
                    var unchangedStatus = lastRecord.Status == healthRecord.Status &&
                                          lastRecord.HttpCode == healthRecord.HttpCode;
                    var minutesSinceLast = (DateTime.UtcNow - lastRecord.CheckedAt).TotalMinutes;

                    // Keep transition records immediately, but suppress repetitive unchanged snapshots
                    // within 60 minutes to avoid data inflation.
                    if (unchangedStatus && minutesSinceLast < 60)
                    {
                        _logger.LogDebug(
                            "Skipping persisted health snapshot for {Name}: unchanged status={Status} code={HttpCode} within {Minutes:F1}m.",
                            config.Name,
                            healthRecord.Status,
                            healthRecord.HttpCode,
                            minutesSinceLast);
                        continue;
                    }
                }

                await dbContext.EndpointHealths.AddAsync(healthRecord);
            }

            await dbContext.SaveChangesAsync();
        }

        public class EndpointConfig
        {
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }
    }
}
