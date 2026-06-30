using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application      = System.Windows.Application;
using MessageBox       = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage  = System.Windows.MessageBoxImage;
using Point            = System.Windows.Point;
using Color            = System.Windows.Media.Color;
using SolidColorBrush  = System.Windows.Media.SolidColorBrush;
using DataObject       = System.Windows.DataObject;
using DragDropEffects  = System.Windows.DragDropEffects;
using DragDrop         = System.Windows.DragDrop;
using MouseEventArgs   = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using DragEventArgs    = System.Windows.DragEventArgs;
namespace RoleplayOverlay
{
  public partial class ManifestEditorWindow : Window
  {
    private readonly Project        _project;
    private readonly string         _projectPath;
    private          RenderManifest _manifest;
    private          string?        _manifestPath;
    private readonly ObservableCollection<SeqViewModel>  _seqPool   = new();
    private readonly ObservableCollection<SegViewModel>  _segments  = new();
    private SeqViewModel? _dragging;
    private Point         _dragStart;
    public RenderOptions? RenderOptions { get; set; }
    public ManifestEditorWindow(
      Project        project,
      string         projectPath,
      RenderManifest manifest,
      string?        manifestPath)
    {
      _project      = project;
      _projectPath  = projectPath;
      _manifest     = manifest;
      _manifestPath = manifestPath;
      InitializeComponent();
      SeqPool.ItemsSource     = _seqPool;
      SegmentList.ItemsSource = _segments;
      SlidesDirBox.Text = manifest.SlidesDirectory;
      LoadSeqPool();
      LoadSegments();
      UpdateSummary();
      ManifestPathLabel.Text = _manifestPath ?? "manifest non sauvegardé";
      StatusLabel.Text       = "Glisser une séquence vers un segment · Cliquer sur 🖼 pour assigner une slide.";
    }
    private void LoadSeqPool()
    {
      _seqPool.Clear();
      var scene = _project.CurrentScene;
      if (scene?.Sequences == null) return;
      var assignedIds = _manifest.Segments
        .SelectMany(s => s.SequenceIds)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      int idx = 0;
      foreach (var seq in scene.Sequences)
      {
        var vm = new SeqViewModel(seq, idx++);
        vm.IsAssigned = !string.IsNullOrWhiteSpace(seq.Id) && assignedIds.Contains(seq.Id);
        _seqPool.Add(vm);
      }
      SeqCountLabel.Text = $"{_seqPool.Count}";
    }
    private void LoadSegments()
    {
      _segments.Clear();
      var seqById = _project.CurrentScene?.Sequences
        .Where(s => !string.IsNullOrWhiteSpace(s.Id))
        .ToDictionary(s => s.Id!, StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, Sequence>();
      foreach (var seg in _manifest.Segments)
      {
        var vm = new SegViewModel(seg.Label, seg.SlidePath, seg.MinDurationSec);
        vm.MediaPath        = seg.MediaPath ?? "";
        vm.MediaScale       = seg.MediaScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        vm.MediaSpeed       = seg.MediaSpeed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        vm.MediaLoop        = seg.MediaLoop;
        vm.MediaBorderColor = seg.MediaBorderColor;
        vm.MediaBorderPx    = seg.MediaBorderPx.ToString();
        vm.MediaShadowBlur  = seg.MediaShadowBlur.ToString();
        vm.MediaShadowAlpha = seg.MediaShadowAlpha.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var id in seg.SequenceIds)
        {
          if (seqById.TryGetValue(id, out var seq))
          {
            var sv = _seqPool.FirstOrDefault(s => s.Id == id);
            vm.AssignedSequences.Add(sv ?? new SeqViewModel(seq, -1));
          }
        }
        ResolveSlide(vm);
        _segments.Add(vm);
      }
    }
    private void OnBrowseSlidesDir(object sender, RoutedEventArgs e)
    {
      using var dlg = new System.Windows.Forms.FolderBrowserDialog
      {
        Description = "Dossier contenant les slides PNG/JPG"
      };
      if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
      {
        SlidesDirBox.Text = dlg.SelectedPath;
        _manifest.SlidesDirectory = dlg.SelectedPath;
      }
    }
    private void OnScanSlides(object sender, RoutedEventArgs e)
    {
      var dir = SlidesDirBox.Text?.Trim();
      if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
      {
        Status("Dossier introuvable. Vérifier le chemin.");
        return;
      }
      _manifest.SlidesDirectory = dir;
      var exts  = new[] { "*.png", "*.jpg", "*.jpeg" };
      var files = exts
        .SelectMany(ext => Directory.GetFiles(dir, ext))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();
      if (files.Count == 0)
      {
        Status($"Aucun fichier PNG/JPG trouvé dans {dir}");
        return;
      }
      for (int i = 0; i < files.Count; i++)
      {
        var relPath = Path.GetFileName(files[i]);
        if (i < _segments.Count)
        {
          _segments[i].SlidePath = relPath;
          ResolveSlide(_segments[i]);
        }
        else
        {
          var vm = new SegViewModel($"Segment {_segments.Count + 1:D3}", relPath, 0);
          ResolveSlide(vm);
          _segments.Add(vm);
        }
      }
      UpdateSummary();
      Status($"{files.Count} slide(s) scannée(s) et assignée(s).");
    }
    private void OnAddSegment(object sender, RoutedEventArgs e)
    {
      var vm = new SegViewModel($"Segment {_segments.Count + 1:D3}", "", 0);
      _segments.Add(vm);
      UpdateSummary();
    }
    private void OnSegUp(object sender, RoutedEventArgs e)
    {
      var sel = _segments.FirstOrDefault(s => s.IsSelected);
      if (sel == null) return;
      int i = _segments.IndexOf(sel);
      if (i > 0) { _segments.Move(i, i - 1); }
    }
    private void OnSegDown(object sender, RoutedEventArgs e)
    {
      var sel = _segments.FirstOrDefault(s => s.IsSelected);
      if (sel == null) return;
      int i = _segments.IndexOf(sel);
      if (i < _segments.Count - 1) { _segments.Move(i, i + 1); }
    }
    private void OnSegDelete(object sender, RoutedEventArgs e)
    {
      var sel = _segments.FirstOrDefault(s => s.IsSelected);
      if (sel == null) return;
      foreach (var sv in sel.AssignedSequences)
        sv.IsAssigned = false;
      _segments.Remove(sel);
      UpdateSummary();
    }
    private void OnSegmentCardClick(object sender, MouseButtonEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel vm)
        SelectSegment(vm);
    }
    private void SelectSegment(SegViewModel vm)
    {
      foreach (var s in _segments) s.IsSelected = false;
      vm.IsSelected = true;
      if (vm.SlideThumbnail != null)
      {
        PreviewImage.Source     = vm.SlideThumbnail;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
      }
      else
      {
        PreviewImage.Source     = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
      }
      PreviewFileName.Text  = vm.SlideFileName;
      SegSlidePathBox.Text  = vm.SlidePath;
      SegSeqCountLabel.Text = $"{vm.AssignedSequences.Count} séquence(s)";
      SegDurationLabel.Text = "calculé au rendu";
    }
    private void OnPickSlide(object sender, RoutedEventArgs e)
    {
      SegViewModel? vm = null;
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel v)
        vm = v;
      vm ??= _segments.FirstOrDefault(s => s.IsSelected);
      if (vm == null) return;
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title            = "Choisir la slide pour ce segment",
        Filter           = "Images|*.png;*.jpg;*.jpeg|Tous|*.*",
        InitialDirectory = string.IsNullOrWhiteSpace(_manifest.SlidesDirectory)
          ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
          : _manifest.SlidesDirectory
      };
      if (ofd.ShowDialog() != true) return;
      var slidesDir = _manifest.SlidesDirectory;
      if (!string.IsNullOrWhiteSpace(slidesDir) &&
          ofd.FileName.StartsWith(slidesDir, StringComparison.OrdinalIgnoreCase))
      {
        vm.SlidePath = Path.GetRelativePath(slidesDir, ofd.FileName);
      }
      else
      {
        vm.SlidePath = ofd.FileName;
      }
      ResolveSlide(vm);
      SelectSegment(vm);
      UpdateSummary();
    }
    private void ResolveSlide(SegViewModel vm)
    {
      var path = vm.SlidePath;
      if (string.IsNullOrWhiteSpace(path)) return;
      if (!Path.IsPathRooted(path))
        path = Path.Combine(_manifest.SlidesDirectory, path);
      vm.SlideFileName = Path.GetFileName(vm.SlidePath);
      vm.HasSlide      = File.Exists(path);
      if (!vm.HasSlide) return;
      try
      {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption     = BitmapCacheOption.OnLoad;
        bmp.UriSource       = new Uri(path, UriKind.Absolute);
        bmp.DecodePixelWidth = 280;
        bmp.EndInit();
        bmp.Freeze();
        vm.SlideThumbnail = bmp;
      }
      catch { vm.SlideThumbnail = null; }
    }
    private void OnSegSlidePathChanged(object sender, TextChangedEventArgs e)
    {
      var sel = _segments.FirstOrDefault(s => s.IsSelected);
      if (sel == null) return;
      sel.SlidePath = SegSlidePathBox.Text;
      ResolveSlide(sel);
      SelectSegment(sel);
    }
    private void OnPickMedia(object sender, RoutedEventArgs e)
    {
      SegViewModel? vm = null;
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel v)
        vm = v;
      vm ??= _segments.FirstOrDefault(s => s.IsSelected);
      if (vm == null) return;
      var ofd = new Microsoft.Win32.OpenFileDialog
      {
        Title  = "Choisir le média pour ce segment",
        Filter = "Média|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.webm;*.mov|Images|*.png;*.jpg;*.jpeg|GIF|*.gif|Vidéo|*.mp4;*.webm;*.mov|Tous|*.*",
      };
      if (ofd.ShowDialog() != true) return;
      vm.MediaPath = ofd.FileName;
      Status($"Média assigné : {Path.GetFileName(ofd.FileName)}");
    }
    private void OnClearMedia(object sender, RoutedEventArgs e)
    {
      SegViewModel? vm = null;
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel v)
        vm = v;
      vm ??= _segments.FirstOrDefault(s => s.IsSelected);
      if (vm == null) return;
      vm.MediaPath = "";
      Status("Média retiré du segment.");
    }
    private void OnAutoAssign(object sender, RoutedEventArgs e)
    {
      foreach (var seg in _segments) seg.AssignedSequences.Clear();
      foreach (var sv in _seqPool)   sv.IsAssigned = false;
      var scene = _project.CurrentScene;
      if (scene?.Sequences == null) return;
      var seqs = scene.Sequences.ToList();
      while (_segments.Count < seqs.Count)
      {
        var slideName = _segments.Count < GetSlideFiles().Count
          ? Path.GetFileName(GetSlideFiles()[_segments.Count])
          : "";
        var newSeg = new SegViewModel($"Segment {_segments.Count + 1:D3}", slideName, 0);
        ResolveSlide(newSeg);
        _segments.Add(newSeg);
      }
      for (int i = 0; i < seqs.Count && i < _segments.Count; i++)
      {
        var sv = _seqPool.FirstOrDefault(s => s.Sequence == seqs[i]);
        if (sv == null) continue;
        _segments[i].AssignedSequences.Clear();
        _segments[i].AssignedSequences.Add(sv);
        sv.IsAssigned = true;
      }
      UpdateSummary();
      Status($"Auto-assignation : {seqs.Count} séquence(s) assignée(s).");
    }
    private List<string> GetSlideFiles()
    {
      var dir = _manifest.SlidesDirectory;
      if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        return new List<string>();
      return new[] { "*.png", "*.jpg", "*.jpeg" }
        .SelectMany(ext => Directory.GetFiles(dir, ext))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }
    private void OnUnassignSeq(object sender, RoutedEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.Tag is SeqViewModel sv)
      {
        foreach (var seg in _segments)
        {
          if (seg.AssignedSequences.Remove(sv))
          {
            sv.IsAssigned = false;
            break;
          }
        }
        UpdateSummary();
      }
    }
    private void OnSeqPoolMouseDown(object sender, MouseButtonEventArgs e)
    {
      if (SeqPool.SelectedItem is SeqViewModel sv)
      {
        _dragging  = sv;
        _dragStart = e.GetPosition(null);
      }
    }
    private void OnSeqPoolMouseMove(object sender, MouseEventArgs e)
    {
      if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
      var pos  = e.GetPosition(null);
      var diff = _dragStart - pos;
      if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
          Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        return;
      var data = new DataObject("SeqViewModel", _dragging);
      DragDrop.DoDragDrop(SeqPool, data, DragDropEffects.Move);
      _dragging = null;
    }
    private void OnSegmentDragOver(object sender, DragEventArgs e)
    {
      if (!e.Data.GetDataPresent("SeqViewModel"))
      {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        return;
      }
      e.Effects = DragDropEffects.Move;
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel vm)
        vm.IsDragOver = true;
      e.Handled = true;
    }
    private void OnSegmentDragLeave(object sender, DragEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel vm)
        vm.IsDragOver = false;
    }
    private void OnSegmentDrop(object sender, DragEventArgs e)
    {
      if (sender is FrameworkElement fe && fe.Tag is SegViewModel segVm)
        segVm.IsDragOver = false;
      if (!e.Data.GetDataPresent("SeqViewModel")) return;
      if (e.Data.GetData("SeqViewModel") is not SeqViewModel sv) return;
      var target = FindSegViewModel(sender as DependencyObject);
      if (target == null) return;
      if (target.AssignedSequences.Contains(sv)) return;
      foreach (var seg in _segments)
      {
        if (seg != target && seg.AssignedSequences.Contains(sv))
        {
          seg.AssignedSequences.Remove(sv);
          break;
        }
      }
      target.AssignedSequences.Add(sv);
      sv.IsAssigned = true;
      UpdateSummary();
      Status($"« {sv.PreviewText} » ajoutée au segment « {target.Label} ».");
      e.Handled = true;
    }
    private SegViewModel? FindSegViewModel(DependencyObject? element)
    {
      while (element != null)
      {
        if (element is FrameworkElement fe && fe.Tag is SegViewModel vm)
          return vm;
        element = VisualTreeHelper.GetParent(element);
      }
      return null;
    }
    private void OnSaveManifest(object sender, RoutedEventArgs e)
    {
      if (string.IsNullOrWhiteSpace(_manifestPath))
      {
        var sfd = new Microsoft.Win32.SaveFileDialog
        {
          Title            = "Sauvegarder le manifest",
          Filter           = "Manifest JSON|*.render.json",
          FileName         = Path.GetFileNameWithoutExtension(_projectPath) + ".render.json",
          InitialDirectory = Path.GetDirectoryName(_projectPath)
        };
        if (sfd.ShowDialog() != true) return;
        _manifestPath = sfd.FileName;
      }
      BuildManifestFromVm();
      try
      {
        _manifest.Save(_manifestPath);
        ManifestPathLabel.Text = _manifestPath;
        Status($"Manifest sauvegardé → {Path.GetFileName(_manifestPath)}");
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, $"Erreur de sauvegarde :\n{ex.Message}", "Erreur",
          MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }
    private void BuildManifestFromVm()
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      _manifest.SlidesDirectory = SlidesDirBox.Text?.Trim() ?? "";
      _manifest.ProjectPath     = _projectPath;
      _manifest.Segments.Clear();
      foreach (var seg in _segments)
      {
        var ms = new ManifestSegment
        {
          Label          = seg.Label,
          SlidePath      = seg.SlidePath,
          MinDurationSec = double.TryParse(seg.MinDuration, out var d) ? d : 0,
          SequenceIds    = seg.AssignedSequences
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .Select(s => s.Id!)
            .ToList(),
          MediaPath        = string.IsNullOrWhiteSpace(seg.MediaPath) ? null : seg.MediaPath,
          MediaScale       = float.TryParse(seg.MediaScale, System.Globalization.NumberStyles.Float, inv, out var sc) ? sc : 0.80f,
          MediaSpeed       = float.TryParse(seg.MediaSpeed, System.Globalization.NumberStyles.Float, inv, out var sp) ? sp : 1.0f,
          MediaLoop        = seg.MediaLoop,
          MediaBorderColor = seg.MediaBorderColor ?? "#FFFFFF",
          MediaBorderPx    = int.TryParse(seg.MediaBorderPx, out var bp) ? bp : 6,
          MediaShadowBlur  = int.TryParse(seg.MediaShadowBlur, out var sb) ? sb : 18,
          MediaShadowAlpha = float.TryParse(seg.MediaShadowAlpha, System.Globalization.NumberStyles.Float, inv, out var sa) ? sa : 0.55f,
        };
        _manifest.Segments.Add(ms);
      }
    }
    private async void OnExportMp4(object sender, RoutedEventArgs e)
    {
      OnSaveManifest(sender, e);
      if (string.IsNullOrWhiteSpace(_manifestPath)) return;
      var options = RenderOptions ?? new RenderOptions
      {
        OutputPath   = Path.Combine(
          System.IO.Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(_projectPath) ?? ".") ?? ".",
            "exports"),
          Path.GetFileNameWithoutExtension(_projectPath) + "_" +
          DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4"),
        TempDirectory       = Path.Combine(Path.GetTempPath(), "ro_render"),
        VideoWidth          = 1920,
        VideoHeight         = 1080,
        Crf                 = 18,
        BurnSubtitles       = true,
        FontSize            = 34,
        FontPath            = GetDefaultFontPath(),
        CleanupTempOnSuccess = true
      };
      Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
      await RenderLauncher.LaunchAsync(this, _manifest, _project, options);
    }
    private void UpdateSummary()
    {
      int totalSeqs      = _seqPool.Count;
      int assignedCount  = _seqPool.Count(s => s.IsAssigned);
      int unassigned     = totalSeqs - assignedCount;
      int segCount       = _segments.Count;
      SummaryLabel.Text    = $"{segCount} segment(s) · {assignedCount} séquence(s) assignée(s) / {totalSeqs}";
      UnassignedLabel.Text = unassigned > 0
        ? $"⚠ {unassigned} séquence(s) non assignée(s)"
        : "✅ Toutes les séquences sont assignées";
      UnassignedLabel.Foreground = unassigned > 0
        ? new SolidColorBrush(Color.FromRgb(0xE8, 0x41, 0x18))
        : new SolidColorBrush(Color.FromRgb(0x4D, 0xC9, 0x3C));
    }
    private void Status(string msg)
      => StatusLabel.Text = msg;
    private static string? GetDefaultFontPath()
    {
      var candidates = new[] { @"C:\Windows\Fonts\segoeui.ttf", @"C:\Windows\Fonts\arial.ttf" };
      return candidates.FirstOrDefault(File.Exists);
    }
  }
  public sealed class SeqViewModel : INotifyPropertyChanged
  {
    public Sequence Sequence { get; }
    public int      Index    { get; }
    public string?  Id          => Sequence.Id;
    public string?  Speaker     => Sequence.Speaker;
    public string?  Mode        => Sequence.Mode;
    public string   PreviewText => !string.IsNullOrWhiteSpace(Sequence.Text)
      ? (Sequence.Text.Length > 60 ? Sequence.Text[..60] + "…" : Sequence.Text)
      : (!string.IsNullOrWhiteSpace(Sequence.Mp3)
          ? Path.GetFileName(Sequence.Mp3)
          : "(vide)");
    public string   ShortId     => string.IsNullOrWhiteSpace(Sequence.Id)
      ? "(sans ID)"
      : Sequence.Id[..Math.Min(8, Sequence.Id.Length)] + "…";
    private bool _isAssigned;
    public bool IsAssigned
    {
      get => _isAssigned;
      set { _isAssigned = value; OnPropertyChanged(); }
    }
    public SeqViewModel(Sequence seq, int idx)
    {
      Sequence = seq;
      Index    = idx;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
      => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
  }
  public sealed class SegViewModel : INotifyPropertyChanged
  {
    private string  _label       = "";
    private string  _slidePath   = "";
    private string  _slideFileName = "";
    private string  _minDuration = "0";
    private bool    _hasSlide;
    private bool    _isSelected;
    private bool    _isDragOver;
    private BitmapSource? _slideThumbnail;
    private string  _mediaPath        = "";
    private string  _mediaScale       = "0.80";
    private string  _mediaSpeed       = "1.0";
    private bool    _mediaLoop        = true;
    private string  _mediaBorderColor = "#FFFFFF";
    private string  _mediaBorderPx    = "6";
    private string  _mediaShadowBlur  = "18";
    private string  _mediaShadowAlpha = "0.55";
    public ObservableCollection<SeqViewModel> AssignedSequences { get; } = new();
    public string Label
    {
      get => _label;
      set { _label = value; OnPropertyChanged(); }
    }
    public string SlidePath
    {
      get => _slidePath;
      set { _slidePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SlideFileName)); }
    }
    public string SlideFileName
    {
      get => _slideFileName;
      set { _slideFileName = value; OnPropertyChanged(); }
    }
    public string MinDuration
    {
      get => _minDuration;
      set { _minDuration = value; OnPropertyChanged(); }
    }
    public bool HasSlide
    {
      get => _hasSlide;
      set { _hasSlide = value; OnPropertyChanged(); }
    }
    public bool IsSelected
    {
      get => _isSelected;
      set { _isSelected = value; OnPropertyChanged(); }
    }
    public bool IsDragOver
    {
      get => _isDragOver;
      set { _isDragOver = value; OnPropertyChanged(); }
    }
    public BitmapSource? SlideThumbnail
    {
      get => _slideThumbnail;
      set { _slideThumbnail = value; OnPropertyChanged(); }
    }
    public string MediaPath
    {
      get => _mediaPath;
      set { _mediaPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMedia)); OnPropertyChanged(nameof(MediaFileName)); }
    }
    public bool HasMedia => !string.IsNullOrWhiteSpace(_mediaPath) && File.Exists(_mediaPath);
    public string MediaFileName => string.IsNullOrWhiteSpace(_mediaPath) ? "" : Path.GetFileName(_mediaPath);
    public string MediaScale
    {
      get => _mediaScale;
      set { _mediaScale = value; OnPropertyChanged(); }
    }
    public string MediaSpeed
    {
      get => _mediaSpeed;
      set { _mediaSpeed = value; OnPropertyChanged(); }
    }
    public bool MediaLoop
    {
      get => _mediaLoop;
      set { _mediaLoop = value; OnPropertyChanged(); }
    }
    public string MediaBorderColor
    {
      get => _mediaBorderColor;
      set { _mediaBorderColor = value; OnPropertyChanged(); }
    }
    public string MediaBorderPx
    {
      get => _mediaBorderPx;
      set { _mediaBorderPx = value; OnPropertyChanged(); }
    }
    public string MediaShadowBlur
    {
      get => _mediaShadowBlur;
      set { _mediaShadowBlur = value; OnPropertyChanged(); }
    }
    public string MediaShadowAlpha
    {
      get => _mediaShadowAlpha;
      set { _mediaShadowAlpha = value; OnPropertyChanged(); }
    }
    public SegViewModel(string label, string slidePath, double minDuration)
    {
      _label       = label;
      _slidePath   = slidePath;
      _minDuration = minDuration.ToString("G");
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
      => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
  }
  public sealed class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
  {
    public static readonly BoolToVisibilityConverter Collapsed = new() { _invert = true };
    private bool _invert;
    public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c)
    {
      bool b = value is bool bv && bv;
      if (_invert) b = !b;
      return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c)
      => throw new NotSupportedException();
  }
}