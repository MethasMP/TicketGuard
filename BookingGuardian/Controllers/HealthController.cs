using BookingGuardian.Data;
using BookingGuardian.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingGuardian.Controllers
{
    [AllowAnonymous]
    [Route("health")]
    [Authorize(Policy = "SupportOrAdmin")]
    public class HealthController : Controller
    {
        private readonly BookingDbContext _dbContext;

        public HealthController(BookingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Retrieves the latest health status for all tracked endpoints.
        /// </summary>
        /// <returns>A JSON object containing current status and response times.</returns>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetHealth()
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var latestHealths = await _dbContext.EndpointHealths
                .GroupBy(h => h.Name)
                .Select(g => g.OrderByDescending(h => h.CheckedAt).FirstOrDefault())
                .ToListAsync();
            var nonNullLatest = latestHealths.Where(h => h != null).Select(h => h!).ToList();

            var uptimeRaw = await _dbContext.EndpointHealths
                .Where(h => h.CheckedAt >= since)
                .GroupBy(h => h.Name)
                .Select(g => new
                {
                    Name = g.Key,
                    Total = g.Count(),
                    Up = g.Count(x => x.Status == "UP")
                })
                .ToListAsync();

            var uptimeMap = uptimeRaw.ToDictionary(
                x => x.Name,
                x => x.Total == 0 ? 0.0 : Math.Round((double)x.Up / x.Total * 100, 1)
            );

            var endpointNames = nonNullLatest.Select(h => h.Name).Distinct().ToList();
            var healthHistoryByName = await _dbContext.EndpointHealths
                .Where(h => endpointNames.Contains(h.Name))
                .OrderByDescending(h => h.CheckedAt)
                .ToListAsync();
            var groupedHistory = healthHistoryByName
                .GroupBy(h => h.Name)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CheckedAt).ToList());

            var endpoints = new List<object>();
            foreach (var h in nonNullLatest)
            {
                DateTime? outageStart = null;
                int? outageMinutes = null;
                int outageAnomalyCount = 0;
                int? activeOutageId = null;

                if (h.Status == "DOWN" && groupedHistory.TryGetValue(h.Name, out var history))
                {
                    outageStart = h.CheckedAt;
                    foreach (var item in history)
                    {
                        if (item.Status != "DOWN")
                        {
                            break;
                        }
                        outageStart = item.CheckedAt;
                    }

                    outageMinutes = (int)Math.Max(0, Math.Round((DateTime.UtcNow - outageStart.Value).TotalMinutes));
                    activeOutageId = h.Id;

                    outageAnomalyCount = await _dbContext.Anomalies
                        .Include(a => a.EndpointHealth)
                        .Where(a =>
                            a.Status == "OPEN" &&
                            a.EndpointHealthId != null &&
                            a.EndpointHealth != null &&
                            a.EndpointHealth.Name == h.Name &&
                            a.DetectedAt >= outageStart.Value)
                        .CountAsync();
                }

                endpoints.Add(new
                {
                    name = h.Name,
                    status = h.Status,
                    responseMs = h.ResponseMs,
                    checkedAt = h.CheckedAt,
                    uptime24h = uptimeMap.TryGetValue(h.Name, out var value) ? value : 0,
                    outageStart,
                    outageMinutes,
                    outageAnomalyCount,
                    activeOutageId
                });
            }

            return Json(new { endpoints });
        }

        /// <summary>
        /// Retrieves or displays health history for a specific endpoint or all endpoints.
        /// </summary>
        /// <param name="name">Optional endpoint name filter.</param>
        /// <param name="hours">Filter by last X hours (max 168).</param>
        /// <returns>HTML view or JSON response with detailed history.</returns>
        [Authorize]
        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] string? name, [FromQuery] int hours = 1)
        {
            if (hours < 1) hours = 1;
            if (hours > 168) hours = 168;

            var cutoff = DateTime.UtcNow.AddHours(-hours);
            var query = _dbContext.EndpointHealths.Where(h => h.CheckedAt >= cutoff);
            if (!string.IsNullOrEmpty(name)) query = query.Where(h => h.Name == name);

            var rawHistory = await query.OrderByDescending(h => h.CheckedAt).ToListAsync();

            // Compact History to avoid redundancy: 
            // 1. Show all status changes
            // 2. Keep at least one record per hour if no status change occurred
            // 3. Keep the most recent record
            var historyModel = new List<EndpointHealth>();
            foreach (var group in rawHistory.GroupBy(h => h.Name))
            {
                var records = group.OrderBy(h => h.CheckedAt).ToList();
                if (!records.Any()) continue;

                EndpointHealth? lastAdded = null;
                for (int i = 0; i < records.Count; i++)
                {
                    var current = records[i];
                    
                    bool statusChanged = lastAdded == null || current.Status != lastAdded.Status;
                    bool newHour = lastAdded != null && current.CheckedAt.Hour != lastAdded.CheckedAt.Hour;
                    bool isLast = i == records.Count - 1;

                    if (statusChanged || newHour || isLast)
                    {
                        historyModel.Add(current);
                        lastAdded = current;
                    }
                }
            }

            // Order for display (newest first)
            historyModel = historyModel.OrderByDescending(h => h.CheckedAt).ToList();
            
            // For View (HTML) request
            if (Request.Headers["Accept"].ToString().Contains("text/html"))
            {
                var latestStatus = await _dbContext.EndpointHealths
                    .GroupBy(h => h.Name)
                    .Select(g => g.OrderByDescending(h => h.CheckedAt).FirstOrDefault())
                    .ToListAsync();
                
                var nonNullLatest = latestStatus.Where(h => h != null).Select(h => h!).ToList();
                var downEndpoints = nonNullLatest.Where(h => h.Status == "DOWN").Select(h => h.Name).ToList();

                var viewModel = new HealthHistoryViewModel
                {
                    History = historyModel,
                    LatestStatus = nonNullLatest,
                    IsSafetyModeActive = downEndpoints.Any(),
                    AffectedEndpoints = downEndpoints,
                    SelectedHours = hours
                };

                // NEW: Impact Map Calculation
                if (viewModel.IsSafetyModeActive)
                {
                    var stuckBookings = await _dbContext.Bookings
                        .Where(b => b.PaymentStatus == "SUCCESS" && b.BookingStatus == "PENDING")
                        .ToListAsync();
                    
                    viewModel.StuckBookingCount = stuckBookings.Count;
                    viewModel.TotalAmountAtRisk = stuckBookings.Sum(b => b.Amount);
                }

                return View(viewModel);
            }

            // For JSON/API request
            if (!historyModel.Any()) return Json(new { });

            var endpointName = !string.IsNullOrEmpty(name) ? name : historyModel.First().Name;
            var relevantHistory = historyModel.Where(h => h.Name == endpointName).ToList();
            if (!relevantHistory.Any()) return Json(new { });

            var upCount = relevantHistory.Count(h => h.Status == "UP");
            var uptimePercent = Math.Round((double)upCount / relevantHistory.Count * 100, 2);

        return Json(new
            {
                name = endpointName,
                uptimePercent = uptimePercent,
                history = relevantHistory.Select(h => new { checkedAt = h.CheckedAt, status = h.Status, responseMs = h.ResponseMs })
            });
        }

        /// <summary>
        /// Maintenance task: Deletes redundant health check records from the database.
        /// Keeps only records where a status change occurred or boundaries.
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PruneHistory()
        {
            var allEndpoints = await _dbContext.EndpointHealths.Select(h => h.Name).Distinct().ToListAsync();
            int deletedCount = 0;

            foreach (var name in allEndpoints)
            {
                var records = await _dbContext.EndpointHealths
                    .Where(h => h.Name == name)
                    .OrderBy(h => h.CheckedAt)
                    .ToListAsync();

                if (records.Count <= 2) continue;

                var toDelete = new List<EndpointHealth>();
                for (int i = 1; i < records.Count - 1; i++)
                {
                    var prev = records[i - 1];
                    var current = records[i];
                    var next = records[i + 1];

                    // If status is same as both neighbors, it's redundant data in between
                    if (current.Status == prev.Status && current.Status == next.Status)
                    {
                        toDelete.Add(current);
                    }
                }

                if (toDelete.Any())
                {
                    _dbContext.EndpointHealths.RemoveRange(toDelete);
                    deletedCount += toDelete.Count;
                }
            }

            await _dbContext.SaveChangesAsync();
            return Json(new { success = true, deletedCount });
        }
    }
}
