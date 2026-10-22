using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;

namespace Recora.Cloud.OAuth;

/// <summary>
/// Microsoft identity OAuth scaffolding for personal OneDrive (Graph).
/// Real interactive browser flow requires ClientId from Azure app registration (user secret).
/// </summary>
public sealed class OneDriveAuthService : IOneDriveAuthService
{
    public const string TokenSecretKey = "onedrive.token";
    public const string ClientIdConfigKey = "RECORA_MSAL_CLIENT_ID";

    private readonly ISecretStore _secrets;
    private readonly ILogger<OneDriveAuthService>? _logger;
    private TokenBundle? _cached;

    public OneDriveAuthService(ISecretStore secrets, ILogger<OneDriveAuthService>? logger = null)
    {
        _secrets = secrets;
        _logger = logger;
    }

    public bool IsAuthenticated => _cached?.AccessToken is not null || HasPersistedToken();

    public string? ClientId =>
        Environment.GetEnvironmentVariable(ClientIdConfigKey)
        ?? Environment.GetEnvironmentVariable("Recora__OneDrive__ClientId");

    public async Task<bool> SignInAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            _logger?.LogWarning(
                "OneDrive OAuth stub: set {Env} (Azure public client app id) to enable real sign-in",
                ClientIdConfigKey);

            // Dev stub: mark as signed-in with placeholder so UI/queue can be exercised offline.
            var stub = new TokenBundle
            {
                AccessToken = "stub-access-token",
                RefreshToken = "stub-refresh-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                AccountHint = "stub@example.com",
                IsStub = true
            };
            await PersistAsync(stub, ct).ConfigureAwait(false);
            _cached = stub;
            return true;
        }

        // Production path (Windows): MSAL PublicClientApplication interactive + Files.ReadWrite
        // scopes: offline_access Files.ReadWrite User.Read
        _logger?.LogInformation("MSAL interactive sign-in would start with client {ClientId}", ClientId);
        throw new NotImplementedException(
            "Wire MSAL PublicClientApplication on Windows once RECORA_MSAL_CLIENT_ID is provided.");
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        _cached = null;
        await _secrets.DeleteAsync(TokenSecretKey, ct).ConfigureAwait(false);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_cached?.AccessToken is not null && _cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return _cached.AccessToken;

        var json = await _secrets.LoadAsync(TokenSecretKey, ct).ConfigureAwait(false);
        if (json is null) return null;
        _cached = JsonSerializer.Deserialize<TokenBundle>(json);
        return _cached?.AccessToken;
    }

    private bool HasPersistedToken()
    {
        try
        {
            return _secrets.LoadAsync(TokenSecretKey).GetAwaiter().GetResult() is not null;
        }
        catch
        {
            return false;
        }
    }

    private Task PersistAsync(TokenBundle bundle, CancellationToken ct) =>
        _secrets.SaveAsync(TokenSecretKey, JsonSerializer.Serialize(bundle), ct);

    private sealed class TokenBundle
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? AccountHint { get; set; }
        public bool IsStub { get; set; }
    }
}
