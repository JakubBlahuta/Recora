using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recora.Core.Paths;

namespace Recora.Core.Settings;

public sealed class SettingsStore
{
    private readonly RecoraPaths _paths;
    private readonly ILogger<SettingsStore>? _logger;
    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsStore(RecoraPaths paths, ILogger<SettingsStore>? logger = null)
    {
        _paths = paths;
        _logger = logger;
        _paths.EnsureCreated();
        _current = LoadOrCreate();
    }

    public AppSettings Current
    {
        get
        {
            lock (_gate) return Clone(_current);
        }
    }

    public event EventHandler? Changed;

    public AppSettings LoadOrCreate()
    {
        lock (_gate)
        {
            if (!File.Exists(_paths.SettingsFile))
            {
                _current = AppSettings.CreateDefaults();
                PersistUnlocked(_current);
                _logger?.LogInformation("Created default settings at {Path}", _paths.SettingsFile);
                return Clone(_current);
            }

            try
            {
                var json = File.ReadAllText(_paths.SettingsFile);
                _current = JsonSerializer.Deserialize<AppSettings>(json, SettingsJson.Options)
                           ?? AppSettings.CreateDefaults();
                return Clone(_current);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read settings; recreating defaults");
                _current = AppSettings.CreateDefaults();
                PersistUnlocked(_current);
                return Clone(_current);
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            _current = Clone(settings);
            PersistUnlocked(_current);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_gate)
        {
            mutate(_current);
            PersistUnlocked(_current);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void PersistUnlocked(AppSettings settings)
    {
        Directory.CreateDirectory(_paths.Config);
        var json = JsonSerializer.Serialize(settings, SettingsJson.Options);
        var tmp = _paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, _paths.SettingsFile, overwrite: true);
        File.Delete(tmp);
    }

    private static AppSettings Clone(AppSettings s)
    {
        var json = JsonSerializer.Serialize(s, SettingsJson.Options);
        return JsonSerializer.Deserialize<AppSettings>(json, SettingsJson.Options)!;
    }
}
