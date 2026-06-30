using WpfColor       = System.Windows.Media.Color;
using WpfBrush       = System.Windows.Media.Brush;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfImage       = System.Windows.Controls.Image;
using WpfButton      = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRectangle   = System.Windows.Shapes.Rectangle;
using WpfCursor      = System.Windows.Input.Cursor;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfMouseArgs   = System.Windows.Input.MouseEventArgs;
using WpfPoint       = System.Windows.Point;
using WpfVector      = System.Windows.Vector;
using WpfHAlign      = System.Windows.HorizontalAlignment;
using WpfVAlign      = System.Windows.VerticalAlignment;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace RoleplayOverlay
{
  public sealed class CropEditorWindow : Window
  {
    private enum HZ { None, TL, TM, TR, LM, RM, BL, BM, BR, Move }
    private readonly BitmapImage _frame;
    private readonly int         _srcW, _srcH;
    private int _cropL, _cropT, _cropR, _cropB;
    public event Action<int, int, int, int>? CropChanged;
    private HZ       _dragging = HZ.None;
    private WpfPoint _dragStart;
    private int      _dL0, _dT0, _dR0, _dB0;
    private readonly WpfImage     _img;
    private readonly Canvas       _cvs;
    private readonly WpfRectangle _mTop, _mBot, _mLeft, _mRight;
    private readonly WpfRectangle _cropBorder;
    private readonly WpfRectangle[] _hRects = new WpfRectangle[8];
    private readonly TextBlock    _lblOverlay;
    private readonly TextBlock    _lblL, _lblT, _lblR, _lblB;
    private const double TOLE   = 18;
    private const double HSIZE  = 10;
    public CropEditorWindow(
      BitmapImage frame,
      int initL, int initT, int initR, int initB,
      string title = "Recadrage interactif")
    {
      _frame = frame ?? throw new ArgumentNullException(nameof(frame));
      _srcW  = frame.PixelWidth;
      _srcH  = frame.PixelHeight;
      _cropL = initL; _cropT = initT; _cropR = initR; _cropB = initB;
      Title                 = $"RoleplayOverlay — ✂ {title}";
      Background            = new SolidColorBrush(WpfColor.FromRgb(0x0B, 0x0B, 0x0C));
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ResizeMode            = ResizeMode.CanResize;
      WindowStyle           = WindowStyle.SingleBorderWindow;
      MinWidth = 640; MinHeight = 480;
      var wa = SystemParameters.WorkArea;
      Width  = wa.Width  * 0.84;
      Height = wa.Height * 0.88;
      _img = new WpfImage
      {
        Source              = _frame,
        Stretch             = Stretch.Uniform,
        HorizontalAlignment = WpfHAlign.Center,
        VerticalAlignment   = WpfVAlign.Center,
        IsHitTestVisible    = false,
      };
      System.Windows.Media.RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
      _cvs = new Canvas
      {
        Background       = WpfBrushes.Transparent,
        IsHitTestVisible = true,
      };
      var mb = new SolidColorBrush(WpfColor.FromArgb(0x40, 0, 0, 0));
      _mTop   = NewRect(mb, false);
      _mBot   = NewRect(mb, false);
      _mLeft  = NewRect(mb, false);
      _mRight = NewRect(mb, false);
      _cropBorder = new WpfRectangle
      {
        Stroke           = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xA0, 0x40)),
        StrokeThickness  = 1.5,
        StrokeDashArray  = new DoubleCollection { 8, 4 },
        Fill             = WpfBrushes.Transparent,
        IsHitTestVisible = false,
      };
      for (int i = 0; i < 8; i++)
      {
        _hRects[i] = new WpfRectangle
        {
          Width            = HSIZE, Height = HSIZE,
          Fill             = WpfBrushes.White,
          Stroke           = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xA0, 0x40)),
          StrokeThickness  = 1.5,
          IsHitTestVisible = false,
          RadiusX = 2, RadiusY = 2,
        };
      }
      _lblOverlay = new TextBlock
      {
        Foreground       = new SolidColorBrush(WpfColor.FromArgb(0xCC, 0xFF, 0xA0, 0x40)),
        FontFamily       = new WpfFontFamily("Consolas"),
        FontSize         = 11, FontWeight = FontWeights.Bold,
        IsHitTestVisible = false,
      };
      foreach (var m in new[] { _mTop, _mBot, _mLeft, _mRight }) _cvs.Children.Add(m);
      _cvs.Children.Add(_cropBorder);
      foreach (var h in _hRects) _cvs.Children.Add(h);
      _cvs.Children.Add(_lblOverlay);
      _cvs.MouseMove  += OnMouseMove;
      _cvs.MouseDown  += OnMouseDown;
      _cvs.MouseUp    += OnMouseUp;
      _cvs.MouseLeave += (_, _2) => { if (_dragging == HZ.None) _cvs.Cursor = WpfCursors.Arrow; };
      var imgGrid = new Grid();
      imgGrid.Children.Add(_img);
      imgGrid.Children.Add(_cvs);
      _lblL = ValLbl("L"); _lblT = ValLbl("T");
      _lblR = ValLbl("R"); _lblB = ValLbl("B");
      var btnReset = MkBtn("✕ Reset",    WpfColor.FromRgb(0x1C, 0x1D, 0x20));
      var btnClose = MkBtn("✓ Fermer",   WpfColor.FromRgb(0xE8, 0x41, 0x18));
      btnReset.Click += (_, _2) => { _cropL = _cropT = _cropR = _cropB = 0; Refresh(); CropChanged?.Invoke(0, 0, 0, 0); };
      btnClose.Click += (_, _2) => Close();
      var bar = new StackPanel
      {
        Orientation         = WpfOrientation.Horizontal,
        HorizontalAlignment = WpfHAlign.Center,
        Margin              = new Thickness(0, 6, 0, 6),
      };
      bar.Children.Add(MkBarLbl("L :")); bar.Children.Add(_lblL);
      bar.Children.Add(MkBarLbl("T :")); bar.Children.Add(_lblT);
      bar.Children.Add(MkBarLbl("R :")); bar.Children.Add(_lblR);
      bar.Children.Add(MkBarLbl("B :")); bar.Children.Add(_lblB);
      bar.Children.Add(btnReset);
      bar.Children.Add(btnClose);
      var hint = new TextBlock
      {
        Text                = "Tirer les poignées blanches · Tirer l'intérieur pour déplacer · Esc = fermer",
        Foreground          = new SolidColorBrush(WpfColor.FromRgb(0x55, 0x55, 0x55)),
        FontSize            = 9,
        HorizontalAlignment = WpfHAlign.Center,
        Margin              = new Thickness(0, 0, 0, 4),
      };
      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      Grid.SetRow(imgGrid, 0); root.Children.Add(imgGrid);
      Grid.SetRow(bar,     1); root.Children.Add(bar);
      Grid.SetRow(hint,    2); root.Children.Add(hint);
      Content = root;
      _cvs.SizeChanged += (_, _2) => Refresh();
      Loaded           += (_, _2) => Refresh();
      KeyDown          += (_, e)  => { if (e.Key == Key.Escape) Close(); };
    }
    private Rect GetImgRect()
    {
      double cw = _cvs.ActualWidth;
      double ch = _cvs.ActualHeight;
      if (cw <= 0 || ch <= 0 || _srcW <= 0 || _srcH <= 0) return new Rect();
      double scale  = Math.Min(cw / _srcW, ch / _srcH);
      double renderW = _srcW * scale;
      double renderH = _srcH * scale;
      double offX    = (cw - renderW) / 2;
      double offY    = (ch - renderH) / 2;
      return new Rect(offX, offY, renderW, renderH);
    }
    private Rect GetCropRectCanvas(Rect img)
    {
      double sx = img.Width  / _srcW;
      double sy = img.Height / _srcH;
      double lx = img.Left   + _cropL * sx;
      double ty = img.Top    + _cropT * sy;
      double rx = img.Right  - _cropR * sx;
      double by = img.Bottom - _cropB * sy;
      return new Rect(new WpfPoint(lx, ty), new WpfPoint(rx, by));
    }
    private HZ HitTest(WpfPoint p, Rect cr)
    {
      bool nL  = Math.Abs(p.X - cr.Left)               < TOLE;
      bool nR  = Math.Abs(p.X - cr.Right)              < TOLE;
      bool nT  = Math.Abs(p.Y - cr.Top)                < TOLE;
      bool nB  = Math.Abs(p.Y - cr.Bottom)             < TOLE;
      bool nMX = Math.Abs(p.X - (cr.Left + cr.Width  / 2)) < TOLE;
      bool nMY = Math.Abs(p.Y - (cr.Top  + cr.Height / 2)) < TOLE;
      bool inX = p.X > cr.Left - TOLE && p.X < cr.Right  + TOLE;
      bool inY = p.Y > cr.Top  - TOLE && p.Y < cr.Bottom + TOLE;
      if (!inX || !inY) return HZ.None;
      if (nT && nL)  return HZ.TL;
      if (nT && nR)  return HZ.TR;
      if (nB && nL)  return HZ.BL;
      if (nB && nR)  return HZ.BR;
      if (nT && nMX) return HZ.TM;
      if (nB && nMX) return HZ.BM;
      if (nL && nMY) return HZ.LM;
      if (nR && nMY) return HZ.RM;
      if (p.X > cr.Left && p.X < cr.Right && p.Y > cr.Top && p.Y < cr.Bottom)
        return HZ.Move;
      return HZ.None;
    }
    private static WpfCursor CursorFor(HZ z) => z switch
    {
      HZ.TL   => WpfCursors.SizeNWSE,
      HZ.BR   => WpfCursors.SizeNWSE,
      HZ.TR   => WpfCursors.SizeNESW,
      HZ.BL   => WpfCursors.SizeNESW,
      HZ.TM   => WpfCursors.SizeNS,
      HZ.BM   => WpfCursors.SizeNS,
      HZ.LM   => WpfCursors.SizeWE,
      HZ.RM   => WpfCursors.SizeWE,
      HZ.Move => WpfCursors.SizeAll,
      _       => WpfCursors.Arrow,
    };
    private void OnMouseMove(object sender, WpfMouseArgs e)
    {
      var p = e.GetPosition(_cvs);
      if (_dragging != HZ.None)
      {
        ApplyDrag(p - _dragStart);
        return;
      }
      var ir = GetImgRect();
      if (ir.Width <= 0) return;
      var cr = GetCropRectCanvas(ir);
      var z  = HitTest(p, cr);
      _cvs.Cursor = CursorFor(z);
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton != MouseButton.Left) return;
      var p  = e.GetPosition(_cvs);
      var ir = GetImgRect(); if (ir.Width <= 0) return;
      var cr = GetCropRectCanvas(ir);
      var z  = HitTest(p, cr);
      if (z == HZ.None) return;
      _dragging = z;
      _dragStart = p;
      _dL0 = _cropL; _dT0 = _cropT; _dR0 = _cropR; _dB0 = _cropB;
      _cvs.CaptureMouse();
      e.Handled = true;
    }
    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
      if (e.ChangedButton != MouseButton.Left) return;
      _dragging = HZ.None;
      _cvs.ReleaseMouseCapture();
    }
    private void ApplyDrag(WpfVector delta)
    {
      var ir = GetImgRect(); if (ir.Width <= 0) return;
      double sx = _srcW / ir.Width;
      double sy = _srcH / ir.Height;
      int dx = (int)Math.Round(delta.X * sx);
      int dy = (int)Math.Round(delta.Y * sy);
      const int minGap = 20;
      switch (_dragging)
      {
        case HZ.TL:
          _cropL = Clamp(_dL0 + dx, 0, _srcW - _dR0 - minGap);
          _cropT = Clamp(_dT0 + dy, 0, _srcH - _dB0 - minGap); break;
        case HZ.TR:
          _cropR = Clamp(_dR0 - dx, 0, _srcW - _dL0 - minGap);
          _cropT = Clamp(_dT0 + dy, 0, _srcH - _dB0 - minGap); break;
        case HZ.BL:
          _cropL = Clamp(_dL0 + dx, 0, _srcW - _dR0 - minGap);
          _cropB = Clamp(_dB0 - dy, 0, _srcH - _dT0 - minGap); break;
        case HZ.BR:
          _cropR = Clamp(_dR0 - dx, 0, _srcW - _dL0 - minGap);
          _cropB = Clamp(_dB0 - dy, 0, _srcH - _dT0 - minGap); break;
        case HZ.TM:
          _cropT = Clamp(_dT0 + dy, 0, _srcH - _dB0 - minGap); break;
        case HZ.BM:
          _cropB = Clamp(_dB0 - dy, 0, _srcH - _dT0 - minGap); break;
        case HZ.LM:
          _cropL = Clamp(_dL0 + dx, 0, _srcW - _dR0 - minGap); break;
        case HZ.RM:
          _cropR = Clamp(_dR0 - dx, 0, _srcW - _dL0 - minGap); break;
        case HZ.Move:
          int nl = _dL0 + dx, nr = _dR0 - dx;
          int nt = _dT0 + dy, nb = _dB0 - dy;
          if (nl >= 0 && nr >= 0) { _cropL = nl; _cropR = nr; }
          if (nt >= 0 && nb >= 0) { _cropT = nt; _cropB = nb; }
          break;
      }
      Refresh();
      CropChanged?.Invoke(_cropL, _cropT, _cropR, _cropB);
    }
    private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
    private void Refresh()
    {
      var ir = GetImgRect();
      if (ir.Width <= 0 || ir.Height <= 0) return;
      double sx = ir.Width  / _srcW;
      double sy = ir.Height / _srcH;
      double lx = ir.Left   + _cropL * sx;
      double ty = ir.Top    + _cropT * sy;
      double rx = ir.Right  - _cropR * sx;
      double by = ir.Bottom - _cropB * sy;
      if (rx < lx) rx = lx;
      if (by < ty) by = ty;
      Rect(_mTop,   ir.Left, ir.Top,   ir.Width,        ty - ir.Top);
      Rect(_mBot,   ir.Left, by,        ir.Width,        ir.Bottom - by);
      Rect(_mLeft,  ir.Left, ty,        lx - ir.Left,    by - ty);
      Rect(_mRight, rx,     ty,         ir.Right - rx,   by - ty);
      Canvas.SetLeft(_cropBorder, lx); Canvas.SetTop(_cropBorder, ty);
      _cropBorder.Width  = Math.Max(0, rx - lx);
      _cropBorder.Height = Math.Max(0, by - ty);
      double cx = (lx + rx) / 2;
      double cy = (ty + by) / 2;
      PlaceH(0, lx,          ty);
      PlaceH(2, rx - HSIZE,  ty);
      PlaceH(5, lx,          by - HSIZE);
      PlaceH(7, rx - HSIZE,  by - HSIZE);
      PlaceH(1, cx - HSIZE / 2, ty);
      PlaceH(6, cx - HSIZE / 2, by - HSIZE);
      PlaceH(3, lx,          cy - HSIZE / 2);
      PlaceH(4, rx - HSIZE,  cy - HSIZE / 2);
      _lblOverlay.Text = $"L:{_cropL}  T:{_cropT}  R:{_cropR}  B:{_cropB}";
      Canvas.SetLeft(_lblOverlay, lx + 6);
      Canvas.SetTop (_lblOverlay, ty + 4);
      _lblL.Text = _cropL.ToString();
      _lblT.Text = _cropT.ToString();
      _lblR.Text = _cropR.ToString();
      _lblB.Text = _cropB.ToString();
    }
    private static void Rect(WpfRectangle rc, double x, double y, double w, double h)
    {
      Canvas.SetLeft(rc, x); Canvas.SetTop(rc, y);
      rc.Width = Math.Max(0, w); rc.Height = Math.Max(0, h);
    }
    private void PlaceH(int i, double x, double y)
    {
      Canvas.SetLeft(_hRects[i], x);
      Canvas.SetTop (_hRects[i], y);
    }
    private static WpfRectangle NewRect(WpfBrush fill, bool hitTest)
      => new WpfRectangle { Fill = fill, IsHitTestVisible = hitTest };
    private static TextBlock ValLbl(string id) => new TextBlock
    {
      Foreground        = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xA0, 0x40)),
      FontFamily        = new WpfFontFamily("Consolas"),
      FontSize          = 12, FontWeight = FontWeights.Bold,
      Width             = 42, TextAlignment = TextAlignment.Right,
      VerticalAlignment = WpfVAlign.Center,
    };
    private static WpfButton MkBtn(string text, WpfColor bg) => new WpfButton
    {
      Content     = text,
      Padding     = new Thickness(14, 6, 14, 6),
      Margin      = new Thickness(12, 0, 0, 0),
      Background  = new SolidColorBrush(bg),
      Foreground  = WpfBrushes.White,
      BorderBrush = WpfBrushes.Transparent,
      FontSize    = 11, FontWeight = FontWeights.SemiBold,
      Cursor      = WpfCursors.Hand,
    };
    private static TextBlock MkBarLbl(string text) => new TextBlock
    {
      Text              = text,
      Foreground        = new SolidColorBrush(WpfColor.FromRgb(0x9A, 0x9A, 0x9A)),
      VerticalAlignment = WpfVAlign.Center,
      Margin            = new Thickness(14, 0, 4, 0),
    };
  }
}