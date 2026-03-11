using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BookingGuardian.Services
{
    public class SmsServiceOptions
    {
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }

    public class SmsNotificationResult
    {
        public bool Attempted { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public interface ISmsNotificationService
    {
        Task<SmsNotificationResult> SendRecoveryNotificationAsync(string phoneNumber, string referenceNo, string route);
    }

    public class SmsNotificationService : ISmsNotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly SmsServiceOptions _options;
        private readonly ILogger<SmsNotificationService> _logger;

        public SmsNotificationService(HttpClient httpClient, IOptions<SmsServiceOptions> options, ILogger<SmsNotificationService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<SmsNotificationResult> SendRecoveryNotificationAsync(string phoneNumber, string referenceNo, string route)
        {
            if (!_options.Enabled)
            {
                return new SmsNotificationResult { Attempted = false, Success = true };
            }

            if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(_options.Url))
            {
                return new SmsNotificationResult { Attempted = false, Success = true };
            }

            try
            {
                var payload = new
                {
                    phoneNumber,
                    message = $"เราพบความล่าช้าเล็กน้อยจากฝั่งธนาคารในการออกตั๋วของท่าน แต่ระบบ TicketGuard ของเราได้จัดการยืนยันตั๋ว {referenceNo} สำหรับ {route} ให้ท่านเรียบร้อยแล้ว เดินทางปลอดภัยครับ"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                }

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("SMS notification sent for booking reference {ReferenceNo}", referenceNo);
                    return new SmsNotificationResult { Attempted = true, Success = true };
                }

                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("SMS notification failed for booking reference {ReferenceNo}. Status={Status} Body={Body}", referenceNo, (int)response.StatusCode, body);
                return new SmsNotificationResult
                {
                    Attempted = true,
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS notification exception for booking reference {ReferenceNo}", referenceNo);
                return new SmsNotificationResult
                {
                    Attempted = true,
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }
}
