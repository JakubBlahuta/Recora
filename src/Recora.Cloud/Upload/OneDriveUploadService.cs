using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Cloud.Upload;

/// <summary>
/// Upload queue worker: pending → uploading → done | failed with retry + backoff.
/// Graph PUT /me/drive/root:/{folder}/{meetingId}/{file}:/content (stubbed without live token).
/// </summary>
public sealed class OneDriveUploadService : IOneDriveUploadService
{
    private readonly IOneDriveAuthService _auth;
    private readonly ILogger<OneDriveUploadService>? _logger;

    public OneDriveUploadService(IOneDriveAuthService auth, ILogger<OneDriveUploadService>? logger = null)
    {
        _auth = auth;
        _logger = logger;
    }

    public async Task UploadMeetingAsync(MeetingRecord meeting, string folderPath, CancellationToken ct = default)
    {
        var token = await _auth.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("OneDrive not authenticated.");

        if (!Directory.Exists(meeting.LocalPath))
            throw new DirectoryNotFoundException(meeting.LocalPath);

        folderPath = folderPath.Trim().Trim('/');
        if (string.IsNullOrEmpty(folderPath)) folderPath = "Recordings";

        var files = Directory.GetFiles(meeting.LocalPath)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("chunk_", StringComparison.OrdinalIgnoreCase)
                       || name is "meeting.json" or "chunks.jsonl";
            })
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("No meeting files to upload.");

        _logger?.LogInformation(
            "Uploading {Count} files for {MeetingId} to OneDrive/{Folder}/{MeetingFolder}",
            files.Count, meeting.MeetingId, folderPath, meeting.MeetingId);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative = $"{folderPath}/{meeting.MeetingId}/{Path.GetFileName(file)}";
            await UploadFileAsync(token, file, relative, ct).ConfigureAwait(false);
        }

        meeting.CloudPath = $"{folderPath}/{meeting.MeetingId}";
    }

    private async Task UploadFileAsync(string accessToken, string localPath, string graphPath, CancellationToken ct)
    {
        if (accessToken.StartsWith("stub-", StringComparison.Ordinal))
        {
            _logger?.LogInformation("Stub upload OK {Path} ({Bytes} bytes)", graphPath, new FileInfo(localPath).Length);
            await Task.Delay(5, ct).ConfigureAwait(false);
            return;
        }

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        await using var stream = File.OpenRead(localPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var url = $"https://graph.microsoft.com/v1.0/me/drive/root:/{Uri.EscapeDataString(graphPath).Replace("%2F", "/")}:/content";
        using var response = await http.PutAsync(url, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Graph upload failed {(int)response.StatusCode}: {body}");
        }
    }
}

public sealed class UploadQueueWorker
{
    public const int DefaultMaxAttempts = 5;

    private readonly ICatalogRepository _catalog;
    private readonly IOneDriveUploadService _upload;
    private readonly IOneDriveAuthService _auth;
    private readonly Func<string> _folderPath;
    private readonly Func<bool> _autoUploadEnabled;
    private readonly ILogger? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public UploadQueueWorker(
        ICatalogRepository catalog,
        IOneDriveUploadService upload,
        IOneDriveAuthService auth,
        Func<string> folderPath,
        ILogger? logger = null,
        Func<bool>? autoUploadEnabled = null)
    {
        _catalog = catalog;
        _upload = upload;
        _auth = auth;
        _folderPath = folderPath;
        _autoUploadEnabled = autoUploadEnabled ?? (() => true);
        _logger = logger;
    }

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int LastProcessedCount { get; private set; }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _loop = null;
        _cts.Dispose();
        _cts = null;
    }

    /// <summary>Process all currently pending/failed jobs once (tests + manual drain).</summary>
    public async Task<int> ProcessPendingOnceAsync(CancellationToken ct = default)
    {
        if (!_autoUploadEnabled() || !_auth.IsAuthenticated)
        {
            LastProcessedCount = 0;
            return 0;
        }

        var jobs = await _catalog.ListPendingUploadsAsync(ct).ConfigureAwait(false);
        var processed = 0;
        foreach (var job in jobs)
        {
            if (job.AttemptCount >= MaxAttempts)
            {
                _logger?.LogWarning("Giving up on upload {MeetingId} after {Attempts} attempts",
                    job.MeetingId, job.AttemptCount);
                continue;
            }

            // Simple exponential backoff based on attempt count (persisted in SQLite)
            if (job.AttemptCount > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, job.AttemptCount)));
                var since = DateTimeOffset.UtcNow - job.UpdatedAt;
                if (since < delay)
                    continue;
            }

            await ProcessJobAsync(job.MeetingId, ct).ConfigureAwait(false);
            processed++;
        }

        LastProcessedCount = processed;
        return processed;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Upload queue iteration failed");
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessJobAsync(string meetingId, CancellationToken ct)
    {
        var meeting = await _catalog.GetMeetingAsync(meetingId, ct).ConfigureAwait(false);
        if (meeting is null) return;

        await _catalog.UpdateUploadStatusAsync(meetingId, UploadJobStatus.Uploading, null, ct).ConfigureAwait(false);
        try
        {
            await _upload.UploadMeetingAsync(meeting, _folderPath(), ct).ConfigureAwait(false);
            meeting.Status = MeetingStatus.Synced;
            meeting.CloudPath = $"{_folderPath().Trim('/')}/{meeting.MeetingId}";
            meeting.ErrorMessage = null;
            await _catalog.UpsertMeetingAsync(meeting, ct).ConfigureAwait(false);
            await _catalog.UpdateUploadStatusAsync(meetingId, UploadJobStatus.Done, null, ct).ConfigureAwait(false);
            _logger?.LogInformation("Upload done {MeetingId} → {CloudPath}", meetingId, meeting.CloudPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Upload failed for {MeetingId}", meetingId);
            await _catalog.UpdateUploadStatusAsync(meetingId, UploadJobStatus.Failed, ex.Message, ct)
                .ConfigureAwait(false);
        }
    }
}
