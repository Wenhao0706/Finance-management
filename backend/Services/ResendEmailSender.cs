using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FinanceManagement.API.Services;

public sealed class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _fromAddress;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, string apiKey, string fromAddress, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _fromAddress = fromAddress;
        _logger = logger;

        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://api.resend.com/");
        }
        if (_http.Timeout > TimeSpan.FromSeconds(5))
        {
            _http.Timeout = TimeSpan.FromSeconds(5);
        }
    }

    public async Task<bool> SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = JsonContent.Create(new
            {
                from = _fromAddress,
                to = new[] { toAddress },
                subject,
                html = htmlBody,
            });

            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend send failed: {Status} — {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend send threw");
            return false;
        }
    }
}
