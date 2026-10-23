using System.Text.RegularExpressions;
using Recora.Core.Models;

namespace Recora.Detection;

/// <summary>
/// Versioned L2 title / chrome heuristics for new Teams + classic (CS / SK / EN).
/// </summary>
public static partial class TitleHeuristics
{
    public const string ProfileVersion = "2026.10.22-v2";

    public static (bool IsInCall, DetectionConfidence Confidence, string RuleId) Evaluate(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (false, DetectionConfidence.None, "l2.empty");

        var normalized = CollapseWhitespace(title);

        // Explicit non-call surfaces first (avoid false positives on "Meeting chat")
        if (ChatOnlyRegex().IsMatch(normalized) && !HasCallChrome(normalized))
            return (false, DetectionConfidence.Low, "l2.chat-only");

        if (CalendarOnlyRegex().IsMatch(normalized) && !HasCallChrome(normalized))
            return (false, DetectionConfidence.Low, "l2.calendar");

        // High: duration timer + leave/hang-up, or localized in-call chrome
        if (CallDurationRegex().IsMatch(normalized) && (MeetingNounRegex().IsMatch(normalized) || LeaveRegex().IsMatch(normalized)))
            return (true, DetectionConfidence.High, "l2.duration+chrome");

        if (LeaveRegex().IsMatch(normalized) && MeetingNounRegex().IsMatch(normalized))
            return (true, DetectionConfidence.High, "l2.leave+meeting");

        if (InCallPhraseRegex().IsMatch(normalized))
            return (true, DetectionConfidence.High, "l2.in-call-phrase");

        if (CallDurationRegex().IsMatch(normalized) && TeamsBrandRegex().IsMatch(normalized))
            return (true, DetectionConfidence.High, "l2.duration+teams");

        // Medium: Teams brand + meeting/call noun without timer
        if (TeamsBrandRegex().IsMatch(normalized) && MeetingNounRegex().IsMatch(normalized))
            return (true, DetectionConfidence.Medium, "l2.teams+meeting");

        if (ShareScreenRegex().IsMatch(normalized))
            return (true, DetectionConfidence.Medium, "l2.screenshare");

        if (MeetingNounRegex().IsMatch(normalized) && ParticipantCountRegex().IsMatch(normalized))
            return (true, DetectionConfidence.Medium, "l2.meeting+participants");

        // Medium: bare meeting noun with pipe separator typical of call windows
        if (PipeMeetingRegex().IsMatch(normalized))
            return (true, DetectionConfidence.Medium, "l2.pipe-meeting");

        // Medium: localized meeting/call noun as primary title (force-record friendly)
        if (MeetingNounRegex().IsMatch(normalized))
            return (true, DetectionConfidence.Medium, "l2.meeting-noun");

        return (false, DetectionConfidence.None, "l2.no-match");
    }

    private static bool HasCallChrome(string title) =>
        CallDurationRegex().IsMatch(title) || LeaveRegex().IsMatch(title) || InCallPhraseRegex().IsMatch(title);

    private static string CollapseWhitespace(string s) =>
        WhitespaceRegex().Replace(s.Trim(), " ");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(:\d{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex CallDurationRegex();

    [GeneratedRegex(
        @"\b(Leave|Hang up|End (call|meeting)|Opustit|Ukončit(\s+hovor)?|Opustiť|Ukončiť(\s+hovor)?|Odísť)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeaveRegex();

    [GeneratedRegex(
        @"\b(Meeting|Call|Schůzka|Schuzka|Hovor|Stretnutie|Porada|Konference|Konferencia)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeetingNounRegex();

    [GeneratedRegex(
        @"\b(In a call|You're in the meeting|Probíhá hovor|Jste na schůzce|Ste na stretnutí|Prebieha hovor|Na hovore)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InCallPhraseRegex();

    [GeneratedRegex(@"\b(Microsoft\s+)?Teams\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TeamsBrandRegex();

    [GeneratedRegex(
        @"\b(Sharing screen|Presenting|Sdílíte obrazovku|Zdieľate obrazovku|Prezentace|Prezentácia)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShareScreenRegex();

    [GeneratedRegex(@"\b\d{1,3}\s*(participants?|účastník[ůuy]?|účastníkov)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParticipantCountRegex();

    [GeneratedRegex(@"\|\s*(Meeting|Call|Schůzka|Hovor|Stretnutie)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipeMeetingRegex();

    [GeneratedRegex(
        @"\b(Chat|Activity|Aktivita|Conversations?|Konverzace|Konverzácie)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatOnlyRegex();

    [GeneratedRegex(
        @"\b(Calendar|Kalendář|Kalendár|Schedule|Plánovač)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalendarOnlyRegex();
}
