using BookingGuardian.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingGuardian.Controllers
{
    [Authorize(Policy = "SupportOrAdmin")]
    [Route("reports")]
    public class ReportsController : Controller
    {
        private readonly IReportService _reportService;
        private readonly IMonthlyPdfReportService _monthlyPdfReportService;

        public ReportsController(IReportService reportService, IMonthlyPdfReportService monthlyPdfReportService)
        {
            _reportService = reportService;
            _monthlyPdfReportService = monthlyPdfReportService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index([FromQuery] string range = "thisMonth")
        {
            var model = await _reportService.BuildReportsPageAsync(range);
            return View(model);
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download([FromQuery] string month, [FromQuery] string format = "pdf")
        {
            if (string.IsNullOrWhiteSpace(month))
            {
                return BadRequest("month is required in yyyy-MM format.");
            }

            var parts = month.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var monthNumber))
            {
                return BadRequest("Invalid month format. Use yyyy-MM.");
            }

            if (monthNumber < 1 || monthNumber > 12)
            {
                return BadRequest("Invalid month value.");
            }

            if (format?.ToLower() == "csv")
            {
                var csv = await _reportService.GenerateMonthlyReportCsvAsync(year, monthNumber);
                var csvFileName = $"ticketguard-report-{year:D4}-{monthNumber:D2}.csv";
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", csvFileName);
            }
            else
            {
                var pdf = await _monthlyPdfReportService.GenerateMonthlyReportPdfAsync(year, monthNumber);
                var pdfFileName = $"ticketguard-report-{year:D4}-{monthNumber:D2}.pdf";
                return File(pdf, "application/pdf", pdfFileName);
            }
        }
    }
}
