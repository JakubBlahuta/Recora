using Recora.Core.Models;
using Recora.Detection;

namespace Recora.Core.Tests;

public class DetectionTests
{
    [Theory]
    [InlineData("Weekly sync | Meeting 00:12:03", true, DetectionConfidence.High)]
    [InlineData("Hovor 05:22 — Microsoft Teams", true, DetectionConfidence.High)]
    [InlineData("Stretnutie s klientom", true, DetectionConfidence.High)]
    [InlineData("Chat | Alice", false, DetectionConfidence.Low)]
    [InlineData("Kalendář", false, DetectionConfidence.Low)]
    [InlineData("", false, DetectionConfidence.None)]
    public void Title_heuristics(string title, bool inCall, DetectionConfidence minConfidence)
    {
        var (isInCall, confidence, _) = TeamsDetector.EvaluateTitleHeuristics(title);
        Assert.Equal(inCall, isInCall);
        if (inCall)
            Assert.True(confidence >= minConfidence);
        else
            Assert.True(confidence <= minConfidence);
    }

    [Fact]
    public void Detector_fail_soft_without_teams()
    {
        var detector = new TeamsDetector(new EmptyWindows());
        var result = detector.Detect();
        Assert.False(result.IsInCall);
        Assert.Equal(DetectionConfidence.None, result.Confidence);
    }

    private sealed class EmptyWindows : IWindowEnumerator
    {
        public IReadOnlyList<WindowInfo> EnumerateTopLevel() => Array.Empty<WindowInfo>();
    }
}
