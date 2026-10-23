using Recora.Core.Models;
using Recora.Detection;

namespace Recora.Core.Tests;

public class DetectionTests
{
    [Theory]
    [InlineData("Weekly sync | Meeting 00:12:03", true, DetectionConfidence.High)]
    [InlineData("Hovor 05:22 — Microsoft Teams", true, DetectionConfidence.High)]
    [InlineData("Stretnutie s klientom", true, DetectionConfidence.Medium)]
    [InlineData("You're in the meeting", true, DetectionConfidence.High)]
    [InlineData("Probíhá hovor | Teams", true, DetectionConfidence.High)]
    [InlineData("Sharing screen — Q3 review", true, DetectionConfidence.Medium)]
    [InlineData("Chat | Alice", false, DetectionConfidence.Low)]
    [InlineData("Kalendář", false, DetectionConfidence.Low)]
    [InlineData("Activity | Microsoft Teams", false, DetectionConfidence.Low)]
    [InlineData("", false, DetectionConfidence.None)]
    public void Title_heuristics(string title, bool inCall, DetectionConfidence minConfidence)
    {
        var (isInCall, confidence, _) = TeamsDetector.EvaluateTitleHeuristics(title);
        Assert.Equal(inCall, isInCall);
        if (inCall)
            Assert.True(confidence >= minConfidence, $"got {confidence} expected >= {minConfidence}");
        else
            Assert.True(confidence <= minConfidence, $"got {confidence} expected <= {minConfidence}");
    }

    [Fact]
    public void Detector_fail_soft_without_teams()
    {
        var detector = new TeamsDetector(new EmptyWindows(), new EmptyProcesses());
        var result = detector.Detect();
        Assert.False(result.IsInCall);
        Assert.Equal(DetectionConfidence.None, result.Confidence);
    }

    [Fact]
    public void Detector_scores_call_window_over_chat()
    {
        var windows = new FakeWindows(
        [
            new WindowInfo { Handle = 1, ProcessId = 42, Title = "Chat | Alice" },
            new WindowInfo { Handle = 2, ProcessId = 42, Title = "Standup | Meeting 00:03:12" }
        ]);
        var processes = new FakeProcesses([new ProcessInfo { ProcessId = 42, ProcessName = "ms-teams" }]);
        var detector = new TeamsDetector(windows, processes) { ConfirmPositivePolls = 1, ConfirmNegativePolls = 1 };
        var result = detector.DetectRaw();
        Assert.True(result.IsInCall);
        Assert.Equal(2, result.WindowHandle);
        Assert.Equal(DetectionConfidence.High, result.Confidence);
    }

    [Fact]
    public void Detector_holds_positive_across_flicker()
    {
        var windows = new MutableWindows();
        var processes = new FakeProcesses([new ProcessInfo { ProcessId = 7, ProcessName = "ms-teams" }]);
        var detector = new TeamsDetector(windows, processes) { ConfirmPositivePolls = 1, ConfirmNegativePolls = 3 };

        windows.Set(new WindowInfo { Handle = 9, ProcessId = 7, Title = "Call 01:00 | Teams" });
        Assert.True(detector.Detect().IsInCall);

        windows.Set(new WindowInfo { Handle = 9, ProcessId = 7, Title = "Microsoft Teams" });
        var held = detector.Detect();
        Assert.True(held.IsInCall);
        Assert.Contains("hold", held.RuleId);
    }

    private sealed class EmptyWindows : IWindowEnumerator
    {
        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => Array.Empty<WindowInfo>();
    }

    private sealed class EmptyProcesses : IProcessScanner
    {
        public IReadOnlyList<ProcessInfo> GetTeamsProcesses(IEnumerable<string> names) => Array.Empty<ProcessInfo>();
    }

    private sealed class FakeWindows(IReadOnlyList<WindowInfo> windows) : IWindowEnumerator
    {
        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => windows;
    }

    private sealed class MutableWindows : IWindowEnumerator
    {
        private IReadOnlyList<WindowInfo> _windows = Array.Empty<WindowInfo>();
        public void Set(params WindowInfo[] w) => _windows = w;
        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => _windows;
    }

    private sealed class FakeProcesses(IReadOnlyList<ProcessInfo> processes) : IProcessScanner
    {
        public IReadOnlyList<ProcessInfo> GetTeamsProcesses(IEnumerable<string> names) => processes;
    }
}
