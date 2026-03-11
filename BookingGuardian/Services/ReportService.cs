using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Services
{
    public interface IReportService
    {
        Task<ReportsPageViewModel> BuildReportsPageAsync(string range);
        Task<MonthlyReportData> BuildMonthlyReportDataAsync(int year, int month);
        Task<string> GenerateMonthlyReportCsvAsync(int year, int month);
    }

    public class ReportService : IReportService
    {
        private readonly BookingDbContext _dbContext;

        public ReportService(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ReportsPageViewModel> BuildReportsPageAsync(string range)
        {
            var (start, end, normalizedRange) = ResolveRange(range);
            var model = new ReportsPageViewModel
            {
                SelectedRange = normalizedRange,
                StartDateUtc = start,
                EndDateUtcExclusive = end,
                DownloadMonth = start.ToString("yyyy-MM")
            };

            var anomaliesQuery = _dbContext.Anomalies
                .Include(a => a.Booking)
                .Include(a => a.EndpointHealth)
                .Where(a => a.DetectedAt >= start && a.DetectedAt < end);

            var anomalies = await anomaliesQuery.ToListAsync();
            model.TotalAnomalies = anomalies.Count;

            var resolvedInRange = anomalies
                .Where(a => a.Status == "RESOLVED" && a.ResolvedAt.HasValue && a.ResolvedAt.Value >= start && a.ResolvedAt.Value < end)
                .ToList();
            model.TotalRevenueRecovered = resolvedInRange.Sum(a => a.Booking.Amount);
            model.AvgResolutionMinutes = resolvedInRange.Any()
                ? Math.Round(resolvedInRange.Average(a => (a.ResolvedAt!.Value - a.DetectedAt).TotalMinutes), 1)
                : 0;

            var endpointRowsInRange = await _dbContext.EndpointHealths
                .Where(e => e.CheckedAt >= start && e.CheckedAt < end)
                .OrderBy(e => e.Name)
                .ThenBy(e => e.CheckedAt)
                .Select(e => new { e.Name, e.Status, e.CheckedAt })
                .ToListAsync();

            var endpointNames = endpointRowsInRange.Select(x => x.Name).Distinct().ToList();
            var baselineStatusByEndpoint = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var endpointName in endpointNames)
            {
                var lastStatusBeforeRange = await _dbContext.EndpointHealths
                    .Where(e => e.Name == endpointName && e.CheckedAt < start)
                    .OrderByDescending(e => e.CheckedAt)
                    .Select(e => e.Status)
                    .FirstOrDefaultAsync();

                baselineStatusByEndpoint[endpointName] = lastStatusBeforeRange ?? string.Empty;
            }

            var downIncidentsByEndpoint = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in endpointRowsInRange.GroupBy(x => x.Name))
            {
                var incidents = 0;
                var previousStatus = baselineStatusByEndpoint.TryGetValue(group.Key, out var baselineStatus)
                    ? baselineStatus
                    : string.Empty;

                foreach (var row in group.OrderBy(x => x.CheckedAt))
                {
                    if (row.Status == "DOWN" && previousStatus != "DOWN")
                    {
                        incidents++;
                    }

                    previousStatus = row.Status;
                }

                downIncidentsByEndpoint[group.Key] = incidents;
            }

            var downEndpoint = downIncidentsByEndpoint
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .FirstOrDefault();

            model.MostDownEventKey = string.IsNullOrEmpty(downEndpoint.Key) ? "-" : downEndpoint.Key;
            model.MostDownEventCount = downEndpoint.Value;
            
            model.EndpointMostDownEvents = string.IsNullOrEmpty(downEndpoint.Key)
                ? "-"
                : $"{downEndpoint.Key} ({downEndpoint.Value} DOWN incidents)";

            var dayBuckets = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var dayCounts = new int[7];
            foreach (var anomaly in anomalies)
            {
                var index = anomaly.DetectedAt.DayOfWeek switch
                {
                    DayOfWeek.Monday => 0,
                    DayOfWeek.Tuesday => 1,
                    DayOfWeek.Wednesday => 2,
                    DayOfWeek.Thursday => 3,
                    DayOfWeek.Friday => 4,
                    DayOfWeek.Saturday => 5,
                    _ => 6
                };
                dayCounts[index]++;
            }
            model.DayOfWeekLabels = dayBuckets.ToList();
            model.DayOfWeekCounts = dayCounts.ToList();

            var topCauses = anomalies
                .GroupBy(a => a.EndpointHealthId != null ? a.EndpointHealth?.Name ?? "Linked Outage" : "Platform Delay / Other")
                .Select(g => new { Cause = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            model.TopCauseLabels = topCauses.Select(x => x.Cause).ToList();
            model.TopCauseCounts = topCauses.Select(x => x.Count).ToList();

            model.TopRoutes = anomalies
                .GroupBy(a => a.Booking.Route)
                .Select(g => new TopRouteRow
                {
                    Route = g.Key,
                    AnomalyCount = g.Count(),
                    TotalAmountAtRisk = g.Sum(x => x.Booking.Amount)
                })
                .OrderByDescending(x => x.AnomalyCount)
                .ThenByDescending(x => x.TotalAmountAtRisk)
                .Take(10)
                .ToList();

            model.ResolutionPerformance = anomalies
                .GroupBy(a => GetWeekStartUtc(a.DetectedAt))
                .OrderByDescending(g => g.Key)
                .Select(g => new ResolutionPerformanceRow
                {
                    WeekLabel = $"{g.Key:yyyy-MM-dd}",
                    Total = g.Count(),
                    Resolved = g.Count(x => x.Status == "RESOLVED"),
                    Ignored = g.Count(x => x.Status == "IGNORED"),
                    AvgMinutesToResolve = Math.Round(
                        g.Where(x => x.ResolvedAt.HasValue)
                         .Select(x => (x.ResolvedAt!.Value - x.DetectedAt).TotalMinutes)
                         .DefaultIfEmpty(0)
                         .Average(), 1)
                })
                .ToList();

            return model;
        }

        public async Task<MonthlyReportData> BuildMonthlyReportDataAsync(int year, int month)
        {
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);

            var anomalies = await _dbContext.Anomalies
                .Include(a => a.Booking)
                .Include(a => a.EndpointHealth)
                .Where(a => a.DetectedAt >= start && a.DetectedAt < end)
                .ToListAsync();

            var endpointHealthRows = await _dbContext.EndpointHealths
                .Where(e => e.CheckedAt >= start && e.CheckedAt < end)
                .ToListAsync();

            var report = new MonthlyReportData
            {
                Year = year,
                Month = month,
                GeneratedAtUtc = DateTime.UtcNow,
                HasData = anomalies.Any() || endpointHealthRows.Any(),
                TotalAnomaliesDetected = anomalies.Count,
                Resolved = anomalies.Count(a => a.Status == "RESOLVED"),
                Ignored = anomalies.Count(a => a.Status == "IGNORED"),
                Unresolved = anomalies.Count(a => a.Status == "OPEN"),
                RevenueRecovered = anomalies.Where(a => a.Status == "RESOLVED").Sum(a => a.Booking.Amount),
                MeanTimeToResolve = Math.Round(
                    anomalies.Where(a => a.Status == "RESOLVED" && a.ResolvedAt.HasValue)
                             .Select(a => (a.ResolvedAt!.Value - a.DetectedAt).TotalMinutes)
                             .DefaultIfEmpty(0)
                             .Average(), 1)
            };

            report.EndpointUptime = endpointHealthRows
                .GroupBy(e => e.Name)
                .Select(g => new EndpointUptimeRow
                {
                    Endpoint = g.Key,
                    UptimePercent = g.Any() ? Math.Round(g.Count(x => x.Status == "UP") * 100.0 / g.Count(), 2) : 0,
                    DownEvents = g.Count(x => x.Status == "DOWN")
                })
                .OrderBy(x => x.Endpoint)
                .ToList();

            report.TopCauses = anomalies
                .GroupBy(a => a.EndpointHealthId != null ? a.EndpointHealth?.Name ?? "Linked Outage" : "Platform Delay / Other")
                .Select(g => new CauseRankRow { Cause = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(3)
                .ToList();

            if (report.EndpointUptime.Any(x => x.Endpoint == "Payment Gateway" && x.UptimePercent < 99))
            {
                report.Recommendations.Add("Payment Gateway experienced degraded performance this month. Engineering review recommended.");
            }

            if (report.Unresolved > 0)
            {
                report.Recommendations.Add($"{report.Unresolved} booking issues remain unresolved. Manual review required.");
            }

            if (!report.Recommendations.Any())
            {
                report.Recommendations.Add("System performance remained stable this month. Continue monitoring.");
            }

            return report;
        }

        public async Task<string> GenerateMonthlyReportCsvAsync(int year, int month)
        {
            var data = await BuildMonthlyReportDataAsync(year, month);
            var sb = new System.Text.StringBuilder();

            // Header
            sb.AppendLine("Metric,Value");
            sb.AppendLine($"Report Month,{new DateTime(year, month, 1):MMMM yyyy}");
            sb.AppendLine($"Generated AtUtc,{data.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Booking Issues Detected,{data.TotalAnomaliesDetected}");
            sb.AppendLine($"Resolved,{data.Resolved}");
            sb.AppendLine($"Ignored,{data.Ignored}");
            sb.AppendLine($"Unresolved,{data.Unresolved}");
            sb.AppendLine($"Revenue Recovered,{data.RevenueRecovered}");
            sb.AppendLine($"Avg Resolution Minutes,{data.MeanTimeToResolve}");
            sb.AppendLine();

            // Endpoints
            sb.AppendLine("Endpoint,Uptime %,Down Events");
            foreach (var e in data.EndpointUptime)
            {
                sb.AppendLine($"{e.Endpoint},{e.UptimePercent:N2},{e.DownEvents}");
            }
            sb.AppendLine();

            // Top Causes
            sb.AppendLine("Cause,Count");
            foreach (var c in data.TopCauses)
            {
                sb.AppendLine($"{c.Cause},{c.Count}");
            }

            return sb.ToString();
        }

        private static (DateTime Start, DateTime End, string Normalized) ResolveRange(string? range)
        {
            var now = DateTime.UtcNow;
            return (range ?? string.Empty).ToLowerInvariant() switch
            {
                "last7days" => (now.Date.AddDays(-6), now.Date.AddDays(1), "last7days"),
                "lastmonth" =>
                    (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1),
                     new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                     "lastmonth"),
                _ =>
                    (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                     new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                     "thismonth")
            };
        }

        private static DateTime GetWeekStartUtc(DateTime value)
        {
            var delta = value.DayOfWeek == DayOfWeek.Sunday ? 6 : ((int)value.DayOfWeek - 1);
            return value.Date.AddDays(-delta);
        }
    }
}
