namespace Recora.Core;

public static class MeetingIdFactory
{
    public static string Create(DateTimeOffset? at = null)
    {
        var t = at ?? DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{t:yyyy-MM-ddTHH-mm-ssZ}_{suffix}";
    }
}
