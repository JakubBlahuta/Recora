using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Recora.App.Services;
using Recora.App.Tray;
using Recora.Cloud.Upload;
using Recora.Core.Agent;
using Recora.Core.Settings;

namespace Recora.App;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;
    private TrayIconService? _tray;

    public App(ServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        Services = services;
    }

    public static ServiceProvider Services { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = _services.GetRequiredService<SettingsStore>();
        var agent = _services.GetRequiredService<RecordingAgent>();
        var upload = _services.GetRequiredService<UploadQueueWorker>();

        await agent.StartAsync().ConfigureAwait(true);
        if (settings.Current.Cloud.AutoUpload)
            upload.Start();

        _tray = new TrayIconService(agent, OpenMainWindow, ExitApp);
        _tray.Initialize();

        if (!settings.Current.FirstRunCompleted)
        {
            OpenMainWindow(showWizard: true);
        }
    }

    public void OpenMainWindow(bool showWizard = false)
    {
        if (_window is null)
        {
            _window = new MainWindow(_services, showWizard);
            _window.Closed += (_, _) => _window = null;
        }

        _window.Activate();
    }

    private async void ExitApp()
    {
        var agent = _services.GetRequiredService<RecordingAgent>();
        var upload = _services.GetRequiredService<UploadQueueWorker>();
        await upload.StopAsync().ConfigureAwait(true);
        await agent.DisposeAsync().ConfigureAwait(true);
        _tray?.Dispose();
        Exit();
    }
}
