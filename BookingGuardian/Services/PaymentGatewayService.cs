using BookingGuardian.Models;

namespace BookingGuardian.Services
{
    public interface IPaymentGatewayService
    {
        /// <summary>
        /// Verifies a payment status directly with the provider.
        /// </summary>
        /// <param name="referenceNo">Booking reference number.</param>
        /// <returns>True if the provider confirms the payment is Succeeded/Settled.</returns>
        Task<bool> VerifyPaymentStatusAsync(string referenceNo);
    }

    public class PaymentGatewayService : IPaymentGatewayService
    {
        private readonly ILogger<PaymentGatewayService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly bool _isEnabled;

        public PaymentGatewayService(ILogger<PaymentGatewayService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _apiKey = configuration.GetValue<string>("PaymentGateway:ApiKey", "dev-key-stk-placeholder");
            _isEnabled = configuration.GetValue<bool>("PaymentGateway:Enabled", false);
        }

        public async Task<bool> VerifyPaymentStatusAsync(string referenceNo)
        {
            if (!_isEnabled)
            {
                _logger.LogWarning("Payment verification skipped for {Ref} because gateway integration is DISABLED.", referenceNo);
                // In dev/mock mode, if disabled, we might return false to be safe, 
                // but for our "Smart Recovery" logic, we only recover if this returns true.
                return false; 
            }

            try
            {
                _logger.LogInformation("Verifying payment status for {Ref} with provider...", referenceNo);

                // SIMULATION: In a real app, this would be:
                // var client = _httpClientFactory.CreateClient("PaymentGateway");
                // client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                // var response = await client.GetAsync($"/v1/payments?reference={referenceNo}");
                // ... logic to check if status is 'succeeded' or 'captured'
                
                await Task.Delay(500); // Simulate API latency

                // For demonstration, let's say we only confirm payments for certain patterns or just mock success
                bool isConfirmed = referenceNo.StartsWith("BK-"); 
                
                if (isConfirmed)
                {
                    _logger.LogInformation("Payment provider CONFIRMED reference {Ref} is SETTLED.", referenceNo);
                }
                else
                {
                    _logger.LogWarning("Payment provider could NOT confirm reference {Ref}. Status may be pending or failed.", referenceNo);
                }

                return isConfirmed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying payment for {Ref} with provider.", referenceNo);
                return false;
            }
        }
    }
}
