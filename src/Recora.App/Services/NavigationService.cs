using Microsoft.UI.Xaml.Controls;

namespace Recora.App.Services;

public enum AppPage
{
    Wizard,
    Library,
    Settings
}

public sealed class NavigationService
{
    private Frame? _frame;
    private object? _parameter;

    public void Attach(Frame frame, object navigationParameter)
    {
        _frame = frame;
        _parameter = navigationParameter;
    }

    public void Navigate(AppPage page)
    {
        if (_frame is null) return;
        var type = page switch
        {
            AppPage.Wizard => typeof(Views.WizardPage),
            AppPage.Settings => typeof(Views.SettingsPage),
            _ => typeof(Views.LibraryPage)
        };
        _frame.Navigate(type, _parameter);
    }
}
