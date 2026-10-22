using System.Collections.ObjectModel;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Recora.App.ViewModels;
using Recora.Core.Abstractions;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;
using Windows.Media.Core;
using Windows.Storage;

namespace Recora.App.Views;

public sealed partial class LibraryPage : Page
{
    private ServiceProvider _services = null!;
    private ICatalogRepository _catalog = null!;
    private SettingsStore _settings = null!;
    private RecoraPaths _paths = null!;
    private readonly ObservableCollection<MeetingListItem> _items = [];

    public LibraryPage()
    {
        InitializeComponent();
        MeetingList.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (ServiceProvider)e.Parameter;
        _catalog = _services.GetRequiredService<ICatalogRepository>();
        _settings = _services.GetRequiredService<SettingsStore>();
        _paths = _services.GetRequiredService<RecoraPaths>();
        await _catalog.InitializeAsync().ConfigureAwait(true);
        ApplyStrings();
        await ReloadAsync().ConfigureAwait(true);
    }

    private UiLanguage Lang => _settings.Current.GetUiLanguage();

    private void ApplyStrings()
    {
        var lang = Lang;
        SearchBox.PlaceholderText = StringCatalog.Get("Library.Search", lang);
        EmptyText.Text = StringCatalog.Get("Library.Empty", lang);
        TranscriptPlaceholder.Text = StringCatalog.Get("Library.TranscriptPlaceholder", lang);
        PlayButton.Content = lang switch
        {
            UiLanguage.Cs => "Přehrát",
            UiLanguage.Sk => "Prehrať",
            _ => "Play"
        };
        ExportButton.Content = lang switch
        {
            UiLanguage.Cs => "Export ZIP",
            UiLanguage.Sk => "Export ZIP",
            _ => "Export ZIP"
        };
        DeleteButton.Content = lang switch
        {
            UiLanguage.Cs => "Smazat",
            UiLanguage.Sk => "Zmazať",
            _ => "Delete"
        };
    }

    private async Task ReloadAsync(string? search = null)
    {
        var meetings = await _catalog.ListMeetingsAsync(search).ConfigureAwait(true);
        _items.Clear();
        foreach (var m in meetings)
            _items.Add(MeetingListItem.From(m, Lang));
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        await ReloadAsync(SearchBox.Text).ConfigureAwait(true);

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await ReloadAsync(SearchBox.Text).ConfigureAwait(true);

    private void MeetingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var has = MeetingList.SelectedItem is MeetingListItem;
        PlayButton.IsEnabled = has;
        ExportButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingList.SelectedItem is not MeetingListItem item) return;
        var chunks = await _catalog.ListChunksAsync(item.MeetingId).ConfigureAwait(true);
        var first = chunks.FirstOrDefault();
        if (first is null) return;
        var path = Path.Combine(item.LocalPath, first.FileName);
        if (!File.Exists(path) || new FileInfo(path).Length < 32)
        {
            TranscriptPlaceholder.Text = Lang switch
            {
                UiLanguage.Cs => "Chunk ještě není přehrávatelný (placeholder encode).",
                UiLanguage.Sk => "Chunk ešte nie je prehrateľný (placeholder encode).",
                _ => "Chunk is not playable yet (placeholder encode)."
            };
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        Player.Visibility = Visibility.Visible;
        Player.Source = MediaSource.CreateFromStorageFile(file);
        Player.MediaPlayer.Play();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingList.SelectedItem is not MeetingListItem item) return;
        if (!Directory.Exists(item.LocalPath)) return;
        var zip = Path.Combine(_paths.Cache, item.MeetingId + ".zip");
        Directory.CreateDirectory(_paths.Cache);
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(item.LocalPath, zip);
        TranscriptPlaceholder.Text = $"ZIP: {zip}";
        await Task.CompletedTask;
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (MeetingList.SelectedItem is not MeetingListItem item) return;
        await _catalog.DeleteMeetingAsync(item.MeetingId).ConfigureAwait(true);
        if (Directory.Exists(item.LocalPath))
            Directory.Delete(item.LocalPath, recursive: true);
        await ReloadAsync(SearchBox.Text).ConfigureAwait(true);
    }
}
