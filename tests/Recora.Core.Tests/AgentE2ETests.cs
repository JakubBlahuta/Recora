using Recora.Capture;
using Recora.Catalog;
using Recora.Cloud.OAuth;
using Recora.Cloud.Upload;
using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;
using Recora.Core.ViewModels;
using Recora.Detection;
using Recora.Encoding;

namespace Recora.Core.Tests;

public class AgentE2ETests
{
    [Fact]
    public async Task Force_record_stop_writes_sidecar_chunks_and_catalog()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-e2e-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        var settings = new SettingsStore(paths);
        settings.Update(s =>
        {
            s.FirstRunCompleted = true;
            s.ConsentAccepted = true;
            s.ChunkDurationMinutes = 15;
            s.Cloud.AutoUpload = false;
        });

        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();

        var detector = new ScriptedDetector();
        var coordinator = new EncodingCoordinator();

        await using var agent = new RecordingAgent(
            paths,
            settings,
            detector,
            catalog,
            () => new FakeCaptureSession(),
            async req =>
            {
                // Short chunks for test: override duration
                var fast = new EncodingSessionRequest
                {
                    MeetingId = req.MeetingId,
                    Title = req.Title,
                    OutputDirectory = req.OutputDirectory,
                    QualityProfile = req.QualityProfile,
                    ChunkDurationSec = 2,
                    IncludeMicrophone = req.IncludeMicrophone,
                    MicrophoneDeviceId = req.MicrophoneDeviceId,
                    AudioMode = req.AudioMode,
                    DetectionConfidence = req.DetectionConfidence
                };
                return await DeterministicEncodingHost.StartAsync(coordinator, fast, catalog);
            });

        agent.DirectoryRecoverer = EncodingCoordinator.RecoverOrphanAsync;
        await agent.StartAsync();
        Assert.Equal(TrayAgentState.Idle, agent.State);

        await agent.ForceRecordAsync("E2E Demo Call");
        Assert.Equal(TrayAgentState.Recording, agent.State);
        Assert.NotNull(agent.ActiveMeeting);

        // Drive encoding ticks via reflection on private field — instead Advance through bridge by stopping after delay
        await Task.Delay(50);
        await agent.ForceStopAsync();
        Assert.Equal(TrayAgentState.Idle, agent.State);

        var meetings = await catalog.ListMeetingsAsync();
        Assert.Single(meetings);
        Assert.Equal(MeetingStatus.Local, meetings[0].Status);
        Assert.True(meetings[0].IncludeMicrophone);
        Assert.True(Directory.Exists(meetings[0].LocalPath));
        Assert.True(File.Exists(Path.Combine(meetings[0].LocalPath, "meeting.json")));
        Assert.True(File.Exists(Path.Combine(meetings[0].LocalPath, "chunks.jsonl")));
        Assert.NotEmpty(Directory.GetFiles(meetings[0].LocalPath, "chunk_*.mp4"));

        var chunks = await catalog.ListChunksAsync(meetings[0].MeetingId);
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task Crash_recovery_reindexes_orphan_chunks()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-crash-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();

