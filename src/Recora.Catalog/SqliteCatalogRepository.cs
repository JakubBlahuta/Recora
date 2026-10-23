using Microsoft.Data.Sqlite;
using Recora.Core.Abstractions;
using Recora.Core.Models;
using Recora.Core.Paths;

namespace Recora.Catalog;

public sealed class SqliteCatalogRepository : ICatalogRepository, IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public SqliteCatalogRepository(RecoraPaths paths)
    {
        paths.EnsureCreated();
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.CatalogDb,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_connection is { State: System.Data.ConnectionState.Open })
            return;

        _connection?.Dispose();
        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            CREATE TABLE IF NOT EXISTS meetings (
              meeting_id TEXT PRIMARY KEY,
              title TEXT NOT NULL,
              started_at TEXT NOT NULL,
              ended_at TEXT NULL,
              duration_sec REAL NOT NULL DEFAULT 0,
              total_bytes INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL,
              detection_confidence TEXT NOT NULL,
              quality_profile TEXT NOT NULL,
              audio_mode TEXT NOT NULL,
              include_microphone INTEGER NOT NULL DEFAULT 1,
              microphone_device_id TEXT NOT NULL DEFAULT 'default',
              chunk_duration_sec INTEGER NOT NULL DEFAULT 1200,
              recovered INTEGER NOT NULL DEFAULT 0,
              local_path TEXT NOT NULL,
              cloud_path TEXT NULL,
              error_message TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS chunks (
              meeting_id TEXT NOT NULL,
              chunk_index INTEGER NOT NULL,
              file_name TEXT NOT NULL,
              start_sec REAL NOT NULL,
              end_sec REAL NOT NULL,
              byte_size INTEGER NOT NULL DEFAULT 0,
              sha256 TEXT NULL,
              complete INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY (meeting_id, chunk_index),
              FOREIGN KEY (meeting_id) REFERENCES meetings(meeting_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS upload_jobs (
              meeting_id TEXT PRIMARY KEY,
              status TEXT NOT NULL,
              attempt_count INTEGER NOT NULL DEFAULT 0,
              last_error TEXT NULL,
              updated_at TEXT NOT NULL,
              FOREIGN KEY (meeting_id) REFERENCES meetings(meeting_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_meetings_started ON meetings(started_at DESC);
            CREATE INDEX IF NOT EXISTS ix_upload_status ON upload_jobs(status);
            """;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private SqliteConnection Conn =>
        _connection ?? throw new InvalidOperationException("Catalog not initialized. Call InitializeAsync first.");

    public async Task UpsertMeetingAsync(MeetingRecord meeting, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO meetings (
              meeting_id, title, started_at, ended_at, duration_sec, total_bytes, status,
              detection_confidence, quality_profile, audio_mode, include_microphone,
              microphone_device_id, chunk_duration_sec, recovered, local_path, cloud_path, error_message)
            VALUES (
              $id, $title, $started, $ended, $dur, $bytes, $status,
              $conf, $quality, $audio, $mic, $micId, $chunkDur, $recovered, $local, $cloud, $err)
            ON CONFLICT(meeting_id) DO UPDATE SET
              title=excluded.title,
              ended_at=excluded.ended_at,
              duration_sec=excluded.duration_sec,
              total_bytes=excluded.total_bytes,
              status=excluded.status,
              detection_confidence=excluded.detection_confidence,
              quality_profile=excluded.quality_profile,
              audio_mode=excluded.audio_mode,
              include_microphone=excluded.include_microphone,
              microphone_device_id=excluded.microphone_device_id,
              chunk_duration_sec=excluded.chunk_duration_sec,
              recovered=excluded.recovered,
              local_path=excluded.local_path,
              cloud_path=excluded.cloud_path,
              error_message=excluded.error_message;
            """;
        cmd.Parameters.AddWithValue("$id", meeting.MeetingId);
        cmd.Parameters.AddWithValue("$title", meeting.Title);
        cmd.Parameters.AddWithValue("$started", meeting.StartedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ended", (object?)meeting.EndedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", meeting.DurationSec);
        cmd.Parameters.AddWithValue("$bytes", meeting.TotalBytes);
        cmd.Parameters.AddWithValue("$status", meeting.Status.ToString());
        cmd.Parameters.AddWithValue("$conf", meeting.DetectionConfidence.ToString());
        cmd.Parameters.AddWithValue("$quality", meeting.QualityProfile.ToString());
        cmd.Parameters.AddWithValue("$audio", meeting.AudioMode.ToString());
        cmd.Parameters.AddWithValue("$mic", meeting.IncludeMicrophone ? 1 : 0);
        cmd.Parameters.AddWithValue("$micId", meeting.MicrophoneDeviceId);
        cmd.Parameters.AddWithValue("$chunkDur", meeting.ChunkDurationSec);
        cmd.Parameters.AddWithValue("$recovered", meeting.Recovered ? 1 : 0);
        cmd.Parameters.AddWithValue("$local", meeting.LocalPath);
        cmd.Parameters.AddWithValue("$cloud", (object?)meeting.CloudPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$err", (object?)meeting.ErrorMessage ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<MeetingRecord?> GetMeetingAsync(string meetingId, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM meetings WHERE meeting_id = $id;";
        cmd.Parameters.AddWithValue("$id", meetingId);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadMeeting(reader);
    }

    public async Task<IReadOnlyList<MeetingRecord>> ListMeetingsAsync(string? search = null, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            cmd.CommandText = "SELECT * FROM meetings ORDER BY started_at DESC;";
        }
        else
        {
            cmd.CommandText = """
                SELECT * FROM meetings
                WHERE title LIKE $q OR meeting_id LIKE $q OR started_at LIKE $q
                ORDER BY started_at DESC;
                """;
            cmd.Parameters.AddWithValue("$q", "%" + search.Trim() + "%");
        }

        var list = new List<MeetingRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(ReadMeeting(reader));
        return list;
    }

    public async Task DeleteMeetingAsync(string meetingId, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM chunks WHERE meeting_id = $id;
            DELETE FROM upload_jobs WHERE meeting_id = $id;
            DELETE FROM meetings WHERE meeting_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", meetingId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertChunkAsync(ChunkRecord chunk, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chunks (meeting_id, chunk_index, file_name, start_sec, end_sec, byte_size, sha256, complete)
            VALUES ($id, $idx, $file, $start, $end, $size, $sha, $complete)
            ON CONFLICT(meeting_id, chunk_index) DO UPDATE SET
              file_name=excluded.file_name,
              start_sec=excluded.start_sec,
              end_sec=excluded.end_sec,
              byte_size=excluded.byte_size,
              sha256=excluded.sha256,
              complete=excluded.complete;
            """;
        cmd.Parameters.AddWithValue("$id", chunk.MeetingId);
        cmd.Parameters.AddWithValue("$idx", chunk.Index);
        cmd.Parameters.AddWithValue("$file", chunk.FileName);
        cmd.Parameters.AddWithValue("$start", chunk.StartSec);
        cmd.Parameters.AddWithValue("$end", chunk.EndSec);
        cmd.Parameters.AddWithValue("$size", chunk.ByteSize);
        cmd.Parameters.AddWithValue("$sha", (object?)chunk.Sha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$complete", chunk.Complete ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChunkRecord>> ListChunksAsync(string meetingId, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chunks WHERE meeting_id = $id ORDER BY chunk_index;";
        cmd.Parameters.AddWithValue("$id", meetingId);
        var list = new List<ChunkRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new ChunkRecord
            {
                MeetingId = reader.GetString(0),
                Index = reader.GetInt32(1),
                FileName = reader.GetString(2),
                StartSec = reader.GetDouble(3),
                EndSec = reader.GetDouble(4),
                ByteSize = reader.GetInt64(5),
                Sha256 = reader.IsDBNull(6) ? null : reader.GetString(6),
                Complete = reader.GetInt32(7) == 1
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<MeetingRecord>> ListOrphanRecordingSessionsAsync(CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM meetings WHERE status IN ('Recording', 'Finalizing');";
        var list = new List<MeetingRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(ReadMeeting(reader));
        return list;
    }

    public async Task EnqueueUploadAsync(string meetingId, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO upload_jobs (meeting_id, status, attempt_count, last_error, updated_at)
            VALUES ($id, 'Pending', 0, NULL, $at)
            ON CONFLICT(meeting_id) DO UPDATE SET
              status='Pending',
              last_error=NULL,
              updated_at=excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$id", meetingId);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var m = Conn.CreateCommand();
        m.CommandText = "UPDATE meetings SET status = 'UploadPending' WHERE meeting_id = $id;";
        m.Parameters.AddWithValue("$id", meetingId);
        await m.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UploadJob>> ListPendingUploadsAsync(CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM upload_jobs WHERE status IN ('Pending', 'Failed') ORDER BY updated_at;";
        var list = new List<UploadJob>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new UploadJob
            {
                MeetingId = reader.GetString(0),
                Status = Enum.Parse<UploadJobStatus>(reader.GetString(1)),
                AttemptCount = reader.GetInt32(2),
                LastError = reader.IsDBNull(3) ? null : reader.GetString(3),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(4))
            });
        }

        return list;
    }

    public async Task UpdateUploadStatusAsync(string meetingId, UploadJobStatus status, string? error = null, CancellationToken ct = default)
    {
        await using var cmd = Conn.CreateCommand();
        cmd.CommandText = """
            UPDATE upload_jobs
            SET status = $status,
                attempt_count = attempt_count + CASE WHEN $status = 'Uploading' THEN 1 ELSE 0 END,
                last_error = $err,
                updated_at = $at
            WHERE meeting_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", meetingId);
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var meetingStatus = status switch
        {
            UploadJobStatus.Uploading => MeetingStatus.Uploading,
            UploadJobStatus.Done => MeetingStatus.Synced,
            UploadJobStatus.Failed => MeetingStatus.Failed,
            _ => MeetingStatus.UploadPending
        };

        await using var m = Conn.CreateCommand();
        m.CommandText = "UPDATE meetings SET status = $s, error_message = $err WHERE meeting_id = $id;";
        m.Parameters.AddWithValue("$id", meetingId);
        m.Parameters.AddWithValue("$s", meetingStatus.ToString());
        m.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        await m.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static MeetingRecord ReadMeeting(SqliteDataReader reader) => new()
    {
        MeetingId = reader.GetString(0),
        Title = reader.GetString(1),
        StartedAt = DateTimeOffset.Parse(reader.GetString(2)),
        EndedAt = reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
        DurationSec = reader.GetDouble(4),
        TotalBytes = reader.GetInt64(5),
        Status = Enum.Parse<MeetingStatus>(reader.GetString(6)),
        DetectionConfidence = Enum.Parse<DetectionConfidence>(reader.GetString(7)),
        QualityProfile = Enum.Parse<QualityProfile>(reader.GetString(8)),
        AudioMode = Enum.Parse<AudioCaptureMode>(reader.GetString(9)),
        IncludeMicrophone = reader.GetInt32(10) == 1,
        MicrophoneDeviceId = reader.GetString(11),
        ChunkDurationSec = reader.GetInt32(12),
        Recovered = reader.GetInt32(13) == 1,
        LocalPath = reader.GetString(14),
        CloudPath = reader.IsDBNull(15) ? null : reader.GetString(15),
        ErrorMessage = reader.IsDBNull(16) ? null : reader.GetString(16)
    };

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
