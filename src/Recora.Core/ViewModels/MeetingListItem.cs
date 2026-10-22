using Recora.Core.Localization;
using Recora.Core.Models;

namespace Recora.Core.ViewModels;

public sealed class MeetingListItem
{
    public required string MeetingId { get; init; }
    public required string Title { get; init; }
    public required string StartedAtLocal { get; init; }
    public required string DurationLabel { get; init; }
    public required string SizeLabel { get; init; }
    public required string StatusLabel { get; init; }
    public required string LocalPath { get; init; }

    public static MeetingListItem From(MeetingRecord m, UiLanguage lang)
    {
        var statusKey = m.Status switch
        {
            MeetingStatus.Synced => "Status.Synced",
            MeetingStatus.Uploading or MeetingStatus.UploadPending => "Status.Uploading",
            MeetingStatus.Failed => "Status.Failed",
            MeetingStatus.Recovered => "Status.Recovered",
            _ => "Status.Local"
        };

        var dur = TimeSpan.FromSeconds(m.DurationSec);
        return new MeetingListItem
        {
            MeetingId = m.MeetingId,
            Title = m.Title,
            StartedAtLocal = m.StartedAt.ToLocalTime().ToString("g"),
            DurationLabel = dur.TotalHours >= 1
                ? $"{(int)dur.TotalHours}:{dur.Minutes:D2}:{dur.Seconds:D2}"
                : $"{dur.Minutes}:{dur.Seconds:D2}",
            SizeLabel = FormatBytes(m.TotalBytes),
            StatusLabel = string.IsNullOrWhiteSpace(m.ErrorMessage)
                ? StringCatalog.Get(statusKey, lang)
                : StringCatalog.Get(statusKey, lang) + " ⚠",
            LocalPath = m.LocalPath
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
