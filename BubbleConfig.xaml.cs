using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Newtonsoft.Json;
using UserControl      = System.Windows.Controls.UserControl;
using WpfButton        = System.Windows.Controls.Button;
using Color            = System.Windows.Media.Color;
using SolidColorBrush  = System.Windows.Media.SolidColorBrush;
using Point            = System.Windows.Point;
using MouseEventArgs   = System.Windows.Input.MouseEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using TextBox          = System.Windows.Controls.TextBox;
using Canvas           = System.Windows.Controls.Canvas;
using Grid             = System.Windows.Controls.Grid;
using Application      = System.Windows.Application;
namespace RoleplayOverlay
{
  public partial class BubbleConfigControl : UserControl
  {
    private bool  _isLoaded;
    private bool  _suppressPositionUpdate;
    private Grid? _dragging;
    private Point _dragStart;
    private double _dragStartLeft, _dragStartTop;
    public Color YouGlow  { get; private set; } = Color.FromRgb(255, 212,   0);
    public Color Bot1Glow { get; private set; } = Color.FromRgb(255,   0, 255);
    public Color Bot2Glow { get; private set; } = Color.FromRgb(  0, 255, 255);
    private const double ScaleX = 1920.0 / 234.0;
    private const double ScaleY = 1080.0 / 131.0;
    public BubbleConfigControl()
    {
      InitializeComponent();
      Loaded += OnControlLoaded;
    }
    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
      if (_isLoaded) return;
      _isLoaded = true;
      ApplyDefaults();
      LoadFromLayoutFile();
      RefreshAllAnchorHighlights();
    }
    private void RefreshAllAnchorHighlights()
    {
      if (Bot1XBox == null || Bot1YBox == null
       || Bot2XBox == null || Bot2YBox == null
       || YouXBox  == null || YouYBox  == null) return;
      double bot1X = ParseField(Bot1XBox,   20), bot1Y = ParseField(Bot1YBox,  20);
      double bot2X = ParseField(Bot2XBox, 1700), bot2Y = ParseField(Bot2YBox,  20);
      double youX  = ParseField(YouXBox,   890), youY  = ParseField(YouYBox,  920);
      if (Bot1AnchorGrid != null) HighlightAnchorGrid(Bot1AnchorGrid, DetectAnchor(bot1X, bot1Y), Bot1Glow);
      if (Bot2AnchorGrid != null) HighlightAnchorGrid(Bot2AnchorGrid, DetectAnchor(bot2X, bot2Y), Bot2Glow);
      if (YouAnchorGrid  != null) HighlightAnchorGrid(YouAnchorGrid,  DetectAnchor(youX,  youY),  YouGlow);
    }
    private void ApplyDefaults()
    {
      _suppressPositionUpdate = true;
      YouXBox.Text  = "890";  YouYBox.Text  = "920";
      Bot1XBox.Text = "20";   Bot1YBox.Text = "20";
      Bot2XBox.Text = "1700"; Bot2YBox.Text = "20";
      SetBubbleCanvasPos(BubbleYou,   890.0 / ScaleX, 920.0 / ScaleY);
      SetBubbleCanvasPos(BubbleBot1,   20.0 / ScaleX,  20.0 / ScaleY);
      SetBubbleCanvasPos(BubbleBot2, 1700.0 / ScaleX,  20.0 / ScaleY);
      YouColorBtn.Background  = new SolidColorBrush(YouGlow);
      Bot1ColorBtn.Background = new SolidColorBrush(Bot1Glow);
      Bot2ColorBtn.Background = new SolidColorBrush(Bot2Glow);
      _suppressPositionUpdate = false;
    }
    private static string LayoutPath =>
      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RoleplayOverlay", "bubble_layout.json");
    public void LoadFromLayoutFile()
    {
      try
      {
        if (!File.Exists(LayoutPath)) return;
        var dto = JsonConvert.DeserializeObject<BubbleLayoutFullDto>(
          File.ReadAllText(LayoutPath));
        if (dto == null) return;
        bool looksLikeVideoCoords =
          dto.YouX  >= 0 && dto.YouX  <= 1920 &&
          dto.YouY  >= 0 && dto.YouY  <= 1080 &&
          dto.Bot1X >= 0 && dto.Bot1X <= 1920 &&
          dto.Bot1Y >= 0 && dto.Bot1Y <= 1080 &&
          dto.Bot2X >= 0 && dto.Bot2X <= 1920 &&
          dto.Bot2Y >= 0 && dto.Bot2Y <= 1080;
        if (!looksLikeVideoCoords) { File.Delete(LayoutPath); return; }
        double maxCanvasX = BubbleCanvas.Width  - 24;
        double maxCanvasY = BubbleCanvas.Height - 24;
        SetBubbleCanvasPos(BubbleYou,
          Math.Min(dto.YouX  / ScaleX, maxCanvasX),
          Math.Min(dto.YouY  / ScaleY, maxCanvasY));
        SetBubbleCanvasPos(BubbleBot1,
          Math.Min(dto.Bot1X / ScaleX, maxCanvasX),
          Math.Min(dto.Bot1Y / ScaleY, maxCanvasY));
        SetBubbleCanvasPos(BubbleBot2,
          Math.Min(dto.Bot2X / ScaleX, maxCanvasX),
          Math.Min(dto.Bot2Y / ScaleY, maxCanvasY));
        _suppressPositionUpdate = true;
        YouXBox.Text  = ((int)dto.YouX).ToString();
        YouYBox.Text  = ((int)dto.YouY).ToString();
        Bot1XBox.Text = ((int)dto.Bot1X).ToString();
        Bot1YBox.Text = ((int)dto.Bot1Y).ToString();
        Bot2XBox.Text = ((int)dto.Bot2X).ToString();
        Bot2YBox.Text = ((int)dto.Bot2Y).ToString();
        _suppressPositionUpdate = false;
        AvatarSizeBox.Text = dto.AvatarSize > 0 ? dto.AvatarSize.ToString() : "140";
        YouGlow  = ParseColor(dto.YouGlowHex,  "#FFD400");
        Bot1Glow = ParseColor(dto.Bot1GlowHex, "#FF00FF");
        Bot2Glow = ParseColor(dto.Bot2GlowHex, "#00FFFF");
        YouColorBtn.Background  = new SolidColorBrush(YouGlow);
        Bot1ColorBtn.Background = new SolidColorBrush(Bot1Glow);
        Bot2ColorBtn.Background = new SolidColorBrush(Bot2Glow);
      }
      catch { }
    }
    public void Save()
    {
      try
      {
        var dto = BuildDto();
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
        File.WriteAllText(LayoutPath,
          JsonConvert.SerializeObject(dto, Formatting.Indented),
          new System.Text.UTF8Encoding(false));
      }
      catch { }
    }
    public BubbleLayoutFullDto BuildDto()
    {
      double youX  = ParseField(YouXBox,   890), youY  = ParseField(YouYBox,  920);
      double bot1X = ParseField(Bot1XBox,   20), bot1Y = ParseField(Bot1YBox,  20);
      double bot2X = ParseField(Bot2XBox, 1700), bot2Y = ParseField(Bot2YBox,  20);
      int    size  = (int)ParseField(AvatarSizeBox, 140);
      return new BubbleLayoutFullDto
      {
        YouX  = youX,  YouY  = youY,
        Bot1X = bot1X, Bot1Y = bot1Y,
        Bot2X = bot2X, Bot2Y = bot2Y,
        AvatarSize  = size,
        YouGlowHex  = ColorToHex(YouGlow),
        Bot1GlowHex = ColorToHex(Bot1Glow),
        Bot2GlowHex = ColorToHex(Bot2Glow),
      };
    }
    private void OnBubbleMouseDown(object sender, MouseButtonEventArgs e)
    {
      if (sender is not Grid g) return;
      _dragging      = g;
      _dragStart     = e.GetPosition(BubbleCanvas);
      _dragStartLeft = Canvas.GetLeft(g);
      _dragStartTop  = Canvas.GetTop(g);
      if (double.IsNaN(_dragStartLeft)) _dragStartLeft = 0;
      if (double.IsNaN(_dragStartTop))  _dragStartTop  = 0;
      g.CaptureMouse();
      e.Handled = true;
    }
    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
      if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
      var pos  = e.GetPosition(BubbleCanvas);
      double nx = _dragStartLeft + (pos.X - _dragStart.X);
      double ny = _dragStartTop  + (pos.Y - _dragStart.Y);
      nx = Math.Clamp(nx, 0, BubbleCanvas.Width  - _dragging.Width);
      ny = Math.Clamp(ny, 0, BubbleCanvas.Height - _dragging.Height);
      Canvas.SetLeft(_dragging, nx);
      Canvas.SetTop (_dragging, ny);
      string tag = (_dragging.Tag as string) ?? "";
      int vidX = (int)(nx * ScaleX);
      int vidY = (int)(ny * ScaleY);
      switch (tag)
      {
        case "you":  YouXBox.Text  = vidX.ToString(); YouYBox.Text  = vidY.ToString(); break;
        case "bot1": Bot1XBox.Text = vidX.ToString(); Bot1YBox.Text = vidY.ToString(); break;
        case "bot2": Bot2XBox.Text = vidX.ToString(); Bot2YBox.Text = vidY.ToString(); break;
      }
      if (_dragging == BubbleBot1 && Bot1AnchorGrid != null)      HighlightAnchorGrid(Bot1AnchorGrid, "", Bot1Glow);
      else if (_dragging == BubbleBot2 && Bot2AnchorGrid != null) HighlightAnchorGrid(Bot2AnchorGrid, "", Bot2Glow);
      else if (_dragging == BubbleYou  && YouAnchorGrid  != null) HighlightAnchorGrid(YouAnchorGrid,  "", YouGlow);
    }
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => EndDrag();
    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)    => EndDrag();
    private void EndDrag()
    {
      if (_dragging == null) return;
      _dragging.ReleaseMouseCapture();
      _dragging = null;
      Save();
    }
    private void OnPositionChanged(object sender, TextChangedEventArgs e)
    {
      if (_suppressPositionUpdate) return;
      if (sender is not TextBox tb) return;
      if (!double.TryParse(tb.Text, out double val)) return;
      string tag = (tb.Tag as string) ?? "";
      double cx = Math.Clamp(val / ScaleX, 0, BubbleCanvas.Width  - 24);
      double cy = Math.Clamp(val / ScaleY, 0, BubbleCanvas.Height - 24);
      switch (tag)
      {
        case "YouX":  Canvas.SetLeft(BubbleYou,  Math.Clamp(val / ScaleX, 0, BubbleCanvas.Width  - 24)); break;
        case "YouY":  Canvas.SetTop (BubbleYou,  Math.Clamp(val / ScaleY, 0, BubbleCanvas.Height - 24)); break;
        case "Bot1X": Canvas.SetLeft(BubbleBot1, Math.Clamp(val / ScaleX, 0, BubbleCanvas.Width  - 24)); break;
        case "Bot1Y": Canvas.SetTop (BubbleBot1, Math.Clamp(val / ScaleY, 0, BubbleCanvas.Height - 24)); break;
        case "Bot2X": Canvas.SetLeft(BubbleBot2, Math.Clamp(val / ScaleX, 0, BubbleCanvas.Width  - 24)); break;
        case "Bot2Y": Canvas.SetTop (BubbleBot2, Math.Clamp(val / ScaleY, 0, BubbleCanvas.Height - 24)); break;
      }
      if (_isLoaded) RefreshAllAnchorHighlights();
      Save();
    }
    private void OnPickYouColor(object sender, RoutedEventArgs e)
    {
      var c = PickColor(YouGlow);
      if (c.HasValue)
      {
        YouGlow = c.Value;
        YouColorBtn.Background = new SolidColorBrush(YouGlow);
        YouGlowEllipse.Fill    = new SolidColorBrush(
          Color.FromArgb(120, YouGlow.R, YouGlow.G, YouGlow.B));
        Save();
      }
    }
    private void OnPickBot1Color(object sender, RoutedEventArgs e)
    {
      var c = PickColor(Bot1Glow);
      if (c.HasValue)
      {
        Bot1Glow = c.Value;
        Bot1ColorBtn.Background = new SolidColorBrush(Bot1Glow);
        Bot1GlowEllipse.Fill    = new SolidColorBrush(
          Color.FromArgb(128, Bot1Glow.R, Bot1Glow.G, Bot1Glow.B));
        Save();
      }
    }
    private void OnPickBot2Color(object sender, RoutedEventArgs e)
    {
      var c = PickColor(Bot2Glow);
      if (c.HasValue)
      {
        Bot2Glow = c.Value;
        Bot2ColorBtn.Background = new SolidColorBrush(Bot2Glow);
        Bot2GlowEllipse.Fill    = new SolidColorBrush(
          Color.FromArgb(128, Bot2Glow.R, Bot2Glow.G, Bot2Glow.B));
        Save();
      }
    }
    private static Color? PickColor(Color initial)
    {
      using var dlg = new System.Windows.Forms.ColorDialog
      {
        Color    = System.Drawing.Color.FromArgb(initial.R, initial.G, initial.B),
        FullOpen = true
      };
      if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
      return Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
    }
    private void OnApplyToOverlay(object sender, RoutedEventArgs e)
    {
      ApplyToOverlay();
      Save();
    }
    private void ApplyToOverlay()
    {
      var dto     = BuildDto();
      var overlay = Application.Current.Windows.OfType<OverlayWindow>().FirstOrDefault();
      if (overlay == null) return;
      overlay.MoveBubbleTo("you",  dto.YouX,  dto.YouY);
      overlay.MoveBubbleTo("bot1", dto.Bot1X, dto.Bot1Y);
      overlay.MoveBubbleTo("bot2", dto.Bot2X, dto.Bot2Y);
    }
    private static void SetBubbleCanvasPos(Grid g, double cx, double cy)
    {
      g.ClearValue(Canvas.BottomProperty);
      g.ClearValue(Canvas.RightProperty);
      Canvas.SetLeft(g, Math.Max(0, cx));
      Canvas.SetTop (g, Math.Max(0, cy));
    }
    private static double ParseField(TextBox tb, double fallback)
      => double.TryParse(tb.Text, out var v) ? v : fallback;
    private static string ColorToHex(Color c)
      => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private (double x, double y) AnchorToPosition(string anchor)
    {
      int avatarSize = int.TryParse(AvatarSizeBox.Text, out var s) ? Math.Clamp(s, 50, 300) : 140;
      const int padding = 20;
      double xLeft   = padding;
      double xCenter = (1920 - avatarSize) / 2.0;
      double xRight  = 1920 - avatarSize - padding;
      double yTop    = padding;
      double yMiddle = (1080 - avatarSize) / 2.0;
      double yBottom = 1080 - avatarSize - padding;
      return anchor switch
      {
        "top-left"      => (xLeft,   yTop),
        "top-center"    => (xCenter, yTop),
        "top-right"     => (xRight,  yTop),
        "middle-left"   => (xLeft,   yMiddle),
        "center"        => (xCenter, yMiddle),
        "middle-right"  => (xRight,  yMiddle),
        "bottom-left"   => (xLeft,   yBottom),
        "bottom-center" => (xCenter, yBottom),
        "bottom-right"  => (xRight,  yBottom),
        _               => (xLeft,   yTop)
      };
    }
    private string DetectAnchor(double x, double y)
    {
      const int tolerance = 30;
      string[] anchors =
      {
        "top-left", "top-center", "top-right",
        "middle-left", "center", "middle-right",
        "bottom-left", "bottom-center", "bottom-right"
      };
      foreach (var anchor in anchors)
      {
        var (ax, ay) = AnchorToPosition(anchor);
        if (Math.Abs(x - ax) <= tolerance && Math.Abs(y - ay) <= tolerance)
          return anchor;
      }
      return "";
    }
    private void HighlightAnchorGrid(Grid grid, string activeAnchor, Color activeColor)
    {
      foreach (var child in grid.Children)
      {
        if (child is WpfButton btn)
        {
          bool isActive = string.Equals(btn.Tag as string, activeAnchor, StringComparison.OrdinalIgnoreCase);
          btn.Background  = new SolidColorBrush(isActive ? activeColor : Color.FromRgb(0x33, 0x33, 0x33));
          btn.BorderBrush = new SolidColorBrush(isActive ? Colors.White  : Color.FromRgb(0x55, 0x55, 0x55));
        }
      }
    }
    private void OnBot1AnchorClick(object sender, RoutedEventArgs e)
    {
      if (sender is not WpfButton btn || btn.Tag is not string anchor) return;
      var (x, y) = AnchorToPosition(anchor);
      _suppressPositionUpdate = true;
      Bot1XBox.Text = ((int)x).ToString();
      Bot1YBox.Text = ((int)y).ToString();
      _suppressPositionUpdate = false;
      SetBubbleCanvasPos(BubbleBot1,
        Math.Clamp(x / ScaleX, 0, BubbleCanvas.Width  - BubbleBot1.Width),
        Math.Clamp(y / ScaleY, 0, BubbleCanvas.Height - BubbleBot1.Height));
      if (Bot1AnchorGrid != null) HighlightAnchorGrid(Bot1AnchorGrid, anchor, Bot1Glow);
      ApplyToOverlay();
      Save();
    }
    private void OnBot2AnchorClick(object sender, RoutedEventArgs e)
    {
      if (sender is not WpfButton btn || btn.Tag is not string anchor) return;
      var (x, y) = AnchorToPosition(anchor);
      _suppressPositionUpdate = true;
      Bot2XBox.Text = ((int)x).ToString();
      Bot2YBox.Text = ((int)y).ToString();
      _suppressPositionUpdate = false;
      SetBubbleCanvasPos(BubbleBot2,
        Math.Clamp(x / ScaleX, 0, BubbleCanvas.Width  - BubbleBot2.Width),
        Math.Clamp(y / ScaleY, 0, BubbleCanvas.Height - BubbleBot2.Height));
      if (Bot2AnchorGrid != null) HighlightAnchorGrid(Bot2AnchorGrid, anchor, Bot2Glow);
      ApplyToOverlay();
      Save();
    }
    private void OnYouAnchorClick(object sender, RoutedEventArgs e)
    {
      if (sender is not WpfButton btn || btn.Tag is not string anchor) return;
      var (x, y) = AnchorToPosition(anchor);
      _suppressPositionUpdate = true;
      YouXBox.Text = ((int)x).ToString();
      YouYBox.Text = ((int)y).ToString();
      _suppressPositionUpdate = false;
      SetBubbleCanvasPos(BubbleYou,
        Math.Clamp(x / ScaleX, 0, BubbleCanvas.Width  - BubbleYou.Width),
        Math.Clamp(y / ScaleY, 0, BubbleCanvas.Height - BubbleYou.Height));
      if (YouAnchorGrid != null) HighlightAnchorGrid(YouAnchorGrid, anchor, YouGlow);
      ApplyToOverlay();
      Save();
    }
    private static Color ParseColor(string? hex, string fallback)
    {
      try
      {
        var h = hex ?? fallback;
        if (h.StartsWith("#")) h = h[1..];
        if (h.Length == 6)
          return Color.FromRgb(
            Convert.ToByte(h[0..2], 16),
            Convert.ToByte(h[2..4], 16),
            Convert.ToByte(h[4..6], 16));
      }
      catch { }
      return ParseColor(fallback, "#FFFFFF");
    }
  }
  public sealed class BubbleLayoutFullDto
  {
    public double YouX  { get; set; } = 890;
    public double YouY  { get; set; } = 920;
    public double Bot1X { get; set; } = 20;
    public double Bot1Y { get; set; } = 20;
    public double Bot2X { get; set; } = 1700;
    public double Bot2Y { get; set; } = 20;
    public int    AvatarSize   { get; set; } = 140;
    public string YouGlowHex   { get; set; } = "#FFD400";
    public string Bot1GlowHex  { get; set; } = "#FF00FF";
    public string Bot2GlowHex  { get; set; } = "#00FFFF";
  }
}