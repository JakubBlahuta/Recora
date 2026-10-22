using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Recora.Core.Abstractions;
using Recora.Core.Paths;

namespace Recora.Cloud.OAuth;

/// <summary>
/// DPAPI-protected secret files under %LOCALAPPDATA%\Recora\config\secrets.
/// On non-Windows, falls back to base64 obfuscation (dev only — not secure).
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly RecoraPaths _paths;

    public DpapiSecretStore(RecoraPaths paths)
    {
        _paths = paths;
        Directory.CreateDirectory(_paths.Secrets);
    }

    public Task SaveAsync(string key, string value, CancellationToken ct = default)
    {
        var path = PathFor(key);
        var bytes = Encoding.UTF8.GetBytes(value);
        byte[] protectedBytes;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        else
        {
            protectedBytes = bytes; // Linux CI/dev only
        }

        File.WriteAllBytes(path, protectedBytes);
        return Task.CompletedTask;
    }

    public Task<string?> LoadAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return Task.FromResult<string?>(null);

        var protectedBytes = File.ReadAllBytes(path);
        byte[] bytes;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }
        else
        {
            bytes = protectedBytes;
        }

        return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            key = key.Replace(c, '_');
        return Path.Combine(_paths.Secrets, key + ".bin");
    }
}
