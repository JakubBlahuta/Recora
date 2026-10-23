using Recora.Core;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;

namespace Recora.Core.Tests;

public class SettingsAndPathsTests
{
    [Fact]
    public void Paths_use_Recora_under_local_app_data()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-test-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        Assert.Equal(Path.Combine(tmp, "Recora"), paths.Root);
        Assert.EndsWith(Path.Combine("Recora", "data", "catalog.db"), paths.CatalogDb);
        paths.EnsureCreated();
        Assert.True(Directory.Exists(paths.Recordings));
        Assert.True(Directory.Exists(paths.Secrets));
    }

    [Fact]
    public void Settings_defaults_include_mic_and_auto_upload()
    {
        var s = AppSettings.CreateDefaults(UiLanguage.Cs);
        Assert.Equal("cs", s.UiLanguage);
        Assert.True(s.Capture.IncludeMicrophone);
        Assert.Equal("default", s.Capture.MicrophoneDeviceId);
        Assert.Equal("teamsPlusMic", s.Capture.Audio);
        Assert.True(s.Cloud.AutoUpload);
        Assert.Equal("Recordings", s.Cloud.FolderPath);
        Assert.Equal(20, s.ChunkDurationMinutes);
    }

    [Fact]
    public void SettingsStore_roundtrips()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-settings-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        var store = new SettingsStore(paths);
        store.Update(s =>
        {
            s.UiLanguage = "sk";
            s.Capture.IncludeMicrophone = false;
            s.FirstRunCompleted = true;
        });

        var store2 = new SettingsStore(paths);
        var loaded = store2.Current;
        Assert.Equal("sk", loaded.UiLanguage);
        Assert.False(loaded.Capture.IncludeMicrophone);
        Assert.True(loaded.FirstRunCompleted);
    }

    [Fact]
    public void MeetingId_format()
    {
        var id = MeetingIdFactory.Create(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
        Assert.StartsWith("2026-10-22T09-00-00Z_", id);
    }
}