        var meetingId = "orphan-1";
        var dir = paths.MeetingDirectory(meetingId);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "chunk_000.mp4"), new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(Path.Combine(dir, "chunk_001.mp4"), new byte[] { 5, 6, 7, 8 });

        await catalog.UpsertMeetingAsync(new MeetingRecord
        {
            MeetingId = meetingId,
            Title = "Crashed",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Status = MeetingStatus.Recording,
            LocalPath = dir
        });

        var recovered = await SessionRecovery.RecoverAllAsync(
            catalog, EncodingCoordinator.RecoverOrphanAsync);

        Assert.Single(recovered);
        Assert.Equal(MeetingStatus.Recovered, recovered[0].Status);
        Assert.True(recovered[0].Recovered);
        Assert.True(File.Exists(Path.Combine(dir, "meeting.json")));

        var chunks = await catalog.ListChunksAsync(meetingId);
        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task Deterministic_encoder_rotates_multiple_chunks()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-rot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        var coordinator = new EncodingCoordinator();
        await using var bridge = await DeterministicEncodingHost.StartAsync(coordinator, new EncodingSessionRequest
        {
            MeetingId = "rot1",
            Title = "Rotate",
            OutputDirectory = tmp,
            ChunkDurationSec = 5,
            IncludeMicrophone = true
        });

        await bridge.AdvanceAsync(12);
        var sidecar = await bridge.FinalizeAsync(false);
        Assert.True(bridge.ChunksWritten >= 3);
        Assert.Equal(bridge.ChunksWritten, sidecar.Chunks.Count);
        Assert.All(sidecar.Chunks, c => Assert.False(string.IsNullOrEmpty(c.Sha256)));
        Assert.True(sidecar.IncludeMicrophone);
    }

    [Fact]
    public async Task Upload_queue_retries_and_completes_with_stub_auth()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-q-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();

        var meetingDir = paths.MeetingDirectory("up-q");
        Directory.CreateDirectory(meetingDir);
        await File.WriteAllTextAsync(Path.Combine(meetingDir, "meeting.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(meetingDir, "chunk_000.mp4"), new byte[] { 9 });

        await catalog.UpsertMeetingAsync(new MeetingRecord
        {
            MeetingId = "up-q",
            Title = "Upload me",
            StartedAt = DateTimeOffset.UtcNow,
            Status = MeetingStatus.Local,
            LocalPath = meetingDir
        });
        await catalog.EnqueueUploadAsync("up-q");

        var secrets = new DpapiSecretStore(paths);
        var auth = new OneDriveAuthService(secrets, useMsal: () => false);
        Assert.True(await auth.SignInAsync());
        var upload = new OneDriveUploadService(auth);
        var worker = new UploadQueueWorker(catalog, upload, auth, () => "Recordings", autoUploadEnabled: () => true)
        {
            PollInterval = TimeSpan.FromMilliseconds(10)
        };

        var n = await worker.ProcessPendingOnceAsync();
        Assert.Equal(1, n);

        var meeting = await catalog.GetMeetingAsync("up-q");
        Assert.Equal(MeetingStatus.Synced, meeting!.Status);
        Assert.Equal("Recordings/up-q", meeting.CloudPath);
    }

    [Fact]
    public async Task LibraryViewModel_refresh_and_settings_persist()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "recora-vm-" + Guid.NewGuid().ToString("N"));
        var paths = new RecoraPaths(tmp);
        var store = new SettingsStore(paths);
        await using var catalog = new SqliteCatalogRepository(paths);
        await catalog.InitializeAsync();
        await catalog.UpsertMeetingAsync(new MeetingRecord
        {
            MeetingId = "vm1",
            Title = "Alpha sync",
            StartedAt = DateTimeOffset.UtcNow,
            Status = MeetingStatus.Local,
            LocalPath = paths.MeetingDirectory("vm1")
        });

        var lib = new LibraryViewModel(catalog, store);
        await lib.RefreshAsync();
        Assert.Single(lib.Items);

        lib.Search = "Alpha";
        await Task.Delay(20);
        await lib.RefreshAsync();
        Assert.Single(lib.Items);

        var settingsVm = new SettingsViewModel(store, new FakeAudioDeviceEnumerator(), new OneDriveAuthService(new DpapiSecretStore(paths), useMsal: () => false));
        settingsVm.IncludeMicrophone = false;
        settingsVm.MicrophoneDeviceId = "mic-usb-1";
        settingsVm.ChunkDurationMinutes = 25;
        Assert.False(store.Current.Capture.IncludeMicrophone);
        Assert.Equal("mic-usb-1", store.Current.Capture.MicrophoneDeviceId);
        Assert.Equal(25, store.Current.ChunkDurationMinutes);
    }

    private sealed class ScriptedDetector : ITeamsDetector
    {
        public DetectionResult Detect() => new()
        {
            IsInCall = false,
            Confidence = DetectionConfidence.None,
            RuleId = "scripted.idle"
        };

        public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Detect());
    }
}
