using Recora.Catalog;
using Recora.Cloud.OAuth;
using Recora.Cloud.Upload;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Encoding;

namespace Recora.Core.Tests;

public class CatalogAndEncodingTests
{
    [Fact]
    public async Task Catalog_crud_and_upload_queue()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-cat-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();

        var meeting = new MeetingRecord
        {
            MeetingId = "m1",
            Title = "Q3 sync",
            StartedAt = DateTimeOffset.UtcNow,
            Status = MeetingStatus.Local,
            LocalPath = paths.MeetingDirectory("m1")
        };
        await catalog.UpsertMeetingAsync(meeting);
        await catalog.UpsertChunkAsync(new ChunkRecord
        {
            MeetingId = "m1",
            Index = 0,
            FileName = "chunk_000.mp4",
            StartSec = 0,
            EndSec = 60,
            Complete = true
        });

        var list = await catalog.ListMeetingsAsync("Q3");
        Assert.Single(list);
        var chunks = await catalog.ListChunksAsync("m1");
        Assert.Single(chunks);

        await catalog.EnqueueUploadAsync("m1");
        var jobs = await catalog.ListPendingUploadsAsync();
        Assert.Single(jobs);
        Assert.Equal(UploadJobStatus.Pending, jobs[0].Status);
    }

    [Fact]
    public async Task Encoding_writes_sidecars_and_chunks()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var coordinator = new EncodingCoordinator();
        await using var active = await coordinator.StartAsync(new Recora.Core.Abstractions.EncodingSessionRequest
        {
            MeetingId = "enc1",
            Title = "Test",
            OutputDirectory = tmp,
            ChunkDurationSec = 1,
            IncludeMicrophone = true
        });

        var chunk = await active.CompleteChunkAsync(null, 1.0);
        Assert.Equal(0, chunk.Index);
        Assert.True(File.Exists(Path.Combine(tmp, "chunk_000.mp4")));

        var sidecar = await active.FinalizeAsync();
        Assert.True(File.Exists(Path.Combine(tmp, "meeting.json")));
        Assert.True(File.Exists(Path.Combine(tmp, "chunks.jsonl")));
        Assert.True(sidecar.IncludeMicrophone);
        Assert.NotEmpty(sidecar.Chunks);
    }

    [Fact]
    public async Task OneDrive_stub_upload_succeeds()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-up-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        paths.EnsureCreated();
        var meetingDir = paths.MeetingDirectory("up1");
        Directory.CreateDirectory(meetingDir);
        await File.WriteAllTextAsync(Path.Combine(meetingDir, "meeting.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(meetingDir, "chunk_000.mp4"), "data");

        var secrets = new DpapiSecretStore(paths);
        var auth = new OneDriveAuthService(secrets);
        Assert.True(await auth.SignInAsync());
        var upload = new OneDriveUploadService(auth);
        var meeting = new MeetingRecord
        {
            MeetingId = "up1",
            Title = "Up",
            StartedAt = DateTimeOffset.UtcNow,
            LocalPath = meetingDir
        };
        await upload.UploadMeetingAsync(meeting, "Recordings");
        Assert.Equal("Recordings/up1", meeting.CloudPath);
    }
}
