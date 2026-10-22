using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Recora.App.Services;
using Recora.Capture;
using Recora.Catalog;
using Recora.Cloud.OAuth;
using Recora.Cloud.Upload;
using Recora.Core.Abstractions;
using Recora.Core.Agent;
using Recora.Core.Logging;
using Recora.Core.Paths;
using Recora.Core.Settings;
using Recora.Detection;
using Recora.Encoding;

namespace Recora.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var services = BuildServices();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(services);
        });
    }

    private static ServiceProvider BuildServices()
    {
        var paths = new RecoraPaths();
        paths.EnsureCreated();

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(paths));
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddProvider(new FileLoggerProvider(paths));
        });

        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ICatalogRepository, SqliteCatalogRepository>();
        services.AddSingleton<ITeamsDetector, TeamsDetector>();
        services.AddSingleton<CaptureEngine>();
        services.AddSingleton<IAudioDeviceEnumerator, WasapiMicrophoneEnumerator>();
        services.AddSingleton<EncodingCoordinator>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<IOneDriveAuthService>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            return new OneDriveAuthService(
                sp.GetRequiredService<ISecretStore>(),
                sp.GetService<ILogger<OneDriveAuthService>>(),
                () => settings.Current.Cloud.UseMsal);
        });
        services.AddSingleton<IOneDriveUploadService, OneDriveUploadService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<AppServices>();
        services.AddSingleton(sp =>
        {
            var appServices = sp.GetRequiredService<AppServices>();
            return appServices.CreateAgent();
        });
        services.AddSingleton<UploadQueueWorker>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsStore>();
            return new UploadQueueWorker(
                sp.GetRequiredService<ICatalogRepository>(),
                sp.GetRequiredService<IOneDriveUploadService>(),
                sp.GetRequiredService<IOneDriveAuthService>(),
                () => settings.Current.Cloud.FolderPath,
                sp.GetService<ILogger<UploadQueueWorker>>(),
                () => settings.Current.Cloud.AutoUpload);
        });

        return services.BuildServiceProvider();
    }
}
