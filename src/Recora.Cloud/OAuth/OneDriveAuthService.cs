using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;

namespace Recora.Cloud.OAuth;

/// <summary>
/// Microsoft identity OAuth scaffolding for personal OneDrive (Graph).
/// MSAL interactive flow is gated on <see cref="ClientIdConfigKey"/> + optional UseMsal flag.
/// Without a client id, SignIn stores a stub token so queue/UI demos work offline.
/// </summary>
public sealed class OneDriveAuthService : IOneDriveAuthService
{
    public const string TokenSecretKey = "onedrive.token";
    public const string ClientIdConfigKey = "RECORA_MSAL_CLIENT_ID";

    private readonly ISecretStore _secrets;
    private readonly ILogger<OneDriveAuthService>? _logger;
    private readonly Func<bool> _useMsal;
    private TokenBundle? _cached;

    public OneDriveAuthService(
        ISecretStore secrets,
        ILogger<OneDriveAuthService>? logger = null,
        Func<bool>? useMsal = null)
    {
        _secrets = secrets;
        _logger = logger;
        _useMsal = useMsal ?? (() => true);
    }

    public bool IsAuthenticated => _cached?.AccessToken is not null || HasPersistedToken();

    public bool IsStubSession => _cached?.IsStub == true || PeekStub();

    public string? ClientId =>
        Environment.GetEnvironmentVariable(ClientIdConfigKey)
        ?? Environment.GetEnvironmentVariable("Recora__OneDrive__ClientId");

    public bool CanUseMsal => _useMsal() && !string.IsNullOrWhiteSpace(ClientId);

    public async Task<bool> SignInAsync(CancellationToken ct = default)
    {
        if (!CanUseMsal)
        {
            _logger?.LogWarning(
                "OneDrive stub sign-in (set {Env} and cloud.useMsal=true for real MSAL)",
                ClientIdConfigKey);

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

        _logger?.LogInformation("MSAL interactive sign-in would start with client {ClientId}", ClientId);
        throw new NotImplementedException(
            "Wire MSAL PublicClientApplication on Windows once RECORA_MSAL_CLIENT_ID is provided. See README Azure app registration.");
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

    private bool PeekStub()
    {
        try
        {
            var json = _secrets.LoadAsync(TokenSecretKey).GetAwaiter().GetResult();
            if (json is null) return false;
            var bundle = JsonSerializer.Deserialize<TokenBundle>(json);
            return bundle?.IsStub == true;
        }
        catch
        {
            return false;
        }
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
