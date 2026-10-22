using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Detection;

/// <summary>
/// L1 (process + window) + L2 (title/toolbar heuristics) Teams call detector.
/// Fail-soft: returns Confidence.None when nothing matches.
/// </summary>
public sealed partial class TeamsDetector : ITeamsDetector
{
    private readonly ILogger<TeamsDetector>? _logger;
    private readonly IWindowEnumerator _windows;

    private static readonly string[] TeamsProcessNames =
    [
        "ms-teams",
        "Teams",
        "teams"
    ];

    public TeamsDetector(IWindowEnumerator? windows = null, ILogger<TeamsDetector>? logger = null)
    {
        _windows = windows ?? new Win32WindowEnumerator();
        _logger = logger;
    }

    public DetectionResult Detect()
    {
        try
        {
            var teamsProcesses = Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return TeamsProcessNames.Any(n =>
                            p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase));
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            if (teamsProcesses.Count == 0)
            {
                return new DetectionResult
                {
                    IsInCall = false,
                    Confidence = DetectionConfidence.None,
                    RuleId = "l1.no-process"
                };
            }

            var windows = _windows.EnumerateTopLevel()
                .Where(w => teamsProcesses.Any(p => p.Id == w.ProcessId))
                .ToList();

            foreach (var window in windows)
            {
                var title = window.Title ?? "";
                var l2 = EvaluateTitleHeuristics(title);
                if (l2.IsInCall)
                {
                    _logger?.LogInformation(
                        "Teams call detected via {Rule} title='{Title}' pid={Pid}",
                        l2.RuleId, Truncate(title), window.ProcessId);

                    return new DetectionResult
                    {
                        IsInCall = true,
                        Confidence = l2.Confidence,
                        WindowTitle = title,
                        WindowHandle = window.Handle,
                        ProcessId = window.ProcessId,
                        ProcessName = teamsProcesses.FirstOrDefault(p => p.Id == window.ProcessId)?.ProcessName,
                        RuleId = l2.RuleId,
                        TeamsClient = ResolveClient(teamsProcesses)
                    };
                }
            }

            // Teams running but no call-like window → low/uncertain
            var anyVisible = windows.FirstOrDefault(w => !string.IsNullOrWhiteSpace(w.Title));
            if (anyVisible is not null)
            {
                return new DetectionResult
                {
                    IsInCall = false,
                    Confidence = DetectionConfidence.Low,
                    WindowTitle = anyVisible.Title,
                    WindowHandle = anyVisible.Handle,
                    ProcessId = anyVisible.ProcessId,
                    ProcessName = teamsProcesses.FirstOrDefault(p => p.Id == anyVisible.ProcessId)?.ProcessName,
                    RuleId = "l1.process-only",
                    TeamsClient = ResolveClient(teamsProcesses)
                };
            }

            return new DetectionResult
            {
                IsInCall = false,
                Confidence = DetectionConfidence.None,
                ProcessId = teamsProcesses[0].Id,
                ProcessName = teamsProcesses[0].ProcessName,
                RuleId = "l1.process-no-window",
                TeamsClient = ResolveClient(teamsProcesses)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teams detection failed soft");
            return new DetectionResult
            {
                IsInCall = false,
                Confidence = DetectionConfidence.None,
                RuleId = "error"
            };
        }
    }

    public Task<DetectionResult> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(Detect, cancellationToken);

    private static string ResolveClient(IEnumerable<Process> processes)
    {
        foreach (var p in processes)
        {
            try
            {
                if (p.ProcessName.Equals("ms-teams", StringComparison.OrdinalIgnoreCase))
                    return "new";
                if (p.ProcessName.Equals("Teams", StringComparison.OrdinalIgnoreCase))
                    return "classic";
            }
            catch
            {
                // ignore
            }
        }

        return "new";
    }

    public static (bool IsInCall, DetectionConfidence Confidence, string RuleId) EvaluateTitleHeuristics(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (false, DetectionConfidence.None, "l2.empty");

        // High confidence: explicit meeting/call duration or leave controls in title patterns (CS/SK/EN)
        if (CallDurationRegex().IsMatch(title) || MeetingKeywordsHigh().IsMatch(title))
            return (true, DetectionConfidence.High, "l2.title-high");

        if (MeetingKeywordsMedium().IsMatch(title))
            return (true, DetectionConfidence.Medium, "l2.title-medium");

        // Chat-only / calendar windows — not a call
        if (ChatOnlyRegex().IsMatch(title))
            return (false, DetectionConfidence.Low, "l2.chat-only");

        return (false, DetectionConfidence.None, "l2.no-match");
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..80];

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(:\d{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex CallDurationRegex();

    [GeneratedRegex(
        @"(\|?\s*(Meeting|Call|Schůzka|Schuzka|Hovor|Stretnutie|Hovor\s+prebieha)\b)|(\bLeave\b|\bOpustit\b|\bOpustiť\b|\bHang up\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeetingKeywordsHigh();

    [GeneratedRegex(
        @"(\bMicrosoft Teams\b).{0,40}(\bmeeting\b|\bcall\b|\bschůzka\b|\bstretnutie\b|\bhovor\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MeetingKeywordsMedium();

    [GeneratedRegex(
        @"\b(Chat|Activity|Calendar|Kalendář|Kalendár|Aktivita)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChatOnlyRegex();
}

public interface IWindowEnumerator
{
    IReadOnlyList<WindowInfo> EnumerateTopLevel();
}

public sealed class WindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = "";
    public int ProcessId { get; init; }
}

public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    public IReadOnlyList<WindowInfo> EnumerateTopLevel()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Array.Empty<WindowInfo>();

        var results = new List<WindowInfo>();

        EnumWindows((hWnd, lParam) =>
        {
            _ = lParam;
            if (!IsWindowVisible(hWnd)) return true;
            var length = GetWindowTextLength(hWnd);
            if (length <= 0) return true;

            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            GetWindowThreadProcessId(hWnd, out var pid);

            results.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = sb.ToString(),
                ProcessId = unchecked((int)pid)
            });
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
