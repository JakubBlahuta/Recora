using Microsoft.Extensions.Logging;
using Recora.Capture;
using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Paths;
using Recora.Core.Settings;
using Recora.Encoding;

namespace Recora.App.Services;

public sealed class AppServices
{
    private readonly RecoraPaths _paths;
    private readonly SettingsStore _settings;
    private readonly ITeamsDetector _detector;
    private readonly ICatalogRepository _catalog;
    private readonly CaptureEngine _capture;
    private readonly EncodingCoordinator _encoding;
    private readonly ILoggerFactory _loggerFactory;

    public AppServices(
        RecoraPaths paths,
        SettingsStore settings,
        ITeamsDetector detector,
        ICatalogRepository catalog,
        CaptureEngine capture,
        EncodingCoordinator encoding,
        ILoggerFactory loggerFactory)
    {
        _paths = paths;
        _settings = settings;
        _detector = detector;
        _catalog = catalog;
        _capture = capture;
        _encoding = encoding;
        _loggerFactory = loggerFactory;
    }

    public RecoraPaths Paths => _paths;
    public SettingsStore Settings => _settings;
    public ICatalogRepository Catalog => _catalog;
    public IAudioDeviceEnumerator Devices => _capture.Devices;

    public RecordingAgent CreateAgent()
    {
        var useFake = string.Equals(
            Environment.GetEnvironmentVariable("RECORA_FAKE_CAPTURE"), "1", StringComparison.Ordinal);

        var agent = new RecordingAgent(
            _paths,
            _settings,
            _detector,
            _catalog,
            () => useFake ? new FakeCaptureSession() : _capture.CreateSession(),
            async request => useFake
                ? await DeterministicEncodingHost.StartAsync(_encoding, request, _catalog).ConfigureAwait(false)
                : await EncodingHost.StartBridgeAsync(_encoding, request, _catalog).ConfigureAwait(false),
            _loggerFactory.CreateLogger<RecordingAgent>());

        agent.DirectoryRecoverer = EncodingCoordinator.RecoverOrphanAsync;
        return agent;
    }
}
