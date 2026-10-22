using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Cloud.Upload;

/// <summary>
/// Upload queue worker: pending → uploading → done | failed with retry.
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
            .ToList();

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
        // Stub: when token is the stub marker, simulate success without network.
        if (accessToken.StartsWith("stub-", StringComparison.Ordinal))
        {
            _logger?.LogInformation("Stub upload OK {Path} ({Bytes} bytes)", graphPath, new FileInfo(localPath).Length);
            await Task.Delay(10, ct).ConfigureAwait(false);
            return;
        }

        // Real Graph upload session:
        // PUT https://graph.microsoft.com/v1.0/me/drive/root:/{graphPath}:/content
        // For large files: createUploadSession + fragment upload with resume.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        await using var stream = File.OpenRead(localPath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var url = $"https://graph.microsoft.com/v1.0/me/drive/root:/{graphPath}:/content";
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
    private readonly ICatalogRepository _catalog;
    private readonly IOneDriveUploadService _upload;
    private readonly IOneDriveAuthService _auth;
    private readonly Func<string> _folderPath;
    private readonly ILogger? _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public UploadQueueWorker(
        ICatalogRepository catalog,
        IOneDriveUploadService upload,
        IOneDriveAuthService auth,
        Func<string> folderPath,
        ILogger? logger = null)
    {
        _catalog = catalog;
        _upload = upload;
        _auth = auth;
        _folderPath = folderPath;
        _logger = logger;
    }

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
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_auth.IsAuthenticated)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                    continue;
                }

                var jobs = await _catalog.ListPendingUploadsAsync(ct).ConfigureAwait(false);
                foreach (var job in jobs)
                {
                    if (job.AttemptCount >= 5) continue;
                    await ProcessJobAsync(job.MeetingId, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Upload queue iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
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
            await _catalog.UpsertMeetingAsync(meeting, ct).ConfigureAwait(false);
            await _catalog.UpdateUploadStatusAsync(meetingId, UploadJobStatus.Done, null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Upload failed for {MeetingId}", meetingId);
            await _catalog.UpdateUploadStatusAsync(meetingId, UploadJobStatus.Failed, ex.Message, ct)
                .ConfigureAwait(false);
        }
    }
}
