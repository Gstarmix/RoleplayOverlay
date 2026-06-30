using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Newtonsoft.Json;
using WF = System.Windows.Forms;
using MediaColor      = System.Windows.Media.Color;
using MediaColors     = System.Windows.Media.Colors;
using MediaBrush      = System.Windows.Media.Brush;
using MediaBrushes    = System.Windows.Media.Brushes;
using WpfPoint        = System.Windows.Point;
using WpfButton       = System.Windows.Controls.Button;
using WpfOrientation  = System.Windows.Controls.Orientation;
using WpfFontFamily   = System.Windows.Media.FontFamily;
using WpfBrowser      = System.Windows.Controls.WebBrowser;
using WpfPanel        = System.Windows.Controls.Panel;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors      = System.Windows.Input.Cursors;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
namespace RoleplayOverlay
{
  public partial class OverlayWindow : Window
  {
    public static OverlayWindow? Current { get; private set; }
    private bool _clickThrough = true;
    private bool _lastAppliedClickThrough = true;
    private bool _lastCursorDragState = false;
    private readonly DispatcherTimer _animTimer = new();
    private double _youPulseTarget, _bot1PulseTarget, _bot2PulseTarget;
    private double _youPulse, _bot1Pulse, _bot2Pulse;
    private string? _lastTextBot1, _lastTextBot2;
    private SpeakerKind _active = SpeakerKind.Bot1;
    private const double SMOOTH = 0.22;
    private const double YOU_GAIN  = 2.2;
    private const double BOT_GAIN  = 0.9;
    private const double YOU_MAX_EXTRA = 0.45;
    private const double BOT_MAX_EXTRA = 0.25;
    private static readonly MediaColor YOU_HOT  = MediaColor.FromArgb(255, 255, 224,   0);
    private static readonly MediaColor B1_HOT   = MediaColor.FromArgb(255,   0, 220, 255);
    private static readonly MediaColor B2_HOT   = MediaColor.FromArgb(255, 255,   0, 200);
    private static readonly MediaColor COLD     = MediaColor.FromArgb(200, 255, 255, 255);
    private readonly DropShadowEffect _youGlow  = new() { ShadowDepth = 0, BlurRadius = 18, Opacity = 0.0, Color = MediaColors.Yellow };
    private readonly DropShadowEffect _b1Glow   = new() { ShadowDepth = 0, BlurRadius = 18, Opacity = 0.0, Color = MediaColors.Cyan   };
    private readonly DropShadowEffect _b2Glow   = new() { ShadowDepth = 0, BlurRadius = 18, Opacity = 0.0, Color = MediaColors.Magenta};
    private const double MARGIN_TOP    = 20;
    private const double MARGIN_SIDE   = 28;
    private const double TEXT_GAP      = 12;
    private const double BOTTOM_MARGIN = 28;
    private WF.Screen? _currentScreen;
    private bool _layoutEditMode = false;
    private bool _dragging = false;
    private FrameworkElement? _dragBubble;
    private WpfPoint _dragStartMouse;
    private double _dragStartLeft;
    private double _dragStartTop;
    private bool _didInitialLayout = false;
    private const string LayoutFileName = "bubble_layout.json";
    private sealed class BubbleLayout
    {
      public double YouX  { get; set; }
      public double YouY  { get; set; }
      public double Bot1X { get; set; }
      public double Bot1Y { get; set; }
      public double Bot2X { get; set; }
      public double Bot2Y { get; set; }
    }
    public OverlayWindow()
    {
      InitializeComponent();
      Current = this;
      Topmost = true;
      RefreshInputModes(force: true);
      BringBubblesToFront();
      YouBubble.RenderTransformOrigin  = new WpfPoint(0.5, 0.5);
      Bot1Bubble.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
      Bot2Bubble.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
      YouBubble.RenderTransform  = new ScaleTransform(1.0, 1.0);
      Bot1Bubble.RenderTransform = new ScaleTransform(1.0, 1.0);
      Bot2Bubble.RenderTransform = new ScaleTransform(1.0, 1.0);
      YouRing.Effect  = _youGlow;
      Bot1Ring.Effect = _b1Glow;
      Bot2Ring.Effect = _b2Glow;
      _animTimer.Interval = TimeSpan.FromMilliseconds(16);
      _animTimer.Tick += OnAnimTick;
      _animTimer.Start();
      HookBubbleDrag(YouBubble);
      HookBubbleDrag(Bot1Bubble);
      HookBubbleDrag(Bot2Bubble);
      SizeChanged += (_, __) => { LayoutBubbles(placeBubbles: false); BringBubblesToFront(); };
      LocationChanged += (_, __) => BringBubblesToFront();
      Activated += (_, __) => BringBubblesToFront();
      RootCanvas.SizeChanged += (_, __) =>
      {
        if (!_didInitialLayout && RootCanvas.ActualWidth > 0 && RootCanvas.ActualHeight > 0)
        {
          _didInitialLayout = true;
          if (!TryApplySavedLayout())
            LayoutBubbles(placeBubbles: true);
          else
            LayoutBubbles(placeBubbles: false);
          BringBubblesToFront();
        }
        else
        {
          LayoutBubbles(placeBubbles: false);
        }
      };
      var keepTop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
      keepTop.Tick += (_, __) => BringBubblesToFront();
      keepTop.Start();
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      Left = 0;
      Top = 0;
      Width  = SystemParameters.PrimaryScreenWidth;
      Height = SystemParameters.PrimaryScreenHeight;
      LayoutBubbles(placeBubbles: true);
      BringBubblesToFront();
      RefreshInputModes(force: true);
    }
    private void Window_Activated(object? sender, EventArgs e) => BringBubblesToFront();
    private static bool IsAltDown()
      => Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
    private void RefreshInputModes(bool force = false)
    {
      bool alt = IsAltDown();
      bool effectiveClickThrough = _clickThrough && !alt && !_dragging;
      if (force || effectiveClickThrough != _lastAppliedClickThrough)
      {
        ApplyClickThrough(effectiveClickThrough);
        _lastAppliedClickThrough = effectiveClickThrough;
      }
      bool dragCursor = _layoutEditMode || alt;
      if (force || dragCursor != _lastCursorDragState)
      {
        _lastCursorDragState = dragCursor;
        UpdateBubbleCursors();
      }
    }
    public bool LayoutEditMode => _layoutEditMode;
    public void SetLayoutEditMode(bool enable)
    {
      _layoutEditMode = enable;
      _clickThrough = !enable;
      RefreshInputModes(force: true);
      BringBubblesToFront();
    }
    public void ToggleLayoutEditMode() => SetLayoutEditMode(!_layoutEditMode);
    private void UpdateBubbleCursors()
    {
      try
      {
        var c = (_layoutEditMode || IsAltDown()) ? WpfCursors.SizeAll : null;
        YouBubble.Cursor = c;
        Bot1Bubble.Cursor = c;
        Bot2Bubble.Cursor = c;
      }
      catch { }
    }
    private void HookBubbleDrag(FrameworkElement bubble)
    {
      bubble.MouseLeftButtonDown += OnBubbleMouseDown;
      bubble.MouseMove += OnBubbleMouseMove;
      bubble.MouseLeftButtonUp += OnBubbleMouseUp;
      bubble.LostMouseCapture += (_, __) => EndDrag(save: true);
    }
    private void OnBubbleMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
      if (!IsAltDown()) return;
      if (sender is not FrameworkElement fe) return;
      _dragging = true;
      _dragBubble = fe;
      RefreshInputModes(force: true);
      _dragStartMouse = e.GetPosition(RootCanvas);
      _dragStartLeft = Canvas.GetLeft(fe);
      _dragStartTop  = Canvas.GetTop(fe);
      if (double.IsNaN(_dragStartLeft)) _dragStartLeft = 0;
      if (double.IsNaN(_dragStartTop))  _dragStartTop  = 0;
      try { fe.CaptureMouse(); } catch { }
      e.Handled = true;
      BringBubblesToFront();
    }
    private void OnBubbleMouseMove(object sender, WpfMouseEventArgs e)
    {
      if (!_dragging || _dragBubble == null) return;
      var cw = RootCanvas.ActualWidth;
      var ch = RootCanvas.ActualHeight;
      if (cw <= 0 || ch <= 0) return;
      var pos = e.GetPosition(RootCanvas);
      double dx = pos.X - _dragStartMouse.X;
      double dy = pos.Y - _dragStartMouse.Y;
      double w = SafeSize(_dragBubble.ActualWidth, _dragBubble.Width);
      double h = SafeSize(_dragBubble.ActualHeight, _dragBubble.Height);
      double nx = Math.Clamp(_dragStartLeft + dx, 0, Math.Max(0, cw - w));
      double ny = Math.Clamp(_dragStartTop  + dy, 0, Math.Max(0, ch - h));
      Canvas.SetLeft(_dragBubble, nx);
      Canvas.SetTop(_dragBubble, ny);
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
    }
    public void MoveBubbleTo(string who, double vidX, double vidY)
    {
      double scaleX = ActualWidth  > 0 ? ActualWidth  / 1920.0 : 1.0;
      double scaleY = ActualHeight > 0 ? ActualHeight / 1080.0 : 1.0;
      double cx = vidX * scaleX;
      double cy = vidY * scaleY;
      FrameworkElement? bubble = who switch
      {
        "you"  => YouBubble,
        "bot1" => Bot1Bubble,
        "bot2" => Bot2Bubble,
        _      => null
      };
      if (bubble == null) return;
      Canvas.SetLeft(bubble, cx);
      Canvas.SetTop (bubble, cy);
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
      SaveLayoutFromCurrent();
    }
    private void OnBubbleMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
      if (!_dragging) return;
      EndDrag(save: true);
      e.Handled = true;
      BringBubblesToFront();
    }
    private void EndDrag(bool save)
    {
      if (!_dragging) return;
      _dragging = false;
      try { _dragBubble?.ReleaseMouseCapture(); } catch { }
      _dragBubble = null;
      if (save) SaveLayoutFromCurrent();
      RefreshInputModes(force: true);
    }
    private string GetLayoutPath()
    {
      string dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RoleplayOverlay"
      );
      Directory.CreateDirectory(dir);
      return Path.Combine(dir, LayoutFileName);
    }
    private void SaveLayoutFromCurrent()
    {
      try
      {
        double cw = RootCanvas.ActualWidth;
        double ch = RootCanvas.ActualHeight;
        double toVidX = cw > 0 ? 1920.0 / cw : 1.0;
        double toVidY = ch > 0 ? 1080.0 / ch : 1.0;
        var lay = new BubbleLayout
        {
          YouX  = SafeGetLeft(YouBubble)  * toVidX,
          YouY  = SafeGetTop (YouBubble)  * toVidY,
          Bot1X = SafeGetLeft(Bot1Bubble) * toVidX,
          Bot1Y = SafeGetTop (Bot1Bubble) * toVidY,
          Bot2X = SafeGetLeft(Bot2Bubble) * toVidX,
          Bot2Y = SafeGetTop (Bot2Bubble) * toVidY
        };
        var json = JsonConvert.SerializeObject(lay, Formatting.Indented);
        File.WriteAllText(GetLayoutPath(), json);
      }
      catch { }
    }
    private bool TryApplySavedLayout()
    {
      try
      {
        var path = GetLayoutPath();
        if (!File.Exists(path)) return false;
        var json = File.ReadAllText(path);
        var lay = JsonConvert.DeserializeObject<BubbleLayout>(json);
        if (lay == null) return false;
        double cw = RootCanvas.ActualWidth;
        double ch = RootCanvas.ActualHeight;
        double toCanvasX = cw > 0 ? cw / 1920.0 : 1.0;
        double toCanvasY = ch > 0 ? ch / 1080.0 : 1.0;
        Canvas.SetLeft(YouBubble,  lay.YouX  * toCanvasX);
        Canvas.SetTop (YouBubble,  lay.YouY  * toCanvasY);
        Canvas.SetLeft(Bot1Bubble, lay.Bot1X * toCanvasX);
        Canvas.SetTop (Bot1Bubble, lay.Bot1Y * toCanvasY);
        Canvas.SetLeft(Bot2Bubble, lay.Bot2X * toCanvasX);
        Canvas.SetTop (Bot2Bubble, lay.Bot2Y * toCanvasY);
        ClampAllBubbles();
        return true;
      }
      catch
      {
        return false;
      }
    }
    private static double SafeGetLeft(FrameworkElement fe)
    {
      double x = Canvas.GetLeft(fe);
      return double.IsNaN(x) ? 0 : x;
    }
    private static double SafeGetTop(FrameworkElement fe)
    {
      double y = Canvas.GetTop(fe);
      return double.IsNaN(y) ? 0 : y;
    }
    private void ClampAllBubbles()
    {
      var cw = RootCanvas.ActualWidth;
      var ch = RootCanvas.ActualHeight;
      if (cw <= 0 || ch <= 0) return;
      ClampBubble(YouBubble, cw, ch);
      ClampBubble(Bot1Bubble, cw, ch);
      ClampBubble(Bot2Bubble, cw, ch);
    }
    private void ClampBubble(FrameworkElement el, double cw, double ch)
    {
      double w = SafeSize(el.ActualWidth, el.Width);
      double h = SafeSize(el.ActualHeight, el.Height);
      double x = SafeGetLeft(el);
      double y = SafeGetTop(el);
      x = Math.Clamp(x, 0, Math.Max(0, cw - w));
      y = Math.Clamp(y, 0, Math.Max(0, ch - h));
      Canvas.SetLeft(el, x);
      Canvas.SetTop(el, y);
    }
    private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0.0, 1.0), 2);
    private void OnAnimTick(object? sender, EventArgs e)
    {
      RefreshInputModes(force: false);
      _youPulse  += (_youPulseTarget  - _youPulse)  * SMOOTH;
      _bot1Pulse += (_bot1PulseTarget - _bot1Pulse) * SMOOTH;
      _bot2Pulse += (_bot2PulseTarget - _bot2Pulse) * SMOOTH;
      double youP  = EaseOut(Math.Clamp(_youPulse  * YOU_GAIN, 0.0, 1.0));
      double b1P   = EaseOut(Math.Clamp(_bot1Pulse * BOT_GAIN, 0.0, 1.0));
      double b2P   = EaseOut(Math.Clamp(_bot2Pulse * BOT_GAIN, 0.0, 1.0));
      double youScale = 1.0 + YOU_MAX_EXTRA * Math.Sqrt(youP);
      double b1Scale  = 1.0 + BOT_MAX_EXTRA * Math.Sqrt(b1P);
      double b2Scale  = 1.0 + BOT_MAX_EXTRA * Math.Sqrt(b2P);
      if (YouBubble.RenderTransform  is ScaleTransform ys) { ys.ScaleX = youScale; ys.ScaleY = youScale; }
      if (Bot1Bubble.RenderTransform is ScaleTransform s1) { s1.ScaleX = b1Scale;  s1.ScaleY = b1Scale;  }
      if (Bot2Bubble.RenderTransform is ScaleTransform s2) { s2.ScaleX = b2Scale;  s2.ScaleY = b2Scale;  }
      var youRing = LerpColor(COLD, YOU_HOT, youP);
      var b1Ring  = LerpColor(COLD, B1_HOT,  b1P);
      var b2Ring  = LerpColor(COLD, B2_HOT,  b2P);
      YouRing.StrokeThickness  = 4 + 4 * youP;
      YouRing.Stroke           = new SolidColorBrush(youRing);
      _youGlow.Color           = youRing;
      _youGlow.Opacity         = 0.35 + 0.65 * youP;
      _youGlow.BlurRadius      = 12 + 18 * youP;
      Bot1Ring.StrokeThickness = 4 + 3 * b1P;
      Bot1Ring.Stroke          = new SolidColorBrush(b1Ring);
      _b1Glow.Color            = b1Ring;
      _b1Glow.Opacity          = 0.30 + 0.60 * b1P;
      _b1Glow.BlurRadius       = 12 + 16 * b1P;
      Bot2Ring.StrokeThickness = 4 + 3 * b2P;
      Bot2Ring.Stroke          = new SolidColorBrush(b2Ring);
      _b2Glow.Color            = b2Ring;
      _b2Glow.Opacity          = 0.30 + 0.60 * b2P;
      _b2Glow.BlurRadius       = 12 + 16 * b2P;
      YouPulse.Opacity  = youP * 0.9;
      YouPulse.Stroke   = new SolidColorBrush(youRing);
      YouPulse.StrokeThickness = 6 + 6 * youP;
      Bot1Pulse.Opacity = b1P * 0.9;
      Bot1Pulse.Stroke  = new SolidColorBrush(b1Ring);
      Bot1Pulse.StrokeThickness = 6 + 6 * b1P;
      Bot2Pulse.Opacity = b2P * 0.9;
      Bot2Pulse.Stroke  = new SolidColorBrush(b2Ring);
      Bot2Pulse.StrokeThickness = 6 + 6 * b2P;
    }
    private static MediaColor LerpColor(MediaColor a, MediaColor b, double t)
    {
      t = Math.Clamp(t, 0.0, 1.0);
      byte L(byte x, byte y) => (byte)(x + (y - x) * t);
      return MediaColor.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
    private static double SafeSize(double actual, double declared = double.NaN, double fallback = 140)
    {
      if (!double.IsNaN(actual) && actual > 0) return actual;
      if (!double.IsNaN(declared) && declared > 0) return declared;
      return fallback;
    }
    private void LayoutBubbles(bool placeBubbles)
    {
      var cw = RootCanvas.ActualWidth;
      var ch = RootCanvas.ActualHeight;
      if (cw <= 0 || ch <= 0) return;
      double youW = SafeSize(YouBubble.ActualWidth,  YouBubble.Width);
      double youH = SafeSize(YouBubble.ActualHeight, YouBubble.Height);
      double b1W  = SafeSize(Bot1Bubble.ActualWidth, Bot1Bubble.Width);
      double b1H  = SafeSize(Bot1Bubble.ActualHeight, Bot1Bubble.Height);
      double b2W  = SafeSize(Bot2Bubble.ActualWidth, Bot2Bubble.Width);
      double b2H  = SafeSize(Bot2Bubble.ActualHeight, Bot2Bubble.Height);
      if (placeBubbles)
      {
        Canvas.SetLeft(YouBubble, MARGIN_SIDE);
        Canvas.SetTop (YouBubble, MARGIN_TOP);
        Canvas.SetLeft(Bot1Bubble, Math.Max(MARGIN_SIDE, cw - b1W - MARGIN_SIDE));
        Canvas.SetTop (Bot1Bubble, MARGIN_TOP);
        double b2X = (cw - b2W) / 2.0;
        double b2Y = Math.Max(MARGIN_TOP, ch - b2H - BOTTOM_MARGIN);
        Canvas.SetLeft(Bot2Bubble, b2X);
        Canvas.SetTop (Bot2Bubble, b2Y);
      }
      ClampAllBubbles();
      if (Bot1TextBox.Visibility == Visibility.Visible) Bot1TextBox.UpdateLayout();
      if (Bot2TextBox.Visibility == Visibility.Visible) Bot2TextBox.UpdateLayout();
      double b1X  = SafeGetLeft(Bot1Bubble);
      double b1Y  = SafeGetTop (Bot1Bubble);
      double b2X2 = SafeGetLeft(Bot2Bubble);
      double b2Y2 = SafeGetTop (Bot2Bubble);
      if (Bot1TextBox.Visibility == Visibility.Visible)
      {
        double t1W = SafeSize(Bot1TextBox.ActualWidth,  Bot1TextBox.Width,  420);
        double t1H = SafeSize(Bot1TextBox.ActualHeight, Bot1TextBox.Height,  80);
        double t1X = b1X - TEXT_GAP - t1W;
        double t1Y = b1Y + (b1H - t1H) / 2.0;
        if (t1X < MARGIN_SIDE) t1X = MARGIN_SIDE;
        if (t1Y < MARGIN_TOP)  t1Y = MARGIN_TOP;
        if (t1X + t1W > cw - MARGIN_SIDE) t1X = Math.Max(MARGIN_SIDE, cw - MARGIN_SIDE - t1W);
        Canvas.SetLeft(Bot1TextBox, t1X);
        Canvas.SetTop (Bot1TextBox, t1Y);
        WpfPanel.SetZIndex(Bot1TextBox, 100);
      }
      if (Bot2TextBox.Visibility == Visibility.Visible)
      {
        double t2W = SafeSize(Bot2TextBox.ActualWidth,  Bot2TextBox.Width,  420);
        double t2H = SafeSize(Bot2TextBox.ActualHeight, Bot2TextBox.Height,  80);
        double t2X = (cw - t2W) / 2.0;
        double t2Y = b2Y2 - TEXT_GAP - t2H;
        if (t2X < MARGIN_SIDE) t2X = MARGIN_SIDE;
        if (t2X + t2W > cw - MARGIN_SIDE) t2X = cw - MARGIN_SIDE - t2W;
        if (t2Y < MARGIN_TOP)  t2Y = MARGIN_TOP;
        Canvas.SetLeft(Bot2TextBox, t2X);
        Canvas.SetTop (Bot2TextBox, t2Y);
        WpfPanel.SetZIndex(Bot2TextBox, 100);
      }
    }
    public void ApplyVisibilityFrom(GlobalSettings g)
      => SetVisibleSpeakers(g.ShowYou, g.ShowBot1, g.ShowBot2);
    public void SetVisibleSpeakers(bool showYou, bool showBot1, bool showBot2)
    {
      YouBubble.Visibility  = showYou  ? Visibility.Visible : Visibility.Collapsed;
      Bot1Bubble.Visibility = showBot1 ? Visibility.Visible : Visibility.Collapsed;
      Bot2Bubble.Visibility = showBot2 ? Visibility.Visible : Visibility.Collapsed;
      if (!showBot1) { Bot1TextBox.Visibility = Visibility.Collapsed; _lastTextBot1 = null; }
      if (!showBot2) { Bot2TextBox.Visibility = Visibility.Collapsed; _lastTextBot2 = null; }
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
    }
    public void ShowSpeakerText(SpeakerKind who, string text)
    {
      switch (who)
      {
        case SpeakerKind.Bot1:
          _lastTextBot1 = text;
          SetTextWithRichContent(Bot1Text, text ?? string.Empty);
          Bot1TextBox.Visibility = Visibility.Visible;
          break;
        case SpeakerKind.Bot2:
          _lastTextBot2 = text;
          SetTextWithRichContent(Bot2Text, text ?? string.Empty);
          Bot2TextBox.Visibility = Visibility.Visible;
          break;
      }
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
    }
    public void HideAllTexts()
    {
      Bot1TextBox.Visibility = Visibility.Collapsed;
      Bot2TextBox.Visibility = Visibility.Collapsed;
      BringBubblesToFront();
    }
    public void ToggleBotText(SpeakerKind who)
    {
      if (who == SpeakerKind.Bot1)
      {
        if (Bot1TextBox.Visibility == Visibility.Visible) Bot1TextBox.Visibility = Visibility.Collapsed;
        else if (!string.IsNullOrWhiteSpace(_lastTextBot1))
        {
          SetTextWithRichContent(Bot1Text, _lastTextBot1!);
          Bot1TextBox.Visibility = Visibility.Visible;
        }
      }
      else if (who == SpeakerKind.Bot2)
      {
        if (Bot2TextBox.Visibility == Visibility.Visible) Bot2TextBox.Visibility = Visibility.Collapsed;
        else if (!string.IsNullOrWhiteSpace(_lastTextBot2))
        {
          SetTextWithRichContent(Bot2Text, _lastTextBot2!);
          Bot2TextBox.Visibility = Visibility.Visible;
        }
      }
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
    }
    private void OnCloseBot1(object sender, RoutedEventArgs e) { Bot1TextBox.Visibility = Visibility.Collapsed; BringBubblesToFront(); }
    private void OnCloseBot2(object sender, RoutedEventArgs e) { Bot2TextBox.Visibility = Visibility.Collapsed; BringBubblesToFront(); }
    private void SetTextWithRichContent(TextBlock tb, string text)
    {
      tb.Inlines.Clear();
      if (string.IsNullOrWhiteSpace(text)) { tb.Text = string.Empty; return; }
      var rx = new Regex(
        @"(\[img\s+src=""(?<img>[^""]+)""\s*\])"
        + @"|(\[html\](?<html>[\s\S]*?)\[/html\])"
        + @"|(<span\s+[^>]*style\s*=\s*""[^""]*color\s*:\s*(?<col>[^;""\)]+)[^""]*""[^>]*>(?<spantxt>[\s\S]*?)</span>)"
        + @"|(?<rawhtml>(?:<!DOCTYPE[\s\S]*?>)|(?:<\s*(?:html|head|body|div|p|ul|ol|li|h[1-6]|img|table|thead|tbody|tr|td|th|style|script|section|article|header|footer|svg|canvas|code|pre|br|hr)\b[\s\S]*?>[\s\S]*?(?:</\s*(?:html|head|body|div|p|ul|ol|li|h[1-6]|table|thead|tbody|tr|td|th|section|article|header|footer|svg|code|pre)\s*>|(?=<)|$)))"
        + @"|(?<url>https?://\S+)",
        RegexOptions.IgnoreCase);
      int last = 0;
      foreach (Match m in rx.Matches(text))
      {
        if (m.Index > last)
        {
          var raw = text.Substring(last, m.Index - last);
          if (!string.IsNullOrEmpty(raw)) tb.Inlines.Add(new Run(raw));
        }
        if (m.Groups["img"].Success)
        {
          tb.Inlines.Add(new LineBreak());
          var inline = CreateImageInline(m.Groups["img"].Value);
          if (inline != null) tb.Inlines.Add(inline);
          tb.Inlines.Add(new LineBreak());
        }
        else if (m.Groups["html"].Success)
        {
          tb.Inlines.Add(new LineBreak());
          tb.Inlines.Add(CreateHtmlChipInline(m.Groups["html"].Value));
          tb.Inlines.Add(new LineBreak());
        }
        else if (m.Groups["spantxt"].Success)
        {
          var inner = m.Groups["spantxt"].Value;
          var col   = m.Groups["col"].Value.Trim();
          var brush = TryMakeBrushFromCssColor(col) ?? new SolidColorBrush(MediaColors.Yellow);
          tb.Inlines.Add(new Run(inner) { Foreground = brush });
        }
        else if (m.Groups["rawhtml"].Success)
        {
          tb.Inlines.Add(new LineBreak());
          tb.Inlines.Add(CreateHtmlChipInline(m.Groups["rawhtml"].Value));
          tb.Inlines.Add(new LineBreak());
        }
        else if (m.Groups["url"].Success)
        {
          string url = m.Groups["url"].Value;
          var link = new Hyperlink(new Run(url))
          {
            NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null,
            Foreground = new SolidColorBrush(MediaColors.DeepSkyBlue),
            TextDecorations = TextDecorations.Underline,
            ToolTip = "Ouvrir le lien"
          };
          link.RequestNavigate += OnHyperlinkRequestNavigate;
          tb.Inlines.Add(link);
        }
        last = m.Index + m.Length;
      }
      if (last < text.Length)
      {
        var tail = text.Substring(last);
        if (!string.IsNullOrEmpty(tail)) tb.Inlines.Add(new Run(tail));
      }
      if (tb.Inlines.Count == 0) tb.Text = text;
    }
    private MediaBrush? TryMakeBrushFromCssColor(string css)
    {
      if (string.IsNullOrWhiteSpace(css)) return null;
      css = css.Trim();
      if (css.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
      {
        try
        {
          var nums = Regex.Matches(css, @"\d+(\.\d+)?");
          int r = nums.Count > 0 ? (int)Math.Round(double.Parse(nums[0].Value)) : 0;
          int g = nums.Count > 1 ? (int)Math.Round(double.Parse(nums[1].Value)) : 0;
          int b = nums.Count > 2 ? (int)Math.Round(double.Parse(nums[2].Value)) : 0;
          byte a = 255;
          if (css.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) && nums.Count > 3)
          {
            var alpha = double.Parse(nums[3].Value);
            a = (byte)Math.Round(Math.Clamp(alpha, 0.0, 1.0) * 255.0);
          }
          return new SolidColorBrush(MediaColor.FromArgb(a, (byte)r, (byte)g, (byte)b));
        }
        catch { return null; }
      }
      try
      {
        var colorObj = WpfColorConverter.ConvertFromString(css);
        if (colorObj is MediaColor mc) return new SolidColorBrush(mc);
      }
      catch { }
      return null;
    }
    private void OnHyperlinkRequestNavigate(object? sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
      try
      {
        if (e.Uri != null) WebViewWindow.OpenUrl(e.Uri.AbsoluteUri);
      }
      catch { }
      finally { e.Handled = true; BringBubblesToFront(); }
    }
    private InlineUIContainer? CreateImageInline(string absPath)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(absPath) || !File.Exists(absPath))
        {
          return new InlineUIContainer(new TextBlock
          {
            Text = "[image introuvable]",
            Foreground = new SolidColorBrush(MediaColors.OrangeRed),
            Margin = new Thickness(0, 4, 0, 4)
          });
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(absPath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        var img = new System.Windows.Controls.Image
        {
          Source = bmp,
          Stretch = Stretch.Uniform,
          MaxWidth = 420,
          MaxHeight = 240,
          Margin = new Thickness(0, 6, 0, 6),
          Cursor = WpfCursors.Hand,
          SnapsToDevicePixels = true,
          UseLayoutRounding = true
        };
        img.MouseLeftButtonUp += (s, e) =>
        {
          e.Handled = true;
          try { ShowImageLightbox(absPath); } catch { }
          BringBubblesToFront();
        };
        return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center };
      }
      catch
      {
        return new InlineUIContainer(new TextBlock
        {
          Text = "[image non lisible]",
          Foreground = new SolidColorBrush(MediaColors.OrangeRed),
          Margin = new Thickness(0, 4, 0, 4)
        });
      }
    }
    private InlineUIContainer CreateHtmlChipInline(string html)
    {
      var chip = new Border
      {
        Background = new SolidColorBrush(MediaColor.FromArgb(40, 135, 206, 250)),
        BorderBrush = new SolidColorBrush(MediaColor.FromArgb(90, 135, 206, 250)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(0, 6, 0, 6),
        Cursor = WpfCursors.Hand,
        Child = new TextBlock { Text = "Aperçu HTML", Foreground = new SolidColorBrush(MediaColors.White) },
        ToolTip = "Cliquer pour afficher l'aperçu HTML"
      };
      chip.MouseLeftButtonUp += (s, e) => { e.Handled = true; try { ShowHtmlOverlay(html); } catch { } BringBubblesToFront(); };
      return new InlineUIContainer(chip) { BaselineAlignment = BaselineAlignment.Center };
    }
    private void ShowHtmlOverlay(string html)
    {
      var grid = new Grid { Background = new SolidColorBrush(MediaColor.FromArgb(180, 0, 0, 0)) };
      var border = new Border
      {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment   = System.Windows.VerticalAlignment.Center,
        Padding = new Thickness(10),
        Background = new SolidColorBrush(MediaColor.FromArgb(240, 20, 20, 20)),
        CornerRadius = new CornerRadius(8),
        Effect = new DropShadowEffect { BlurRadius = 16, Opacity = 0.4, ShadowDepth = 0 }
      };
      var browser = new WpfBrowser { Width = 1000, Height = 620 };
      browser.NavigateToString(html);
      var close = new WpfButton
      {
        Content = "Fermer",
        Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(0, 10, 0, 0),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
      };
      var stack = new StackPanel { Orientation = WpfOrientation.Vertical };
      stack.Children.Add(browser);
      stack.Children.Add(close);
      border.Child = stack;
      grid.Children.Add(border);
      grid.MouseDown += (s, e) =>
      {
        if (e.OriginalSource == grid)
        {
          var w0 = Window.GetWindow(grid);
          w0?.Close();
        }
      };
      var win = new Window
      {
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        Background = MediaBrushes.Transparent,
        ShowInTaskbar = false,
        Topmost = true,
        Content = grid,
        Left = this.Left,
        Top = this.Top,
        Width = this.ActualWidth,
        Height = this.ActualHeight
      };
      close.Click += (s, e) => win.Close();
      win.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
      win.Show();
      win.Activate();
      BringBubblesToFront();
    }
    private void ShowImageLightbox(string absPath)
    {
      if (string.IsNullOrWhiteSpace(absPath) || !File.Exists(absPath)) return;
      var bmp = new BitmapImage();
      bmp.BeginInit();
      bmp.CacheOption = BitmapCacheOption.OnLoad;
      bmp.UriSource = new Uri(absPath, UriKind.Absolute);
      bmp.EndInit();
      bmp.Freeze();
      var img = new System.Windows.Controls.Image { Source = bmp, Stretch = Stretch.Uniform };
      var grid = new Grid { Background = new SolidColorBrush(MediaColor.FromArgb(180, 0, 0, 0)) };
      var border = new Border
      {
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        VerticalAlignment   = System.Windows.VerticalAlignment.Center,
        Child = img,
        Padding = new Thickness(8)
      };
      grid.Children.Add(border);
      grid.MouseDown += (s, e) => { var win0 = Window.GetWindow(grid); win0?.Close(); };
      var win = new Window
      {
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        Background = MediaBrushes.Transparent,
        ShowInTaskbar = false,
        Topmost = true,
        Content = grid,
        Left = this.Left,
        Top = this.Top,
        Width = this.ActualWidth,
        Height = this.ActualHeight
      };
      win.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
      win.Loaded += (_, __) =>
      {
        img.MaxWidth = win.ActualWidth * 0.9;
        img.MaxHeight = win.ActualHeight * 0.9;
      };
      win.Show();
      win.Activate();
      BringBubblesToFront();
    }
    public void BringBubblesToFront()
    {
      try
      {
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
      }
      catch { }
    }
    public void ToggleVisibility()
    {
      if (IsVisible)
      {
        ResetVisualState();
        Hide();
      }
      else
      {
        ResetVisualState();
        Show();
        Topmost = true;
        Activate();
        LayoutBubbles(placeBubbles: false);
        BringBubblesToFront();
        RefreshInputModes(force: true);
      }
    }
    public void SetActiveSpeaker(SpeakerKind who)
    {
      _active = who;
      switch (who)
      {
        case SpeakerKind.You:  _youPulseTarget  = Math.Max(_youPulseTarget,  0.35); break;
        case SpeakerKind.Bot1: _bot1PulseTarget = Math.Max(_bot1PulseTarget, 0.35); break;
        case SpeakerKind.Bot2: _bot2PulseTarget = Math.Max(_bot2PulseTarget, 0.35); break;
      }
      BringBubblesToFront();
    }
    public void ResetVisualState()
    {
      _youPulse = _bot1Pulse = _bot2Pulse = 0.0;
      _youPulseTarget = _bot1PulseTarget = _bot2PulseTarget = 0.0;
      if (YouBubble.RenderTransform  is ScaleTransform ys) { ys.ScaleX = 1.0; ys.ScaleY = 1.0; }
      if (Bot1Bubble.RenderTransform is ScaleTransform s1) { s1.ScaleX = 1.0; s1.ScaleY = 1.0; }
      if (Bot2Bubble.RenderTransform is ScaleTransform s2) { s2.ScaleX = 1.0; s2.ScaleY = 1.0; }
      _lastTextBot1 = null;
      _lastTextBot2 = null;
      HideAllTexts();
      BringBubblesToFront();
      RefreshInputModes(force: true);
    }
    public void MoveToScreen(WF.Screen sc)
    {
      if (sc == null) return;
      _currentScreen = sc;
      var dpi = VisualTreeHelper.GetDpi(this);
      double sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
      double sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
      Left   = sc.Bounds.Left   / sx;
      Top    = sc.Bounds.Top    / sy;
      Width  = sc.Bounds.Width  / sx;
      Height = sc.Bounds.Height / sy;
      LayoutBubbles(placeBubbles: false);
      BringBubblesToFront();
    }
    public void SetYouLevel (float v) => Dispatcher.Invoke(() => _youPulseTarget  = Math.Clamp(v, 0f, 1f));
    public void SetBot1Level(float v) => Dispatcher.Invoke(() => _bot1PulseTarget = Math.Clamp(v, 0f, 1f));
    public void SetBot2Level(float v) => Dispatcher.Invoke(() => _bot2PulseTarget = Math.Clamp(v, 0f, 1f));
    public void SetBotLevel(float v) => SetBot1Level(v);
    public void SetYouImage (string path) => SetEllipseImage(YouImage,  path);
    public void SetBot1Image(string path) => SetEllipseImage(Bot1Image, path);
    public void SetBot2Image(string path) => SetEllipseImage(Bot2Image, path);
    private static void SetEllipseImage(System.Windows.Shapes.Ellipse ellipse, string? absPath)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(absPath) || !File.Exists(absPath)) return;
        var brush = ellipse.Fill as ImageBrush ?? new ImageBrush();
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(absPath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        brush.ImageSource = bmp;
        brush.Stretch = Stretch.UniformToFill;
        ellipse.Fill = brush;
      }
      catch { }
    }
    private void ApplyClickThrough(bool enable)
    {
      var hwnd = new WindowInteropHelper(this).Handle;
      int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
      if (enable) SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
      else        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
    }
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
  }
}