﻿﻿
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WF = System.Windows.Forms;
using Application      = System.Windows.Application;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Brush            = System.Windows.Media.Brush;
using SolidColorBrush  = System.Windows.Media.SolidColorBrush;
using Color            = System.Windows.Media.Color;
using WpfTextBox       = System.Windows.Controls.TextBox;
using WpfButton        = System.Windows.Controls.Button;
using WpfBrushes       = System.Windows.Media.Brushes;
using WpfComboBox      = System.Windows.Controls.ComboBox;
using BitmapImage      = System.Windows.Media.Imaging.BitmapImage;
using BitmapCacheOpt   = System.Windows.Media.Imaging.BitmapCacheOption;
namespace RoleplayOverlay
{
  public partial class EditorWindow : Window
  {
    private readonly ProjectService    _projectService;
    private readonly RenderService     _renderService;
    private readonly AutoSaveService   _autoSave;
    private readonly UndoRedoService   _undoRedo = new(maxLevels: 50);
    private readonly TtsTimingService  _ttsTiming = new(debounceMs: 800);
    private readonly MediaProxyService _mediaProxy = new(debounceMs: 200);
    private readonly SequencePlayer         _player;
    private readonly AudioRecorderService    _recorder;
    private readonly ObservableCollection<Sequence> _items = new();
    private Project _project => _projectService.Project;
    private bool    _suppressVisibility;
    private bool    _suppressMediaUpdate;
    private int _selectedMediaItemIndex = -1;
    private bool _suppressMediaItemSelection;
    private enum SortMode { Free = 0, PairsEnFr = 1, PairsFrEn = 2 }
    private SortMode _sortMode = SortMode.Free;
    private bool     _suppressSortModeChange;
    private bool     IsSortByPairs => _sortMode != SortMode.Free;
    private readonly List<string> _originalOrder = new();
    private bool    _previewExpanded;
    private string? _lastPreviewPngPath;
    private string _subB1Primary  = "#FF00FF";
    private string _subB1Outline  = "#FFFFFF";
    private string _subB2Primary  = "#00FFFF";
    private string _subB2Outline  = "#FFFFFF";
    private string _subYouPrimary = "#FFD400";
    private string _subYouOutline = "#FFFFFF";
    public EditorWindow(ProjectService projectService, RenderService renderService, AutoSaveService autoSave, SequencePlayer player)
    {
      _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
      _renderService  = renderService  ?? throw new ArgumentNullException(nameof(renderService));
      _autoSave       = autoSave       ?? throw new ArgumentNullException(nameof(autoSave));
      _player         = player;
      _recorder       = new AudioRecorderService(@"C:\ffmpeg\bin\ffmpeg.exe");
      InitializeComponent();
      Closed += (_, _) => _recorder?.Dispose();
      GridItems.ItemsSource = _items;
      InitSortMode();
      RefreshSceneList();
      LoadSceneIntoGrid();
      LoadMonitorsIntoDisplayBox();
      InitQuickVoiceBoxes();
      _suppressVisibility = true;
      ShowYouCheck.IsChecked  = _project.Global.ShowYou;
      ShowBot1Check.IsChecked = _project.Global.ShowBot1;
      ShowBot2Check.IsChecked = _project.Global.ShowBot2;
      _suppressVisibility = false;
      Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault()
        ?.ApplyVisibilityFrom(_project.Global);
      RegisterKeyBindings();
      _projectService.DirtyChanged  += (_, _2) => Dispatcher.Invoke(UpdateTitle);
      _projectService.ProjectSaved  += (_, _2) => Dispatcher.Invoke(UpdateTitle);
      _projectService.ErrorOccurred += (_, msg) =>
        Dispatcher.Invoke(() => MessageBox.Show(this, msg, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error));
      _autoSave.Pending += (_, _2) => Dispatcher.Invoke(() => SetSaveIndicator("?", "#E8A317", "Modifications non sauvegardées…"));
      _autoSave.Saved   += (_, _2) => Dispatcher.Invoke(() => SetSaveIndicator("?", "#4DC93C", "Sauvegardé automatiquement"));
      _autoSave.Failed  += (_, _2) => Dispatcher.Invoke(() => SetSaveIndicator("?", "#E84118", "échec auto-save"));
      _ttsTiming.DurationComputed += (seqId, duration) => Dispatcher.Invoke(() =>
      {
        var seq = _items.FirstOrDefault(s => s.Id == seqId);
        if (seq != null)
        {
          seq.EstimatedDuration = duration;
          seq.DurationIsExact   = true;
          if (ReferenceEquals(seq, GridItems.SelectedItem))
            RecalcVideoSpeed(seq);
        }
      });
      _mediaProxy.SnapshotReady += (pngPath) => Dispatcher.Invoke(() => LoadScrubberThumb(pngPath));
      _undoRedo.StateChanged += (_, _2) => Dispatcher.Invoke(() =>
      {
        UpdateUndoRedoButtons();
        RefreshHistory();
      });
      CleanupOldPreviews();
      MediaThumbnailConverter.ThumbnailExtracted += _ =>
        Dispatcher.Invoke(() => GridItems.Items.Refresh());
      UpdateTitle();
      Logger.Info("EditorWindow constructed OK");
      Loaded += (_, _2) =>
      {
        GridItems.RowHeight = UserPrefs.RowHeight;
        RestoreVideoAspectFromPrefs();
        var last = UserPrefs.LastProjectPath;
        if (!string.IsNullOrWhiteSpace(last) && File.Exists(last))
        {
          Logger.Info($"[AutoLoad] Rechargement du dernier projet : {last}");
          AutoLoadProject(last);
        }
      };
    }
    private void UpdateTitle()
    {
      Title = _projectService.WindowTitle;
      SetSaveIndicator(_projectService.IsDirty ? "?" : "?",
        _projectService.IsDirty ? "#E8A317" : "#4DC93C",
        _projectService.IsDirty ? "Modifications non sauvegardées" : "Projet sauvegardé");
    }
    private void SetSaveIndicator(string symbol, string hex, string tip)
    {
      SaveIndicator.Text    = symbol;
      SaveIndicator.Foreground = HexToBrush(hex);
      SaveIndicator.ToolTip = tip;
    }
    private void LoadSceneIntoGrid()
    {
      foreach (var s in _items) s.PropertyChanged -= OnSequencePropertyChanged;
      MediaThumbnailConverter.FfmpegExe = CollectRenderSettings().FfmpegExePath;
      _projectService.LoadCurrentSceneInto(_items);
      foreach (var s in _items) s.PropertyChanged += OnSequencePropertyChanged;
      _undoRedo.Clear();
      var migrated = PairingService.MigrateFromLegacy(_items);
      if (migrated > 0)
      {
        _projectService.SyncAndMarkDirty(_items);
        Logger.Info($"[Pairing] Migré {migrated} séquences en paires.");
      }
      int autoSwitched = 0;
      foreach (var seq in _items)
      {
        if (string.Equals(seq.Mode, "mp3", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(seq.Mp3))
        {
          var before = seq.Speaker;
          AutoSwitchSpeakerForMp3(seq);
          if (!string.Equals(before, seq.Speaker, StringComparison.OrdinalIgnoreCase))
            autoSwitched++;
        }
      }
      if (autoSwitched > 0)
      {
        _projectService.SyncAndMarkDirty(_items);
        Logger.Info($"[AutoSwitch] Rétroactif : {autoSwitched} séquences FR+MP3 ? speaker=you");
      }
      foreach (var s in _items)
        if (s.EstimatedDuration == default && !string.IsNullOrWhiteSpace(s.Text))
          s.EstimatedDuration = TtsTimingService.EstimateFromWordCount(s.Text, s.Voice);
      CaptureOriginalOrder();
      ApplySort();
      RefreshPairNumbers();
      UpdateStats();
      _ttsTiming.ComputeAllAsync(_items);
    }
    private void UpdateStats()
    {
      if (StatsBlock == null) return;
      var total   = _items.Count;
      var paired  = _items.Count(s => s.IsPaired);
      var pairIds = _items.Where(s => s.IsPaired).Select(s => s.PairId)
                          .Distinct(StringComparer.OrdinalIgnoreCase).Count();
      var orphans = total - paired;
      StatsBlock.Text = $"{total} séq — {pairIds} paires ({paired} liées) — {orphans} orphelines";
      if (StatsDur != null)
      {
        var totalSec = _items.Sum(s => s.EstimatedDuration.TotalSeconds);
        if (totalSec > 0)
        {
          var ts = TimeSpan.FromSeconds(totalSec);
          StatsDur.Text = ts.TotalHours >= 1
            ? $"≈ {(int)ts.TotalHours}h{ts.Minutes:D2}m{ts.Seconds:D2}s"
            : ts.TotalMinutes >= 1
              ? $"≈ {(int)ts.TotalMinutes}m{ts.Seconds:D2}s"
              : $"≈ {ts.Seconds}s";
        }
        else StatsDur.Text = "";
      }
      if (StatsMedia != null)
      {
        var withMedia = _items.Count(s => !string.IsNullOrWhiteSpace(s.MediaPath));
        StatsMedia.Text = withMedia > 0 ? $"?? {withMedia}/{total} avec média" : "";
      }
    }
    private void ApplySort()
    {
      if (_sortMode == SortMode.Free) return;
      var sorted = PairingService.SortByPairs(_items, frenchFirst: _sortMode == SortMode.PairsFrEn);
      bool needsSort = false;
      if (sorted.Count == _items.Count)
        for (int i = 0; i < sorted.Count; i++)
          if (!ReferenceEquals(sorted[i], _items[i])) { needsSort = true; break; }
      if (!needsSort) { RefreshPairNumbers(); return; }
      var selected = GridItems.SelectedItem;
      _items.Clear();
      foreach (var s in sorted) _items.Add(s);
      if (selected != null) GridItems.SelectedItem = selected;
      RefreshPairNumbers();
    }
    private void CaptureOriginalOrder()
    {
      _originalOrder.Clear();
      foreach (var s in _items)
        _originalOrder.Add(s.Id ?? "");
    }
    private void RestoreOriginalOrder()
    {
      if (_originalOrder.Count == 0) return;
      var orderMap = new Dictionary<string, int>(StringComparer.Ordinal);
      for (int i = 0; i < _originalOrder.Count; i++)
        if (!orderMap.ContainsKey(_originalOrder[i]))
          orderMap[_originalOrder[i]] = i;
      var sorted = _items
        .Select((s, curIdx) => (seq: s, curIdx))
        .OrderBy(t => orderMap.TryGetValue(t.seq.Id ?? "", out var idx) ? idx : int.MaxValue)
        .ThenBy(t => t.curIdx)
        .Select(t => t.seq)
        .ToList();
      bool changed = false;
      for (int i = 0; i < sorted.Count; i++)
        if (!ReferenceEquals(sorted[i], _items[i])) { changed = true; break; }
      if (!changed) { RefreshPairNumbers(); return; }
      var selected = GridItems.SelectedItem;
      _items.Clear();
      foreach (var s in sorted) _items.Add(s);
      if (selected != null) GridItems.SelectedItem = selected;
      RefreshPairNumbers();
    }
    private void RefreshPairNumbers()
    {
      var nums = PairBadgeConverter.PairNumbers;
      nums.Clear();
      int next = 1;
      foreach (var s in _items)
      {
        if (string.IsNullOrWhiteSpace(s.PairId)) continue;
        if (!nums.ContainsKey(s.PairId))
          nums[s.PairId] = next++;
      }
      GridItems.Items.Refresh();
    }
    private void InitSortMode()
    {
      _sortMode = UserPrefs.SortMode switch
      {
        "PairsEnFr" => SortMode.PairsEnFr,
        "PairsFrEn" => SortMode.PairsFrEn,
        _           => SortMode.Free,
      };
      _suppressSortModeChange = true;
      SortModeBox.SelectedIndex = (int)_sortMode;
      _suppressSortModeChange = false;
      Logger.Info($"[Sort] init ? {_sortMode} (depuis prefs)");
    }
    private void OnSortModeChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressSortModeChange) return;
      if (SortModeBox.SelectedIndex < 0) return;
      var newMode = (SortMode)SortModeBox.SelectedIndex;
      if (newMode == _sortMode) return;
      var prev = _sortMode;
      _sortMode = newMode;
      Logger.Info($"[Sort] {prev} ? {_sortMode}");
      if (_sortMode == SortMode.Free)
        RestoreOriginalOrder();
      else
        ApplySort();
      UserPrefs.SortMode = _sortMode.ToString();
      try { UserPrefs.Save(); }
      catch (Exception ex) { Logger.Warn($"[Sort] UserPrefs.Save failed: {ex.Message}"); }
    }
    private void OnLinkPairClick(object sender, RoutedEventArgs e)
    {
      var selected = GridItems.SelectedItems.Cast<Sequence>().ToList();
      Logger.Info($"[LinkPair] click, selected={selected.Count}, mode={_sortMode}");
      if (selected.Count != 2)
      {
        MessageBox.Show(this,
          "Sélectionne exactement 2 séquences dans la grille pour les lier en paire.\n(Ctrl+clic pour une sélection multiple.)",
          "Lier paire", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      _undoRedo.PushUndo(_items, "Lier paire");
      var a = selected[0]; var b = selected[1];
      if (!string.IsNullOrWhiteSpace(a.PairId)) PairingService.Unlink(a);
      if (!string.IsNullOrWhiteSpace(b.PairId)) PairingService.Unlink(b);
      PairingService.LinkPair(a, b);
      PropagateMedia(a);
      _projectService.SyncAndMarkDirty(_items);
      ApplySort();
      GridItems.Items.Refresh();
      Logger.Info($"[LinkPair] Liés pairId={a.PairId} — {a.Id} ? {b.Id}");
    }
    private void OnPickMp3Click(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      if (string.Equals(seq.Mode, "mp3", StringComparison.OrdinalIgnoreCase)
          && !string.IsNullOrWhiteSpace(seq.Mp3)
          && File.Exists(seq.Mp3))
      {
        OnPlayAudioClick(sender, e);
        return;
      }
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title  = "Choisir un fichier audio",
        Filter = "Audio|*.mp3;*.wav;*.ogg;*.flac;*.m4a|Tous|*.*"
      };
      if (ofd.ShowDialog() != true) return;
      _undoRedo.PushUndo(_items, "Assigner MP3");
      seq.Mp3  = CopyAndRenameAsset(seq, ofd.FileName, isAudio: true);
      seq.Mode = "mp3";
      AutoSwitchSpeakerForMp3(seq);
      _projectService.SyncAndMarkDirty(_items);
      GridItems.Items.Refresh();
    }
    private void AutoSwitchSpeakerForMp3(Sequence seq)
    {
      if (string.IsNullOrWhiteSpace(seq.Mp3)) return;
      if (!string.Equals(seq.Speaker, "you", StringComparison.OrdinalIgnoreCase))
      {
        seq.Speaker = "you";
        seq.Mode    = "mp3";
        Logger.Info($"[AutoSwitch] Seq {seq.Id}: speaker ? you (MP3 assigned)");
      }
    }
    private void OnClearMp3Click(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      _undoRedo.PushUndo(_items, "Retirer MP3");
      seq.Mp3  = null;
      seq.Mode = "tts";
      _projectService.SyncAndMarkDirty(_items);
      GridItems.Items.Refresh();
    }
    private string? GetSelectedMediaItemPath(Sequence seq)
    {
      if (seq.MediaItems != null && _selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < seq.MediaItems.Count)
        return seq.MediaItems[_selectedMediaItemIndex].Path;
      return seq.MediaPath;
    }
    private string? GetSelectedMediaItemSourcePath(Sequence seq)
    {
      if (seq.MediaItems != null && _selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < seq.MediaItems.Count)
        return seq.MediaItems[_selectedMediaItemIndex].SourcePath;
      return seq.MediaSourcePath;
    }
    private void OnMediaCtxViewSource(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      OpenFile(GetSelectedMediaItemSourcePath(seq) ?? GetSelectedMediaItemPath(seq));
    }
    private void OnMediaCtxShowInExplorer(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      ShowInExplorer(GetSelectedMediaItemPath(seq));
    }
    private void OnMediaCtxOpenCurrent(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      OpenFile(GetSelectedMediaItemPath(seq));
    }
    private void OnMediaCtxSourceInExplorer(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      ShowInExplorer(GetSelectedMediaItemSourcePath(seq) ?? GetSelectedMediaItemPath(seq));
    }
    private void OnMediaCtxMigrate(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      if (string.IsNullOrWhiteSpace(seq.MediaPath) || !File.Exists(seq.MediaPath)) return;
      _undoRedo.PushUndo(_items, "Migrer média");
      seq.MediaPath = CopyAndRenameAsset(seq, seq.MediaPath, isAudio: false);
      PropagateMedia(seq); _ = UpdateMediaPanelAsync(); _projectService.SyncAndMarkDirty(_items);
    }
    private void OnMediaCtxRename(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence;
      if (string.IsNullOrWhiteSpace(seq?.MediaPath) || !File.Exists(seq.MediaPath)) return;
      var projName  = Path.GetFileNameWithoutExtension(_projectService.FilePath ?? "project");
      var slideNum  = ExtractSlideNumber(seq.Note ?? "");
      var origExt   = Path.GetExtension(seq.MediaPath);
      var origBase  = Path.GetFileNameWithoutExtension(seq.MediaPath);
      var newName   = slideNum.HasValue
        ? $"{projName}_slide{slideNum:D3}_{origBase}{origExt}"
        : $"{projName}_{seq.Id![..8]}_{origBase}{origExt}";
      var slidesDir = Path.Combine(@"C:\RoleplayOverlay\slides", projName, "media");
      Directory.CreateDirectory(slidesDir);
      var destPath = Path.Combine(slidesDir, newName);
      if (destPath == seq.MediaPath)
      { MessageBox.Show(this, "Le fichier est déjà correctement nommé.", "Renommer", MessageBoxButton.OK, MessageBoxImage.Information); return; }
      try
      {
        File.Copy(seq.MediaPath, destPath, overwrite: true);
        _undoRedo.PushUndo(_items, "Renommer média");
        seq.MediaPath = destPath;
        PropagateMedia(seq);
        _projectService.SyncAndMarkDirty(_items);
        _ = UpdateMediaPanelAsync();
        MessageBox.Show(this, $"Copié vers :\n{destPath}", "Renommer", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      catch (Exception ex) { MessageBox.Show(this, $"Erreur : {ex.Message}", "Renommer", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private static int? ExtractSlideNumber(string note)
    {
      var m = System.Text.RegularExpressions.Regex.Match(note, @"\bslide\s*(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
      return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }
    private void OnTextCellGotFocus(object sender, RoutedEventArgs e)
    {
      if (sender is System.Windows.Controls.TextBox tb)
      {
        tb.SelectAll();
        tb.CaretIndex = tb.Text.Length;
      }
    }
    private void OnGridPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
      if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
        return;
      e.Handled = true;
      double newH = Math.Clamp(GridItems.RowHeight + (e.Delta > 0 ? 4 : -4), 24, 200);
      GridItems.RowHeight = newH;
      UserPrefs.RowHeight = newH;
      UserPrefs.Save();
    }
    private void PropagateMedia(Sequence source) => PairingService.PropagateMediaIfNeeded(source, _items);
    private void RegisterKeyBindings()
    {
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnAdd(null!, null!)),         new KeyGesture(Key.Insert)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnRemove(null!, null!)),      new KeyGesture(Key.Delete, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnUp(null!, null!)),          new KeyGesture(Key.Up, ModifierKeys.Alt)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnDown(null!, null!)),        new KeyGesture(Key.Down, ModifierKeys.Alt)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnPlaySelected(null!, null!)),new KeyGesture(Key.F5)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnStop(null!, null!)),        new KeyGesture(Key.Escape)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnUndo(null!, null!)),        new KeyGesture(Key.Z, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnRedo(null!, null!)),        new KeyGesture(Key.Y, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => { SearchBox.Focus(); SearchBox.SelectAll(); }), new KeyGesture(Key.F, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnToggleReplace(null!, null!)), new KeyGesture(Key.H, ModifierKeys.Control)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnSearchNext(null!, null!)),   new KeyGesture(Key.F3)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnSearchPrev(null!, null!)),   new KeyGesture(Key.F3, ModifierKeys.Shift)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnSplitSequence(null!, null!)), new KeyGesture(Key.X, ModifierKeys.Control | ModifierKeys.Shift)));
      InputBindings.Add(new KeyBinding(new RelayCommand(_ => OnLinkPairClick(null!, null!)), new KeyGesture(Key.L, ModifierKeys.Control)));
    }
    private readonly List<(int Index, string Field)> _searchHits = new();
    private int _searchCursor = -1;
    private void OpenSearchBar(bool withReplace)
    {
      SearchBar.Visibility = Visibility.Visible;
      ReplacePanel.Visibility = withReplace ? Visibility.Visible : Visibility.Collapsed;
      SearchBox.Focus();
      SearchBox.SelectAll();
    }
    private void CloseSearchBar()
    {
      SearchBox.Text = "";
      ReplacePanel.Visibility = Visibility.Collapsed;
      _searchHits.Clear();
      _searchCursor = -1;
      SearchCountLabel.Text = "";
    }
    private void OnSearchClose(object sender, RoutedEventArgs e) => CloseSearchBar();
    private void OnToggleReplace(object sender, RoutedEventArgs e)
    {
      if (ReplacePanel.Visibility == Visibility.Visible)
        ReplacePanel.Visibility = Visibility.Collapsed;
      else
      {
        ReplacePanel.Visibility = Visibility.Visible;
        ReplaceBox.Focus();
      }
    }
    private void OnSearchBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == Key.Escape) { CloseSearchBar(); e.Handled = true; }
      else if (e.Key == Key.Enter)
      {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) OnSearchPrev(null!, null!);
        else OnSearchNext(null!, null!);
        e.Handled = true;
      }
    }
    private void OnReplaceBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key == Key.Escape) { CloseSearchBar(); e.Handled = true; }
      else if (e.Key == Key.Enter) { OnReplaceCurrent(null!, null!); e.Handled = true; }
    }
    private void OnSearchTermChanged(object sender, EventArgs e)
    {
      RunSearch();
    }
    private void RunSearch()
    {
      _searchHits.Clear();
      _searchCursor = -1;
      var term = SearchBox.Text;
      if (string.IsNullOrEmpty(term)) { SearchCountLabel.Text = ""; return; }
      var cmp = SearchCaseCheck.IsChecked == true
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;
      bool allFields = SearchFieldsCheck.IsChecked == true;
      for (int i = 0; i < _items.Count; i++)
      {
        var seq = _items[i];
        if (!string.IsNullOrEmpty(seq.Text) && seq.Text.Contains(term, cmp))
          _searchHits.Add((i, "Text"));
        if (allFields)
        {
          if (!string.IsNullOrEmpty(seq.Note) && seq.Note.Contains(term, cmp))
            _searchHits.Add((i, "Note"));
          if (!string.IsNullOrEmpty(seq.Speaker) && seq.Speaker.Contains(term, cmp))
            _searchHits.Add((i, "Speaker"));
          if (!string.IsNullOrEmpty(seq.Voice) && seq.Voice.Contains(term, cmp))
            _searchHits.Add((i, "Voice"));
        }
      }
      if (_searchHits.Count > 0) _searchCursor = 0;
      UpdateSearchUI();
      NavigateToCurrentHit();
    }
    private void OnSearchNext(object sender, RoutedEventArgs e)
    {
      if (_searchHits.Count == 0) return;
      _searchCursor = (_searchCursor + 1) % _searchHits.Count;
      UpdateSearchUI();
      NavigateToCurrentHit();
    }
    private void OnSearchPrev(object sender, RoutedEventArgs e)
    {
      if (_searchHits.Count == 0) return;
      _searchCursor = (_searchCursor - 1 + _searchHits.Count) % _searchHits.Count;
      UpdateSearchUI();
      NavigateToCurrentHit();
    }
    private void UpdateSearchUI()
    {
      if (_searchHits.Count == 0)
        SearchCountLabel.Text = "0 résultat";
      else
        SearchCountLabel.Text = $"{_searchCursor + 1}/{_searchHits.Count}";
    }
    private void NavigateToCurrentHit()
    {
      if (_searchCursor < 0 || _searchCursor >= _searchHits.Count) return;
      var (idx, _) = _searchHits[_searchCursor];
      if (idx < 0 || idx >= GridItems.Items.Count) return;
      GridItems.SelectedIndex = idx;
      GridItems.ScrollIntoView(GridItems.Items[idx]);
    }
    private void OnReplaceCurrent(object sender, RoutedEventArgs e)
    {
      if (_searchCursor < 0 || _searchCursor >= _searchHits.Count) return;
      var term = SearchBox.Text;
      var replacement = ReplaceBox.Text ?? "";
      if (string.IsNullOrEmpty(term)) return;
      var (idx, field) = _searchHits[_searchCursor];
      var seq = _items[idx];
      _undoRedo.PushUndo(_items, "Remplacer texte");
      var cmp = SearchCaseCheck.IsChecked == true
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;
      ReplaceInField(seq, field, term, replacement, cmp);
      _projectService.SyncAndMarkDirty(_items);
      RunSearch();
    }
    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
      if (_searchHits.Count == 0) return;
      var term = SearchBox.Text;
      var replacement = ReplaceBox.Text ?? "";
      if (string.IsNullOrEmpty(term)) return;
      _undoRedo.PushUndo(_items, $"Tout remplacer ({_searchHits.Count})");
      var cmp = SearchCaseCheck.IsChecked == true
        ? StringComparison.Ordinal
        : StringComparison.OrdinalIgnoreCase;
      var processed = new HashSet<(int, string)>();
      foreach (var hit in _searchHits)
      {
        if (processed.Add(hit))
          ReplaceInField(_items[hit.Index], hit.Field, term, replacement, cmp);
      }
      _projectService.SyncAndMarkDirty(_items);
      var count = processed.Count;
      RunSearch();
      MessageBox.Show(this, $"{count} remplacement(s) effectué(s).", "Remplacer tout",
        MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private static void ReplaceInField(Sequence seq, string field, string term, string replacement, StringComparison cmp)
    {
      switch (field)
      {
        case "Text":
          seq.Text = ReplaceAll(seq.Text, term, replacement, cmp);
          break;
        case "Note":
          seq.Note = ReplaceAll(seq.Note, term, replacement, cmp);
          break;
        case "Speaker":
          seq.Speaker = ReplaceAll(seq.Speaker, term, replacement, cmp);
          break;
        case "Voice":
          seq.Voice = ReplaceAll(seq.Voice, term, replacement, cmp);
          break;
      }
    }
    private static string ReplaceAll(string? source, string oldValue, string newValue, StringComparison cmp)
    {
      if (string.IsNullOrEmpty(source)) return source ?? "";
      var sb = new StringBuilder();
      int startIndex = 0;
      while (startIndex < source.Length)
      {
        int idx = source.IndexOf(oldValue, startIndex, cmp);
        if (idx < 0) { sb.Append(source, startIndex, source.Length - startIndex); break; }
        sb.Append(source, startIndex, idx - startIndex);
        sb.Append(newValue);
        startIndex = idx + oldValue.Length;
      }
      return sb.ToString();
    }
    private static Scene? SceneOf(object? item) => item switch
    {
      SceneDisplayItem sdi => sdi.Scene,
      Scene s              => s,
      _                    => null,
    };
    private void RefreshSceneList()
    {
      if (SceneList == null) return;
      var currentScene = SceneOf(SceneList.SelectedItem) ?? _project.CurrentScene;
      var items = _project.Scenes
        .Select(sc =>
        {
          bool hasSlides = false;
          try
          {
            var dir = _renderService.ResolveSlidesDirectory(sc);
            hasSlides = !string.IsNullOrWhiteSpace(dir)
                        && System.IO.Directory.Exists(dir)
                        && System.IO.Directory.EnumerateFiles(dir, "slide_*.png").Any();
          }
          catch { }
          return new SceneDisplayItem(sc, hasSlides);
        })
        .ToList();
      SceneList.ItemsSource = null;
      SceneList.ItemsSource = items;
      if (currentScene != null)
      {
        var toSelect = items.FirstOrDefault(i => i.Scene == currentScene);
        if (toSelect != null) SceneList.SelectedItem = toSelect;
      }
      if (SceneList.SelectedItem == null && items.Count > 0)
        SceneList.SelectedItem = items[0];
    }
    private void OnSceneSelected(object sender, SelectionChangedEventArgs e)
    {
      var scene = SceneOf(SceneList.SelectedItem);
      if (scene != null) { _projectService.SelectScene(scene); LoadSceneIntoGrid(); }
    }
    private void OnAddScene(object sender, RoutedEventArgs e)
    { _projectService.AddScene(); RefreshSceneList(); LoadSceneIntoGrid(); }
    private void OnDeleteScene(object sender, RoutedEventArgs e)
    {
      var scene = SceneOf(SceneList.SelectedItem);
      if (scene == null) return;
      if (_project.Scenes.Count <= 1)
      { MessageBox.Show(this, "Impossible de supprimer la dernière scène.", "Scènes", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
      if (MessageBox.Show(this, $"Supprimer la scène « {scene.Name} » et ses {scene.Sequences.Count} séquences ?",
        "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
      _project.Scenes.Remove(scene);
      if (_project.CurrentSceneId == scene.Id)
        _project.CurrentSceneId = _project.Scenes[0].Id;
      _projectService.MarkDirty();
      RefreshSceneList(); LoadSceneIntoGrid();
    }
    private void OnDuplicateScene(object sender, RoutedEventArgs e)
    {
      var src = SceneOf(SceneList.SelectedItem);
      if (src == null) return;
      var clone = new Scene
      {
        Id        = Guid.NewGuid().ToString("N"),
        Name      = src.Name + " (copie)",
        Sequences = src.Sequences.Select(s => s.Clone()).ToList(),
      };
      var idx = _project.Scenes.IndexOf(src);
      _project.Scenes.Insert(idx + 1, clone);
      _project.CurrentSceneId = clone.Id;
      _projectService.MarkDirty();
      RefreshSceneList(); LoadSceneIntoGrid();
    }
    private void OnSceneNameDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      if (e.ClickCount < 2) return;
      var scene = SceneOf(SceneList.SelectedItem);
      if (scene != null) RenameSelectedScene(scene);
    }
    private void OnRenameScene(object sender, RoutedEventArgs e)
    {
      var scene = SceneOf(SceneList.SelectedItem);
      if (scene != null) RenameSelectedScene(scene);
    }
    private void RenameSelectedScene(Scene scene)
    {
      var dlg = new RenameDialog(scene.Name) { Owner = this };
      if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Result))
      {
        scene.Name = dlg.Result.Trim();
        _projectService.MarkDirty();
        RefreshSceneList();
      }
    }
    private Scene? _sceneDragSource;
    private System.Windows.Point _sceneDragStartPos;
    private void OnSceneListPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      _sceneDragStartPos = e.GetPosition(SceneList);
      _sceneDragSource   = SceneOf(SceneList.SelectedItem);
    }
    private void OnSceneListMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
      if (_sceneDragSource == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
      var pos  = e.GetPosition(SceneList);
      var diff = _sceneDragStartPos - pos;
      if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
          Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;
      DragDrop.DoDragDrop(SceneList, _sceneDragSource, System.Windows.DragDropEffects.Move);
      _sceneDragSource = null;
    }
    private void OnSceneListDragOver(object sender, System.Windows.DragEventArgs e)
    {
      e.Effects = e.Data.GetDataPresent(typeof(Scene)) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
      e.Handled = true;
    }
    private void OnSceneListDrop(object sender, System.Windows.DragEventArgs e)
    {
      if (!e.Data.GetDataPresent(typeof(Scene))) return;
      var dragged = (Scene)e.Data.GetData(typeof(Scene));
      var pos = e.GetPosition(SceneList);
      var hit = SceneList.InputHitTest(pos) as DependencyObject;
      while (hit != null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
      if (hit is not ListBoxItem lbi) return;
      var target = SceneOf(lbi.DataContext);
      if (target == null || target == dragged) return;
      var oldIdx = _project.Scenes.IndexOf(dragged);
      var newIdx = _project.Scenes.IndexOf(target);
      if (oldIdx < 0 || newIdx < 0 || oldIdx == newIdx) return;
      _project.Scenes.RemoveAt(oldIdx);
      _project.Scenes.Insert(newIdx, dragged);
      _projectService.MarkDirty();
      RefreshSceneList();
      if (SceneList.ItemsSource is System.Collections.IEnumerable items)
      {
        foreach (var it in items)
          if (it is SceneDisplayItem sdi && sdi.Scene == dragged)
          { SceneList.SelectedItem = it; break; }
      }
    }
    private void InitQuickVoiceBoxes()
    {
      SelectComboByTag(Bot1VoiceBox, "en-US");
      SelectComboByTag(Bot2VoiceBox, "fr-FR");
    }
    private static void SelectComboByTag(System.Windows.Controls.ComboBox cb, string tag)
    {
      foreach (System.Windows.Controls.ComboBoxItem item in cb.Items)
        if (item.Tag?.ToString() == tag) { cb.SelectedItem = item; return; }
      if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }
    private static string? GetComboTag(System.Windows.Controls.ComboBox cb)
      => (cb.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString();
    private void OnQuickVoiceChanged(object sender, SelectionChangedEventArgs e) {  }
    private void OnApplyVoiceToAll(object sender, RoutedEventArgs e)
    {
      var bot1Voice = GetComboTag(Bot1VoiceBox) ?? "en-US";
      var bot2Voice = GetComboTag(Bot2VoiceBox) ?? "fr-FR";
      int changed = 0;
      _undoRedo.PushUndo(_items, "Appliquer voix à toutes");
      foreach (var seq in _items)
      {
        if (string.Equals(seq.Speaker, "bot1", StringComparison.OrdinalIgnoreCase) && seq.Voice != bot1Voice)
        { seq.Voice = bot1Voice; changed++; }
        else if (string.Equals(seq.Speaker, "bot2", StringComparison.OrdinalIgnoreCase) && seq.Voice != bot2Voice)
        { seq.Voice = bot2Voice; changed++; }
      }
      if (changed > 0)
      {
        _projectService.SyncAndMarkDirty(_items);
        _ttsTiming.ComputeAllAsync(_items);
        Logger.Info($"[QuickVoice] Bot1={bot1Voice} Bot2={bot2Voice} ? {changed} séquences mises à jour");
      }
    }
    private async void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e) => await UpdateMediaPanelAsync();
    private async Task UpdateMediaPanelAsync()
    {
      var seq = GridItems.SelectedItem as Sequence;
      UpdateCamClipUi(seq);
      if (seq == null)
      {
        MediaPreviewImage.Source = null;
        MediaPreviewPlaceholder.Visibility = Visibility.Visible;
        MediaFileNameLabel.Text = "Aucune séquence sélectionnée";
        MediaUnlinkCheck.Visibility = Visibility.Collapsed;
        ScrubberPanel.Visibility = Visibility.Collapsed;
        return;
      }
      _suppressMediaUpdate = true;
      MediaScaleBox.Text       = seq.MediaScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      MediaSpeedBox.Text       = seq.MediaSpeed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
      MediaBorderBox.Text      = seq.MediaBorderPx.ToString();
      MediaShadowBox.Text      = seq.MediaShadowBlur.ToString();
      MediaShadowAlphaBox.Text = seq.MediaShadowAlpha.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      MediaLoopCheck.IsChecked = seq.MediaLoop;
      MediaUnlinkCheck.Visibility = seq.IsPaired ? Visibility.Visible : Visibility.Collapsed;
      MediaUnlinkCheck.IsChecked  = seq.MediaUnlinked;
      CropLeftBox.Text   = seq.MediaCropLeft.ToString();
      CropTopBox.Text    = seq.MediaCropTop.ToString();
      CropRightBox.Text  = seq.MediaCropRight.ToString();
      CropBottomBox.Text = seq.MediaCropBottom.ToString();
      UpdateGravityButtons(seq.MediaCropGravity ?? "center");
      bool isVid = IsVideoMedia(seq.MediaPath);
      MediaSpeedBox.IsReadOnly = false;
      MediaSpeedBox.Opacity    = 1.0;
      MediaSpeedBox.ToolTip    = isVid
        ? "Vitesse calculée automatiquement (durée vidéo — durée TTS)"
        : "Vitesse de lecture (0.1–4.0)";
      _suppressMediaUpdate = false;
      if (!string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath))
      {
        MediaFileNameLabel.Text = Path.GetFileName(seq.MediaPath);
        MediaPreviewPlaceholder.Visibility = Visibility.Collapsed;
        LoadMediaThumbnail(seq.MediaPath);
        if (MediaProxyService.IsScrubable(seq.MediaPath))
        {
          ScrubberPanel.Visibility = Visibility.Visible;
          TrimGuidePanel.Visibility = UserPrefs.TrimGuideVisible ? Visibility.Visible : Visibility.Collapsed;
          _suppressMediaUpdate = true;
          ScrubberSlider.Value = 0;
          ScrubberSlider.Maximum = 1;
          ScrubberThumb.Source = null;
          TrimInBox.Text  = seq.MediaTrimIn?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "";
          TrimOutBox.Text = seq.MediaTrimOut?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "";
          UpdateTrimDurationLabel(seq);
          await ProbAndSetSlider(seq.MediaPath);
          _suppressMediaUpdate = false;
          var initialPos = (double)(seq.MediaTrimIn ?? 0f);
          ScrubberSlider.Value = initialPos;
          UpdateScrubberCurrentTimeLabel(initialPos);
          UpdateTrimHandlePositions();
          UpdateScrubberPositionLine();
          RecalcVideoSpeed(seq);
          var ffmpegBin = CollectRenderSettings().FfmpegBinaryPath;
          _mediaProxy.RequestSnapshot(seq.MediaPath, initialPos, ffmpegBin);
        }
        else
        {
          ScrubberPanel.Visibility = Visibility.Collapsed;
        }
      }
      else
      {
        MediaPreviewImage.Source = null;
        MediaPreviewPlaceholder.Visibility = Visibility.Visible;
        ScrubberPanel.Visibility = Visibility.Collapsed;
        MediaFileNameLabel.Text = string.IsNullOrWhiteSpace(seq.MediaPath)
          ? "Aucun média assigné" : $"? Fichier introuvable : {seq.MediaPath}";
      }
      RefreshMediaItemsList(seq);
    }
    private async Task ProbAndSetSlider(string mediaPath)
    {
      var ffmpegBin = CollectRenderSettings().FfmpegBinaryPath;
      var duration = await _mediaProxy.ProbeDurationAsync(mediaPath, ffmpegBin);
      if (duration > TimeSpan.Zero)
      {
        ScrubberSlider.Maximum = duration.TotalSeconds;
        ScrubberDurationLabel.Text = duration.ToString(@"m\:ss\.f");
      }
      else
      {
        ScrubberSlider.Maximum = 1;
        ScrubberDurationLabel.Text = "—";
      }
      UpdateTrimHandlePositions();
    }
    private void LoadMediaThumbnail(string path)
    {
      try
      {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv")
        {
          var thumbPath = Path.Combine(Path.GetTempPath(), $"ro_mediathumb_{Guid.NewGuid():N}.png");
          var exe = CollectRenderSettings().FfmpegExePath;
          var psi = new System.Diagnostics.ProcessStartInfo
          {
            FileName = exe,
            Arguments = $"-y -ss 0 -i \"{path}\" -vframes 1 -q:v 2 \"{thumbPath}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
          };
          using var proc = System.Diagnostics.Process.Start(psi);
          if (proc != null)
          {
            var _ = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(5000)) try { proc.Kill(); } catch { }
          }
          if (File.Exists(thumbPath)) { LoadBitmapFromPath(thumbPath); try { File.Delete(thumbPath); } catch { } }
          else MediaPreviewImage.Source = null;
        }
        else LoadBitmapFromPath(path);
      }
      catch { MediaPreviewImage.Source = null; }
    }
    private void LoadBitmapFromPath(string path)
    {
      var bmp = new BitmapImage();
      bmp.BeginInit();
      bmp.CacheOption = BitmapCacheOpt.OnLoad;
      bmp.UriSource = new Uri(path, UriKind.Absolute);
      bmp.DecodePixelWidth = 260;
      bmp.EndInit(); bmp.Freeze();
      MediaPreviewImage.Source = bmp;
    }
    private static BitmapImage? LoadBitmapFullRes(string? path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
      var bmp = new BitmapImage();
      bmp.BeginInit();
      bmp.CacheOption = BitmapCacheOpt.OnLoad;
      bmp.UriSource = new Uri(path, UriKind.Absolute);
      bmp.EndInit(); bmp.Freeze();
      return bmp;
    }
    private void RefreshMediaItemsList(Sequence? seq)
    {
      MediaItemsList.Items.Clear();
      if (seq?.MediaItems == null || seq.MediaItems.Count == 0)
      {
        _selectedMediaItemIndex = -1;
        MediaItemCountLabel.Text = "";
        return;
      }
      MediaItemCountLabel.Text = $"{seq.MediaItems.Count}/6";
      for (int i = 0; i < seq.MediaItems.Count; i++)
      {
        var mi = seq.MediaItems[i];
        string label;
        if (string.IsNullOrWhiteSpace(mi.Path))
        {
          label = $"[{i + 1}] (vide)";
        }
        else
        {
          var fname = System.IO.Path.GetFileName(mi.Path);
          if (fname.Length > 30)
            fname = fname[..12] + "..." + fname[^12..];
          label = $"[{i + 1}] {fname}";
        }
        MediaItemsList.Items.Add(label);
      }
      if (MediaItemsList.Items.Count > 0)
      {
        int targetIdx = (_selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < MediaItemsList.Items.Count)
          ? _selectedMediaItemIndex
          : 0;
        _suppressMediaItemSelection = true;
        MediaItemsList.SelectedIndex = targetIdx;
        _selectedMediaItemIndex = targetIdx;
        _suppressMediaItemSelection = false;
      }
    }
    private void SyncMediaItemToFlat(Sequence seq, MediaItem mi)
    {
      seq.MediaPath       = mi.Path;
      seq.MediaSourcePath = mi.SourcePath;
      seq.MediaSpeed      = mi.Speed;
      seq.MediaTrimIn     = mi.TrimIn;
      seq.MediaTrimOut    = mi.TrimOut;
      seq.MediaCropLeft   = mi.CropLeft;
      seq.MediaCropTop    = mi.CropTop;
      seq.MediaCropRight  = mi.CropRight;
      seq.MediaCropBottom = mi.CropBottom;
      seq.MediaCropGravity = mi.CropGravity;
    }
    private void SyncFlatToMediaItem(Sequence seq)
    {
      if (seq.MediaItems == null || _selectedMediaItemIndex < 0 || _selectedMediaItemIndex >= seq.MediaItems.Count) return;
      var mi = seq.MediaItems[_selectedMediaItemIndex];
      mi.Path        = seq.MediaPath;
      mi.SourcePath  = seq.MediaSourcePath;
      mi.Speed       = seq.MediaSpeed;
      mi.TrimIn      = seq.MediaTrimIn;
      mi.TrimOut     = seq.MediaTrimOut;
      mi.CropLeft    = seq.MediaCropLeft;
      mi.CropTop     = seq.MediaCropTop;
      mi.CropRight   = seq.MediaCropRight;
      mi.CropBottom  = seq.MediaCropBottom;
      mi.CropGravity = seq.MediaCropGravity;
    }
    private async void OnMediaItemSelected(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressMediaItemSelection) return;
      var seq = GridItems.SelectedItem as Sequence;
      if (seq?.MediaItems == null) return;
      int idx = MediaItemsList.SelectedIndex;
      if (idx < 0 || idx >= seq.MediaItems.Count) return;
      _selectedMediaItemIndex = idx;
      SyncMediaItemToFlat(seq, seq.MediaItems[idx]);
      await UpdateMediaPanelAsync();
    }
    private async void OnMediaItemAdd(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      int currentCount = seq.MediaItems?.Count ?? 0;
      if (currentCount >= 6)
      { MessageBox.Show(this, "Maximum 6 medias par sequence.", "Limite", MessageBoxButton.OK, MessageBoxImage.Information); return; }
      var ofd = new Microsoft.Win32.OpenFileDialog
      { Title = "Ajouter un ou plusieurs medias", Filter = "Media|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.webm;*.mov;*.mkv|Tous|*.*", Multiselect = true };
      if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;
      if (currentCount + ofd.FileNames.Length > 6)
      { MessageBox.Show(this, $"Maximum 6 medias par sequence.\nActuellement : {currentCount}, selection : {ofd.FileNames.Length}.", "Limite", MessageBoxButton.OK, MessageBoxImage.Information); return; }
      _undoRedo.PushUndo(_items, ofd.FileNames.Length == 1 ? "Ajouter media mosaic" : $"Ajouter {ofd.FileNames.Length} medias");
      if (seq.MediaItems == null) seq.MediaItems = new List<MediaItem>();
      int lastIdx = 0;
      foreach (var file in ofd.FileNames)
      {
        var newPath = CopyAndRenameAsset(seq, file, isAudio: false);
        var mi = new MediaItem { Path = newPath, SourcePath = file };
        seq.MediaItems.Add(mi);
        lastIdx = seq.MediaItems.Count - 1;
      }
      SyncMediaItemToFlat(seq, seq.MediaItems[lastIdx]);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      await UpdateMediaPanelAsync();
      _suppressMediaItemSelection = true;
      MediaItemsList.SelectedIndex = lastIdx;
      _selectedMediaItemIndex = lastIdx;
      _suppressMediaItemSelection = false;
    }
    private async void OnMediaItemRemove(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      if (seq.MediaItems == null || _selectedMediaItemIndex < 0 || _selectedMediaItemIndex >= seq.MediaItems.Count) return;
      _undoRedo.PushUndo(_items, "Supprimer media mosaic");
      seq.MediaItems.RemoveAt(_selectedMediaItemIndex);
      if (seq.MediaItems.Count == 0)
      {
        seq.MediaItems = null;
        seq.MediaPath = null;
      }
      else if (_selectedMediaItemIndex >= seq.MediaItems.Count)
      {
        _selectedMediaItemIndex = seq.MediaItems.Count - 1;
        SyncMediaItemToFlat(seq, seq.MediaItems[_selectedMediaItemIndex]);
      }
      else
      {
        SyncMediaItemToFlat(seq, seq.MediaItems[_selectedMediaItemIndex]);
      }
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      await UpdateMediaPanelAsync();
    }
    private async void OnMediaItemMoveUp(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq?.MediaItems == null) return;
      int idx = _selectedMediaItemIndex;
      if (idx <= 0 || idx >= seq.MediaItems.Count) return;
      _undoRedo.PushUndo(_items, "Monter media");
      (seq.MediaItems[idx], seq.MediaItems[idx - 1]) = (seq.MediaItems[idx - 1], seq.MediaItems[idx]);
      _selectedMediaItemIndex = idx - 1;
      SyncMediaItemToFlat(seq, seq.MediaItems[_selectedMediaItemIndex]);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      await UpdateMediaPanelAsync();
    }
    private async void OnMediaItemMoveDown(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq?.MediaItems == null) return;
      int idx = _selectedMediaItemIndex;
      if (idx < 0 || idx >= seq.MediaItems.Count - 1) return;
      _undoRedo.PushUndo(_items, "Descendre media");
      (seq.MediaItems[idx], seq.MediaItems[idx + 1]) = (seq.MediaItems[idx + 1], seq.MediaItems[idx]);
      _selectedMediaItemIndex = idx + 1;
      SyncMediaItemToFlat(seq, seq.MediaItems[_selectedMediaItemIndex]);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      await UpdateMediaPanelAsync();
    }
    private void OnCamClipBrowse(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var faceCamDir = System.IO.Path.Combine(AppContext.BaseDirectory, "FaceCam");
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title = "Choisir un clip face-cam (.mp4)",
        Filter = "Clip cam|*.mp4;*.mov;*.mkv|Tous|*.*",
      };
      if (System.IO.Directory.Exists(faceCamDir)) ofd.InitialDirectory = faceCamDir;
      if (ofd.ShowDialog() != true) return;
      _undoRedo.PushUndo(_items, "Associer clip cam");
      seq.CamClipPath = ofd.FileName;
      UpdateCamClipUi(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnCamClipClear(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      if (string.IsNullOrEmpty(seq.CamClipPath)) return;
      _undoRedo.PushUndo(_items, "Retirer clip cam");
      seq.CamClipPath = null;
      UpdateCamClipUi(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void UpdateCamClipUi(Sequence? seq)
    {
      if (CamClipLabel == null) return;
      if (seq == null || string.IsNullOrWhiteSpace(seq.CamClipPath))
      {
        CamClipLabel.Text = "aucun";
        CamClipLabel.Foreground = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
      }
      else
      {
        CamClipLabel.Text = System.IO.Path.GetFileName(seq.CamClipPath);
        CamClipLabel.Foreground = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromRgb(0x4D, 0xC9, 0x3C));
      }
    }
    private void OnMediaPanelBrowse(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var ofd = new Microsoft.Win32.OpenFileDialog { Title = "Choisir un média", Filter = "Média|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.webm;*.mov|Tous|*.*" };
      if (ofd.ShowDialog() != true) return;
      _undoRedo.PushUndo(_items, "Assigner média");
      seq.MediaPath = CopyAndRenameAsset(seq, ofd.FileName, isAudio: false);
      if (seq.MediaItems == null) seq.MediaItems = new List<MediaItem>();
      if (_selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < seq.MediaItems.Count)
      {
        seq.MediaItems[_selectedMediaItemIndex].Path = seq.MediaPath;
        seq.MediaItems[_selectedMediaItemIndex].SourcePath = ofd.FileName;
      }
      else
      {
        seq.MediaItems.Add(new MediaItem { Path = seq.MediaPath, SourcePath = ofd.FileName });
        _selectedMediaItemIndex = seq.MediaItems.Count - 1;
      }
      PropagateMedia(seq); _ = UpdateMediaPanelAsync(); _projectService.SyncAndMarkDirty(_items);
    }
    private void OnMediaPanelClear(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, "Retirer média");
      seq.MediaPath = null;
      if (seq.MediaItems != null && _selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < seq.MediaItems.Count)
      {
        seq.MediaItems.RemoveAt(_selectedMediaItemIndex);
        if (seq.MediaItems.Count == 0) seq.MediaItems = null;
      }
      PropagateMedia(seq); _ = UpdateMediaPanelAsync(); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
    }
    private void OnMediaParamChanged(object sender, RoutedEventArgs e)
    {
      if (_suppressMediaUpdate) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      if (float.TryParse(MediaScaleBox.Text, System.Globalization.NumberStyles.Float, inv, out var sc)) seq.MediaScale = Math.Clamp(sc, 0.1f, 1.0f);
      if (float.TryParse(MediaSpeedBox.Text, System.Globalization.NumberStyles.Float, inv, out var sp))
      {
        sp = Math.Clamp(sp, 0.1f, 4.0f);
        seq.MediaSpeed = sp;
        if (IsVideoMedia(seq.MediaPath) && seq.EstimatedDuration.TotalSeconds > 0)
          RecalcTrimFromSpeed(seq, sp);
      }
      if (int.TryParse(MediaBorderBox.Text, out var bp))  seq.MediaBorderPx  = Math.Max(0, bp);
      if (int.TryParse(MediaShadowBox.Text, out var sb))  seq.MediaShadowBlur = Math.Max(0, sb);
      if (float.TryParse(MediaShadowAlphaBox.Text, System.Globalization.NumberStyles.Float, inv, out var sa)) seq.MediaShadowAlpha = Math.Clamp(sa, 0f, 1f);
      seq.MediaLoop = MediaLoopCheck.IsChecked == true;
      SyncFlatToMediaItem(seq);
      PropagateMedia(seq); _projectService.SyncAndMarkDirty(_items);
    }
    private void OnMediaParamChanged(object sender, TextChangedEventArgs e) => OnMediaParamChanged(sender, (RoutedEventArgs)e);
    private void OnMediaUnlinkChanged(object sender, RoutedEventArgs e)
    {
      if (_suppressMediaUpdate) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, "Déconnecter sibling");
      seq.MediaUnlinked = MediaUnlinkCheck.IsChecked == true;
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnReorganizeAllAssets(object sender, RoutedEventArgs e)
    {
      if (string.IsNullOrWhiteSpace(_projectService.FilePath))
      { MessageBox.Show(this, "Sauvegarde le projet d'abord (le nom de projet est nécessaire pour nommer les dossiers).", "Organisation assets", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
      var projName  = Path.GetFileNameWithoutExtension(_projectService.FilePath);
      var mediaDir  = Path.Combine(@"C:\RoleplayOverlay\slides", projName, "media");
      var audioDir  = Path.Combine(@"C:\RoleplayOverlay\slides", projName, "audio");
      var allSeqs = _project.Scenes.SelectMany(s => s.Sequences).ToList();
      int mediaCount = allSeqs.Count(s => !string.IsNullOrWhiteSpace(s.MediaPath) && File.Exists(s.MediaPath));
      int audioCount = allSeqs.Count(s => !string.IsNullOrWhiteSpace(s.Mp3) && File.Exists(s.Mp3));
      if (mediaCount == 0 && audioCount == 0)
      { MessageBox.Show(this, "Aucun fichier média ou audio trouvé dans ce projet.", "Organisation assets", MessageBoxButton.OK, MessageBoxImage.Information); return; }
      var confirm = MessageBox.Show(this,
        $"Cette opération va copier et renommer :\n\n" +
        $"• {mediaCount} fichier(s) média ? {mediaDir}\\\n" +
        $"• {audioCount} fichier(s) audio ? {audioDir}\\\n\n" +
        $"Les fichiers originaux ne sont PAS supprimés.\n" +
        $"Les séquences seront mises à jour pour pointer vers les copies.\n\n" +
        $"Continuer ?",
        "Organisation assets", MessageBoxButton.YesNo, MessageBoxImage.Question);
      if (confirm != MessageBoxResult.Yes) return;
      _projectService.SyncFromGrid(_items);
      _undoRedo.PushUndo(_items, "Réorganiser assets");
      int done = 0, skipped = 0, errors = 0;
      foreach (var scene in _project.Scenes)
      {
        foreach (var seq in scene.Sequences)
        {
          if (!string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath))
          {
            try
            {
              var newPath = CopyAndRenameAsset(seq, seq.MediaPath, isAudio: false);
              if (newPath != seq.MediaPath) { seq.MediaPath = newPath; done++; }
              else skipped++;
            }
            catch { errors++; }
          }
          if (!string.IsNullOrWhiteSpace(seq.Mp3) && File.Exists(seq.Mp3))
          {
            try
            {
              var newPath = CopyAndRenameAsset(seq, seq.Mp3, isAudio: true);
              if (newPath != seq.Mp3) { seq.Mp3 = newPath; done++; }
              else skipped++;
            }
            catch { errors++; }
          }
        }
      }
      _projectService.LoadCurrentSceneInto(_items);
      foreach (var s in _items) s.PropertyChanged += OnSequencePropertyChanged;
      ApplySort(); RefreshPairNumbers(); UpdateStats();
      _projectService.SyncAndMarkDirty(_items);
      Logger.Info($"[ReorganizeAssets] {done} copiés, {skipped} déjà en place, {errors} erreurs");
      MessageBox.Show(this,
        $"Terminé !\n\n? {done} fichier(s) copié(s) et renommé(s)\n" +
        (skipped > 0 ? $"? {skipped} déjà en place (ignorés)\n" : "") +
        (errors  > 0 ? $"? {errors} erreur(s) — voir les logs\n" : "") +
        $"\nLe projet a été marqué comme modifié. N'oublie pas de sauvegarder.",
        "Organisation assets", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void OnGridMediaCtxRevertSource(object sender, RoutedEventArgs e) { }
    private void OnMediaCtxRevertSource(object sender, RoutedEventArgs e) { }
    private void OnGridMediaCtxOpenCurrent(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      OpenFile(seq.MediaPath);
    }
    private void OnGridMediaCtxViewSource(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      OpenFile(seq.MediaSourcePath ?? seq.MediaPath);
    }
    private void OnGridMediaCtxShowInExplorer(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      ShowInExplorer(seq.MediaPath);
    }
    private void OnGridMediaCtxSourceInExplorer(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      ShowInExplorer(seq.MediaSourcePath ?? seq.MediaPath);
    }
    private void OnGridMediaCtxCopyPath(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      CopyToClipboard(seq.MediaPath);
    }
    private void OnGridMediaCtxCopySourcePath(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      CopyToClipboard(seq.MediaSourcePath ?? seq.MediaPath);
    }
    private void OnGridMediaCtxRename(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      var saved = GridItems.SelectedItem; GridItems.SelectedItem = seq;
      OnMediaCtxRename(sender, e);
      GridItems.SelectedItem = saved;
    }
    private void OnGridMediaCtxMigrate(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      if (string.IsNullOrWhiteSpace(seq.MediaPath) || !File.Exists(seq.MediaPath)) return;
      _undoRedo.PushUndo(_items, "Migrer média");
      seq.MediaPath = CopyAndRenameAsset(seq, seq.MediaPath, isAudio: false);
      PropagateMedia(seq); _projectService.SyncAndMarkDirty(_items);
      GridItems.Items.Refresh();
    }
    private void OpenFile(string? path)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      { MessageBox.Show(this, $"Fichier introuvable :\n{path ?? "(vide)"}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
      try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
      catch (Exception ex) { MessageBox.Show(this, $"Impossible d'ouvrir :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ShowInExplorer(string? path)
    {
      if (string.IsNullOrWhiteSpace(path)) return;
      try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); }
      catch { }
    }
    private void CopyToClipboard(string? text)
    {
      if (string.IsNullOrWhiteSpace(text)) return;
      try { System.Windows.Clipboard.SetText(text); Logger.Info($"[Clipboard] {text}"); }
      catch { }
    }
    private string CopyAndRenameAsset(Sequence seq, string sourcePath, bool isAudio)
    {
      if (isAudio) seq.Mp3SourcePath   = sourcePath;
      else         seq.MediaSourcePath = sourcePath;
      try
      {
        var projName  = Path.GetFileNameWithoutExtension(_projectService.FilePath ?? "project");
        var slideNum  = ExtractSlideNumber(seq.Note ?? "");
        var ext       = Path.GetExtension(sourcePath);
        var origBase  = Path.GetFileNameWithoutExtension(sourcePath);
        var subFolder = isAudio ? "audio" : "media";
        var destDir   = Path.Combine(@"C:\RoleplayOverlay\slides", projName, subFolder);
        Directory.CreateDirectory(destDir);
        var newName = slideNum.HasValue
          ? $"{projName}_slide{slideNum:D3}_{origBase}{ext}"
          : $"{projName}_{seq.Id![..Math.Min(8, seq.Id.Length)]}_{origBase}{ext}";
        var destPath = Path.Combine(destDir, newName);
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
            StringComparison.OrdinalIgnoreCase))
          return sourcePath;
        File.Copy(sourcePath, destPath, overwrite: true);
        Logger.Info($"[Asset] {(File.Exists(destPath) ? "Remplacé" : "Copié")} ? {destPath}");
        return destPath;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[Asset] Copie échouée, chemin original conservé : {ex.Message}");
        return sourcePath;
      }
    }
    private void OnPickMediaClick(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title = "Choisir un ou plusieurs medias",
        Filter = "Media|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.webm;*.mov;*.mkv|Tous|*.*",
        Multiselect = true
      };
      if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;
      int currentCount = seq.MediaItems?.Count(m => !string.IsNullOrWhiteSpace(m.Path)) ?? 0;
      int toAdd = ofd.FileNames.Length;
      if (currentCount + toAdd > 6)
      {
        MessageBox.Show(this,
          $"Maximum 6 medias par sequence.\nActuellement : {currentCount}, selection : {toAdd}.",
          "Limite", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      _undoRedo.PushUndo(_items, toAdd == 1 ? "Assigner media (grille)" : $"Assigner {toAdd} medias");
      if (seq.MediaItems == null) seq.MediaItems = new List<MediaItem>();
      foreach (var file in ofd.FileNames)
      {
        var newPath = CopyAndRenameAsset(seq, file, isAudio: false);
        seq.MediaItems.Add(new MediaItem { Path = newPath, SourcePath = file });
      }
      if (seq.MediaItems.Count > 0)
        seq.MediaPath = seq.MediaItems[0].Path;
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      _ = UpdateMediaPanelAsync();
    }
    private void OnClearMediaClick(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      _undoRedo.PushUndo(_items, "Retirer média (grille)");
      seq.MediaPath = null;
      if (seq.MediaItems != null && _selectedMediaItemIndex >= 0 && _selectedMediaItemIndex < seq.MediaItems.Count)
      {
        seq.MediaItems.RemoveAt(_selectedMediaItemIndex);
        if (seq.MediaItems.Count == 0) seq.MediaItems = null;
      }
      PropagateMedia(seq); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      GridItems.Items.Refresh();
      _ = UpdateMediaPanelAsync();
    }
    private void OnToggleExpandPreview(object sender, RoutedEventArgs e)
    {
      _previewExpanded = !_previewExpanded;
      PreviewBorder.Height = _previewExpanded ? 350 : 120;
      BtnExpandPreview.Content = _previewExpanded ? "?" : "?";
      BtnExpandPreview.ToolTip = _previewExpanded ? "Réduire l'aperçu" : "Agrandir l'aperçu";
    }
    private void OnPreviewThumbClick(object sender, MouseButtonEventArgs e)
    {
      if (e.ClickCount != 2) return;
      BitmapImage? fullRes = LoadBitmapFullRes(_lastPreviewPngPath);
      if (fullRes == null)
      {
        var seq = GridItems.SelectedItem as Sequence;
        if (seq != null && !string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath))
        {
          var ext = Path.GetExtension(seq.MediaPath).ToLowerInvariant();
          if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
            fullRes = LoadBitmapFullRes(seq.MediaPath);
        }
      }
      if (fullRes == null)
      {
        if (MediaPreviewImage.Source == null) return;
        fullRes = MediaPreviewImage.Source as BitmapImage;
        if (fullRes == null) return;
      }
      var title = (GridItems.SelectedItem as Sequence)?.Note
               ?? (GridItems.SelectedItem as Sequence)?.Id
               ?? "Aperçu";
      var popup = new PreviewPopupWindow(fullRes, title) { Owner = this };
      popup.Show();
    }
    private async void OnScrubberThumbClick(object sender, MouseButtonEventArgs e)
    {
      if (e.ClickCount != 2) return;
      var seq = GridItems.SelectedItem as Sequence;
      var ts  = ScrubberSlider.Value;
      var title = seq != null
        ? $"Scrubber — {seq.Note ?? seq.Id} @ {ts:F1}s"
        : $"Scrubber @ {ts:F1}s";
      System.Windows.Media.ImageSource? source = null;
      if (seq != null && !string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath))
      {
        var ffmpegBin = CollectRenderSettings().FfmpegBinaryPath;
        var hiResPng = await GenerateHiResSnapshotAsync(seq.MediaPath, ts, ffmpegBin);
        if (hiResPng != null)
        {
          source = LoadBitmapFullRes(hiResPng);
          try { File.Delete(hiResPng); } catch { }
        }
      }
      source ??= ScrubberThumb.Source;
      if (source == null) return;
      var popup = new PreviewPopupWindow(source, title) { Owner = this };
      popup.Show();
    }
    private async Task<string?> GenerateHiResSnapshotAsync(string mediaPath, double timestamp, string? ffmpegBin)
    {
      var exe = ffmpegBin ?? "ffmpeg";
      var outPath = Path.Combine(Path.GetTempPath(), $"ro_hires_{Guid.NewGuid():N}.png");
      try
      {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var psi = new System.Diagnostics.ProcessStartInfo
        {
          FileName  = exe,
          Arguments = $"-y -ss {timestamp.ToString("F3", inv)} -i \"{mediaPath}\" -vframes 1 -q:v 1 \"{outPath}\"",
          UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return null;
        await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return File.Exists(outPath) ? outPath : null;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[HiResSnapshot] Failed: {ex.Message}");
        return null;
      }
    }
    private void OnScrubberValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (_suppressMediaUpdate) return;
      var seq = GridItems.SelectedItem as Sequence;
      if (seq == null || string.IsNullOrWhiteSpace(seq.MediaPath)) return;
      if (e.NewValue < 0 || e.NewValue > ScrubberSlider.Maximum) return;
      UpdateScrubberCurrentTimeLabel(e.NewValue);
      UpdateScrubberPositionLine();
      var ffmpegBin = CollectRenderSettings().FfmpegBinaryPath;
      _mediaProxy.RequestSnapshot(seq.MediaPath, e.NewValue, ffmpegBin);
    }
    private void LoadScrubberThumb(string pngPath)
    {
      try
      {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOpt.OnLoad;
        bmp.UriSource   = new Uri(pngPath, UriKind.Absolute);
        bmp.EndInit(); bmp.Freeze();
        ScrubberThumb.Source = bmp;
      }
      catch { ScrubberThumb.Source = null; }
    }
    private void UpdateScrubberCurrentTimeLabel(double val)
    {
      var seq = GridItems.SelectedItem as Sequence;
      double total = ScrubberSlider.Maximum;
      string pos   = $"{val:F1}s";
      string dur   = total > 0 ? $" / {total:F1}s" : "";
      string ctx = "";
      if (seq != null && (seq.MediaTrimIn.HasValue || seq.MediaTrimOut.HasValue))
      {
        double inSec  = seq.MediaTrimIn  ?? 0;
        double outSec = seq.MediaTrimOut ?? total;
        double range  = outSec - inSec;
        ctx = $"  [In {inSec:F1}s ? Out {outSec:F1}s = {range:F1}s]";
      }
      ScrubberCurrentTimeLabel.Text = pos + dur + ctx;
      BtnSetIn.Content  = $"? In ici ({val:F1}s)";
      BtnSetOut.Content = $"Out ici ? ({val:F1}s)";
    }
    private void OnTrimBarContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
      UpdateTrimHandlePositions();
      UpdateScrubberPositionLine();
    }
    private void OnTrimRangeDragDelta(object sender, DragDeltaEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var max = ScrubberSlider.Maximum; if (max <= 0) return;
      var cw = TrimBarContainer.ActualWidth; if (cw <= 0) return;
      float inVal  = seq.MediaTrimIn  ?? 0f;
      float outVal = seq.MediaTrimOut ?? (float)max;
      float rangeDur = outVal - inVal;
      float deltaSec = (float)(e.HorizontalChange / cw * max);
      float newIn  = inVal + deltaSec;
      float newOut = outVal + deltaSec;
      if (newIn < 0)            { newIn = 0;           newOut = rangeDur; }
      if (newOut > (float)max)  { newOut = (float)max;  newIn = newOut - rangeDur; }
      newIn = Math.Max(0, newIn);
      seq.MediaTrimIn  = newIn;
      seq.MediaTrimOut = newOut;
      SyncFlatToMediaItem(seq);
      _suppressMediaUpdate = true;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      TrimInBox.Text  = newIn.ToString("F2", inv);
      TrimOutBox.Text = newOut.ToString("F2", inv);
      _suppressMediaUpdate = false;
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnTrimDragStarted(object sender, DragStartedEventArgs e)
    {
      _undoRedo.PushUndo(_items, "Drag trim");
    }
    private void OnTrimInDragDelta(object sender, DragDeltaEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var max = ScrubberSlider.Maximum; if (max <= 0) return;
      var cw = TrimBarContainer.ActualWidth; if (cw <= 0) return;
      var cur = Canvas.GetLeft(TrimInHandle);
      if (double.IsNaN(cur)) cur = 0;
      var newLeft = Math.Clamp(cur + e.HorizontalChange, 0, cw - TrimInHandle.Width);
      Canvas.SetLeft(TrimInHandle, newLeft);
      var newSec = (float)((newLeft + TrimInHandle.Width / 2.0) / cw * max);
      seq.MediaTrimIn = newSec;
      SyncFlatToMediaItem(seq);
      TrimInBox.Text = newSec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      UpdateTrimDurationLabel(seq);
      RefreshTrimRangeBar();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnTrimOutDragDelta(object sender, DragDeltaEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var max = ScrubberSlider.Maximum; if (max <= 0) return;
      var cw = TrimBarContainer.ActualWidth; if (cw <= 0) return;
      var cur = Canvas.GetLeft(TrimOutHandle);
      if (double.IsNaN(cur)) cur = cw - TrimOutHandle.Width;
      var newLeft = Math.Clamp(cur + e.HorizontalChange, 0, cw - TrimOutHandle.Width);
      Canvas.SetLeft(TrimOutHandle, newLeft);
      var newSec = (float)((newLeft + TrimOutHandle.Width / 2.0) / cw * max);
      seq.MediaTrimOut = newSec;
      SyncFlatToMediaItem(seq);
      TrimOutBox.Text = newSec.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      UpdateTrimDurationLabel(seq);
      RefreshTrimRangeBar();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void RefreshTrimRangeBar()
    {
      var inLeft  = Canvas.GetLeft(TrimInHandle);
      var outLeft = Canvas.GetLeft(TrimOutHandle);
      if (double.IsNaN(inLeft))  inLeft  = 0;
      if (double.IsNaN(outLeft)) outLeft = TrimBarContainer.ActualWidth - TrimOutHandle.Width;
      var inCenter  = inLeft  + TrimInHandle.Width  / 2.0;
      var outCenter = outLeft + TrimOutHandle.Width  / 2.0;
      Canvas.SetLeft(TrimRangeBar, inCenter);
      Canvas.SetTop(TrimRangeBar, 0);
      TrimRangeBar.Width  = Math.Max(0, outCenter - inCenter);
      TrimRangeBar.Height = 18;
    }
    private void UpdateTrimHandlePositions()
    {
      var seq = GridItems.SelectedItem as Sequence;
      if (seq == null || ScrubberPanel.Visibility != Visibility.Visible) return;
      var max = ScrubberSlider.Maximum;
      if (max <= 0) max = 1;
      var cw = TrimBarContainer.ActualWidth;
      if (cw <= 0) return;
      var inVal  = (double)(seq.MediaTrimIn  ?? 0f);
      var outVal = (double)(seq.MediaTrimOut ?? (float)max);
      double inPx  = Math.Clamp(inVal  / max * cw, 0, cw);
      double outPx = Math.Clamp(outVal / max * cw, 0, cw);
      if (outPx < inPx) outPx = inPx;
      Canvas.SetLeft(TrimInHandle,  Math.Clamp(inPx  - TrimInHandle.Width  / 2, 0, cw - TrimInHandle.Width));
      Canvas.SetLeft(TrimOutHandle, Math.Clamp(outPx - TrimOutHandle.Width / 2, 0, cw - TrimOutHandle.Width));
      Canvas.SetLeft(TrimRangeBar, inPx);
      Canvas.SetTop(TrimRangeBar, 0);
      TrimRangeBar.Width  = Math.Max(0, outPx - inPx);
      TrimRangeBar.Height = 18;
    }
    private void UpdateScrubberPositionLine()
    {
      var max = ScrubberSlider.Maximum; if (max <= 0) return;
      var cw  = TrimBarContainer.ActualWidth; if (cw <= 0) return;
      var px  = ScrubberSlider.Value / max * cw;
      Canvas.SetLeft(ScrubberPositionLine, Math.Clamp(px, 0, cw - ScrubberPositionLine.Width));
    }
    private void OnDismissTrimGuide(object sender, RoutedEventArgs e)
    {
      TrimGuidePanel.Visibility = Visibility.Collapsed;
      UserPrefs.TrimGuideVisible = false;
      UserPrefs.Save();
    }
    private void OnSetTrimInHere(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, $"Point In ({ScrubberSlider.Value:F1}s)");
      var val = (float)ScrubberSlider.Value;
      seq.MediaTrimIn = val;
      SyncFlatToMediaItem(seq);
      TrimInBox.Text  = val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnSetTrimOutHere(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, $"Point Out ({ScrubberSlider.Value:F1}s)");
      var val = (float)ScrubberSlider.Value;
      seq.MediaTrimOut = val;
      SyncFlatToMediaItem(seq);
      TrimOutBox.Text  = val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnResetTrim(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, "Reset trim");
      seq.MediaTrimIn = null; seq.MediaTrimOut = null;
      SyncFlatToMediaItem(seq);
      TrimInBox.Text = ""; TrimOutBox.Text = "";
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnTrimChanged(object sender, RoutedEventArgs e)
    {
      if (_suppressMediaUpdate) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      seq.MediaTrimIn  = float.TryParse(TrimInBox.Text,  System.Globalization.NumberStyles.Float, inv, out var iv) ? Math.Max(0f, iv) : (float?)null;
      seq.MediaTrimOut = float.TryParse(TrimOutBox.Text, System.Globalization.NumberStyles.Float, inv, out var ov) ? Math.Max(0f, ov) : (float?)null;
      SyncFlatToMediaItem(seq);
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnTrimFieldKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key != Key.Up && e.Key != Key.Down) return;
      if (sender is not WpfTextBox tb) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      float step = 0.1f;
      if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) step = 1.0f;
      if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) step = 0.01f;
      float delta = e.Key == Key.Up ? step : -step;
      if (!float.TryParse(tb.Text, System.Globalization.NumberStyles.Float, inv, out var cur)) cur = 0f;
      var newVal = Math.Max(0f, cur + delta);
      tb.Text = newVal.ToString("F2", inv);
      if (ReferenceEquals(tb, TrimInBox))  seq.MediaTrimIn  = newVal;
      else if (ReferenceEquals(tb, TrimOutBox)) seq.MediaTrimOut = newVal;
      SyncFlatToMediaItem(seq);
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      RecalcVideoSpeed(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      e.Handled = true;
    }
    private void UpdateTrimDurationLabel(Sequence seq)
    {
      var dur = seq.MediaTrimDuration;
      TrimDurationLabel.Text = dur.HasValue ? $"{dur.Value:F1}s" : "—";
    }
    private void OnCropChanged(object sender, RoutedEventArgs e)
    {
      if (_suppressMediaUpdate) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      if (int.TryParse(CropLeftBox.Text,   out var cl)) seq.MediaCropLeft   = Math.Max(0, cl); else seq.MediaCropLeft   = 0;
      if (int.TryParse(CropTopBox.Text,    out var ct)) seq.MediaCropTop    = Math.Max(0, ct); else seq.MediaCropTop    = 0;
      if (int.TryParse(CropRightBox.Text,  out var cr)) seq.MediaCropRight  = Math.Max(0, cr); else seq.MediaCropRight  = 0;
      if (int.TryParse(CropBottomBox.Text, out var cb)) seq.MediaCropBottom = Math.Max(0, cb); else seq.MediaCropBottom = 0;
      SyncFlatToMediaItem(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnResetCrop(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, "Reset crop");
      seq.MediaCropLeft = 0; seq.MediaCropTop = 0; seq.MediaCropRight = 0; seq.MediaCropBottom = 0;
      SyncFlatToMediaItem(seq);
      CropLeftBox.Text = "0"; CropTopBox.Text = "0"; CropRightBox.Text = "0"; CropBottomBox.Text = "0";
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private static readonly System.Windows.Controls.Button[] _gravButtons = Array.Empty<System.Windows.Controls.Button>();
    private void OnGravityClick(object sender, RoutedEventArgs e)
    {
      if (sender is not WpfButton btn || btn.Tag is not string gravity) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      _undoRedo.PushUndo(_items, $"Ancrage {gravity}");
      seq.MediaCropGravity = gravity;
      SyncFlatToMediaItem(seq);
      UpdateGravityButtons(gravity);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void UpdateGravityButtons(string gravity)
    {
      var accent = new System.Windows.Media.SolidColorBrush(
        System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0x80, 0x00));
      var normal = new System.Windows.Media.SolidColorBrush(
        System.Windows.Media.Color.FromRgb(0x1C, 0x1D, 0x20));
      var map = new Dictionary<string, WpfButton>
      {
        ["topleft"]     = GravTL, ["top"]    = GravT,  ["topright"]    = GravTR,
        ["left"]        = GravL,  ["center"] = GravC,  ["right"]       = GravR,
        ["bottomleft"]  = GravBL, ["bottom"] = GravB,  ["bottomright"] = GravBR,
      };
      foreach (var (key, btn) in map)
        btn.Background = key == gravity ? accent : normal;
    }
    private void OnOpenImageEditor(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence;
      if (seq == null) return;
      var slidesDir = _renderService.ResolveSlidesDirectory();
      var slidePath = _renderService.ResolveSlidePathFromNote(seq.Note, slidesDir);
      var mediaPath = seq.MediaPath;
      bool hasSlide = !string.IsNullOrWhiteSpace(slidePath) && File.Exists(slidePath);
      bool hasMediaImage = !string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath)
                           && IsImageFile(mediaPath);
      bool canCompose = hasSlide;
      if (!hasSlide && !hasMediaImage)
      {
        MessageBox.Show(this,
            "Aucun slide ni image media trouvés pour cette séquence.\n" +
            "Vérifiez que le champ Note contient un numéro de slide (ex: 'Slide 5 — ...')\n" +
            "ou qu'un media image est assigné.",
            "éditeur d'image", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      var menu = new ContextMenu();
      if (hasSlide)
      {
        var itemSlide = new MenuItem { Header = "\U0001F5BC Annoter le slide", Tag = slidePath };
        itemSlide.Click += (_, _2) => OpenImageEditor(slidePath!, seq);
        menu.Items.Add(itemSlide);
      }
      if (hasMediaImage)
      {
        var itemMedia = new MenuItem { Header = "\U0001F3AC Annoter le media", Tag = mediaPath };
        itemMedia.Click += (_, _2) => OpenImageEditor(mediaPath!, seq);
        menu.Items.Add(itemMedia);
      }
      if (canCompose)
      {
        var itemCompose = new MenuItem { Header = "\U0001F4F7 Annoter l'aperçu composé" };
        itemCompose.Click += async (_, _2) =>
        {
          try
          {
            _projectService.SyncFromGrid(_items);
            var pngPath = await _renderService.GeneratePreviewFrameAsync(seq, CollectRenderSettings());
            if (pngPath != null && File.Exists(pngPath))
            {
              OpenImageEditor(pngPath, seq);
            }
            else
            {
              MessageBox.Show(this, "échec de la généraation de l'aperçu composé.",
                  "éditeur d'image", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
          }
          catch (Exception ex)
          {
            Logger.Error("[ImageEditor] Compose failed", ex);
            MessageBox.Show(this, $"Erreur :\n{ex.Message}",
                "éditeur d'image", MessageBoxButton.OK, MessageBoxImage.Error);
          }
        };
        menu.Items.Add(itemCompose);
      }
      if (menu.Items.Count == 1)
      {
        if (menu.Items[0] is MenuItem single)
          single.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return;
      }
      if (sender is WpfButton btn)
      {
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
      }
      menu.IsOpen = true;
    }
    private void OpenImageEditor(string imagePath, Sequence seq)
    {
      var win = new ImageEditorWindow(imagePath) { Owner = this };
      win.Show();
      win.Closed += (_, _2) => OnPreviewFrame(this, new RoutedEventArgs());
      Logger.Info($"[ImageEditor] Opened for seq={seq.Id} path={imagePath}");
    }
    private static bool IsImageFile(string? path)
    {
      if (string.IsNullOrWhiteSpace(path)) return false;
      var ext = Path.GetExtension(path).ToLowerInvariant();
      return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
    }
    private async void OnOpenCropEditor(object sender, RoutedEventArgs e)
    {
      var seq = GridItems.SelectedItem as Sequence;
      if (seq == null) return;
      if (string.IsNullOrWhiteSpace(seq.MediaPath) || !File.Exists(seq.MediaPath))
      {
        MessageBox.Show(this, "Assigne d'abord un média à cette séquence.", "Crop interactif", MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      BitmapImage? frame = null;
      var ext = Path.GetExtension(seq.MediaPath).ToLowerInvariant();
      if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
      {
        frame = LoadBitmapFullRes(seq.MediaPath);
      }
      else if (IsVideoMedia(seq.MediaPath))
      {
        var ts         = ScrubberSlider.Value > 0 ? ScrubberSlider.Value : 0;
        var ffmpegBin  = CollectRenderSettings().FfmpegBinaryPath;
        var hiResPng   = await GenerateHiResSnapshotAsync(seq.MediaPath, ts, ffmpegBin);
        if (hiResPng != null)
        {
          frame = LoadBitmapFullRes(hiResPng);
          try { File.Delete(hiResPng); } catch { }
        }
      }
      frame ??= ScrubberThumb.Source as BitmapImage;
      if (frame == null)
      {
        MessageBox.Show(this, "Impossible de charger la frame pour le crop.\nGlisse le scrubber sur une position non nulle et réessaie.", "Crop interactif", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      _undoRedo.PushUndo(_items, "Crop interactif");
      var win = new CropEditorWindow(
        frame,
        seq.MediaCropLeft, seq.MediaCropTop, seq.MediaCropRight, seq.MediaCropBottom,
        seq.Note ?? seq.Id ?? "Crop")
      { Owner = this };
      win.CropChanged += (l, t, r, b) =>
      {
        seq.MediaCropLeft   = l;
        seq.MediaCropTop    = t;
        seq.MediaCropRight  = r;
        seq.MediaCropBottom = b;
        SyncFlatToMediaItem(seq);
        _suppressMediaUpdate = true;
        CropLeftBox.Text   = l.ToString();
        CropTopBox.Text    = t.ToString();
        CropRightBox.Text  = r.ToString();
        CropBottomBox.Text = b.ToString();
        _suppressMediaUpdate = false;
        PropagateMedia(seq);
        _projectService.SyncAndMarkDirty(_items);
      };
      win.Show();
      win.Closed += (_, _2) => OnPreviewFrame(this, new RoutedEventArgs());
      Logger.Info($"[CropEditor] Opened for seq={seq.Id} frame={frame.PixelWidth}×{frame.PixelHeight}");
    }
    private void OnNumericFieldKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key != Key.Up && e.Key != Key.Down) return;
      if (sender is not WpfTextBox tb) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      float step, minVal, maxVal;
      bool isFloat = true;
      if      (ReferenceEquals(tb, MediaScaleBox))       { step = 0.05f; minVal = 0.1f;  maxVal = 1.0f; }
      else if (ReferenceEquals(tb, MediaSpeedBox))       { step = 0.25f; minVal = 0.1f;  maxVal = 4.0f; }
      else if (ReferenceEquals(tb, MediaBorderBox))      { step = 1f;    minVal = 0f;    maxVal = 50f;  isFloat = false; }
      else if (ReferenceEquals(tb, MediaShadowBox))      { step = 1f;    minVal = 0f;    maxVal = 50f;  isFloat = false; }
      else if (ReferenceEquals(tb, MediaShadowAlphaBox)) { step = 0.05f; minVal = 0f;    maxVal = 1.0f; }
      else return;
      if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) step *= 10f;
      if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) step *= 0.1f;
      float delta = e.Key == Key.Up ? step : -step;
      if (!float.TryParse(tb.Text, System.Globalization.NumberStyles.Float, inv, out var cur)) cur = 0f;
      float newVal = Math.Clamp(cur + delta, minVal, maxVal);
      tb.Text = isFloat ? newVal.ToString("F2", inv) : ((int)newVal).ToString();
      e.Handled = true;
    }
    private void OnCropFieldKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
      if (e.Key != Key.Up && e.Key != Key.Down) return;
      if (sender is not WpfTextBox tb) return;
      var seq = GridItems.SelectedItem as Sequence; if (seq == null) return;
      int step = 1;
      if ((Keyboard.Modifiers & ModifierKeys.Shift)   != 0) step = 10;
      if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) step = 100;
      int delta = e.Key == Key.Up ? step : -step;
      if (!int.TryParse(tb.Text, out var cur)) cur = 0;
      var newVal = Math.Max(0, cur + delta);
      tb.Text = newVal.ToString();
      if      (ReferenceEquals(tb, CropLeftBox))   seq.MediaCropLeft   = newVal;
      else if (ReferenceEquals(tb, CropTopBox))    seq.MediaCropTop    = newVal;
      else if (ReferenceEquals(tb, CropRightBox))  seq.MediaCropRight  = newVal;
      else if (ReferenceEquals(tb, CropBottomBox)) seq.MediaCropBottom = newVal;
      SyncFlatToMediaItem(seq);
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
      e.Handled = true;
    }
    private static bool IsVideoMedia(string? path)
    {
      if (string.IsNullOrWhiteSpace(path)) return false;
      return Path.GetExtension(path).ToLowerInvariant()
        is ".mp4" or ".mkv" or ".webm" or ".mov" or ".avi" or ".m4v" or ".flv" or ".wmv";
    }
    private void RecalcVideoSpeed(Sequence seq)
    {
      if (!IsVideoMedia(seq.MediaPath)) return;
      var ttsSec = seq.EstimatedDuration.TotalSeconds;
      if (ttsSec <= 0) return;
      var trimIn  = (double)(seq.MediaTrimIn  ?? 0f);
      var trimOut = (double)(seq.MediaTrimOut ?? (float)ScrubberSlider.Maximum);
      var effectiveDur = trimOut - trimIn;
      if (effectiveDur <= 0) return;
      var rawSpeed = (float)(effectiveDur / ttsSec);
      var clamped  = Math.Clamp(rawSpeed, 0.1f, 4.0f);
      bool wasClamped = Math.Abs(rawSpeed - clamped) > 0.001f;
      seq.MediaSpeed = clamped;
      SyncFlatToMediaItem(seq);
      _suppressMediaUpdate = true;
      MediaSpeedBox.Text = clamped.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      if (wasClamped)
      {
        MediaSpeedBox.BorderBrush = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromRgb(0xE8, 0x41, 0x18));
        double neededDur = ttsSec * (rawSpeed > 4f ? 4f : 0.1f);
        string hint = rawSpeed > 4f
          ? $"? Vitesse max (4×) — vidéo trop longue ({effectiveDur:F1}s) vs TTS ({ttsSec:F1}s). Réduis la plage In?Out à ~{ttsSec * 4:F1}s."
          : $"? Vitesse min (0.1×) — vidéo trop courte ({effectiveDur:F1}s) vs TTS ({ttsSec:F1}s).";
        MediaSpeedBox.ToolTip = hint;
        Logger.Warn($"[AutoSpeed] CLAMPED seq={seq.Id} rawSpeed={rawSpeed:F3} ? {clamped:F3} — {hint}");
      }
      else
      {
        MediaSpeedBox.BorderBrush = new System.Windows.Media.SolidColorBrush(
          System.Windows.Media.Color.FromRgb(0x2A, 0x2B, 0x2E));
        MediaSpeedBox.ToolTip = $"Vitesse calculée automatiquement ({effectiveDur:F1}s vidéo — {ttsSec:F1}s TTS)";
        Logger.Info($"[AutoSpeed] seq={seq.Id} effectiveDur={effectiveDur:F2}s ttsSec={ttsSec:F2}s ? speed={clamped:F3}");
      }
      _suppressMediaUpdate = false;
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void RecalcTrimFromSpeed(Sequence seq, float speed)
    {
      var ttsSec = seq.EstimatedDuration.TotalSeconds;
      if (ttsSec <= 0) return;
      var max = ScrubberSlider.Maximum;
      if (max <= 0) return;
      double neededSourceSec = ttsSec * speed;
      double trimIn = (double)(seq.MediaTrimIn ?? 0f);
      double trimOut = trimIn + neededSourceSec;
      if (trimOut > max)
      {
        trimOut = max;
        trimIn  = Math.Max(0, trimOut - neededSourceSec);
      }
      seq.MediaTrimIn  = (float)trimIn;
      seq.MediaTrimOut = (float)trimOut;
      SyncFlatToMediaItem(seq);
      _suppressMediaUpdate = true;
      TrimInBox.Text  = trimIn.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      TrimOutBox.Text = trimOut.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
      _suppressMediaUpdate = false;
      UpdateTrimDurationLabel(seq);
      UpdateTrimHandlePositions();
      PropagateMedia(seq);
      _projectService.SyncAndMarkDirty(_items);
    }
    private async void OnPreviewFrame(object sender, RoutedEventArgs e)
    {
      var selected = GridItems.SelectedItems.OfType<Sequence>().ToList();
      if (selected.Count == 0 && GridItems.SelectedItem is Sequence single) selected.Add(single);
      if (selected.Count == 0)
      {
        MessageBox.Show(this, "Selectionne au moins une ligne.", "Apercu compose",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      var seenNotes = new HashSet<string>();
      var segments = new List<Sequence>();
      foreach (var seq in selected)
      {
        var key = seq.Note ?? string.Empty;
        if (seenNotes.Add(key)) segments.Add(seq);
      }
      Logger.Info($"OnPreviewFrame: selected={selected.Count} uniqueSegments={segments.Count}");
      var slidesDir = _renderService.ResolveSlidesDirectory();
      int withSlide = segments.Count(seq => _renderService.ResolveSlidePathFromNote(seq.Note, slidesDir) != null);
      if (withSlide == 0)
      {
        var displayDir = string.IsNullOrWhiteSpace(slidesDir) ? "(non résolu)" : slidesDir;
        MessageBox.Show(this,
          $"Aucune slide trouvée dans {displayDir}.\nVérifiez que les fichiers slide_000.png, slide_001.png, etc. sont présents.",
          "Apercu compose", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      BtnPreviewFrame.IsEnabled = false;
      string? lastPng = null;
      try
      {
        for (int i = 0; i < segments.Count; i++)
        {
          var seq = segments[i];
          BtnPreviewFrame.Content = segments.Count > 1
            ? $"? Apercu {i + 1}/{segments.Count}…"
            : "? Génération…";
          var pngPath = await _renderService.GeneratePreviewFrameAsync(seq, CollectRenderSettings());
          if (pngPath != null)
          {
            lastPng = pngPath;
            Logger.Info($"OnPreviewFrame: segment {i + 1}/{segments.Count} OK seq={seq.Id} note={seq.Note}");
          }
          else
          {
            Logger.Warn($"OnPreviewFrame: segment {i + 1}/{segments.Count} FAILED seq={seq.Id}");
          }
        }
        if (lastPng != null)
        {
          LoadBitmapFromPath(lastPng);
          MediaPreviewPlaceholder.Visibility = Visibility.Collapsed;
          _lastPreviewPngPath = lastPng;
          if (!_previewExpanded) { _previewExpanded = true; PreviewBorder.Height = 350; BtnExpandPreview.Content = "?"; }
        }
        else
        {
          MessageBox.Show(this, $"L'aperçu composé a échoué.\nVoir : {Logger.SessionPath}", "Apercu compose",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
      }
      catch (Exception ex)
      {
        Logger.Error("OnPreviewFrame", ex);
        MessageBox.Show(this, $"Erreur :\n{ex.Message}", "Apercu compose",
          MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        BtnPreviewFrame.IsEnabled = true;
        BtnPreviewFrame.Content = "?? Aperçu composé";
      }
    }
    private async void OnPreviewSegment(object sender, RoutedEventArgs e)
    {
      var selected = GridItems.SelectedItems.OfType<Sequence>()
        .OrderBy(s => _items.IndexOf(s))
        .ToList();
      if (selected.Count == 0 && GridItems.SelectedItem is Sequence single) selected.Add(single);
      if (selected.Count == 0)
      {
        MessageBox.Show(this, "Sélectionne d'abord une ou plusieurs séquences.", "Prévisualisation",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      var segments = selected;
      Logger.Info($"OnPreviewSegment: selected={selected.Count} segments={segments.Count}");
      _projectService.SyncAndMarkDirty(_items);
      if (!string.IsNullOrWhiteSpace(_projectService.FilePath)) _projectService.Save();
      BtnPreviewSegment.IsEnabled = false;
      var renderSettings = CollectRenderSettings();
      var producedMp4s = new List<string>();
      try
      {
        for (int i = 0; i < segments.Count; i++)
        {
          BtnPreviewSegment.Content = segments.Count > 1
            ? $"? Rendu segment {i + 1}/{segments.Count}…"
            : "? Rendu en cours…";
          var result = await _renderService.RenderPreviewSegmentAsync(segments[i], renderSettings);
          if (result.Success && File.Exists(result.OutputPath))
          {
            var renamedDir  = Path.GetDirectoryName(result.OutputPath!) ?? Path.Combine(Path.GetTempPath(), "ro_preview");
            var renamedPath = Path.Combine(renamedDir, $"preview_seg_{i:D3}_{Guid.NewGuid():N}.mp4");
            try
            {
              if (File.Exists(renamedPath)) File.Delete(renamedPath);
              File.Move(result.OutputPath!, renamedPath);
              producedMp4s.Add(renamedPath);
              Logger.Info($"OnPreviewSegment: segment {i + 1}/{segments.Count} OK ? {renamedPath} (renamed from {result.OutputPath})");
            }
            catch (Exception moveEx)
            {
              Logger.Warn($"OnPreviewSegment: rename failed for segment {i + 1} ({moveEx.Message}) — fallback copy");
              try
              {
                File.Copy(result.OutputPath!, renamedPath, overwrite: true);
                producedMp4s.Add(renamedPath);
              }
              catch (Exception copyEx)
              {
                Logger.Error($"OnPreviewSegment: copy fallback also failed for segment {i + 1}", copyEx);
              }
            }
          }
          else
          {
            Logger.Warn($"OnPreviewSegment: segment {i + 1}/{segments.Count} FAILED — {result.ErrorMessage}");
          }
        }
        if (producedMp4s.Count == 0)
        {
          MessageBox.Show(this, "Aucun segment n'a pu être rendu.\nVoir : " + Logger.SessionPath,
            "Prévisualisation", MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }
        string finalPath;
        if (producedMp4s.Count == 1)
        {
          finalPath = producedMp4s[0];
        }
        else
        {
          BtnPreviewSegment.Content = "? Concaténation…";
          var tempDir = Path.Combine(Path.GetTempPath(), "ro_preview");
          Directory.CreateDirectory(tempDir);
          finalPath = Path.Combine(tempDir, $"preview_concat_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
          await ConcatPreviewMp4sAsync(producedMp4s, finalPath, renderSettings.FfmpegExePath);
          if (!File.Exists(finalPath))
          {
            MessageBox.Show(this, $"La concaténation a échoué.\nVoir : {Logger.SessionPath}",
              "Prévisualisation", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
          }
          Logger.Info($"OnPreviewSegment: concat OK ? {finalPath}");
        }
        try
        {
          System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
          {
            FileName        = finalPath,
            UseShellExecute = true
          });
        }
        catch (Exception ex)
        {
          MessageBox.Show(this,
            $"MP4 généré mais impossible d'ouvrir :\n{finalPath}\n\n{ex.Message}",
            "Prévisualisation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
      }
      catch (Exception ex)
      {
        Logger.Error("OnPreviewSegment", ex);
        MessageBox.Show(this, $"Erreur :\n{ex.Message}", "Prévisualisation",
          MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        BtnPreviewSegment.IsEnabled = true;
        BtnPreviewSegment.Content = "▶ Prévisualiser ce segment";
      }
    }
    private async System.Threading.Tasks.Task ConcatPreviewMp4sAsync(
      List<string> mp4Paths, string outputPath, string ffmpegExe)
    {
      if (mp4Paths.Count == 0) return;
      var outDir = Path.GetDirectoryName(outputPath);
      if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);
      if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
      var cmd = new System.Text.StringBuilder();
      cmd.Append("-y ");
      foreach (var p in mp4Paths) cmd.Append("-i \"").Append(p).Append("\" ");
      var fc = new System.Text.StringBuilder();
      for (int i = 0; i < mp4Paths.Count; i++) fc.Append('[').Append(i).Append(":v][").Append(i).Append(":a]");
      fc.Append("concat=n=").Append(mp4Paths.Count).Append(":v=1:a=1[outv][outa]");
      cmd.Append("-filter_complex \"").Append(fc).Append("\" ");
      cmd.Append("-map \"[outv]\" -map \"[outa]\" ");
      cmd.Append("-c:v libx264 -preset ultrafast -crf 30 -pix_fmt yuv420p ");
      cmd.Append("-c:a aac -b:a 96k -ar 44100 -ac 2 ");
      cmd.Append("-movflags +faststart \"").Append(outputPath).Append('"');
      var args = cmd.ToString();
      Logger.Info($"[Concat] {ffmpegExe} {args}");
      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName               = ffmpegExe,
        Arguments              = args,
        UseShellExecute        = false,
        CreateNoWindow         = true,
        RedirectStandardError  = true,
        RedirectStandardOutput = true,
      };
      await System.Threading.Tasks.Task.Run(() =>
      {
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) { Logger.Error("[Concat] Process.Start returned null"); return; }
        var stderr = proc.StandardError.ReadToEnd();
        var exited = proc.WaitForExit(180_000);
        if (!exited)
        {
          try { proc.Kill(); } catch { }
          Logger.Warn("[Concat] Timeout 180s — killed");
          return;
        }
        Logger.FfmpegResult("PreviewConcat", ffmpegExe, args, proc.ExitCode, stderr, outputPath);
      });
    }
    private void OnBrowseOutputDir(object sender, RoutedEventArgs e)
    { using var dlg = new WF.FolderBrowserDialog { Description = "Dossier de sortie MP4" }; if (dlg.ShowDialog() == WF.DialogResult.OK) OutputDirBox.Text = dlg.SelectedPath; }
    private void OnBrowseFfmpeg(object sender, RoutedEventArgs e)
    { using var dlg = new WF.FolderBrowserDialog { Description = "Dossier ffmpeg.exe" }; if (dlg.ShowDialog() == WF.DialogResult.OK) FfmpegPathBox.Text = dlg.SelectedPath; }
    private async void OnExportMp4(object sender, RoutedEventArgs e)
    {
      var projectPath = _projectService.FilePath ?? PathBox.Text?.Trim();
      if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
      { MessageBox.Show(this, "Sauvegardez d'abord le projet.", "Export MP4", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
      Logger.Section("Export MP4");
      _projectService.SyncAndMarkDirty(_items);
      _projectService.SaveAs(projectPath);
      _player.Preload(_project);
      var (manifest, options) = _renderService.PrepareFullExport(CollectRenderSettings());
      try { await RenderLauncher.LaunchAsync(this, manifest, _project, options); }
      finally { }
    }
    private async void OnExportAllScenes(object sender, RoutedEventArgs e)
    {
      var projectPath = _projectService.FilePath ?? PathBox.Text?.Trim();
      if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
      {
        MessageBox.Show(this, "Sauvegardez d'abord le projet.", "Export toutes scènes",
          MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      int total = _project.Scenes.Count;
      if (total == 0)
      {
        MessageBox.Show(this, "Le projet ne contient aucune scène.", "Export toutes scènes",
          MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      var confirm = MessageBox.Show(this,
        $"Exporter {total} scène(s) en MP4 ?\n\n" +
        "Les exports se succèdent automatiquement, sans validation entre chaque.",
        "Export toutes les scènes", MessageBoxButton.OKCancel, MessageBoxImage.Question);
      if (confirm != MessageBoxResult.OK) return;
      Logger.Section($"Export lot — {total} scène(s)");
      _projectService.SyncAndMarkDirty(_items);
      _projectService.SaveAs(projectPath);
      var settings = CollectRenderSettings();
      var batchWin = new BatchExportWindow(this, total);
      var cts      = new System.Threading.CancellationTokenSource();
      batchWin.CancelRequested += () => cts.Cancel();
      batchWin.Show();
      BtnExportAll.IsEnabled    = false;
      BtnExportBottom.IsEnabled = false;
      int succeeded  = 0;
      var batchStart = DateTime.UtcNow;
      var errors     = new List<string>();
      try
      {
        for (int i = 0; i < _project.Scenes.Count; i++)
        {
          if (cts.IsCancellationRequested) break;
          var scene = _project.Scenes[i];
          Logger.Info($"[ExportAll] Scène {i + 1}/{total} : {scene.Name}");
          batchWin.StartScene(i, scene.Name);
          _projectService.SelectScene(scene);
          LoadSceneIntoGrid();
          _player.Preload(_project);
          Title = $"RoleplayOverlay — Export {i + 1}/{total} : {scene.Name}";
          var sceneStart = DateTime.UtcNow;
          RenderResult result;
          try
          {
            var (manifest, options) = _renderService.PrepareFullExport(settings);
            result = await RenderLauncher.LaunchAsync(
              this, manifest, _project, options,
              batchMode:  true,
              onProgress: p => batchWin.UpdateProgress(p));
          }
          catch (OperationCanceledException)
          {
            break;
          }
          catch (Exception ex)
          {
            result = new RenderResult(false, null, ex.Message, TimeSpan.Zero, 0);
          }
          var elapsed = DateTime.UtcNow - sceneStart;
          if (result.Success)
          {
            succeeded++;
            Logger.Info($"[ExportAll] ✓ Scène {i + 1} terminée en {elapsed:mm\\:ss}");
          }
          else
          {
            errors.Add($"• {scene.Name} : {result.ErrorMessage}");
            Logger.Error($"[ExportAll] ✗ Scène '{scene.Name}' : {result.ErrorMessage}");
          }
          batchWin.CompleteScene(scene.Name, elapsed, result.Success);
        }
      }
      finally
      {
        BtnExportAll.IsEnabled    = true;
        BtnExportBottom.IsEnabled = true;
        UpdateTitle();
        batchWin.FinishAll(succeeded, total, DateTime.UtcNow - batchStart);
      }
      Logger.Info($"[ExportAll] Terminé — {succeeded}/{total} succès");
    }
    private RenderSettings CollectRenderSettings()
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      double youX = 833, youY = 885, bot1X = 20, bot1Y = 20, bot2X = 1760, bot2Y = 20;
      try
      {
        var layoutPath = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "RoleplayOverlay", "bubble_layout.json");
        if (File.Exists(layoutPath))
        {
          var lay = Newtonsoft.Json.JsonConvert.DeserializeObject<BubbleLayoutFullDto>(System.IO.File.ReadAllText(layoutPath));
          if (lay != null) { youX = lay.YouX; youY = lay.YouY; bot1X = lay.Bot1X; bot1Y = lay.Bot1Y; bot2X = lay.Bot2X; bot2Y = lay.Bot2Y; }
        }
      }
      catch { }
      return new RenderSettings
      {
        Aspect              = GetSelectedVideoAspect(),
        Crf                 = int.TryParse(CrfBox.Text,       out var c)  ? Math.Clamp(c, 0, 51)    : 18,
        FontSize            = int.TryParse(FontSizeBox.Text,  out var f)  ? Math.Clamp(f, 12, 120)  : 34,
        SubtitleFontSize    = int.TryParse(SubFontSizeBox?.Text, out var sf) ? Math.Clamp(sf, 8, 120) : 50,
        BurnSubtitles       = BurnSubsCheck.IsChecked == true,
        UseAzureTts         = TtsModeAzure.IsChecked  == true,
        ShowAvatars         = ShowAvatarsCheck.IsChecked != false,
        UseNvenc            = UseNvencCheck.IsChecked == true,
        MaxParallelSegments = int.TryParse(ParallelBox?.Text,  out var mp) ? Math.Max(0, mp) : 0,
        FfmpegBinaryPath    = string.IsNullOrWhiteSpace(FfmpegPathBox.Text?.Trim()) ? null : FfmpegPathBox.Text.Trim(),
        OutputDirectory     = OutputDirBox.Text?.Trim(),
        AvatarSize          = int.TryParse(BubbleConfig.AvatarSizeBox.Text, out var avSz) ? avSz : 140,
        YouAvatarPath       = _project.Global.YouImage,
        Bot1AvatarPath      = _project.Global.Bot1Image,
        Bot2AvatarPath      = _project.Global.Bot2Image,
        YouX = youX, YouY = youY, Bot1X = bot1X, Bot1Y = bot1Y, Bot2X = bot2X, Bot2Y = bot2Y,
        YouGlowR  = BubbleConfig.YouGlow.R,  YouGlowG  = BubbleConfig.YouGlow.G,  YouGlowB  = BubbleConfig.YouGlow.B,
        Bot1GlowR = BubbleConfig.Bot1Glow.R, Bot1GlowG = BubbleConfig.Bot1Glow.G, Bot1GlowB = BubbleConfig.Bot1Glow.B,
        Bot2GlowR = BubbleConfig.Bot2Glow.R, Bot2GlowG = BubbleConfig.Bot2Glow.G, Bot2GlowB = BubbleConfig.Bot2Glow.B,
        GlowIntensity = float.TryParse(GlowIntensityBox?.Text,
          System.Globalization.NumberStyles.Float, inv, out var gi) ? Math.Clamp(gi, 0f, 1f) : 0.7f,
        SubColors = new SubtitleColors
        {
          Bot1Primary = _subB1Primary, Bot1Outline = _subB1Outline,
          Bot2Primary = _subB2Primary, Bot2Outline = _subB2Outline,
          YouPrimary  = _subYouPrimary, YouOutline  = _subYouOutline,
        },
        PreviewIncludeAvatar = PvIncludeAvatar?.IsChecked == true,
        PreviewIncludeShadow = PvIncludeShadow?.IsChecked == true,
        PreviewIncludeSubs   = PvIncludeSubs?.IsChecked   == true,
      };
    }
    private VideoAspect GetSelectedVideoAspect()
    {
      var item = VideoAspectBox?.SelectedItem as ComboBoxItem;
      var tag  = item?.Tag as string;
      return string.Equals(tag, "Portrait", StringComparison.OrdinalIgnoreCase)
        ? VideoAspect.Portrait
        : VideoAspect.Landscape;
    }
    private void RestoreVideoAspectFromPrefs()
    {
      if (VideoAspectBox == null) return;
      var pref = UserPrefs.VideoAspect ?? "Landscape";
      foreach (ComboBoxItem item in VideoAspectBox.Items)
      {
        if (string.Equals(item.Tag as string, pref, StringComparison.OrdinalIgnoreCase))
        {
          VideoAspectBox.SelectedItem = item;
          return;
        }
      }
      VideoAspectBox.SelectedIndex = 0;
    }
    private void OnVideoAspectChanged(object sender, SelectionChangedEventArgs e)
    {
      if (!IsLoaded) return;
      var aspect = GetSelectedVideoAspect();
      UserPrefs.VideoAspect = aspect.ToString();
      UserPrefs.Save();
      if (SubFontSizeBox != null)
      {
        var current = SubFontSizeBox.Text?.Trim();
        if (aspect == VideoAspect.Portrait && current == "50")
          SubFontSizeBox.Text = "80";
        else if (aspect == VideoAspect.Landscape && current == "80")
          SubFontSizeBox.Text = "50";
      }
      Logger.Info($"[VideoAspect] Switched to {aspect}");
    }
    private void AutoLoadProject(string path)
    {
      if (!_projectService.Load(path)) return;
      _undoRedo.Clear();
      PathBox.Text = _projectService.FilePath;
      RefreshSceneList(); LoadSceneIntoGrid(); _player.Preload(_project);
      _suppressVisibility = true;
      ShowYouCheck.IsChecked  = _project.Global.ShowYou;
      ShowBot1Check.IsChecked = _project.Global.ShowBot1;
      ShowBot2Check.IsChecked = _project.Global.ShowBot2;
      _suppressVisibility = false;
      Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault()?.ApplyVisibilityFrom(_project.Global);
      UpdateTitle();
      Logger.Info($"[AutoLoad] OK — {_items.Count} sequences");
      WarnIfSlidesMissing();
    }
    private void WarnIfSlidesMissing()
    {
      var dir = _renderService.ResolveSlidesDirectory();
      bool hasSlides = !string.IsNullOrWhiteSpace(dir)
                       && Directory.Exists(dir)
                       && Directory.EnumerateFiles(dir, "slide_*.png").Any();
      if (hasSlides) return;
      var displayDir = string.IsNullOrWhiteSpace(dir) ? "(non résolu)" : dir;
      Logger.Warn($"[SlidesCheck] Dossier slides vide ou introuvable: {displayDir}");
      MessageBox.Show(this,
        $"Le dossier slides '{displayDir}' est vide ou introuvable.\nL'aperçu utilisera un fond noir.",
        "Slides manquantes", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void OnLoad(object sender, RoutedEventArgs e)
    {
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title  = "Ouvrir un projet JSON",
        Filter = "Projet JSON|*.json|Tous|*.*"
      };
      if (ofd.ShowDialog() != true) return;
      var path = ofd.FileName;
      Logger.Section($"OnLoad: {path}");
      bool hasProject = _project.Scenes.Count > 0 && _items.Count > 0;
      if (hasProject)
      {
        var choice = MessageBox.Show(this,
          $"Un projet est déjà ouvert.\n\n" +
          $"OUI : Remplacer (ferme le projet actuel)\n" +
          $"NON : Importer les scènes (ajoute au projet actuel)\n" +
          $"ANNULER : Ne rien faire",
          "Charger un projet",
          MessageBoxButton.YesNoCancel,
          MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return;
        if (choice == MessageBoxResult.No)
        {
          ImportScenesFrom(path);
          return;
        }
      }
      if (!_projectService.Load(path)) return;
      _undoRedo.Clear();
      PathBox.Text = _projectService.FilePath;
      RefreshSceneList(); LoadSceneIntoGrid(); _player.Preload(_project);
      _suppressVisibility = true;
      ShowYouCheck.IsChecked  = _project.Global.ShowYou;
      ShowBot1Check.IsChecked = _project.Global.ShowBot1;
      ShowBot2Check.IsChecked = _project.Global.ShowBot2;
      _suppressVisibility = false;
      Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault()?.ApplyVisibilityFrom(_project.Global);
      UpdateTitle();
      UserPrefs.LastProjectPath = _projectService.FilePath;
      UserPrefs.Save();
      Logger.Info($"Project loaded OK — {_items.Count} sequences");
      WarnIfSlidesMissing();
    }
    private void ImportScenesFrom(string path)
    {
      try
      {
        var json   = File.ReadAllText(path);
        var source = Newtonsoft.Json.JsonConvert.DeserializeObject<Project>(json);
        if (source == null || source.Scenes == null || source.Scenes.Count == 0)
        {
          MessageBox.Show(this, "Aucune scène trouvée dans ce fichier.", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }
        _projectService.SyncFromGrid(_items);
        var baseName = Path.GetFileNameWithoutExtension(path);
        int imported = 0;
        foreach (var scene in source.Scenes)
        {
          var newScene = new Scene
          {
            Id        = Guid.NewGuid().ToString("N"),
            Name      = $"{baseName} — {scene.Name}",
            Sequences = scene.Sequences
              .Select(s => { var c = s.Clone(); c.Id = Guid.NewGuid().ToString("N"); return c; })
              .ToList(),
          };
          _project.Scenes.Add(newScene);
          imported++;
        }
        _projectService.MarkDirty();
        RefreshSceneList();
        var first = _project.Scenes[^imported];
        if (SceneList.ItemsSource is System.Collections.IEnumerable items)
        {
          foreach (var it in items)
            if (it is SceneDisplayItem sdi && sdi.Scene == first)
            { SceneList.SelectedItem = it; break; }
        }
        _projectService.SelectScene(first);
        LoadSceneIntoGrid();
        Logger.Info($"[Import] {imported} scène(s) importée(s) depuis {path}");
        MessageBox.Show(this,
          $"{imported} scène(s) importée(s) depuis :\n{Path.GetFileName(path)}\n\n" +
          $"Elles sont disponibles dans la liste. La croix ? les retire du projet en mémoire.",
          "Import réussi", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      catch (Exception ex)
      {
        Logger.Error("ImportScenesFrom", ex);
        MessageBox.Show(this, $"Erreur d'import :\n{ex.Message}", "Import", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
      GridItems.CommitEdit(); GridItems.CommitEdit();
      _projectService.SyncFromGrid(_items);
      var path = _projectService.FilePath ?? PathBox.Text?.Trim();
      if (string.IsNullOrWhiteSpace(path))
      { var sfd = new Microsoft.Win32.SaveFileDialog { Filter = "Projet JSON|*.json" }; if (sfd.ShowDialog() != true) return; path = sfd.FileName; }
      Logger.Section($"Saving project: {path}");
      _projectService.SaveAs(path);
      PathBox.Text = _projectService.FilePath;
      UpdateTitle();
    }
    private Cam.CamStudioForm? _camStudio;
    private void OnOpenCamStudio(object sender, RoutedEventArgs e)
    {
      if (_camStudio != null && !_camStudio.IsDisposed)
      {
        _camStudio.WindowState = System.Windows.Forms.FormWindowState.Normal;
        _camStudio.Activate();
        return;
      }
      _camStudio = new Cam.CamStudioForm();
      _camStudio.FormClosed += (_, _) => _camStudio = null;
      _camStudio.Show();
    }
    private async void OnGenerateVoiceFromScript(object sender, RoutedEventArgs e)
    {
      var dlg = new Microsoft.Win32.OpenFileDialog
      {
        Title  = "Choisir un script à dire",
        Filter = "Scripts texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*"
      };
      var scriptsDir = ScriptToVoice.FindScriptsDir();
      if (scriptsDir != null) dlg.InitialDirectory = scriptsDir;
      if (dlg.ShowDialog() != true) return;
      string input = dlg.FileName;
      bool azure = false;
      var (key, region) = AzureConfig.Load();
      if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region))
      {
        var choice = System.Windows.MessageBox.Show(
          "Quelle voix utiliser ?\n\nOui = Azure (production, qualité max)\nNon = SAPI (test, hors-ligne, gratuit)",
          "Voix off", System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
        if (choice == System.Windows.MessageBoxResult.Cancel) return;
        azure = choice == System.Windows.MessageBoxResult.Yes;
      }
      (bool ok, string message, string? outPath, double seconds) res = default;
      System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
      try
      {
        res = await System.Threading.Tasks.Task.Run(() => ScriptToVoice.Generate(input, azure, null, null));
      }
      finally { System.Windows.Input.Mouse.OverrideCursor = null; }
      if (res.ok && res.outPath != null)
      {
        var open = System.Windows.MessageBox.Show(
          $"Audio généré :\n{res.outPath}\n\n({res.seconds:F1} s)\n\nOuvrir le dossier ?",
          "Voix off — terminé", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
        if (open == System.Windows.MessageBoxResult.Yes)
        {
          try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{res.outPath}\""); } catch { }
        }
      }
      else
      {
        System.Windows.MessageBox.Show(res.message, "Voix off — échec",
          System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
      }
    }
    private async void OnImportPdf(object sender, RoutedEventArgs e)
    {
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title  = "Sélectionner un PDF ou un ZIP de pages",
        Filter = "Fichiers PDF|*.pdf|Archive ZIP|*.zip|Tous|*.*"
      };
      if (ofd.ShowDialog(this) != true) return;
      string sourcePath = ofd.FileName;
      string ext = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
      var dfd = new Microsoft.Win32.OpenFileDialog
      {
        Title           = "Destination des slides — naviguer vers le dossier puis cliquer Ouvrir",
        Filter          = "Dossier|.",
        FileName        = "Sélectionner ce dossier",
        ValidateNames   = false,
        CheckFileExists = false,
        CheckPathExists = true,
      };
      if (dfd.ShowDialog(this) != true) return;
      string? destDir = System.IO.Path.GetDirectoryName(dfd.FileName);
      if (string.IsNullOrEmpty(destDir) || !System.IO.Directory.Exists(destDir))
      {
        MessageBox.Show(this, "Impossible de déterminer le dossier de destination.",
          "Import PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      IsEnabled = false;
      try
      {
        int count = ext == ".zip"
          ? await Task.Run(() => ImportFromZip(sourcePath, destDir))
          : await Task.Run(() => ImportFromPdf(sourcePath, destDir));
        MessageBox.Show(this,
          $"{count} slide(s) exportée(s) vers :\n{destDir}",
          "Import terminé", MessageBoxButton.OK, MessageBoxImage.Information);
        Logger.Info($"OnImportPdf: {count} slides → {destDir}");
      }
      catch (Exception ex)
      {
        Logger.Error("OnImportPdf failed", ex);
        MessageBox.Show(this,
          $"Erreur lors de l'import :\n\n{ex.Message}",
          "Import PDF — Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        IsEnabled = true;
      }
    }
    private static int ImportFromPdf(string pdfPath, string destDir)
    {
      string? tool = FindRasterTool("pdftoppm") ?? FindRasterTool("magick");
      if (tool == null)
        throw new InvalidOperationException(
          "Aucun outil de rasterisation trouvé.\n" +
          "Installez MiKTeX (pdftoppm) ou ImageMagick (magick) et relancez.");
      string toolName = System.IO.Path.GetFileNameWithoutExtension(tool).ToLowerInvariant();
      string tmpDir   = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            "RO_import_" + Guid.NewGuid().ToString("N")[..8]);
      System.IO.Directory.CreateDirectory(tmpDir);
      try
      {
        string args = toolName == "pdftoppm"
          ? $"-png -r 150 \"{pdfPath}\" \"{System.IO.Path.Combine(tmpDir, "page")}\""
          : $"-density 150 \"{pdfPath}\" \"{System.IO.Path.Combine(tmpDir, "page-%03d.png")}\"";
        RunExternalProcess(tool, args);
        var pngs = System.IO.Directory.GetFiles(tmpDir, "*.png").OrderBy(f => f).ToArray();
        if (pngs.Length == 0)
          throw new InvalidOperationException("La conversion n'a produit aucun PNG.");
        for (int i = 0; i < pngs.Length; i++)
          System.IO.File.Copy(pngs[i],
            System.IO.Path.Combine(destDir, $"slide_{i + 1:D3}.png"), overwrite: true);
        return pngs.Length;
      }
      finally
      {
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
      }
    }
    private static int ImportFromZip(string zipPath, string destDir)
    {
      string tmpDir = System.IO.Path.Combine(
                          System.IO.Path.GetTempPath(),
                          "RO_zip_" + Guid.NewGuid().ToString("N")[..8]);
      try
      {
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tmpDir);
        var images = System.IO.Directory
          .GetFiles(tmpDir, "*.*", System.IO.SearchOption.AllDirectories)
          .Where(f => { var x = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        return x == ".png" || x == ".jpg" || x == ".jpeg"; })
          .OrderBy(f => f)
          .ToArray();
        if (images.Length == 0)
          throw new InvalidOperationException("Le ZIP ne contient aucune image PNG/JPG.");
        for (int i = 0; i < images.Length; i++)
          System.IO.File.Copy(images[i],
            System.IO.Path.Combine(destDir, $"slide_{i + 1:D3}.png"), overwrite: true);
        return images.Length;
      }
      finally
      {
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
      }
    }
    private static void RunExternalProcess(string exe, string args)
    {
      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName              = exe,
        Arguments             = args,
        UseShellExecute       = false,
        CreateNoWindow        = true,
        RedirectStandardError = true,
        RedirectStandardOutput= true
      };
      using var p = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException($"Impossible de lancer {exe}");
      string stderr = p.StandardError.ReadToEnd();
      p.WaitForExit();
      if (p.ExitCode != 0)
        throw new InvalidOperationException(
          $"{System.IO.Path.GetFileName(exe)} a échoué (exit {p.ExitCode}).\n{stderr}");
    }
    private static string? FindRasterTool(string name)
    {
      foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                               .Split(System.IO.Path.PathSeparator))
      {
        foreach (string candidate in new[] { name, name + ".exe" })
        {
          string full = System.IO.Path.Combine(dir.Trim(), candidate);
          if (System.IO.File.Exists(full)) return full;
        }
      }
      foreach (string dir in new[]
      {
        @"C:\Program Files\MiKTeX\miktex\bin\x64",
        @"C:\Users\Gstar\AppData\Local\Programs\MiKTeX\miktex\bin\x64",
        @"C:\Program Files\MiKTeX 2.9\miktex\bin\x64",
      })
      {
        string full = System.IO.Path.Combine(dir, name + ".exe");
        if (System.IO.File.Exists(full)) return full;
      }
      foreach (string dir in new[]
      {
        @"C:\Program Files\ImageMagick-7.1.1-Q16-HDRI",
        @"C:\Program Files\ImageMagick-7.1.0-Q16-HDRI",
      })
      {
        string full = System.IO.Path.Combine(dir, name + ".exe");
        if (System.IO.File.Exists(full)) return full;
      }
      return null;
    }
    private void OnBubbleVisibilityChanged(object sender, RoutedEventArgs e)
    {
      if (_suppressVisibility) return;
      _project.Global.ShowYou  = ShowYouCheck.IsChecked  == true;
      _project.Global.ShowBot1 = ShowBot1Check.IsChecked == true;
      _project.Global.ShowBot2 = ShowBot2Check.IsChecked == true;
      Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault()?.ApplyVisibilityFrom(_project.Global);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void LoadMonitorsIntoDisplayBox()
    {
      if (DisplayBox == null) return;
      var screens = WF.Screen.AllScreens; DisplayBox.Items.Clear();
      for (int i = 0; i < screens.Length; i++)
      { var s = screens[i]; var parts = s.DeviceName.Trim('\\').Split('\\');
        DisplayBox.Items.Add($"écran {i + 1}: {parts.Last()} ({s.Bounds.Width}×{s.Bounds.Height})"); }
      if (DisplayBox.Items.Count > 0) DisplayBox.SelectedIndex = 0;
    }
    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e) => ApplyMonitorSelection();
    private void OnRefreshMonitors(object sender, RoutedEventArgs e) => LoadMonitorsIntoDisplayBox();
    private void OnApplyMonitor(object sender, RoutedEventArgs e) => ApplyMonitorSelection();
    private void ApplyMonitorSelection()
    {
      int idx = DisplayBox?.SelectedIndex ?? 0;
      var screens = WF.Screen.AllScreens;
      if (idx < 0 || idx >= screens.Length) return;
      var overlay = Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault();
      overlay?.MoveToScreen(screens[idx]);
    }
    private void OnPickSubB1Primary(object s, RoutedEventArgs e)  { var c = PickSubColor(_subB1Primary);  if (c != null) { _subB1Primary  = c; SubB1PrimaryBtn.Background  = HexToBrush(c); SubB1PrimaryBtn.Foreground  = ContrastFg(c); } }
    private void OnPickSubB1Outline(object s, RoutedEventArgs e)  { var c = PickSubColor(_subB1Outline);  if (c != null) { _subB1Outline  = c; SubB1OutlineBtn.Background  = HexToBrush(c); SubB1OutlineBtn.Foreground  = ContrastFg(c); } }
    private void OnPickSubB2Primary(object s, RoutedEventArgs e)  { var c = PickSubColor(_subB2Primary);  if (c != null) { _subB2Primary  = c; SubB2PrimaryBtn.Background  = HexToBrush(c); SubB2PrimaryBtn.Foreground  = ContrastFg(c); } }
    private void OnPickSubB2Outline(object s, RoutedEventArgs e)  { var c = PickSubColor(_subB2Outline);  if (c != null) { _subB2Outline  = c; SubB2OutlineBtn.Background  = HexToBrush(c); SubB2OutlineBtn.Foreground  = ContrastFg(c); } }
    private void OnPickSubYouPrimary(object s, RoutedEventArgs e) { var c = PickSubColor(_subYouPrimary); if (c != null) { _subYouPrimary = c; SubYouPrimaryBtn.Background = HexToBrush(c); SubYouPrimaryBtn.Foreground = ContrastFg(c); } }
    private void OnPickSubYouOutline(object s, RoutedEventArgs e) { var c = PickSubColor(_subYouOutline); if (c != null) { _subYouOutline = c; SubYouOutlineBtn.Background = HexToBrush(c); SubYouOutlineBtn.Foreground = ContrastFg(c); } }
    private static string? PickSubColor(string current)
    {
      var hex = current.TrimStart('#');
      using var dlg = new WF.ColorDialog { Color = System.Drawing.Color.FromArgb(
        Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)), FullOpen = true };
      return dlg.ShowDialog() == WF.DialogResult.OK ? $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}" : null;
    }
    private static SolidColorBrush HexToBrush(string hex)
    { var h = hex.TrimStart('#'); return new SolidColorBrush(Color.FromRgb(Convert.ToByte(h[0..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16))); }
    private static Brush ContrastFg(string hex)
    { var h = hex.TrimStart('#'); double lum = 0.299*Convert.ToByte(h[0..2],16) + 0.587*Convert.ToByte(h[2..4],16) + 0.114*Convert.ToByte(h[4..6],16);
      return lum > 128 ? WpfBrushes.Black : WpfBrushes.White; }
    private void OnGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
      var header = e.Column?.Header?.ToString() ?? "cellule";
      _undoRedo.PushUndo(_items, $"éditer {header}");
    }
    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
      if (e.EditAction != DataGridEditAction.Commit) return;
      if (e.Row.DataContext is not Sequence seq) return;
      var header = e.Column?.Header?.ToString() ?? "";
      if (header == "?" && e.EditingElement is WpfComboBox cbSpk)
        ApplyVoiceForSpeaker(seq, cbSpk.SelectedItem as string ?? "");
      if (header == "Lang" && e.EditingElement is WpfComboBox cbLang)
      {
        var voice = cbLang.SelectedItem as string ?? seq.Voice ?? "";
        if (voice.StartsWith("fr", StringComparison.OrdinalIgnoreCase) && seq.Speaker != "bot2")
          seq.Speaker = "bot2";
        else if (voice.StartsWith("en", StringComparison.OrdinalIgnoreCase) && seq.Speaker != "bot1")
          seq.Speaker = "bot1";
      }
      _projectService.SyncAndMarkDirty(_items);
      if (header is "Texte / Script" or "Text" or "Lang" or "?" or "Note")
      {
        seq.EstimatedDuration = TtsTimingService.EstimateFromWordCount(seq.Text, seq.Voice);
        seq.DurationIsExact   = false;
        _ttsTiming.Invalidate(seq.Text, seq.Voice);
        _ttsTiming.RequestExactDuration(seq);
      }
    }
    private static void ApplyVoiceForSpeaker(Sequence seq, string speaker)
    {
      switch (speaker.Trim().ToLowerInvariant())
      { case "bot1": seq.Voice = "en-US"; break; case "bot2": seq.Voice = "fr-FR"; break; }
    }
    private void OnSequencePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
      if (e.PropertyName != nameof(Sequence.Voice)) return;
      if (sender is not Sequence seq) return;
      if (seq.Voice?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) == true && seq.Speaker != "bot2")
        seq.Speaker = "bot2";
      else if (seq.Voice?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true && seq.Speaker != "bot1")
        seq.Speaker = "bot1";
    }
    private void OnAdd(object sender, RoutedEventArgs e)
    {
      _undoRedo.PushUndo(_items, "Ajouter ligne");
      var newSeq = new Sequence { Id = Guid.NewGuid().ToString("N"), Mode = "tts",
        Voice = "en-US", ShowText = true, Text = "", Speaker = "bot1", Note = "" };
      newSeq.EstimatedDuration = TtsTimingService.EstimateFromWordCount(newSeq.Text, newSeq.Voice);
      _items.Add(newSeq);
      _originalOrder.Add(newSeq.Id ?? "");
      newSeq.PropertyChanged += OnSequencePropertyChanged;
      PairingService.AutoPairNewSequence(newSeq, _items);
      ApplySort();
      GridItems.SelectedItem = newSeq; GridItems.ScrollIntoView(newSeq);
      _projectService.SyncAndMarkDirty(_items); UpdateStats();
    }
    private void OnRemove(object sender, RoutedEventArgs e)
    {
      var i = GridItems.SelectedIndex; if (i < 0 || i >= _items.Count) return;
      _undoRedo.PushUndo(_items, "Supprimer ligne");
      var removedId = _items[i].Id ?? "";
      _items.RemoveAt(i);
      _originalOrder.Remove(removedId);
      if (_items.Count > 0) GridItems.SelectedIndex = Math.Min(i, _items.Count - 1);
      _projectService.SyncAndMarkDirty(_items); UpdateStats();
    }
    private void OnUp(object sender, RoutedEventArgs e)
    {
      var i = GridItems.SelectedIndex; if (i <= 0 || i >= _items.Count) return;
      _undoRedo.PushUndo(_items, "Déplacer ?");
      var (blockStart, blockEnd) = GetPairBlock(i);
      if (blockStart <= 0) return;
      var moved = _items.Skip(blockStart).Take(blockEnd - blockStart + 1).ToList();
      for (int k = blockEnd; k >= blockStart; k--) _items.RemoveAt(k);
      foreach (var s in Enumerable.Reverse(moved)) _items.Insert(blockStart - 1, s);
      GridItems.SelectedIndex = blockStart - 1 + (i - blockStart);
      _projectService.SyncAndMarkDirty(_items);
      if (!IsSortByPairs) CaptureOriginalOrder();
    }
    private void OnDown(object sender, RoutedEventArgs e)
    {
      var i = GridItems.SelectedIndex; if (i < 0 || i >= _items.Count) return;
      _undoRedo.PushUndo(_items, "Déplacer ?");
      var (blockStart, blockEnd) = GetPairBlock(i);
      if (blockEnd >= _items.Count - 1) return;
      var moved = _items.Skip(blockStart).Take(blockEnd - blockStart + 1).ToList();
      for (int k = blockEnd; k >= blockStart; k--) _items.RemoveAt(k);
      foreach (var s in Enumerable.Reverse(moved)) _items.Insert(blockStart + 1, s);
      GridItems.SelectedIndex = blockStart + 1 + (i - blockStart);
      _projectService.SyncAndMarkDirty(_items);
      if (!IsSortByPairs) CaptureOriginalOrder();
    }
    private (int start, int end) GetPairBlock(int idx)
    {
      var seq = _items[idx];
      if (string.IsNullOrWhiteSpace(seq.PairId))
        return (idx, idx);
      int sibIdx = -1;
      if (idx > 0 && _items[idx - 1].PairId == seq.PairId)
        sibIdx = idx - 1;
      else if (idx < _items.Count - 1 && _items[idx + 1].PairId == seq.PairId)
        sibIdx = idx + 1;
      if (sibIdx < 0) return (idx, idx);
      int start = Math.Min(idx, sibIdx);
      int end   = Math.Max(idx, sibIdx);
      return (start, end);
    }
    private void OnSplitSequence(object sender, RoutedEventArgs e)
    {
      if (GridItems.SelectedItem is not Sequence selected)
      {
        MessageBox.Show(this, "Sélectionnez une ligne à diviser.", "Split",
          MessageBoxButton.OK, MessageBoxImage.Information);
        return;
      }
      Sequence? seqEn = null;
      Sequence? seqFr = null;
      if (!string.IsNullOrWhiteSpace(selected.PairId))
      {
        var siblings = _items.Where(s => s.PairId == selected.PairId).ToList();
        seqEn = siblings.FirstOrDefault(PairingService.IsEnglish)
             ?? siblings.FirstOrDefault(s => !PairingService.IsFrench(s));
        seqFr = siblings.FirstOrDefault(PairingService.IsFrench)
             ?? siblings.FirstOrDefault(s => !PairingService.IsEnglish(s));
      }
      seqEn ??= selected;
      seqFr ??= selected;
      var dlg = new SplitSequenceDialog(seqEn, seqFr) { Owner = this };
      var ok  = dlg.ShowDialog();
      if (ok != true || dlg.Result.Count == 0) return;
      _undoRedo.PushUndo(_items, $"Diviser séquence {selected.PairId ?? selected.Id}");
      int idxEn = -1, idxFr = -1;
      for (int i = 0; i < _items.Count; i++)
      {
        if (_items[i].Id == seqEn!.Id) idxEn = i;
        if (_items[i].Id == seqFr!.Id) idxFr = i;
      }
      if (idxEn < 0 && idxFr >= 0) idxEn = idxFr;
      if (idxFr < 0 && idxEn >= 0) idxFr = idxEn;
      if (idxEn < 0 && idxFr < 0)
      {
        idxEn = _items.Count;
        idxFr = _items.Count;
      }
      bool orphan   = ReferenceEquals(seqEn, seqFr) || seqEn!.Id == seqFr!.Id;
      bool adjacent = orphan || Math.Abs(idxEn - idxFr) <= 1;
      bool selectFrench = PairingService.IsFrench(selected);
      if (adjacent)
      {
        int insertIdx = Math.Min(idxEn, idxFr);
        if (insertIdx < 0) insertIdx = _items.Count;
        var sourceIds = new HashSet<string?>(
          new[] { seqEn?.Id, seqFr?.Id }.Where(id => !string.IsNullOrWhiteSpace(id)));
        var toRemove = _items.Where(s => sourceIds.Contains(s.Id)).ToList();
        foreach (var s in toRemove)
        {
          s.PropertyChanged -= OnSequencePropertyChanged;
          _items.Remove(s);
        }
        int offset = 0;
        foreach (var (newEn, newFr) in dlg.Result)
        {
          newEn.PropertyChanged += OnSequencePropertyChanged;
          newFr.PropertyChanged += OnSequencePropertyChanged;
          _items.Insert(Math.Min(insertIdx + offset, _items.Count), newEn);
          offset++;
          _items.Insert(Math.Min(insertIdx + offset, _items.Count), newFr);
          offset++;
        }
        Logger.Info($"[Split] pairId={seqEn?.PairId} adjacent ? {dlg.Result.Count} paires créées");
      }
      else
      {
        int lowIdx  = Math.Min(idxEn, idxFr);
        int highIdx = Math.Max(idxEn, idxFr);
        bool enIsLow = (idxEn == lowIdx);
        _items[highIdx].PropertyChanged -= OnSequencePropertyChanged;
        _items.RemoveAt(highIdx);
        _items[lowIdx].PropertyChanged -= OnSequencePropertyChanged;
        _items.RemoveAt(lowIdx);
        int lowInsert  = lowIdx;
        int highInsert = highIdx - 1;
        var enFragments = dlg.Result.Select(p => p.En).ToList();
        var frFragments = dlg.Result.Select(p => p.Fr).ToList();
        foreach (var frag in enFragments) frag.PropertyChanged += OnSequencePropertyChanged;
        foreach (var frag in frFragments) frag.PropertyChanged += OnSequencePropertyChanged;
        var lowFragments  = enIsLow ? enFragments : frFragments;
        var highFragments = enIsLow ? frFragments : enFragments;
        for (int i = 0; i < lowFragments.Count; i++)
        {
          int at = Math.Min(lowInsert + i, _items.Count);
          _items.Insert(at, lowFragments[i]);
        }
        int highInsertAdjusted = highInsert + lowFragments.Count;
        for (int i = 0; i < highFragments.Count; i++)
        {
          int at = Math.Min(highInsertAdjusted + i, _items.Count);
          _items.Insert(at, highFragments[i]);
        }
        Logger.Info($"[Split] pairId={seqEn?.PairId} séparé (idxEn={idxEn}, idxFr={idxFr}) ? {dlg.Result.Count} paires créées");
      }
      if (dlg.Result.Count > 0)
      {
        Sequence firstNew = selectFrench ? dlg.Result[0].Fr : dlg.Result[0].En;
        GridItems.SelectedItem = firstNew;
        GridItems.ScrollIntoView(firstNew);
      }
      ApplySort();
      RefreshPairNumbers();
      UpdateStats();
      _ttsTiming.ComputeAllAsync(_items);
      _projectService.SyncAndMarkDirty(_items);
    }
    private void OnUndo(object sender, RoutedEventArgs e)
    {
      if (!_undoRedo.CanUndo) return;
      var restored = _undoRedo.Undo(_items);
      if (restored == null) return;
      _items.Clear(); foreach (var s in restored) _items.Add(s);
      ApplySort(); UpdateStats(); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      _ = UpdateMediaPanelAsync();
    }
    private void OnRedo(object sender, RoutedEventArgs e)
    {
      if (!_undoRedo.CanRedo) return;
      var restored = _undoRedo.Redo(_items);
      if (restored == null) return;
      _items.Clear(); foreach (var s in restored) _items.Add(s);
      ApplySort(); UpdateStats(); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      _ = UpdateMediaPanelAsync();
    }
    private void UpdateUndoRedoButtons()
    {
      BtnUndo.IsEnabled = _undoRedo.CanUndo;
      BtnRedo.IsEnabled = _undoRedo.CanRedo;
      BtnUndo.ToolTip   = _undoRedo.CanUndo ? $"Annuler (Ctrl+Z) — {_undoRedo.UndoCount} niveaux" : "Rien à annuler";
      BtnRedo.ToolTip   = _undoRedo.CanRedo ? $"Rétablir (Ctrl+Y) — {_undoRedo.RedoCount} niveaux" : "Rien à rétablir";
    }
    private sealed class HistoryEntry
    {
      public string Index { get; init; } = "";
      public string Label { get; init; } = "";
      public int    N     { get; init; }
    }
    private bool _suppressHistorySelection;
    private void RefreshHistory()
    {
      if (UndoHistoryList == null || RedoHistoryList == null) return;
      _suppressHistorySelection = true;
      var undoLabels = _undoRedo.UndoLabels;
      UndoHistoryList.ItemsSource = undoLabels
        .Select((lbl, i) => new HistoryEntry { Index = $"#{i + 1}", Label = lbl, N = i + 1 })
        .ToList();
      UndoCountLabel.Text = undoLabels.Count.ToString();
      var redoLabels = _undoRedo.RedoLabels;
      RedoHistoryList.ItemsSource = redoLabels
        .Select((lbl, i) => new HistoryEntry { Index = $"#{i + 1}", Label = lbl, N = i + 1 })
        .ToList();
      RedoCountLabel.Text = redoLabels.Count.ToString();
      _suppressHistorySelection = false;
    }
    private void OnUndoHistorySelected(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressHistorySelection) return;
      if (UndoHistoryList.SelectedItem is not HistoryEntry entry) return;
      UndoHistoryList.SelectedIndex = -1;
      var restored = _undoRedo.UndoN(_items, entry.N);
      if (restored == null) return;
      _items.Clear(); foreach (var s in restored) _items.Add(s);
      ApplySort(); UpdateStats(); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      _ = UpdateMediaPanelAsync();
    }
    private void OnRedoHistorySelected(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressHistorySelection) return;
      if (RedoHistoryList.SelectedItem is not HistoryEntry entry) return;
      RedoHistoryList.SelectedIndex = -1;
      var restored = _undoRedo.RedoN(_items, entry.N);
      if (restored == null) return;
      _items.Clear(); foreach (var s in restored) _items.Add(s);
      ApplySort(); UpdateStats(); _projectService.SyncAndMarkDirty(_items);
      MosaicThumbnailConverter.InvalidateCache();
      _ = UpdateMediaPanelAsync();
    }
    private void OnEditTextClick(object sender, RoutedEventArgs e)
    { if (sender is WpfButton btn && btn.Tag is Sequence seq) OpenBigTextEditor(seq); }
    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      var dep = e.OriginalSource as DependencyObject;
      while (dep != null && dep is not DataGridCell && dep is not DataGridRow)
        dep = VisualTreeHelper.GetParent(dep);
      if (dep is DataGridCell cell && cell.Column?.Header?.ToString() is "Texte / Script" or "Text" && cell.DataContext is Sequence seq)
        OpenBigTextEditor(seq);
    }
    private void OpenBigTextEditor(Sequence seq)
    {
      var dlg = new BigTextDialog(seq.Text ?? string.Empty) { Owner = this };
      if (dlg.ShowDialog() == true)
      {
      _undoRedo.PushUndo(_items, "éditer texte");
      seq.Text = dlg.ResultText ?? ""; GridItems.CommitEdit(); _projectService.SyncAndMarkDirty(_items);
        seq.EstimatedDuration = TtsTimingService.EstimateFromWordCount(seq.Text, seq.Voice);
        seq.DurationIsExact   = false;
        _ttsTiming.Invalidate(seq.Text, seq.Voice);
        _ttsTiming.RequestExactDuration(seq);
      }
    }
    private void OnPlaySelected(object sender, RoutedEventArgs e) { var idx = GridItems.SelectedIndex; if (idx >= 0) _player.PlayAt(idx, showBubbleText: false); }
    private void OnStop(object sender, RoutedEventArgs e) => _player.Stop();
    public void HighlightRow(int index, SpeakerKind who)
    {
      Dispatcher.Invoke(() =>
      {
        if (index < 0 || index >= GridItems.Items.Count) return;
        GridItems.SelectedIndex = index;
        GridItems.ScrollIntoView(GridItems.Items[index]);
        ClearRowHighlights(); PaintRow(index, BrushFor(who));
        ActiveLineLabel.Text = $"Ligne {index + 1} active — {who}";
        ActiveLineLabel.Foreground = BrushFor(who);
      });
    }
    private void OnGridLoadingRow(object sender, DataGridRowEventArgs e)
    {
    }
    private static Brush BrushFor(SpeakerKind who) => who switch
    {
      SpeakerKind.You  => new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD4, 0x00)),
      SpeakerKind.Bot1 => new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x00, 0xFF)),
      SpeakerKind.Bot2 => new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0xFF, 0xFF)),
      _ => WpfBrushes.Transparent
    };
    private DataGridRow? GetRow(int index)
    {
      if (index < 0 || index >= GridItems.Items.Count) return null;
      var row = GridItems.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
      if (row == null) { GridItems.UpdateLayout(); GridItems.ScrollIntoView(GridItems.Items[index]);
        row = GridItems.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow; }
      return row;
    }
    private static DataGridCell? GetCell(DataGridRow row, DataGridColumn col) => col.GetCellContent(row)?.Parent as DataGridCell;
    private void PaintRow(int index, Brush bg)
    {
      var row = GetRow(index); if (row == null) return;
      row.Background = bg; row.BorderBrush = null; row.BorderThickness = new Thickness(0);
      foreach (var col in GridItems.Columns)
        if (GetCell(row, col) is DataGridCell cell) { cell.Background = WpfBrushes.Transparent; cell.BorderThickness = new Thickness(0); }
    }
    private void ClearRowHighlights()
    {
      for (int i = 0; i < GridItems.Items.Count; i++)
      {
        if (GetRow(i) is not DataGridRow row) continue;
        row.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        row.BorderBrush = null;
        row.BorderThickness = new Thickness(0);
        foreach (var col in GridItems.Columns)
          if (GetCell(row, col) is DataGridCell cell)
          { cell.Background = WpfBrushes.Transparent; cell.BorderThickness = new Thickness(0); }
      }
    }
    private static SpeakerKind ParseSpeaker(string? s) => s?.ToLowerInvariant() switch
    { "you" => SpeakerKind.You, "bot2" => SpeakerKind.Bot2, _ => SpeakerKind.Bot1 };
    private string? ResolveSlidesDirectory()
      => _renderService.ResolveSlidesDirectory();
    private sealed class RenameDialog : Window
    {
      public string Result { get; private set; } = "";
      public RenameDialog(string current)
      {
        Title = "Renommer la scène"; Width = 340; Height = 120;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C));
        var root = new StackPanel { Margin = new Thickness(12) };
        var tb = new WpfTextBox { Text = current, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };
        tb.Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13));
        tb.Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA));
        tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2B, 0x2E));
        tb.Padding = new Thickness(8, 5, 8, 5);
        tb.SelectAll();
        var btns = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new WpfButton { Content = "Annuler", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _2) => { DialogResult = false; Close(); };
        var ok = new WpfButton { Content = "? OK", Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        ok.Click += (_, _2) => { Result = tb.Text; DialogResult = true; Close(); };
        btns.Children.Add(cancel); btns.Children.Add(ok);
        root.Children.Add(tb); root.Children.Add(btns);
        Content = root;
        Loaded += (_, _2) => tb.Focus();
      }
    }
    private sealed class BigTextDialog : Window
    {
      private readonly WpfTextBox _tb;
      public string ResultText { get; private set; } = "";
      public BigTextDialog(string initial)
      {
        Title = "édition du texte"; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 900; Height = 600; MinWidth = 640; MinHeight = 440; ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x0B, 0x0B, 0x0C));
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(new TextBlock
        {
          Text = "Texte lu par le TTS — le média s'ajoute via la colonne ??",
          Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
          Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11
        });
        Grid.SetRow(bar, 0); root.Children.Add(bar);
        _tb = new WpfTextBox
        {
          Text = initial ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
          FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13,
          Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13)),
          Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA)),
          BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2B, 0x2E)),
          Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var scroll = new ScrollViewer { Content = _tb };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var buttons = new StackPanel
        {
          Orientation = System.Windows.Controls.Orientation.Horizontal,
          HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0)
        };
        var cancel = new WpfButton { Content = "Annuler", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _2) => { DialogResult = false; Close(); };
        var ok = new WpfButton { Content = "? Valider", Padding = new Thickness(16, 8, 16, 8), IsDefault = true };
        ok.Click += (_, _2) => { ResultText = _tb.Text ?? ""; DialogResult = true; Close(); };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        Content = root;
      }
    }
    private sealed class RelayCommand : ICommand
    {
      private readonly Action<object?> _run;
      public RelayCommand(Action<object?> run) { _run = run; }
      public bool CanExecute(object? p) => true;
      public void Execute(object? p) => _run(p);
      public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private Sequence? _recordingTarget;
    private void OnRecorderStateChanged(RecorderState state)
    {
      Dispatcher.Invoke(() =>
      {
        switch (state)
        {
          case RecorderState.Recording:
            ActiveLineLabel.Text = "? REC en cours... (cliquer ?? pour arrêter)";
            break;
          case RecorderState.Converting:
            ActiveLineLabel.Text = "? Conversion MP3...";
            break;
          case RecorderState.Playing:
            ActiveLineLabel.Text = "? Réécoute audio...";
            break;
          case RecorderState.Idle:
            ActiveLineLabel.Text = string.Empty;
            break;
        }
        GridItems.Items.Refresh();
      });
    }
    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      var projName = Path.GetFileNameWithoutExtension(_projectService.FilePath ?? "project");
      var safeId   = (seq.Id ?? Guid.NewGuid().ToString("N").Substring(0, 8))
                       .Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
      string audioDir;
      if (!string.IsNullOrWhiteSpace(_projectService.FilePath))
      {
        var jsonDir  = Path.GetDirectoryName(_projectService.FilePath) ?? string.Empty;
        var repoRoot = Path.GetDirectoryName(jsonDir) ?? jsonDir;
        audioDir = Path.Combine(repoRoot, "slides", projName, "audio");
      }
      else
      {
        audioDir = Path.Combine(Path.GetTempPath(), "ro_recorder_out");
      }
      Directory.CreateDirectory(audioDir);
      var mp3Path = Path.Combine(audioDir, projName + "_" + safeId + "_rec.mp3");
      var slidesDir  = _renderService.ResolveSlidesDirectory();
      var slidePath  = string.IsNullOrWhiteSpace(slidesDir) ? null
                         : _renderService.ResolveSlidePathFromNote(seq.Note, slidesDir);
      var win = new AudioRecorderWindow(_recorder, mp3Path, this,
        initialMp3:     null,
        sequenceText:   seq.Text,
        slideImagePath: slidePath);
      win.ShowDialog();
      if (win.RecorderResult?.Validated == true && !string.IsNullOrWhiteSpace(win.RecorderResult.Mp3Path))
      {
        _undoRedo.PushUndo(_items, "Enregistrer audio");
        seq.Mp3  = win.RecorderResult.Mp3Path;
        seq.Mode = "mp3";
        AutoSwitchSpeakerForMp3(seq);
        UpdateMp3Duration(seq);
        _projectService.SyncAndMarkDirty(_items);
        GridItems.Items.Refresh();
        Logger.Info("[Recorder] Assigned " + win.RecorderResult.Mp3Path + " -> seq " + seq.Id);
      }
    }
    private async System.Threading.Tasks.Task StopRecordingAndAssignAsync(Sequence seq)
    {
      await System.Threading.Tasks.Task.CompletedTask;
    }
    private void OnPlayAudioClick(object sender, RoutedEventArgs e)
    {
      if (sender is not FrameworkElement fe || fe.Tag is not Sequence seq) return;
      if (string.IsNullOrWhiteSpace(seq.Mp3) || !File.Exists(seq.Mp3))
      {
        System.Windows.MessageBox.Show(this, "Fichier audio introuvable.", "Réécoute",
          MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
      var projName = Path.GetFileNameWithoutExtension(_projectService.FilePath ?? "project");
      var safeId   = (seq.Id ?? Guid.NewGuid().ToString("N").Substring(0, 8))
                       .Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
      string audioDir;
      if (!string.IsNullOrWhiteSpace(_projectService.FilePath))
      {
        var jsonDir  = Path.GetDirectoryName(_projectService.FilePath) ?? string.Empty;
        var repoRoot = Path.GetDirectoryName(jsonDir) ?? jsonDir;
        audioDir = Path.Combine(repoRoot, "slides", projName, "audio");
      }
      else
      {
        audioDir = Path.Combine(Path.GetTempPath(), "ro_recorder_out");
      }
      Directory.CreateDirectory(audioDir);
      var mp3Path = Path.Combine(audioDir, projName + "_" + safeId + "_rec.mp3");
      var slidesDir  = _renderService.ResolveSlidesDirectory();
      var slidePath  = string.IsNullOrWhiteSpace(slidesDir) ? null
                         : _renderService.ResolveSlidePathFromNote(seq.Note, slidesDir);
      var win = new AudioRecorderWindow(_recorder, mp3Path, this,
        initialMp3:     seq.Mp3,
        sequenceText:   seq.Text,
        slideImagePath: slidePath);
      win.ShowDialog();
      if (win.RecorderResult?.Validated == true && !string.IsNullOrWhiteSpace(win.RecorderResult.Mp3Path))
      {
        _undoRedo.PushUndo(_items, "Enregistrer audio");
        seq.Mp3  = win.RecorderResult.Mp3Path;
        seq.Mode = "mp3";
        AutoSwitchSpeakerForMp3(seq);
        UpdateMp3Duration(seq);
        _projectService.SyncAndMarkDirty(_items);
        GridItems.Items.Refresh();
        Logger.Info("[Recorder] Assigned " + win.RecorderResult.Mp3Path + " -> seq " + seq.Id);
      }
    }
    private void OnStopAudioPlayback(object sender, RoutedEventArgs e)
    {
      _recorder.StopPlayback();
    }
    private void UpdateMp3Duration(Sequence seq)
    {
      if (string.IsNullOrWhiteSpace(seq.Mp3) || !File.Exists(seq.Mp3)) return;
      try
      {
        using var reader = new NAudio.Wave.Mp3FileReader(seq.Mp3);
        seq.EstimatedDuration = reader.TotalTime;
        seq.DurationIsExact   = true;
      }
      catch (Exception ex) { Logger.Warn("[Recorder] UpdateMp3Duration: " + ex.Message); }
    }
    private static void CleanupOldPreviews()
    {
      try
      {
        var dir = Path.Combine(Path.GetTempPath(), "ro_preview"); if (!Directory.Exists(dir)) return;
        var cutoff = DateTime.Now.AddHours(-24);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
          try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
      }
      catch { }
    }
    public sealed class SceneDisplayItem
    {
      public Scene  Scene    { get; }
      public string Name     => Scene.Name;
      public string Id       => Scene.Id;
      public bool   SlidesOk { get; }
      public string SlideIcon    => SlidesOk ? "✓" : "✗";
      public string SlideTooltip => SlidesOk
        ? "Slides présentes — prêt à exporter"
        : "Slides manquantes — importer le PDF avant d'exporter";
      public System.Windows.Media.Brush SlideColor => SlidesOk
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4D, 0xC9, 0x3C))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x41, 0x18));
      public SceneDisplayItem(Scene scene, bool slidesOk)
      { Scene = scene; SlidesOk = slidesOk; }
    }
  }
  public sealed class RowIndexConverter : System.Windows.Data.IValueConverter
  {
    public static readonly RowIndexConverter Instance = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
      => value is DataGridRow row ? (row.GetIndex() + 1).ToString() : "";
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class PairBadgeConverter : System.Windows.Data.IValueConverter
  {
    public static readonly PairBadgeConverter Instance = new();
    public static readonly System.Collections.Generic.Dictionary<string, int> PairNumbers = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not true) return "";
      return "??";
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class ModeToBoolConverter : System.Windows.Data.IValueConverter
  {
    public static readonly ModeToBoolConverter Mp3Visibility  = new("mp3",  true);
    public static readonly ModeToBoolConverter TtsVisibility  = new("tts",  true);
    public static readonly ModeToBoolConverter TtsPickerVisibility = new("tts", false);
    private readonly string _mode;
    private readonly bool   _showWhenMatch;
    private ModeToBoolConverter(string mode, bool showWhenMatch)
    { _mode = mode; _showWhenMatch = showWhenMatch; }
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
    {
      bool isMatch = string.Equals(v as string, _mode, StringComparison.OrdinalIgnoreCase);
      bool show    = _showWhenMatch ? isMatch : !isMatch;
      return show ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
  public sealed class PairColorConverter : System.Windows.Data.IValueConverter
  {
    public static readonly PairColorConverter Instance = new();
    private static readonly System.Windows.Media.Color[] _palette =
    {
      System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0x80, 0x00),
      System.Windows.Media.Color.FromArgb(0xCC, 0x00, 0xCC, 0xFF),
      System.Windows.Media.Color.FromArgb(0xCC, 0xDD, 0xDD, 0x00),
      System.Windows.Media.Color.FromArgb(0xCC, 0x00, 0xEE, 0x80),
      System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0x40, 0x80),
      System.Windows.Media.Color.FromArgb(0xCC, 0x80, 0x00, 0xFF),
      System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xA0, 0x00),
      System.Windows.Media.Color.FromArgb(0xCC, 0x00, 0xEE, 0xEE),
      System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0x60, 0x20),
      System.Windows.Media.Color.FromArgb(0xCC, 0x40, 0xCC, 0x40),
      System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0x20, 0xFF),
      System.Windows.Media.Color.FromArgb(0xCC, 0x60, 0xA0, 0xFF),
    };
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string pairId || string.IsNullOrWhiteSpace(pairId))
        return System.Windows.Media.Brushes.Transparent;
      if (!PairBadgeConverter.PairNumbers.TryGetValue(pairId, out var num))
        return System.Windows.Media.Brushes.Transparent;
      var col = _palette[(num - 1) % _palette.Length];
      return new System.Windows.Media.SolidColorBrush(col);
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class SpeakerToBrushConverter : System.Windows.Data.IValueConverter
  {
    public static readonly SpeakerToBrushConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush
      _bot1 = new(System.Windows.Media.Color.FromArgb(0x14, 0xFF, 0x00, 0xFF)),
      _bot2 = new(System.Windows.Media.Color.FromArgb(0x14, 0x00, 0xFF, 0xFF)),
      _you  = new(System.Windows.Media.Color.FromArgb(0x20, 0xFF, 0xD4, 0x00));
    static SpeakerToBrushConverter()
    { _bot1.Freeze(); _bot2.Freeze(); _you.Freeze(); }
    public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
      => (v as string)?.ToLowerInvariant() switch
      {
        "bot1" => _bot1, "bot2" => _bot2, "you" => _you,
        _ => System.Windows.Media.Brushes.Transparent
      };
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
  public sealed class MediaThumbnailConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaThumbnailConverter Instance = new();
    public static string FfmpegExe { get; set; } = "ffmpeg";
    public static event Action<string>? ThumbnailExtracted;
    private static readonly System.Collections.Generic.Dictionary<string, BitmapImage?> _cache = new();
    private static readonly System.Collections.Generic.HashSet<string> _pending = new();
    private static readonly System.Collections.Generic.HashSet<string> _imgExts =
      new() { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly System.Collections.Generic.HashSet<string> _vidExts =
      new() { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".flv", ".wmv" };
    public object? Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return null;
      var ext = Path.GetExtension(path).ToLowerInvariant();
      if (_imgExts.Contains(ext))
      {
        if (_cache.TryGetValue(path, out var ci)) return ci;
        try
        {
          var bmp = new BitmapImage();
          bmp.BeginInit();
          bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
          bmp.UriSource = new Uri(path, UriKind.Absolute);
          bmp.DecodePixelWidth = 80;
          bmp.EndInit(); bmp.Freeze();
          _cache[path] = bmp;
          return bmp;
        }
        catch { _cache[path] = null; return null; }
      }
      if (_vidExts.Contains(ext))
      {
        if (_cache.TryGetValue(path, out var cv)) return cv;
        lock (_pending)
        {
          if (_pending.Contains(path)) return null;
          _pending.Add(path);
        }
        Task.Run(() => ExtractVideoThumbnail(path));
        return null;
      }
      return null;
    }
    private static void ExtractVideoThumbnail(string path)
    {
      var hash = Math.Abs(path.GetHashCode()).ToString("X8");
      var outPng = Path.Combine(Path.GetTempPath(), $"ro_vth_{hash}.png");
      try
      {
        if (!File.Exists(outPng))
        {
          var psi = new System.Diagnostics.ProcessStartInfo
          {
            FileName  = FfmpegExe,
            Arguments = $"-y -ss 1 -i \"{path}\" -vframes 1 -q:v 5 -vf scale=80:-1 \"{outPng}\"",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true
          };
          using var proc = System.Diagnostics.Process.Start(psi);
          if (proc != null)
          {
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(6000);
          }
        }
        BitmapImage? bmp = null;
        if (File.Exists(outPng))
        {
          System.Windows.Application.Current?.Dispatcher.Invoke(() =>
          {
            try
            {
              var b = new BitmapImage();
              b.BeginInit();
              b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
              b.UriSource = new Uri(outPng, UriKind.Absolute);
              b.DecodePixelWidth = 80;
              b.EndInit(); b.Freeze();
              bmp = b;
            }
            catch { }
          });
        }
        _cache[path] = bmp;
      }
      catch { _cache[path] = null; }
      finally
      {
        lock (_pending) { _pending.Remove(path); }
        ThumbnailExtracted?.Invoke(path);
      }
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
  public sealed class MediaHasImagePreviewConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaHasImagePreviewConverter Instance = new();
    private static readonly System.Collections.Generic.HashSet<string> _thumbExts =
      new() { ".png", ".jpg", ".jpeg", ".webp", ".bmp",
              ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".flv", ".wmv" };
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Visibility.Collapsed;
      return _thumbExts.Contains(Path.GetExtension(path).ToLowerInvariant())
        ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class MediaNoImagePreviewConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaNoImagePreviewConverter Instance = new();
    private static readonly System.Collections.Generic.HashSet<string> _thumbExts =
      new() { ".png", ".jpg", ".jpeg", ".webp", ".bmp",
              ".mp4", ".mkv", ".webm", ".mov", ".avi", ".m4v", ".flv", ".wmv" };
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Visibility.Visible;
      return _thumbExts.Contains(Path.GetExtension(path).ToLowerInvariant())
        ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class MediaBadgeIconConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaBadgeIconConverter Instance = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string path || string.IsNullOrWhiteSpace(path)) return "+";
      return Path.GetExtension(path).ToLowerInvariant() switch
      {
        ".gif"  => "??",
        ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv" => "??",
        _ => "??"
      };
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class MediaFileNameConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaFileNameConverter Instance = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
      => value is string path && !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : "";
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
  public sealed class MediaBadgeConverter : System.Windows.Data.IValueConverter
  {
    public static readonly MediaBadgeConverter Instance = new();
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      if (value is not string path || string.IsNullOrWhiteSpace(path)) return "+ média";
      var ext = Path.GetExtension(path).ToLowerInvariant();
      return (ext switch { ".gif" => "??", ".mp4" or ".webm" or ".mov" => "??", _ => "??" })
           + " " + Path.GetFileName(path);
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
  }
}