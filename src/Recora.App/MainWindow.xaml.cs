using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Recora.App.Views;
using Recora.Core.Localization;
using Recora.Core.Settings;

namespace Recora.App;

public sealed partial class MainWindow : Window
{
    private readonly ServiceProvider _services;
    private readonly SettingsStore _settings;

    public MainWindow(ServiceProvider services, bool showWizard)
    {
        _services = services;
        _settings = services.GetRequiredService<SettingsStore>();
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        ApplyLanguageChrome();

        if (showWizard || !_settings.Current.FirstRunCompleted)
            ContentFrame.Navigate(typeof(WizardPage), _services);
        else
            ContentFrame.Navigate(typeof(LibraryPage), _services);
    }

    private void ApplyLanguageChrome()
    {
        var lang = _settings.Current.GetUiLanguage();
        BrandTitle.Text = StringCatalog.Get("App.Name", lang);
        BrandSubtitle.Text = lang switch
        {
            Core.Models.UiLanguage.Cs => "Nahrávač Teams hovorů — připraveno pro AI",
            Core.Models.UiLanguage.Sk => "Nahrávač Teams hovorov — pripravené pre AI",
            _ => "Teams call recorder — AI-ready"
        };
        LibraryNav.Content = StringCatalog.Get("Library.Title", lang);
        SettingsNav.Content = StringCatalog.Get("Settings.Title", lang);
    }

    private void LibraryNav_Click(object sender, RoutedEventArgs e) =>
        ContentFrame.Navigate(typeof(LibraryPage), _services);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) =>
        ContentFrame.Navigate(typeof(SettingsPage), _services);

    public void RefreshChrome() => ApplyLanguageChrome();
}
