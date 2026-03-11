using System.Net.Mail;
using BookingGuardian.Services;
using Microsoft.Extensions.Options;

namespace BookingGuardian.BackgroundServices
{
    public class MonthlyReportOptions
    {
        public bool AutoSend { get; set; }
        public List<string> Recipients { get; set; } = new();
        public string SmtpHost { get; set; } = string.Empty;
    }

    public class MonthlyReportEmailJob : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly MonthlyReportOptions _options;
        private readonly ILogger<MonthlyReportEmailJob> _logger;
        private string? _lastSentForMonth;

        public MonthlyReportEmailJob(IServiceProvider serviceProvider, IOptions<MonthlyReportOptions> options, ILogger<MonthlyReportEmailJob> logger)
        {
            _serviceProvider = serviceProvider;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TrySendAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Graceful shutdown path
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monthly report email job failed");
                }
            }
        }

        private async Task TrySendAsync(CancellationToken ct)
        {
            if (!_options.AutoSend || string.IsNullOrWhiteSpace(_options.SmtpHost) || !_options.Recipients.Any())
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now.Day != 1 || now.Hour < 8)
            {
                return;
            }

            var sendMarker = now.ToString("yyyy-MM");
            if (_lastSentForMonth == sendMarker)
            {
                return;
            }

            var monthToReport = now.AddMonths(-1);
            using var scope = _serviceProvider.CreateScope();
            var pdfService = scope.ServiceProvider.GetRequiredService<IMonthlyPdfReportService>();
            var pdf = await pdfService.GenerateMonthlyReportPdfAsync(monthToReport.Year, monthToReport.Month);

            using var message = new MailMessage
            {
                From = new MailAddress("booking-guardian@local.internal"),
                Subject = $"TicketGuard Monthly Report - {monthToReport:yyyy-MM}",
                Body = "Attached is the monthly TicketGuard report.",
                IsBodyHtml = false
            };

            foreach (var recipient in _options.Recipients)
            {
                message.To.Add(recipient);
            }

            message.Attachments.Add(new Attachment(new MemoryStream(pdf), $"ticketguard-report-{monthToReport:yyyy-MM}.pdf", "application/pdf"));

            using var smtpClient = new SmtpClient(_options.SmtpHost);
            await smtpClient.SendMailAsync(message, ct);

            _lastSentForMonth = sendMarker;
            _logger.LogInformation("Monthly report email sent for {Month}", monthToReport.ToString("yyyy-MM"));
        }
    }
}
