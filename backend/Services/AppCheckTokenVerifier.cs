using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace FinanceManagement.API.Services;

public interface IAppCheckTokenVerifier
{
    Task<AppCheckVerificationResult> VerifyAsync(string? token, CancellationToken cancellationToken = default);
}

public record AppCheckVerificationResult(bool IsValid, string? FailureReason);

// Verifies Firebase App Check tokens.
// FirebaseAdmin .NET 3.5.0 has no AppCheck module (only Auth/, Messaging/, Util/),
// so we fetch Google's JWKS ourselves and validate the JWT manually.
//
// App Check token claims (per Firebase Admin Node reference):
//   iss  "https://firebaseappcheck.googleapis.com/{projectNumber}"
//   aud  array containing "projects/{projectNumber}" and "projects/{projectId}"
//   sub  the Firebase app ID (e.g., "1:123456789:web:abcdef")
//   exp / iat / nbf  standard
//
// JWKS endpoint: https://firebaseappcheck.googleapis.com/v1/jwks (RS256 keys).
// ConfigurationManager caches the keyset and refreshes every 6h; transient
// fetch failures keep serving the cached keys, so a blip in Google's auth
// infrastructure doesn't cascade into a backend outage.
public class AppCheckTokenVerifier : IAppCheckTokenVerifier
{
    private const string JwksUri = "https://firebaseappcheck.googleapis.com/v1/jwks";
    private static readonly string[] AllowedAlgorithms = { "RS256" };

    private readonly ConfigurationManager<JsonWebKeySet> _configManager;
    private readonly string _expectedIssuer;
    private readonly string _expectedAudience;
    private readonly string _expectedAppId;
    // MapInboundClaims = false stops the handler from renaming standard JWT
    // claims to their legacy WS-Security URIs (e.g. 'sub' → ClaimTypes.NameIdentifier).
    // Without this, principal.FindFirst("sub") returns empty and every App Check
    // token fails with app_id_mismatch:got=''.
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly ILogger<AppCheckTokenVerifier> _logger;

    public AppCheckTokenVerifier(
        string projectNumber,
        string appId,
        IHttpClientFactory httpClientFactory,
        ILogger<AppCheckTokenVerifier> logger)
    {
        _expectedIssuer = $"https://firebaseappcheck.googleapis.com/{projectNumber}";
        _expectedAudience = $"projects/{projectNumber}";
        _expectedAppId = appId;
        _logger = logger;

        var httpClient = httpClientFactory.CreateClient("AppCheckJwks");
        var documentRetriever = new HttpDocumentRetriever(httpClient) { RequireHttps = true };

        _configManager = new ConfigurationManager<JsonWebKeySet>(
            JwksUri,
            new JwksRetriever(),
            documentRetriever)
        {
            AutomaticRefreshInterval = TimeSpan.FromHours(6),
            RefreshInterval = TimeSpan.FromMinutes(5),
        };
    }

    public async Task<AppCheckVerificationResult> VerifyAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AppCheckVerificationResult(false, "missing_token");
        }

        JsonWebKeySet keys;
        try
        {
            keys = await _configManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch App Check JWKS from {Uri}.", JwksUri);
            return new AppCheckVerificationResult(false, "jwks_unavailable");
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _expectedIssuer,
            ValidateAudience = true,
            ValidAudiences = new[] { _expectedAudience },
            AudienceValidator = (audiences, _, _) => audiences.Contains(_expectedAudience, StringComparer.Ordinal),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.Keys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = AllowedAlgorithms,
        };

        try
        {
            var principal = _handler.ValidateToken(token, parameters, out _);

            var sub = principal.FindFirst("sub")?.Value;
            if (!string.Equals(sub, _expectedAppId, StringComparison.Ordinal))
            {
                return new AppCheckVerificationResult(false, "app_id_mismatch");
            }

            return new AppCheckVerificationResult(true, null);
        }
        catch (SecurityTokenException ex)
        {
            return new AppCheckVerificationResult(false, ex.GetType().Name);
        }
    }

    private sealed class JwksRetriever : IConfigurationRetriever<JsonWebKeySet>
    {
        public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
        {
            var json = await retriever.GetDocumentAsync(address, cancel);
            return new JsonWebKeySet(json);
        }
    }
}
