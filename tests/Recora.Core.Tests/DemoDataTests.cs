using Recora.Catalog;
using Recora.Core.Demo;
using Recora.Core.Paths;
using Recora.Core.Settings;

namespace Recora.Core.Tests;

public class DemoDataTests
{
    [Fact]
    public async Task Seeder_is_idempotent_and_creates_sidecars()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-demo-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();

        var settings = AppSettings.CreateDefaults();
        settings.DemoDataEnabled = true;

        var first = await DemoDataSeeder.SeedIfEnabledAsync(paths, catalog, settings);
        Assert.Equal(2, first);

        var second = await DemoDataSeeder.SeedIfEnabledAsync(paths, catalog, settings);
        Assert.Equal(0, second);

        var list = await catalog.ListMeetingsAsync("demo");
        Assert.Equal(2, list.Count);
        Assert.All(list, m => Assert.StartsWith(DemoDataSeeder.FixtureIdPrefix, m.MeetingId));
        Assert.True(File.Exists(Path.Combine(list[0].LocalPath, "meeting.json")));
        Assert.True(File.Exists(Path.Combine(list[0].LocalPath, "chunk_000.mp4")));
    }

    [Fact]
    public async Task Seeder_skips_when_disabled()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-demo-off-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        await using var catalog = new SqliteCatalogRepository(paths);
        var settings = AppSettings.CreateDefaults();
        settings.DemoDataEnabled = false;
        Environment.SetEnvironmentVariable("RECORA_DEMO_DATA", null);

        var n = await DemoDataSeeder.SeedIfEnabledAsync(paths, catalog, settings);
        Assert.Equal(0, n);
    }
}
