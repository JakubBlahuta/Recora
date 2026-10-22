using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Recora.Core.Abstractions;
using Recora.Core.Localization;
using Recora.Core.Models;
using Recora.Core.Settings;

namespace Recora.Core.ViewModels;

public sealed class LibraryViewModel : INotifyPropertyChanged
{
    private readonly ICatalogRepository _catalog;
    private readonly SettingsStore _settings;
    private string _search = "";
    private MeetingListItem? _selected;
    private string _statusMessage = "";
    private bool _isBusy;

    public LibraryViewModel(ICatalogRepository catalog, SettingsStore settings)
    {
        _catalog = catalog;
        _settings = settings;
    }

    public ObservableCollection<MeetingListItem> Items { get; } = [];

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
                _ = RefreshAsync();
        }
    }

    public MeetingListItem? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public string EmptyText => StringCatalog.Get("Library.Empty", _settings.Current.GetUiLanguage());
    public string TranscriptPlaceholder => StringCatalog.Get("Library.TranscriptPlaceholder", _settings.Current.GetUiLanguage());

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await _catalog.InitializeAsync(ct).ConfigureAwait(true);
            var meetings = await _catalog.ListMeetingsAsync(
                string.IsNullOrWhiteSpace(Search) ? null : Search, ct).ConfigureAwait(true);
            var lang = _settings.Current.GetUiLanguage();
            Items.Clear();
            foreach (var m in meetings)
                Items.Add(MeetingListItem.From(m, lang));
            StatusMessage = $"{Items.Count}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        if (Selected is null) return;
        var item = Selected;
        await _catalog.DeleteMeetingAsync(item.MeetingId, ct).ConfigureAwait(true);
        if (Directory.Exists(item.LocalPath))
            Directory.Delete(item.LocalPath, recursive: true);
        Selected = null;
        await RefreshAsync(ct).ConfigureAwait(true);
    }

    public async Task<string?> ExportSelectedZipAsync(string cacheDir, CancellationToken ct = default)
    {
        if (Selected is null || !Directory.Exists(Selected.LocalPath)) return null;
        Directory.CreateDirectory(cacheDir);
        var zip = Path.Combine(cacheDir, Selected.MeetingId + ".zip");
        if (File.Exists(zip)) File.Delete(zip);
        System.IO.Compression.ZipFile.CreateFromDirectory(Selected.LocalPath, zip);
        await Task.CompletedTask;
        return zip;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
