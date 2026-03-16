using System.Net.Mail;
using Microsoft.Extensions.Options;
using BookingGuardian.BackgroundServices;

namespace BookingGuardian.Services
{
    public interface IEmailNotificationService
    {
        Task<bool> SendAlertAsync(string subject, string body, string? toEmail = null);
    }

    public class EmailNotificationService : IEmailNotificationService
    {
        private readonly MonthlyReportOptions _options;
        private readonly ILogger<EmailNotificationService> _logger;

        public EmailNotificationService(IOptions<MonthlyReportOptions> options, ILogger<EmailNotificationService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> SendAlertAsync(string subject, string body, string? toEmail = null)
        {
            var target = toEmail ?? string.Join(", ", _options.Recipients);

            if (string.IsNullOrWhiteSpace(_options.SmtpHost) || _options.SmtpHost.Contains("example.com") || _options.SmtpPass == "your-app-password")
            {
                _logger.LogInformation("==========================================================");
                _logger.LogInformation("📧 [EMAIL SIMULATION] - No real credentials provided.");
                _logger.LogInformation("TO: {To}", target);
                _logger.LogInformation("SUBJECT: {Subject}", subject);
                _logger.LogInformation("CONTENT: {BodySnippet}...", body.Length > 100 ? body.Substring(0, 100) : body);
                _logger.LogInformation("==========================================================");
                return true; // Simulate success for Dev/Test flow
            }

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress("ticketguard@internal.service"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                if (!string.IsNullOrWhiteSpace(toEmail))
                {
                    message.To.Add(toEmail);
                }
                else
                {
                    foreach (var recipient in _options.Recipients)
                    {
                        message.To.Add(recipient);
                    }
                }

                if (!message.To.Any())
                {
                    _logger.LogWarning("Email notification skipped: No recipients defined.");
                    return false;
                }

                using var smtpClient = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
                {
                    EnableSsl = true, // Common standard for Gmail/Office365
                    UseDefaultCredentials = false
                };

                if (!string.IsNullOrWhiteSpace(_options.SmtpUser) && !string.IsNullOrWhiteSpace(_options.SmtpPass))
                {
                    smtpClient.Credentials = new System.Net.NetworkCredential(_options.SmtpUser, _options.SmtpPass);
                }

                await smtpClient.SendMailAsync(message);
                
                _logger.LogInformation("Real email sent: To={To}, Subject={Subject}", target, subject);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send real alert email to {To}", target);
                return false;
            }
        }
    }
}
