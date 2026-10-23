using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Recora.Core.Abstractions;
using Recora.Core.Models;

namespace Recora.Detection;

/// <summary>
/// L1 (process + window) + L2 (title heuristics) Teams call detector with stability debounce.
/// Fail-soft: returns Confidence.None when nothing matches.
/// </summary>
public sealed class TeamsDetector : ITeamsDetector
{
    private readonly ILogger<TeamsDetector>? _logger;
    private readonly IWindowEnumerator _windows;
    private readonly IProcessScanner _processes;
    private readonly object _gate = new();
    private DetectionResult? _lastPositive;
    private int _positiveStreak;
    private int _negativeStreak;

    /// <summary>Require this many consecutive positive polls before auto-start (reduces flicker).</summary>
    public int ConfirmPositivePolls { get; set; } = 1;

    /// <summary>Require this many consecutive negative polls before auto-stop.</summary>
    public int ConfirmNegativePolls { get; set; } = 2;

    private static readonly string[] TeamsProcessNames =
    [
        "ms-teams",
        "Teams",
        "teams"
    ];

    public TeamsDetector(
        IWindowEnumerator? windows = null,
        IProcessScanner? processes = null,
        ILogger<TeamsDetector>? logger = null)
    {
        _windows = windows ?? new Win32WindowEnumerator();
        _processes = processes ?? new SystemProcessScanner();
        _logger = logger;
    }

    public DetectionResult Detect()
    {
        try
        {
            var raw = DetectRaw();
            return Stabilize(raw);
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

    /// <summary>Raw evaluation without debounce — useful for tests.</summary>
    public DetectionResult DetectRaw()
    {
        var teamsProcesses = _processes.GetTeamsProcesses(TeamsProcessNames);
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
            .Where(w => teamsProcesses.Any(p => p.ProcessId == w.ProcessId))
            .OrderByDescending(w => ScoreWindow(w))
            .ToList();

        foreach (var window in windows)
        {
            var title = window.Title ?? "";
            var l2 = TitleHeuristics.Evaluate(title);
            if (l2.IsInCall)
            {
                _logger?.LogInformation(
                    "Teams call detected via {Rule} profile={Profile} title='{Title}' pid={Pid}",
                    l2.RuleId, TitleHeuristics.ProfileVersion, Truncate(title), window.ProcessId);

                return new DetectionResult
                {
                    IsInCall = true,
                    Confidence = l2.Confidence,
                    WindowTitle = title,
                    WindowHandle = window.Handle,
                    ProcessId = window.ProcessId,
                    ProcessName = teamsProcesses.FirstOrDefault(p => p.ProcessId == window.ProcessId)?.ProcessName,
                    RuleId = l2.RuleId,
                    TeamsClient = ResolveClient(teamsProcesses)
                };
            }
        }

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
                ProcessName = teamsProcesses.FirstOrDefault(p => p.ProcessId == anyVisible.ProcessId)?.ProcessName,
                RuleId = "l1.process-only",
                TeamsClient = ResolveClient(teamsProcesses)
            };
        }

        return new DetectionResult
        {
            IsInCall = false,
            Confidence = DetectionConfidence.None,
            ProcessId = teamsProcesses[0].ProcessId,
            ProcessName = teamsProcesses[0].ProcessName,
            RuleId = "l1.process-no-window",
            TeamsClient = ResolveClient(teamsProcesses)
        };
    }

    private DetectionResult Stabilize(DetectionResult raw)
    {
        lock (_gate)
        {
            if (raw.IsInCall)
            {
                _positiveStreak++;
                _negativeStreak = 0;
                if (_positiveStreak >= ConfirmPositivePolls)
                {
                    _lastPositive = raw;
                    return raw;
                }

                // Not yet confirmed — surface as uncertain medium if we have a candidate
                return raw with { Confidence = DetectionConfidence.Medium, RuleId = raw.RuleId + ".pending" };
            }

            _negativeStreak++;
            _positiveStreak = 0;

            if (_lastPositive is not null && _negativeStreak < ConfirmNegativePolls)
            {
                // Hold last positive briefly to avoid dropping mid-call on title flicker
                return _lastPositive with { RuleId = _lastPositive.RuleId + ".hold" };
            }

            _lastPositive = null;
            return raw;
        }
    }

    private static int ScoreWindow(WindowInfo w)
    {
        var score = 0;
        var title = w.Title ?? "";
        var eval = TitleHeuristics.Evaluate(title);
        if (eval.IsInCall) score += 100 + (int)eval.Confidence * 10;
        if (title.Contains('|')) score += 5;
        if (w.Handle != IntPtr.Zero) score += 1;
        return score;
    }

    private static string ResolveClient(IReadOnlyList<ProcessInfo> processes)
    {
        foreach (var p in processes)
        {
            if (p.ProcessName.Equals("ms-teams", StringComparison.OrdinalIgnoreCase))
                return "new";
            if (p.ProcessName.Equals("Teams", StringComparison.OrdinalIgnoreCase))
                return "classic";
        }

        return "new";
    }

    /// <summary>Backward-compatible entry used by existing tests.</summary>
    public static (bool IsInCall, DetectionConfidence Confidence, string RuleId) EvaluateTitleHeuristics(string title) =>
        TitleHeuristics.Evaluate(title);

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..80];
}

public interface IProcessScanner
{
    IReadOnlyList<ProcessInfo> GetTeamsProcesses(IEnumerable<string> names);
}

public sealed class ProcessInfo
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
}

public sealed class SystemProcessScanner : IProcessScanner
{
    public IReadOnlyList<ProcessInfo> GetTeamsProcesses(IEnumerable<string> names)
    {
        var nameSet = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (nameSet.Contains(p.ProcessName))
                    list.Add(new ProcessInfo { ProcessId = p.Id, ProcessName = p.ProcessName });
            }
            catch
            {
                // access denied / exited
            }
        }

        return list;
    }
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
    public string? ClassName { get; init; }
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

            var classSb = new StringBuilder(256);
            GetClassName(hWnd, classSb, classSb.Capacity);

            results.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = sb.ToString(),
                ProcessId = unchecked((int)pid),
                ClassName = classSb.ToString()
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
}
