using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using WpfButton           = System.Windows.Controls.Button;
using WpfTextBlock        = System.Windows.Controls.TextBlock;
using WpfListBox          = System.Windows.Controls.ListBox;
using WpfListBoxItem      = System.Windows.Controls.ListBoxItem;
using WpfScrollViewer     = System.Windows.Controls.ScrollViewer;
using WpfBrushes          = System.Windows.Media.Brushes;
using WpfRectangle        = System.Windows.Shapes.Rectangle;
using WpfColor            = System.Windows.Media.Color;
using WpfSolidColorBrush  = System.Windows.Media.SolidColorBrush;
using WpfThickness        = System.Windows.Thickness;
using WpfVisibility       = System.Windows.Visibility;
using WpfHAlign           = System.Windows.HorizontalAlignment;
using WpfVAlign           = System.Windows.VerticalAlignment;
using WpfFontWeights      = System.Windows.FontWeights;
using WpfCursors          = System.Windows.Input.Cursors;
using WpfKey              = System.Windows.Input.Key;
using WpfKeyEventArgs     = System.Windows.Input.KeyEventArgs;
using WpfRoutedEventArgs  = System.Windows.RoutedEventArgs;
using WpfMessageBox       = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage  = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfFontFamily       = System.Windows.Media.FontFamily;
namespace RoleplayOverlay
{
  public sealed class AudioRecorderResult
  {
    public bool    Validated { get; init; }
    public string? Mp3Path   { get; init; }
  }
  public sealed class AudioRecorderWindow : Window
  {
    private readonly AudioRecorderService _recorder;
    private readonly string               _mp3DestPath;
    private readonly string?              _sequenceText;
    private readonly string?              _slideImagePath;
    private enum UiState { Idle, Recording, Recorded, Playing, Converting }
    private UiState  _uiState = UiState.Idle;
    private string?  _recordedMp3;
    private TimeSpan _recordingTime;
    private DateTime _timerStart;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly List<float> _waveformSamples = new();
    private bool _waveformDiagLogged;
    private const int    WaveformMaxBars  = 80;
    private const double WaveformBarWidth = 4;
    private const double WaveformBarGap   = 2;
    public AudioRecorderResult? RecorderResult { get; private set; }
    private WpfTextBlock _timerLabel     = null!;
    private WpfTextBlock _statusLabel    = null!;
    private Canvas       _waveformCanvas = null!;
    private WpfButton    _btnRecord      = null!;
    private WpfButton    _btnPlay        = null!;
    private WpfButton    _btnDelete      = null!;
    private WpfButton    _btnValidate    = null!;
    private WpfButton    _micDropBtn     = null!;
    private WpfTextBlock _micDropLabel   = null!;
    private Popup        _micPopup       = null!;
    private WpfListBox   _micList        = null!;
    private readonly List<string?> _micDeviceNames = new();
    private bool _suppressMicChange;
    private static WpfColor C(byte r, byte g, byte b)          => WpfColor.FromRgb(r, g, b);
    private static WpfColor CA(byte a, byte r, byte g, byte b) => WpfColor.FromArgb(a, r, g, b);
    private static WpfSolidColorBrush B(WpfColor c)            => new(c);
    private static readonly WpfColor BgColor      = C(0x0B, 0x0B, 0x0C);
    private static readonly WpfColor Bg2Color     = C(0x14, 0x14, 0x16);
    private static readonly WpfColor Bg3Color     = C(0x1C, 0x1D, 0x20);
    private static readonly WpfColor FgColor      = C(0xEA, 0xEA, 0xEA);
    private static readonly WpfColor Fg2Color     = C(0x9A, 0x9A, 0x9A);
    private static readonly WpfColor LineColor    = C(0x2A, 0x2B, 0x2E);
    private static readonly WpfColor RecordColor  = C(0xE8, 0x41, 0x18);
    private static readonly WpfColor GreenColor   = C(0x4D, 0xC9, 0x3C);
    private static readonly WpfColor WaveRecColor = CA(0xCC, 0xE8, 0x41, 0x18);
    private static readonly WpfColor WavePlayColor= CA(0xCC, 0x4D, 0xC9, 0x3C);
    private static readonly WpfColor WaveIdleColor= CA(0x44, 0xEA, 0xEA, 0xEA);
    public AudioRecorderWindow(
      AudioRecorderService recorder,
      string               mp3DestPath,
      Window?              owner          = null,
      string?              initialMp3     = null,
      string?              sequenceText   = null,
      string?              slideImagePath = null)
    {
      _recorder       = recorder;
      _mp3DestPath    = mp3DestPath;
      _sequenceText   = sequenceText;
      _slideImagePath = (!string.IsNullOrWhiteSpace(slideImagePath) && File.Exists(slideImagePath))
                          ? slideImagePath : null;
      Title                 = "Enregistrement audio";
      Width                 = 500;
      Height                = 440;
      MinWidth              = 420;
      MinHeight             = 400;
      ResizeMode            = ResizeMode.NoResize;
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
      Background            = B(BgColor);
      BorderBrush           = B(LineColor);
      BorderThickness       = new WpfThickness(1);
      WindowStyle           = WindowStyle.None;
      AllowsTransparency    = false;
      if (owner != null) Owner = owner;
      if (!string.IsNullOrWhiteSpace(initialMp3) && File.Exists(initialMp3))
      {
        _recordedMp3 = initialMp3;
        try
        {
          using var reader = new NAudio.Wave.Mp3FileReader(initialMp3);
          _recordingTime = reader.TotalTime;
        }
        catch { _recordingTime = TimeSpan.Zero; }
      }
      BuildUi();
      _timer.Tick            += OnTimerTick;
      _recorder.LevelChanged += OnLevelChanged;
      _recorder.StateChanged += OnRecorderStateChanged;
      Loaded += (_, _) =>
      {
        PopulateMicList();
        if (_recordedMp3 != null)
        {
          _timerLabel.Text = FormatTime(_recordingTime);
          SetUiState(UiState.Recorded);
          Dispatcher.InvokeAsync(() => OnPlayButtonClick(this, new WpfRoutedEventArgs()),
            DispatcherPriority.Loaded);
        }
        else
        {
          UpdateUi();
        }
        var wa = SystemParameters.WorkArea;
        if (Top  < wa.Top)             Top  = wa.Top  + 20;
        if (Left < wa.Left)            Left = wa.Left + 20;
        if (Top  + Height > wa.Bottom) Top  = Math.Max(wa.Top,  wa.Bottom - Height - 20);
        if (Left + Width  > wa.Right)  Left = Math.Max(wa.Left, wa.Right  - Width  - 20);
      };
      Closing += OnWindowClosing;
    }
    private void BuildUi()
    {
      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
      if (_slideImagePath != null)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      if (!string.IsNullOrWhiteSpace(_sequenceText))
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
      Content = root;
      int row = 0;
      var titleBar = new Border { Background = B(Bg2Color) };
      Grid.SetRow(titleBar, row++); root.Children.Add(titleBar);
      titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
      var titlePanel = new DockPanel { Margin = new WpfThickness(14, 0, 6, 0) };
      titleBar.Child = titlePanel;
      var btnClose = MakeIconButton("\u2715", 13, Fg2Color);
      btnClose.Width = 36; btnClose.Height = 36; btnClose.Padding = new WpfThickness(0);
      btnClose.Click += (_, _) => CancelAndClose();
      DockPanel.SetDock(btnClose, Dock.Right);
      titlePanel.Children.Add(btnClose);
      titlePanel.Children.Add(new WpfTextBlock { Text = "\U0001F399", FontSize = 15, Foreground = B(FgColor), VerticalAlignment = WpfVAlign.Center, Margin = new WpfThickness(0, 0, 8, 0) });
      titlePanel.Children.Add(new WpfTextBlock { Text = "Enregistrement audio", FontSize = 12, FontWeight = WpfFontWeights.SemiBold, Foreground = B(FgColor), VerticalAlignment = WpfVAlign.Center });
      var sep1 = new Border { Background = B(LineColor) };
      Grid.SetRow(sep1, row++); root.Children.Add(sep1);
      var micBar = new Border { Background = B(Bg2Color), Padding = new WpfThickness(14, 0, 14, 0) };
      Grid.SetRow(micBar, row++); root.Children.Add(micBar);
      var micPanel = new DockPanel { VerticalAlignment = WpfVAlign.Center };
      micBar.Child = micPanel;
      var btnRefresh = MakeIconButton("\u21BB", 12, Fg2Color);
      btnRefresh.Padding = new WpfThickness(6, 2, 6, 2);
      btnRefresh.ToolTip = "Rafraichir";
      btnRefresh.Click   += (_, _) => PopulateMicList();
      DockPanel.SetDock(btnRefresh, Dock.Right);
      micPanel.Children.Add(btnRefresh);
      micPanel.Children.Add(new WpfTextBlock { Text = "\U0001F3A4", FontSize = 13, Foreground = B(Fg2Color), VerticalAlignment = WpfVAlign.Center, Margin = new WpfThickness(0, 0, 8, 0) });
      micPanel.Children.Add(new WpfTextBlock { Text = "Micro :", FontSize = 11, Foreground = B(Fg2Color), VerticalAlignment = WpfVAlign.Center, Margin = new WpfThickness(0, 0, 8, 0) });
      _micDropBtn = new WpfButton
      {
        Background = B(Bg3Color), BorderBrush = B(LineColor), BorderThickness = new WpfThickness(1),
        Padding = new WpfThickness(0), Cursor = WpfCursors.Hand, Height = 28,
      };
      var dropInner = new DockPanel { Margin = new WpfThickness(8, 0, 4, 0) };
      _micDropLabel = new WpfTextBlock { Text = "Defaut Windows", FontSize = 11, Foreground = B(FgColor), VerticalAlignment = WpfVAlign.Center, TextTrimming = TextTrimming.CharacterEllipsis };
      var dropArrow = new WpfTextBlock { Text = "\u25BC", FontSize = 8, Foreground = B(Fg2Color), VerticalAlignment = WpfVAlign.Center, Margin = new WpfThickness(4, 0, 4, 0) };
      DockPanel.SetDock(dropArrow, Dock.Right);
      dropInner.Children.Add(dropArrow);
      dropInner.Children.Add(_micDropLabel);
      _micDropBtn.Content = dropInner;
      _micDropBtn.Click += OnMicDropClick;
      micPanel.Children.Add(_micDropBtn);
      _micList = new WpfListBox { Background = B(Bg2Color), BorderBrush = B(LineColor), BorderThickness = new WpfThickness(1), Foreground = B(FgColor), FontSize = 11, MaxHeight = 200 };
      _micList.SelectionChanged += OnMicListSelectionChanged;
      var itemStyle = new Style(typeof(WpfListBoxItem));
      itemStyle.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty,      B(Bg2Color)));
      itemStyle.Setters.Add(new Setter(WpfListBoxItem.ForegroundProperty,      B(FgColor)));
      itemStyle.Setters.Add(new Setter(WpfListBoxItem.PaddingProperty,         new WpfThickness(10, 6, 10, 6)));
      itemStyle.Setters.Add(new Setter(WpfListBoxItem.BorderThicknessProperty, new WpfThickness(0)));
      var hT = new Trigger { Property = WpfListBoxItem.IsMouseOverProperty, Value = true };
      hT.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty, B(Bg3Color)));
      itemStyle.Triggers.Add(hT);
      var sT = new Trigger { Property = WpfListBoxItem.IsSelectedProperty, Value = true };
      sT.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty, B(RecordColor)));
      sT.Setters.Add(new Setter(WpfListBoxItem.ForegroundProperty, B(C(0xFF, 0xFF, 0xFF))));
      itemStyle.Triggers.Add(sT);
      _micList.ItemContainerStyle = itemStyle;
      _micPopup = new Popup { Child = _micList, PlacementTarget = _micDropBtn, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
      _micPopup.Opened += (_, _) => _micList.Width = _micDropBtn.ActualWidth;
      var sep2 = new Border { Background = B(LineColor) };
      Grid.SetRow(sep2, row++); root.Children.Add(sep2);
      if (_slideImagePath != null)
      {
        const double SlideMaxHeight = 220;
        const double SlideMaxWidth  = 460;
        BitmapImage? bmp = null;
        try
        {
          bmp = new BitmapImage();
          bmp.BeginInit();
          bmp.CacheOption  = BitmapCacheOption.OnLoad;
          bmp.UriSource    = new Uri(_slideImagePath, UriKind.Absolute);
          bmp.EndInit();
          bmp.Freeze();
        }
        catch (Exception ex)
        {
          Logger.Warn($"[RecorderUI] Slide preview load failed: {ex.Message}");
          bmp = null;
        }
        if (bmp != null)
        {
          var slideImg = new System.Windows.Controls.Image
          {
            Source              = bmp,
            Stretch             = Stretch.Uniform,
            StretchDirection    = StretchDirection.DownOnly,
            MaxHeight           = SlideMaxHeight,
            MaxWidth            = SlideMaxWidth,
            HorizontalAlignment = WpfHAlign.Center,
            VerticalAlignment   = WpfVAlign.Center,
            Cursor              = WpfCursors.Hand,
            ToolTip             = "Cliquer pour agrandir",
          };
          System.Windows.Media.RenderOptions.SetBitmapScalingMode(slideImg, BitmapScalingMode.HighQuality);
          var bmpRef = bmp;
          slideImg.MouseLeftButtonUp += (_, _) => ShowSlideFullscreen(bmpRef);
          var slideBorder = new Border
          {
            Background      = B(Bg2Color),
            BorderBrush     = B(LineColor),
            BorderThickness = new WpfThickness(0, 0, 0, 1),
            Padding         = new WpfThickness(14, 10, 14, 10),
            Child           = slideImg,
          };
          Grid.SetRow(slideBorder, row++); root.Children.Add(slideBorder);
        }
      }
      if (!string.IsNullOrWhiteSpace(_sequenceText))
      {
        const double MaxPrompterHeight = 250;
        var promptText = new WpfTextBlock
        {
          Text        = _sequenceText,
          FontSize    = 12,
          Foreground  = B(FgColor),
          TextWrapping = TextWrapping.Wrap,
          LineHeight  = 20,
        };
        promptText.Measure(new System.Windows.Size(Width - 28 - 20, double.PositiveInfinity));
        double desiredTextH = promptText.DesiredSize.Height;
        double promptPadding = 16;
        double desiredPromptH = Math.Min(desiredTextH + promptPadding, MaxPrompterHeight);
        var promptScroll = new WpfScrollViewer
        {
          VerticalScrollBarVisibility   = desiredTextH + promptPadding > MaxPrompterHeight
                                            ? ScrollBarVisibility.Auto
                                            : ScrollBarVisibility.Disabled,
          HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
          MaxHeight                     = MaxPrompterHeight,
        };
        var promptBorder = new Border
        {
          Background      = B(CA(0x22, 0xFF, 0xFF, 0xFF)),
          BorderBrush     = B(LineColor),
          BorderThickness = new WpfThickness(0, 0, 0, 1),
          Padding         = new WpfThickness(14, 8, 14, 8),
        };
        promptScroll.Content = promptText;
        promptBorder.Child   = promptScroll;
        Grid.SetRow(promptBorder, row++); root.Children.Add(promptBorder);
        Height = 440 + desiredPromptH + 1;
        var wa = SystemParameters.WorkArea;
        if (Height > wa.Height * 0.85)
          Height = wa.Height * 0.85;
      }
      if (_slideImagePath != null)
      {
        Height += 240;
        var wa = SystemParameters.WorkArea;
        if (Height > wa.Height * 0.9)
          Height = wa.Height * 0.9;
      }
      var center = new Grid { Margin = new WpfThickness(28, 12, 28, 8) };
      center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
      Grid.SetRow(center, row++); root.Children.Add(center);
      _timerLabel = new WpfTextBlock
      {
        Text = "0:00", FontSize = 36, FontWeight = WpfFontWeights.Light,
        FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B(FgColor),
        HorizontalAlignment = WpfHAlign.Center, Margin = new WpfThickness(0, 0, 0, 2),
      };
      Grid.SetRow(_timerLabel, 0); center.Children.Add(_timerLabel);
      _statusLabel = new WpfTextBlock
      {
        Text = "Appuyez sur \u23FA ou Espace pour commencer", FontSize = 11,
        Foreground = B(Fg2Color), HorizontalAlignment = WpfHAlign.Center,
        Margin = new WpfThickness(0, 0, 0, 8),
      };
      Grid.SetRow(_statusLabel, 1); center.Children.Add(_statusLabel);
      _waveformCanvas = new Canvas
      {
        ClipToBounds        = true,
        HorizontalAlignment = WpfHAlign.Stretch,
        VerticalAlignment   = WpfVAlign.Stretch,
      };
      _waveformCanvas.SizeChanged += (_, _) => DrawWaveform();
      var wfBorder = new Border
      {
        CornerRadius = new CornerRadius(6), Background = B(CA(0x18, 0xFF, 0xFF, 0xFF)),
        Height = 48, Margin = new WpfThickness(0, 0, 0, 8), ClipToBounds = true,
        Child = _waveformCanvas,
      };
      Grid.SetRow(wfBorder, 2); center.Children.Add(wfBorder);
      var btnZone = new Grid();
      Grid.SetRow(btnZone, 3); center.Children.Add(btnZone);
      btnZone.Children.Add(new Ellipse
      {
        Width = 66, Height = 66,
        Stroke = B(CA(0x33, 0xFF, 0xFF, 0xFF)), StrokeThickness = 2,
        HorizontalAlignment = WpfHAlign.Center, VerticalAlignment = WpfVAlign.Center,
        IsHitTestVisible = false,
      });
      _btnRecord = MakeCircleButton("\u23FA", 54, RecordColor);
      _btnRecord.HorizontalAlignment = WpfHAlign.Center;
      _btnRecord.VerticalAlignment   = WpfVAlign.Center;
      _btnRecord.Click += OnRecordButtonClick;
      btnZone.Children.Add(_btnRecord);
      _btnPlay = MakeCircleButton("\u25B6", 42, Bg3Color);
      _btnPlay.HorizontalAlignment = WpfHAlign.Left;
      _btnPlay.VerticalAlignment   = WpfVAlign.Center;
      _btnPlay.Visibility          = WpfVisibility.Hidden;
      _btnPlay.ToolTip             = "Reecouter / Pause";
      _btnPlay.Click += OnPlayButtonClick;
      btnZone.Children.Add(_btnPlay);
      _btnDelete = MakeCircleButton("\U0001F5D1", 42, Bg3Color);
      _btnDelete.HorizontalAlignment = WpfHAlign.Right;
      _btnDelete.VerticalAlignment   = WpfVAlign.Center;
      _btnDelete.Visibility          = WpfVisibility.Hidden;
      _btnDelete.ToolTip             = "Supprimer cet enregistrement";
      _btnDelete.Click += OnDeleteButtonClick;
      btnZone.Children.Add(_btnDelete);
      var sep3 = new Border { Background = B(LineColor) };
      Grid.SetRow(sep3, row++); root.Children.Add(sep3);
      var bottomBar = new Border { Background = B(Bg2Color) };
      Grid.SetRow(bottomBar, row++); root.Children.Add(bottomBar);
      var bottomPanel = new DockPanel { LastChildFill = false, Margin = new WpfThickness(16, 0, 16, 0), VerticalAlignment = WpfVAlign.Center };
      bottomBar.Child = bottomPanel;
      var btnCancel = MakeTextButton("Annuler", false);
      btnCancel.Click += (_, _) => CancelAndClose();
      DockPanel.SetDock(btnCancel, Dock.Left);
      bottomPanel.Children.Add(btnCancel);
      _btnValidate = MakeTextButton("  \u2713  Valider  ", true);
      _btnValidate.IsEnabled = false;
      _btnValidate.Click += OnValidateClick;
      DockPanel.SetDock(_btnValidate, Dock.Right);
      bottomPanel.Children.Add(_btnValidate);
      KeyDown += OnKeyDown;
    }
    private void PopulateMicList()
    {
      _suppressMicChange = true;
      _micList.Items.Clear();
      _micDeviceNames.Clear();
      _micDeviceNames.Add(null);
      _micList.Items.Add(new WpfListBoxItem { Content = "Defaut Windows" });
      try
      {
        using var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
          _micDeviceNames.Add(d.FriendlyName);
          _micList.Items.Add(new WpfListBoxItem { Content = d.FriendlyName });
        }
      }
      catch (Exception ex) { Logger.Warn($"[RecorderWindow] PopulateMicList: {ex.Message}"); }
      string? saved = UserPrefs.PreferredMicDevice;
      int idx = 0;
      if (!string.IsNullOrWhiteSpace(saved))
        for (int i = 1; i < _micDeviceNames.Count; i++)
          if (_micDeviceNames[i] != null && _micDeviceNames[i]!.IndexOf(saved, StringComparison.OrdinalIgnoreCase) >= 0)
          { idx = i; break; }
      _micList.SelectedIndex = idx;
      _micDropLabel.Text = idx == 0 ? "Defaut Windows" : (_micDeviceNames[idx] ?? "Defaut Windows");
      _suppressMicChange = false;
    }
    private void OnMicDropClick(object sender, WpfRoutedEventArgs e)
    {
      if (_uiState is UiState.Recording or UiState.Converting) return;
      _micPopup.IsOpen = !_micPopup.IsOpen;
    }
    private void OnMicListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressMicChange) return;
      int idx = _micList.SelectedIndex;
      if (idx < 0 || idx >= _micDeviceNames.Count) return;
      string? name = _micDeviceNames[idx];
      _micDropLabel.Text           = idx == 0 ? "Defaut Windows" : (name ?? "Defaut Windows");
      UserPrefs.PreferredMicDevice = name;
      UserPrefs.Save();
      _micPopup.IsOpen = false;
    }
    private string? GetSelectedDevice()
      => (_micList.SelectedIndex >= 0 && _micList.SelectedIndex < _micDeviceNames.Count)
         ? _micDeviceNames[_micList.SelectedIndex] : null;
    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
      if (e.Key == WpfKey.Escape) { CancelAndClose(); return; }
      if (e.Key == WpfKey.Space && _uiState is UiState.Idle or UiState.Recording or UiState.Recorded or UiState.Playing)
      {
        if (_uiState is UiState.Idle or UiState.Recording)
          OnRecordButtonClick(this, new WpfRoutedEventArgs());
        else
          OnPlayButtonClick(this, new WpfRoutedEventArgs());
        e.Handled = true;
      }
      if (e.Key == WpfKey.Enter && _btnValidate.IsEnabled)
      { OnValidateClick(this, new WpfRoutedEventArgs()); e.Handled = true; }
    }
    private async void OnRecordButtonClick(object sender, WpfRoutedEventArgs e)
    {
      if (_uiState == UiState.Recording)
      {
        _timer.Stop();
        _recordingTime = DateTime.Now - _timerStart;
        SetUiState(UiState.Converting);
        var result = await _recorder.StopAndConvertAsync(_mp3DestPath);
        if (result.Success)
        {
          _recordedMp3 = result.Mp3Path;
          SetUiState(UiState.Recorded);
          _timerLabel.Text = FormatTime(_recordingTime);
        }
        else
        {
          WpfMessageBox.Show(this, $"Echec de la conversion :\n{result.Error}", "Erreur",
            WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
          SetUiState(UiState.Idle);
        }
        return;
      }
      if (_uiState == UiState.Playing) _recorder.StopPlayback();
      _waveformSamples.Clear();
      _waveformDiagLogged = false;
      DrawWaveform();
      bool ok = _recorder.StartRecording(GetSelectedDevice());
      if (!ok)
      {
        WpfMessageBox.Show(this,
          "Impossible d'ouvrir le micro.\nVerifiez les permissions dans Parametres > Confidentialite > Microphone.",
          "Erreur micro", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
        return;
      }
      _timerStart = DateTime.Now; _timer.Start(); SetUiState(UiState.Recording);
    }
    private void OnPlayButtonClick(object sender, WpfRoutedEventArgs e)
    {
      if (_uiState == UiState.Playing)
      {
        _recorder.StopPlayback();
        _timer.Stop();
        SetUiState(UiState.Recorded);
        _timerLabel.Text = FormatTime(_recordingTime);
        return;
      }
      if (string.IsNullOrWhiteSpace(_recordedMp3) || !File.Exists(_recordedMp3)) return;
      _waveformSamples.Clear();
      _waveformDiagLogged = false;
      _recorder.PlayPreview(_recordedMp3);
      _timerStart = DateTime.Now; _timer.Start(); SetUiState(UiState.Playing);
    }
    private void OnDeleteButtonClick(object sender, WpfRoutedEventArgs e)
    {
      if (WpfMessageBox.Show(this, "Supprimer cet enregistrement ?", "Supprimer",
        WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question) != WpfMessageBoxResult.Yes) return;
      _recorder.StopPlayback(); _recorder.CancelRecording();
      try { if (_recordedMp3 != null && File.Exists(_recordedMp3)) File.Delete(_recordedMp3); } catch { }
      _recordedMp3 = null;
      _waveformSamples.Clear(); _timer.Stop(); SetUiState(UiState.Idle);
    }
    private void OnValidateClick(object sender, WpfRoutedEventArgs e)
    {
      if (string.IsNullOrWhiteSpace(_recordedMp3) || !File.Exists(_recordedMp3)) return;
      RecorderResult = new AudioRecorderResult { Validated = true, Mp3Path = _recordedMp3 };
      _recorder.StopPlayback(); _timer.Stop(); DialogResult = true; Close();
    }
    private void CancelAndClose()
    {
      _recorder.StopPlayback();
      if (_uiState == UiState.Recording) _recorder.CancelRecording();
      RecorderResult = new AudioRecorderResult { Validated = false };
      DialogResult = false; Close();
    }
    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
      _recorder.LevelChanged -= OnLevelChanged;
      _recorder.StateChanged -= OnRecorderStateChanged;
      _timer.Stop();
      _recorder.StopPlayback();
    }
    private void OnLevelChanged(float level)
    {
      Dispatcher.InvokeAsync(() =>
      {
        if (_uiState != UiState.Recording && _uiState != UiState.Playing) return;
        _waveformSamples.Add(Math.Min(1.0f, level));
        while (_waveformSamples.Count > WaveformMaxBars) _waveformSamples.RemoveAt(0);
        DrawWaveform();
      });
    }
    private void OnRecorderStateChanged(RecorderState state)
    {
      Dispatcher.InvokeAsync(() =>
      {
        if (state == RecorderState.Idle && _uiState == UiState.Playing)
        {
          _timer.Stop();
          _timerLabel.Text = FormatTime(_recordingTime);
          SetUiState(UiState.Recorded);
        }
      });
    }
    private void OnTimerTick(object? sender, EventArgs e)
    {
      var elapsed = DateTime.Now - _timerStart;
      _timerLabel.Text = FormatTime(elapsed);
      if (_uiState == UiState.Playing && elapsed > _recordingTime + TimeSpan.FromMilliseconds(500))
      { _timer.Stop(); _timerLabel.Text = FormatTime(_recordingTime); SetUiState(UiState.Recorded); }
    }
    private static string FormatTime(TimeSpan t)
      => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                           : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    private void DrawWaveform()
    {
      if (_waveformCanvas.ActualWidth <= 0 || _waveformCanvas.ActualHeight <= 0)
        _waveformCanvas.UpdateLayout();
      if (!_waveformDiagLogged && _waveformSamples.Count > 0)
      {
        float maxS = 0f;
        for (int k = 0; k < _waveformSamples.Count; k++)
          if (_waveformSamples[k] > maxS) maxS = _waveformSamples[k];
        Logger.Info($"[RecorderUI] DrawWaveform diag: canvas={_waveformCanvas.ActualWidth:F0}x{_waveformCanvas.ActualHeight:F0} samples={_waveformSamples.Count} first={_waveformSamples[0]:F3} max={maxS:F3} state={_uiState}");
        _waveformDiagLogged = true;
      }
      _waveformCanvas.Children.Clear();
      double h = _waveformCanvas.ActualHeight > 0 ? _waveformCanvas.ActualHeight : 48;
      double w = _waveformCanvas.ActualWidth  > 0 ? _waveformCanvas.ActualWidth  : 444;
      var brush = B(_uiState switch { UiState.Recording => WaveRecColor, UiState.Playing => WavePlayColor, _ => WaveIdleColor });
      if (_waveformSamples.Count == 0)
      {
        var bl = new WpfRectangle { Width = w, Height = 2, Fill = B(CA(0x33, 0xFF, 0xFF, 0xFF)), RadiusX = 1, RadiusY = 1 };
        Canvas.SetLeft(bl, 0); Canvas.SetTop(bl, h / 2 - 1); _waveformCanvas.Children.Add(bl); return;
      }
      double tw = WaveformBarWidth + WaveformBarGap;
      double sx = Math.Max(0.0, w - _waveformSamples.Count * tw);
      for (int i = 0; i < _waveformSamples.Count; i++)
      {
        double bh = Math.Max(1.0, _waveformSamples[i] * (h - 6));
        var r = new WpfRectangle { Width = WaveformBarWidth, Height = bh, Fill = brush, RadiusX = 2, RadiusY = 2 };
        Canvas.SetLeft(r, sx + i * tw); Canvas.SetTop(r, (h - bh) / 2);
        _waveformCanvas.Children.Add(r);
      }
    }
    private void SetUiState(UiState s)
    {
      _uiState = s;
      if (s != UiState.Recording && s != UiState.Playing)
        _waveformSamples.Clear();
      UpdateUi();
    }
    private void UpdateUi()
    {
      _micDropBtn.IsEnabled = _uiState is UiState.Idle or UiState.Recorded;
      switch (_uiState)
      {
        case UiState.Idle:
          _statusLabel.Text = "Appuyez sur \u23FA ou Espace pour commencer";
          _timerLabel.Text = "0:00"; _timerLabel.Foreground = B(FgColor);
          SetRecordIcon("\u23FA"); _btnRecord.IsEnabled = true;
          _btnPlay.Visibility = WpfVisibility.Hidden; _btnDelete.Visibility = WpfVisibility.Hidden;
          _btnValidate.IsEnabled = false; break;
        case UiState.Recording:
          _statusLabel.Text = "Enregistrement... Espace pour arreter";
          _timerLabel.Foreground = B(RecordColor); SetRecordIcon("\u23F9");
          _btnRecord.IsEnabled = true;
          _btnPlay.Visibility = WpfVisibility.Hidden; _btnDelete.Visibility = WpfVisibility.Hidden;
          _btnValidate.IsEnabled = false; break;
        case UiState.Converting:
          _statusLabel.Text = "Conversion MP3..."; _timerLabel.Foreground = B(Fg2Color);
          _btnRecord.IsEnabled = false;
          _btnPlay.Visibility = WpfVisibility.Hidden; _btnDelete.Visibility = WpfVisibility.Hidden;
          _btnValidate.IsEnabled = false; break;
        case UiState.Recorded:
          _statusLabel.Text = "Pret \u2014 Ecoutez, recommencez ou validez";
          _timerLabel.Foreground = B(GreenColor); SetRecordIcon("\u23FA");
          _btnRecord.IsEnabled = true;
          _btnPlay.Visibility = WpfVisibility.Visible; _btnDelete.Visibility = WpfVisibility.Visible;
          _btnValidate.IsEnabled = true; SetPlayIcon("\u25B6"); break;
        case UiState.Playing:
          _statusLabel.Text = "Lecture en cours... Espace ou \u25B6 pour arreter";
          _timerLabel.Foreground = B(GreenColor); SetPlayIcon("\u23F9"); break;
      }
      DrawWaveform();
    }
    private void SetRecordIcon(string i) { if (_btnRecord.Content is WpfTextBlock t) t.Text = i; }
    private void SetPlayIcon(string i)   { if (_btnPlay.Content   is WpfTextBlock t) t.Text = i; }
    private static WpfButton MakeCircleButton(string icon, double size, WpfColor bg)
    {
      var tpl  = new ControlTemplate(typeof(WpfButton));
      var grid = new FrameworkElementFactory(typeof(Grid));
      var ell  = new FrameworkElementFactory(typeof(Ellipse));
      ell.Name = "Bg"; ell.SetValue(Ellipse.FillProperty, B(bg)); ell.SetValue(Ellipse.StrokeThicknessProperty, 0.0);
      grid.AppendChild(ell);
      var cp = new FrameworkElementFactory(typeof(ContentPresenter));
      cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHAlign.Center);
      cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   WpfVAlign.Center);
      grid.AppendChild(cp); tpl.VisualTree = grid;
      var hov = new Trigger { Property = WpfButton.IsMouseOverProperty, Value = true };
      hov.Setters.Add(new Setter(Ellipse.FillProperty, B(WpfColor.FromRgb((byte)Math.Min(255,(int)bg.R+20),(byte)Math.Min(255,(int)bg.G+10),(byte)Math.Min(255,(int)bg.B+10))), "Bg"));
      tpl.Triggers.Add(hov);
      var dis = new Trigger { Property = WpfButton.IsEnabledProperty, Value = false };
      dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4)); tpl.Triggers.Add(dis);
      return new WpfButton { Template = tpl, Width = size, Height = size, Cursor = WpfCursors.Hand,
        Content = new WpfTextBlock { Text = icon, FontSize = size * 0.38, Foreground = B(C(0xFF,0xFF,0xFF)), HorizontalAlignment = WpfHAlign.Center, VerticalAlignment = WpfVAlign.Center } };
    }
    private void ShowSlideFullscreen(BitmapImage bmp)
    {
      var wa = SystemParameters.WorkArea;
      double maxW = wa.Width  * 0.92;
      double maxH = wa.Height * 0.92;
      var big = new System.Windows.Controls.Image
      {
        Source              = bmp,
        Stretch             = Stretch.Uniform,
        StretchDirection    = StretchDirection.Both,
        HorizontalAlignment = WpfHAlign.Center,
        VerticalAlignment   = WpfVAlign.Center,
      };
      System.Windows.Media.RenderOptions.SetBitmapScalingMode(big, BitmapScalingMode.HighQuality);
      var hintText = new WpfTextBlock
      {
        Text                = "Clic ou Échap pour fermer",
        Foreground          = B(CA(0xBB, 0xFF, 0xFF, 0xFF)),
        FontSize            = 11,
        HorizontalAlignment = WpfHAlign.Center,
        VerticalAlignment   = WpfVAlign.Bottom,
        Margin              = new WpfThickness(0, 0, 0, 14),
      };
      var layout = new Grid { Background = B(CA(0xEE, 0x00, 0x00, 0x00)) };
      layout.Children.Add(big);
      layout.Children.Add(hintText);
      var preview = new Window
      {
        Owner                 = this,
        Title                 = "Aperçu slide",
        WindowStyle           = WindowStyle.None,
        ResizeMode            = ResizeMode.NoResize,
        AllowsTransparency    = true,
        Background            = WpfBrushes.Transparent,
        ShowInTaskbar         = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Width                 = Math.Min(maxW, bmp.PixelWidth  > 0 ? bmp.PixelWidth  : maxW),
        Height                = Math.Min(maxH, bmp.PixelHeight > 0 ? bmp.PixelHeight : maxH),
        Content               = layout,
      };
      preview.MouseLeftButtonUp += (_, _) => preview.Close();
      preview.KeyDown           += (_, e) => { if (e.Key == WpfKey.Escape) preview.Close(); };
      preview.ShowDialog();
    }
    private static WpfButton MakeIconButton(string icon, double fontSize, WpfColor fg)
      => new WpfButton { Content = new WpfTextBlock { Text = icon, FontSize = fontSize, Foreground = B(fg), HorizontalAlignment = WpfHAlign.Center, VerticalAlignment = WpfVAlign.Center },
         Background = WpfBrushes.Transparent, BorderThickness = new WpfThickness(0), Cursor = WpfCursors.Hand, Padding = new WpfThickness(8,4,8,4) };
    private static WpfButton MakeTextButton(string text, bool accent)
    {
      WpfColor bg  = accent ? C(0xE8,0x41,0x18) : C(0x1C,0x1D,0x20);
      WpfColor brd = accent ? C(0x7C,0x1A,0x0E) : C(0x2A,0x2B,0x2E);
      var tpl = new ControlTemplate(typeof(WpfButton));
      var b   = new FrameworkElementFactory(typeof(Border));
      b.Name = "Bd"; b.SetValue(Border.BackgroundProperty, B(bg)); b.SetValue(Border.BorderBrushProperty, B(brd));
      b.SetValue(Border.BorderThicknessProperty, new WpfThickness(1)); b.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
      var cp = new FrameworkElementFactory(typeof(ContentPresenter));
      cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHAlign.Center);
      cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   WpfVAlign.Center);
      b.AppendChild(cp); tpl.VisualTree = b;
      var dis = new Trigger { Property = WpfButton.IsEnabledProperty, Value = false };
      dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35)); tpl.Triggers.Add(dis);
      return new WpfButton { Template = tpl, Padding = new WpfThickness(20,10,20,10), Cursor = WpfCursors.Hand,
        Content = new WpfTextBlock { Text = text, FontSize = 12, FontWeight = accent ? WpfFontWeights.SemiBold : WpfFontWeights.Normal, Foreground = B(FgColor) } };
    }
  }
}