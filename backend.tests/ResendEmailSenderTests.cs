using System.Net;
using FinanceManagement.API.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagement.API.Tests;

public class ResendEmailSenderTests
{
    [Fact]
    public async Task SendAsync_Returns_True_On_2xx()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"id\":\"abc\"}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, "key", "noreply@example.com", NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync("to@example.com", "Subject", "<p>body</p>", CancellationToken.None);

        Assert.True(result);
        Assert.Equal("Bearer key", handler.LastAuthorization);
        Assert.Contains("to@example.com", handler.LastRequestBody);
        Assert.Contains("noreply@example.com", handler.LastRequestBody);
    }

    [Fact]
    public async Task SendAsync_Returns_False_On_4xx()
    {
        var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, "{\"error\":\"bad\"}");
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, "key", "noreply@example.com", NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync("to@example.com", "Subject", "<p>body</p>", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task SendAsync_Returns_False_On_Exception()
    {
        var handler = new StubHandler(throwException: true);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.com/") };
        var sender = new ResendEmailSender(client, "key", "noreply@example.com", NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendAsync("to@example.com", "Subject", "<p>body</p>", CancellationToken.None);

        Assert.False(result);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly bool _throwException;

        public string? LastAuthorization { get; private set; }
        public string LastRequestBody { get; private set; } = "";

        public StubHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "", bool throwException = false)
        {
            _status = status;
            _body = body;
            _throwException = throwException;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_throwException) throw new HttpRequestException("connection refused");

            LastAuthorization = request.Headers.Authorization?.ToString();
            LastRequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
