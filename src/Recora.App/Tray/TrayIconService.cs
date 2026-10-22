using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Recora.Core.Agent;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.App.Tray;

/// <summary>
/// System tray host: Idle / Recording (red dot) / Uncertain + Force record / Stop / Open / Exit.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly RecordingAgent _agent;
    private readonly Action<bool> _openWindow;
    private readonly Action _exit;
    private TaskbarIcon? _icon;

    public TrayIconService(RecordingAgent agent, Action<bool> openWindow, Action exit)
    {
        _agent = agent;
        _openWindow = openWindow;
        _exit = exit;
        _agent.StateChanged += (_, _) => UpdateTooltip();
    }

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "Recora",
            ContextMenuMode = ContextMenuMode.SecondWindow
        };

        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateItem("force", OnForce));
        flyout.Items.Add(CreateItem("stop", OnStop));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(CreateItem("library", () => _openWindow(false)));
        flyout.Items.Add(CreateItem("exit", _exit));
        _icon.ContextFlyout = flyout;
        _icon.ForceCreate();
        UpdateTooltip();
    }

    private MenuFlyoutItem CreateItem(string id, Action action)
    {
        var item = new MenuFlyoutItem { Tag = id };
        item.Click += (_, _) => action();
        Relabel(item);
        return item;
    }

    private void Relabel(MenuFlyoutItem item)
    {
        var lang = App.Services.GetService<SettingsStore>()?.Current.GetUiLanguage() ?? UiLanguage.En;

        item.Text = (string?)item.Tag switch
        {
            "force" => StringCatalog.Get("Tray.ForceRecord", lang),
            "stop" => StringCatalog.Get("Tray.Stop", lang),
            "library" => StringCatalog.Get("Tray.OpenLibrary", lang),
            "exit" => StringCatalog.Get("Tray.Exit", lang),
            _ => item.Text
        };
    }

    private void UpdateTooltip()
    {
        if (_icon is null) return;
        var lang = App.Services.GetService<SettingsStore>()?.Current.GetUiLanguage() ?? UiLanguage.En;

        _icon.ToolTipText = _agent.State switch
        {
            TrayAgentState.Recording => StringCatalog.Get("Tray.Recording", lang),
            TrayAgentState.Uncertain => StringCatalog.Get("Tray.Uncertain", lang),
            _ => StringCatalog.Get("Tray.Idle", lang)
        };

        // Recording indicator: red-dot semantics via tooltip; custom icon asset can replace later.
        if (_icon.ContextFlyout is MenuFlyout flyout)
        {
            foreach (var obj in flyout.Items)
            {
                if (obj is MenuFlyoutItem mi)
                    Relabel(mi);
            }
        }
    }

    private async void OnForce() => await _agent.ForceRecordAsync().ConfigureAwait(true);
    private async void OnStop() => await _agent.ForceStopAsync().ConfigureAwait(true);

    public void Dispose() => _icon?.Dispose();
}
