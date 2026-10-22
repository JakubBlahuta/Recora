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
        return new RecordingAgent(
            _paths,
            _settings,
            _detector,
            _catalog,
            () => _capture.CreateSession(),
            async request => await EncodingHost.StartBridgeAsync(_encoding, request).ConfigureAwait(false),
            _loggerFactory.CreateLogger<RecordingAgent>());
    }
}
