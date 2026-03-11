namespace BookingGuardian.Models
{
    public class ReportsPageViewModel
    {
        public string SelectedRange { get; set; } = "thisMonth";
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtcExclusive { get; set; }
        public string DownloadMonth { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");

        public int TotalAnomalies { get; set; }
        public decimal TotalRevenueRecovered { get; set; }
        public double AvgResolutionMinutes { get; set; }
        public string EndpointMostDownEvents { get; set; } = "-";
        public string MostDownEventKey { get; set; } = "-";
        public int MostDownEventCount { get; set; }

        public List<string> DayOfWeekLabels { get; set; } = new();
        public List<int> DayOfWeekCounts { get; set; } = new();

        public List<string> TopCauseLabels { get; set; } = new();
        public List<int> TopCauseCounts { get; set; } = new();

        public List<TopRouteRow> TopRoutes { get; set; } = new();
        public List<ResolutionPerformanceRow> ResolutionPerformance { get; set; } = new();
    }

    public class TopRouteRow
    {
        public string Route { get; set; } = string.Empty;
        public int AnomalyCount { get; set; }
        public decimal TotalAmountAtRisk { get; set; }
    }

    public class ResolutionPerformanceRow
    {
        public string WeekLabel { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Resolved { get; set; }
        public int Ignored { get; set; }
        public double AvgMinutesToResolve { get; set; }
    }

    public class MonthlyReportData
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public bool HasData { get; set; }

        public int TotalAnomaliesDetected { get; set; }
        public int Resolved { get; set; }
        public int Ignored { get; set; }
        public int Unresolved { get; set; }
        public decimal RevenueRecovered { get; set; }
        public double MeanTimeToResolve { get; set; }

        public List<EndpointUptimeRow> EndpointUptime { get; set; } = new();
        public List<CauseRankRow> TopCauses { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class EndpointUptimeRow
    {
        public string Endpoint { get; set; } = string.Empty;
        public double UptimePercent { get; set; }
        public int DownEvents { get; set; }
    }

    public class CauseRankRow
    {
        public string Cause { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class HealthHistoryViewModel
    {
        public List<EndpointHealth> History { get; set; } = new();
        public List<EndpointHealth> LatestStatus { get; set; } = new();
        public bool IsSafetyModeActive { get; set; }
        public List<string> AffectedEndpoints { get; set; } = new();
        public int SelectedHours { get; set; }

        public int StuckBookingCount { get; set; }
        public decimal TotalAmountAtRisk { get; set; }
    }
}
