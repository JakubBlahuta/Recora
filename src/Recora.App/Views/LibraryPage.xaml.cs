using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Recora.Core.Abstractions;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Paths;
using Recora.Core.Settings;
using Recora.Core.ViewModels;
using Windows.Media.Core;
using Windows.Storage;

namespace Recora.App.Views;

public sealed partial class LibraryPage : Page
{
    private ServiceProvider _services = null!;
    private LibraryViewModel _vm = null!;
    private RecoraPaths _paths = null!;
    private SettingsStore _settings = null!;
    private ICatalogRepository _catalog = null!;

    public LibraryPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _services = (ServiceProvider)e.Parameter;
        _catalog = _services.GetRequiredService<ICatalogRepository>();
        _settings = _services.GetRequiredService<SettingsStore>();
        _paths = _services.GetRequiredService<RecoraPaths>();
        _vm = new LibraryViewModel(_catalog, _settings);
        MeetingList.ItemsSource = _vm.Items;
        ApplyStrings();
        await _vm.RefreshAsync().ConfigureAwait(true);
        EmptyText.Visibility = _vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UiLanguage Lang => _settings.Current.GetUiLanguage();

    private void ApplyStrings()
    {
        SearchBox.PlaceholderText = StringCatalog.Get("Library.Search", Lang);
        EmptyText.Text = _vm.EmptyText;
        TranscriptPlaceholder.Text = _vm.TranscriptPlaceholder;
        PlayButton.Content = Lang switch
        {
            UiLanguage.Cs => "Přehrát",
            UiLanguage.Sk => "Prehrať",
            _ => "Play"
        };
        ExportButton.Content = "Export ZIP";
        DeleteButton.Content = Lang switch
        {
            UiLanguage.Cs => "Smazat",
            UiLanguage.Sk => "Zmazať",
            _ => "Delete"
        };
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Search = SearchBox.Text;
        await Task.Delay(10);
        EmptyText.Visibility = _vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _vm.RefreshAsync().ConfigureAwait(true);
        EmptyText.Visibility = _vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MeetingList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.Selected = MeetingList.SelectedItem as MeetingListItem;
        var has = _vm.HasSelection;
        PlayButton.IsEnabled = has;
        ExportButton.IsEnabled = has;
        DeleteButton.IsEnabled = has;
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is null) return;
        var chunks = await _catalog.ListChunksAsync(_vm.Selected.MeetingId).ConfigureAwait(true);
        var first = chunks.FirstOrDefault();
        if (first is null) return;
        var path = Path.Combine(_vm.Selected.LocalPath, first.FileName);
        if (!File.Exists(path) || new FileInfo(path).Length < 32)
        {
            TranscriptPlaceholder.Text = Lang switch
            {
                UiLanguage.Cs => "Chunk ještě není přehrávatelný (demo encode).",
                UiLanguage.Sk => "Chunk ešte nie je prehrateľný (demo encode).",
                _ => "Chunk is not playable yet (demo encode)."
            };
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            Player.Visibility = Visibility.Visible;
            Player.Source = MediaSource.CreateFromStorageFile(file);
            Player.MediaPlayer.Play();
        }
        catch
        {
            TranscriptPlaceholder.Text = "Playback unavailable for this chunk format.";
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var zip = await _vm.ExportSelectedZipAsync(_paths.Cache).ConfigureAwait(true);
        if (zip is not null)
            TranscriptPlaceholder.Text = $"ZIP: {zip}";
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await _vm.DeleteSelectedAsync().ConfigureAwait(true);
        EmptyText.Visibility = _vm.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
