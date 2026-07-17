using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shell;
using Application         = System.Windows.Application;
using MessageBox          = System.Windows.MessageBox;
using MessageBoxButton    = System.Windows.MessageBoxButton;
using MessageBoxImage     = System.Windows.MessageBoxImage;
using MessageBoxResult    = System.Windows.MessageBoxResult;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment   = System.Windows.VerticalAlignment;
using ProgressBar         = System.Windows.Controls.ProgressBar;
using Button              = System.Windows.Controls.Button;
using Grid                = System.Windows.Controls.Grid;
using GridLength          = System.Windows.GridLength;
using GridUnitType        = System.Windows.GridUnitType;
using RowDefinition       = System.Windows.Controls.RowDefinition;
using StackPanel          = System.Windows.Controls.StackPanel;
using DockPanel           = System.Windows.Controls.DockPanel;
using Dock                = System.Windows.Controls.Dock;
using ScrollViewer        = System.Windows.Controls.ScrollViewer;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using TextBlock           = System.Windows.Controls.TextBlock;
using TextWrapping        = System.Windows.TextWrapping;
using Thickness           = System.Windows.Thickness;
using Visibility          = System.Windows.Visibility;
using FontWeights         = System.Windows.FontWeights;
using VisualTreeHelper    = System.Windows.Media.VisualTreeHelper;
namespace RoleplayOverlay
{
  public static class RenderLauncher
  {
    public static async Task<RenderResult> LaunchAsync(
      Window         owner,
      RenderManifest manifest,
      Project        project,
      RenderOptions  options,
      bool           batchMode  = false,
      Action<RenderProgress>? onProgress = null)
    {
      RenderProgressDialog? dlg = null;
      if (!batchMode)
      {
        dlg = new RenderProgressDialog(owner);
        dlg.Show();
      }
      var cts = new CancellationTokenSource();
      if (dlg != null) dlg.Cancelled += () => cts.Cancel();
      var progress = new Progress<RenderProgress>(p =>
      {
        dlg?.UpdateProgress(p);
        onProgress?.Invoke(p);
        if (!batchMode && owner.TaskbarItemInfo != null)
        {
          owner.TaskbarItemInfo.ProgressState = p.Phase == RenderPhase.Done
            ? TaskbarItemProgressState.None
            : TaskbarItemProgressState.Normal;
          owner.TaskbarItemInfo.ProgressValue = p.Percent / 100.0;
        }
      });
      RenderResult result;
      try
      {
        result = await Task.Run(
          () => RenderPipeline.BuildAsync(manifest, project, options, progress, cts.Token),
          cts.Token);
      }
      catch (OperationCanceledException)
      {
        result = new RenderResult(false, null, "Annulé.", TimeSpan.Zero, 0);
      }
      dlg?.Close();
      if (!batchMode)
      {
        if (result.Success)
        {
          var msg = $"Rendu terminé en {result.ElapsedTime:mm\\:ss}.\n" +
                    $"Fichier : {result.OutputPath}\n" +
                    $"Segments : {result.SegmentCount}";
          var choice = MessageBox.Show(owner, msg, "Rendu MP4 terminé",
            MessageBoxButton.YesNo, MessageBoxImage.Information,
            MessageBoxResult.Yes);
          if (choice == MessageBoxResult.Yes && File.Exists(result.OutputPath!))
          {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{result.OutputPath}\""); }
            catch { }
          }
        }
        else if (!string.IsNullOrWhiteSpace(result.ErrorMessage)
                 && result.ErrorMessage != "Annulé.")
        {
          MessageBox.Show(owner,
            $"Le rendu a échoué :\n\n{result.ErrorMessage}\n\n" +
            "Vérifiez que ffmpeg.exe est dans le PATH ou configurez FfmpegBinaryPath dans les options.",
            "Erreur de rendu", MessageBoxButton.OK, MessageBoxImage.Error);
        }
      }
      return result;
    }
    public static RenderManifest LoadOrScaffoldManifest(
      string  projectJsonPath,
      Project project,
      string? slidesDirectoryOverride = null)
    {
      var activeScene  = project.CurrentScene;
      var manifestPath = GetManifestPath(projectJsonPath, project, activeScene);
      string slidesDir;
      if (!string.IsNullOrWhiteSpace(slidesDirectoryOverride)
          && Directory.Exists(slidesDirectoryOverride))
      {
        slidesDir = slidesDirectoryOverride;
      }
      else if (activeScene != null
               && !string.IsNullOrWhiteSpace(activeScene.SlidesDirectory)
               && Directory.Exists(activeScene.SlidesDirectory))
      {
        slidesDir = activeScene.SlidesDirectory;
      }
      else
      {
        slidesDir = ResolveSlidesDirForProject(projectJsonPath);
      }
      if (File.Exists(manifestPath))
      {
        try
        {
          var existing = RenderManifest.Load(manifestPath);
          if (string.IsNullOrWhiteSpace(slidesDirectoryOverride)
              && (activeScene == null || string.IsNullOrWhiteSpace(activeScene.SlidesDirectory))
              && !string.IsNullOrWhiteSpace(existing.SlidesDirectory)
              && Directory.Exists(existing.SlidesDirectory))
          {
            slidesDir = existing.SlidesDirectory;
          }
        }
        catch { }
      }
      var fresh = ScaffoldFresh(project, projectJsonPath, slidesDir);
      if (File.Exists(manifestPath))
      {
        try
        {
          var existing = RenderManifest.Load(manifestPath);
          for (int i = 0; i < fresh.Segments.Count && i < existing.Segments.Count; i++)
          {
            var src = existing.Segments[i];
            var dst = fresh.Segments[i];
            if (!string.IsNullOrWhiteSpace(src.SlidePath))
              dst.SlidePath = src.SlidePath;
            if (!string.IsNullOrWhiteSpace(src.Label))
              dst.Label = src.Label;
            if (src.MinDurationSec > 0)
              dst.MinDurationSec = src.MinDurationSec;
            if (!string.IsNullOrWhiteSpace(src.MediaPath))
            {
              dst.MediaPath        = src.MediaPath;
              dst.MediaScale       = src.MediaScale;
              dst.MediaSpeed       = src.MediaSpeed;
              dst.MediaLoop        = src.MediaLoop;
              dst.MediaBorderColor = src.MediaBorderColor;
              dst.MediaBorderPx    = src.MediaBorderPx;
              dst.MediaShadowBlur  = src.MediaShadowBlur;
              dst.MediaShadowAlpha = src.MediaShadowAlpha;
              dst.MediaTrimIn      = src.MediaTrimIn;
              dst.MediaTrimOut     = src.MediaTrimOut;
            }
          }
          for (int i = fresh.Segments.Count; i < existing.Segments.Count; i++)
            fresh.Segments.Add(existing.Segments[i]);
        }
        catch { }
      }
      try { fresh.Save(manifestPath); } catch { }
      return fresh;
    }
    private static string GetManifestPath(string projectJsonPath, Project project, Scene? activeScene)
    {
      if (project.Scenes.Count <= 1 || activeScene == null)
        return Path.ChangeExtension(projectJsonPath, ".render.json");
      var safeId = SafeFileFragment(activeScene.Id);
      var dir    = Path.GetDirectoryName(projectJsonPath)!;
      var baseN  = Path.GetFileNameWithoutExtension(projectJsonPath);
      return Path.Combine(dir, $"{baseN}.{safeId}.render.json");
    }
    private static string SafeFileFragment(string? raw)
    {
      if (string.IsNullOrWhiteSpace(raw)) return "default";
      var sb = new System.Text.StringBuilder(raw.Length);
      foreach (var c in raw)
        sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
      return sb.ToString();
    }
    private static string ResolveSlidesDirForProject(string projectJsonPath)
    {
      var jsonDir  = Path.GetDirectoryName(projectJsonPath)!;
      var parent   = Path.GetDirectoryName(jsonDir) ?? jsonDir;
      var baseName = Path.GetFileNameWithoutExtension(projectJsonPath);
      var candidates = new[]
      {
        Path.Combine(jsonDir, "slides", baseName),
        Path.Combine(parent,  "slides", baseName),
        Path.Combine(jsonDir, "slides"),
        Path.Combine(parent,  "slides"),
        jsonDir
      };
      return candidates.FirstOrDefault(Directory.Exists) ?? jsonDir;
    }
    private static RenderManifest ScaffoldFresh(Project project, string projectJsonPath, string slidesDir)
    {
      var manifest = RenderManifest.ScaffoldFromProject(project, slidesDir);
      manifest.ProjectPath = projectJsonPath;
      return manifest;
    }
  }
  internal sealed class RenderProgressDialog : Window
  {
    public event Action? Cancelled;
    private readonly TextBlock   _header;
    private readonly TextBlock   _phase;
    private readonly TextBlock   _message;
    private readonly ProgressBar _bar;
    private readonly TextBlock   _eta;
    private readonly Button      _cancel;
    private readonly DateTime    _started = DateTime.UtcNow;
    internal RenderProgressDialog(Window owner)
    {
      Owner                 = owner;
      Title                 = "Export MP4 en cours…";
      Width                 = 560;
      Height                = 280;
      MinHeight             = 240;
      MinWidth              = 420;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode            = ResizeMode.CanResize;
      WindowStyle           = WindowStyle.SingleBorderWindow;
      ShowInTaskbar         = true;
      var root = new DockPanel { Margin = new Thickness(16) };
      _cancel = new Button
      {
        Content             = "Annuler",
        HorizontalAlignment = HorizontalAlignment.Right,
        Padding             = new Thickness(16, 6, 16, 6),
        Margin              = new Thickness(0, 10, 0, 0)
      };
      _cancel.Click += (_, __) =>
      {
        _cancel.IsEnabled = false;
        _cancel.Content   = "Annulation…";
        Cancelled?.Invoke();
      };
      DockPanel.SetDock(_cancel, Dock.Bottom);
      root.Children.Add(_cancel);
      var main = new Grid();
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      _header = new TextBlock
      {
        Text         = "Rendu MP4 d'une scène",
        FontSize     = 14,
        FontWeight   = FontWeights.SemiBold,
        Margin       = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap
      };
      _phase = new TextBlock
      {
        Text       = "⚙ Initialisation…",
        FontWeight = FontWeights.Medium,
        Margin     = new Thickness(0, 0, 0, 2)
      };
      _message = new TextBlock
      {
        Text         = "",
        Foreground   = System.Windows.Media.Brushes.Gray,
        FontSize     = 11,
        Margin       = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap
      };
      _bar = new ProgressBar
      {
        Height  = 18,
        Minimum = 0,
        Maximum = 100,
        Value   = 0,
        Margin  = new Thickness(0, 0, 0, 4)
      };
      _eta = new TextBlock
      {
        Text       = "",
        Foreground = System.Windows.Media.Brushes.Gray,
        FontSize   = 11,
        Margin     = new Thickness(0, 0, 0, 0)
      };
      Grid.SetRow(_header,  0);
      Grid.SetRow(_phase,   1);
      Grid.SetRow(_message, 2);
      Grid.SetRow(_bar,     3);
      Grid.SetRow(_eta,     4);
      main.Children.Add(_header);
      main.Children.Add(_phase);
      main.Children.Add(_message);
      main.Children.Add(_bar);
      main.Children.Add(_eta);
      root.Children.Add(main);
      Content = root;
    }
    internal void UpdateProgress(RenderProgress p)
    {
      Dispatcher.Invoke(() =>
      {
        _phase.Text   = PhaseName(p.Phase);
        _message.Text = p.Message;
        _bar.Value    = p.Percent;
        var elapsed = DateTime.UtcNow - _started;
        var etaTxt  = new System.Text.StringBuilder();
        etaTxt.Append($"Temps écoulé : {elapsed:mm\\:ss}");
        if (p.Percent > 1 && p.Percent < 100 && p.Phase != RenderPhase.Done && p.Phase != RenderPhase.Failed)
        {
          var remainingSec = elapsed.TotalSeconds * (100.0 - p.Percent) / p.Percent;
          etaTxt.Append($"  |  Restant estimé : {TimeSpan.FromSeconds(remainingSec):mm\\:ss}");
        }
        etaTxt.Append($"  |  Progression : {p.Percent:F0}%");
        _eta.Text = etaTxt.ToString();
        if (p.Phase == RenderPhase.Done)
        {
          _header.Text      = "✅ Rendu terminé";
          _cancel.IsEnabled = false;
        }
        else if (p.Phase == RenderPhase.Failed)
        {
          _header.Text      = "❌ Échec du rendu";
          _cancel.IsEnabled = false;
        }
      });
    }
    private static string PhaseName(RenderPhase phase) => phase switch
    {
      RenderPhase.Initializing  => "⚙ Initialisation",
      RenderPhase.Collecting    => "🎙 Génération audio + timings",
      RenderPhase.Rendering     => "🎬 Rendu des segments",
      RenderPhase.Concatenating => "🔗 Assemblage final",
      RenderPhase.Cleaning      => "🧹 Nettoyage",
      RenderPhase.Done          => "✅ Terminé",
      RenderPhase.Failed        => "❌ Erreur",
      _                         => phase.ToString()
    };
  }
  internal sealed class BatchExportWindow : Window
  {
    private readonly TextBlock   _queueHeader;
    private readonly TextBlock   _phaseLabel;
    private readonly TextBlock   _segmentLabel;
    private readonly ProgressBar _sceneBar;
    private readonly TextBlock   _etaLabel;
    private readonly StackPanel  _historyPanel;
    private readonly ScrollViewer _historyScroll;
    private readonly Button      _cancelBtn;
    public event Action? CancelRequested;
    private int    _totalScenes;
    private int    _currentIndex;
    private string _currentName = "";
    private readonly List<(string name, TimeSpan elapsed, bool ok)> _history = new();
    private DateTime _sceneStart;
    private DateTime _batchStart;
    internal BatchExportWindow(Window owner, int totalScenes)
    {
      Owner                 = owner;
      _totalScenes          = totalScenes;
      Title                 = $"Export en lot — 0 / {totalScenes}";
      Width                 = 560;
      Height                = 420;
      MinHeight             = 300;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode            = ResizeMode.CanResize;
      WindowStyle           = WindowStyle.SingleBorderWindow;
      var root = new DockPanel { Margin = new Thickness(16) };
      _cancelBtn = new Button
      {
        Content             = "Annuler la file d'attente",
        HorizontalAlignment = HorizontalAlignment.Right,
        Padding             = new Thickness(16, 6, 16, 6),
        Margin              = new Thickness(0, 10, 0, 0)
      };
      _cancelBtn.Click += (_, __) =>
      {
        _cancelBtn.IsEnabled = false;
        _cancelBtn.Content   = "Annulation en cours…";
        CancelRequested?.Invoke();
      };
      DockPanel.SetDock(_cancelBtn, Dock.Bottom);
      root.Children.Add(_cancelBtn);
      var main = new Grid();
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
      main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      _queueHeader = new TextBlock
      {
        Text         = "En attente…",
        FontSize     = 14,
        FontWeight   = FontWeights.SemiBold,
        Margin       = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap
      };
      _phaseLabel = new TextBlock
      {
        Text       = "",
        FontWeight = FontWeights.Medium,
        Margin     = new Thickness(0, 0, 0, 2)
      };
      _segmentLabel = new TextBlock
      {
        Text         = "",
        Foreground   = System.Windows.Media.Brushes.Gray,
        FontSize     = 11,
        Margin       = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap
      };
      _sceneBar = new ProgressBar
      {
        Height  = 16,
        Minimum = 0,
        Maximum = 100,
        Value   = 0,
        Margin  = new Thickness(0, 0, 0, 4)
      };
      _etaLabel = new TextBlock
      {
        Text       = "",
        Foreground = System.Windows.Media.Brushes.Gray,
        FontSize   = 11,
        Margin     = new Thickness(0, 0, 0, 0)
      };
      var historyLabel = new TextBlock
      {
        Text       = "Scènes terminées :",
        FontWeight = FontWeights.SemiBold,
        Margin     = new Thickness(0, 0, 0, 4)
      };
      _historyPanel  = new StackPanel();
      _historyScroll = new ScrollViewer
      {
        Content                       = _historyPanel,
        VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      Grid.SetRow(_queueHeader,   0);
      Grid.SetRow(_phaseLabel,    1);
      Grid.SetRow(_segmentLabel,  2);
      Grid.SetRow(_sceneBar,      3);
      Grid.SetRow(_etaLabel,      4);
      Grid.SetRow(historyLabel,   6);
      Grid.SetRow(_historyScroll, 7);
      main.Children.Add(_queueHeader);
      main.Children.Add(_phaseLabel);
      main.Children.Add(_segmentLabel);
      main.Children.Add(_sceneBar);
      main.Children.Add(_etaLabel);
      main.Children.Add(historyLabel);
      main.Children.Add(_historyScroll);
      root.Children.Add(main);
      Content = root;
    }
    internal void StartScene(int index, string sceneName)
    {
      _currentIndex = index;
      _currentName  = sceneName;
      _sceneStart   = DateTime.UtcNow;
      if (index == 0) _batchStart = _sceneStart;
      Dispatcher.Invoke(() =>
      {
        Title                = $"Export en lot — {index + 1} / {_totalScenes}";
        _queueHeader.Text    = $"Scène {index + 1} / {_totalScenes}  —  {sceneName}";
        _phaseLabel.Text     = "⚙ Initialisation…";
        _segmentLabel.Text   = "";
        _sceneBar.Value      = 0;
        _etaLabel.Text       = $"Scènes restantes : {_totalScenes - index}";
        _cancelBtn.IsEnabled = true;
      });
    }
    internal void UpdateProgress(RenderProgress p)
    {
      Dispatcher.Invoke(() =>
      {
        _phaseLabel.Text   = PhaseName(p.Phase);
        _segmentLabel.Text = p.Message;
        _sceneBar.Value    = p.Percent;
        var elapsedScene = DateTime.UtcNow - _sceneStart;
        var elapsedBatch = DateTime.UtcNow - _batchStart;
        var etaParts     = new System.Text.StringBuilder();
        etaParts.Append($"Scène en cours : {elapsedScene:mm\\:ss}");
        if (_currentIndex > 0)
        {
          var avgPerScene = elapsedBatch.TotalSeconds / _currentIndex;
          var remaining   = (_totalScenes - _currentIndex) * avgPerScene;
          etaParts.Append($"  |  Temps restant estimé : {TimeSpan.FromSeconds(remaining):mm\\:ss}");
        }
        _etaLabel.Text = etaParts.ToString();
      });
    }
    internal void CompleteScene(string sceneName, TimeSpan elapsed, bool success)
    {
      _history.Add((sceneName, elapsed, success));
      Dispatcher.Invoke(() =>
      {
        var icon  = success ? "✅" : "❌";
        var color = success
          ? System.Windows.Media.Color.FromRgb(0x4D, 0xC9, 0x3C)
          : System.Windows.Media.Color.FromRgb(0xE8, 0x41, 0x18);
        var line = new TextBlock
        {
          Text       = $"{icon}  {sceneName}  ({elapsed:mm\\:ss})",
          Foreground = new System.Windows.Media.SolidColorBrush(color),
          FontSize   = 11,
          Margin     = new Thickness(0, 1, 0, 1)
        };
        _historyPanel.Children.Add(line);
        _historyScroll.ScrollToBottom();
      });
    }
    internal void FinishAll(int succeeded, int total, TimeSpan totalElapsed)
    {
      Dispatcher.Invoke(() =>
      {
        Title                = $"Export terminé — {succeeded} / {total}";
        _queueHeader.Text    = $"✅ File d'attente terminée — {succeeded} / {total} scènes exportées";
        _phaseLabel.Text     = $"Durée totale : {totalElapsed:mm\\:ss}";
        _segmentLabel.Text   = "";
        _sceneBar.Value      = 100;
        _etaLabel.Text       = "";
        _cancelBtn.Content   = "Fermer";
        _cancelBtn.IsEnabled = true;
        _cancelBtn.Click    += (_, __) => Close();
      });
    }
    private static string PhaseName(RenderPhase phase) => phase switch
    {
      RenderPhase.Initializing  => "⚙ Initialisation",
      RenderPhase.Collecting    => "🎙 Génération audio + timings",
      RenderPhase.Rendering     => "🎬 Rendu des segments",
      RenderPhase.Concatenating => "🔗 Assemblage final",
      RenderPhase.Cleaning      => "🧹 Nettoyage",
      RenderPhase.Done          => "✅ Terminé",
      RenderPhase.Failed        => "❌ Erreur",
      _                         => phase.ToString()
    };
  }
}