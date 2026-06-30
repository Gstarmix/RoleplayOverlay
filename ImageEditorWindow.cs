using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfColor      = System.Windows.Media.Color;
using WpfBrush      = System.Windows.Media.Brush;
using WpfBrushes    = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfImage      = System.Windows.Controls.Image;
using WpfPoint      = System.Windows.Point;
using WpfRectangle  = System.Windows.Shapes.Rectangle;
using WpfButton     = System.Windows.Controls.Button;
using TextBox       = System.Windows.Controls.TextBox;
using ComboBox      = System.Windows.Controls.ComboBox;
using CheckBox      = System.Windows.Controls.CheckBox;
using KeyEventArgs    = System.Windows.Input.KeyEventArgs;
using MouseEventArgs  = System.Windows.Input.MouseEventArgs;
using Cursors         = System.Windows.Input.Cursors;
using Orientation     = System.Windows.Controls.Orientation;
using Size            = System.Windows.Size;
using ColorConverter  = System.Windows.Media.ColorConverter;
using MessageBox      = System.Windows.MessageBox;
using Brushes         = System.Windows.Media.Brushes;
namespace RoleplayOverlay
{
  public sealed class ImageEditorWindow : Window
  {
    private enum Tool { Pencil, Text, Rectangle, Ellipse, Line, Arrow, Eraser }
    private abstract record UndoAction;
    private sealed record StrokeAdded(Stroke Stroke) : UndoAction;
    private sealed record StrokesErased(StrokeCollection Strokes) : UndoAction;
    private sealed record ElementAdded(UIElement Element) : UndoAction;
    private const string BgColor    = "#0B0B0C";
    private const string Bg2Color   = "#111113";
    private const string LineColor  = "#2A2B2E";
    private const string FgColor    = "#EAEAEA";
    private const string Fg2Color   = "#808080";
    private const string AccColor   = "#FF8000";
    private readonly string _imagePath;
    private readonly BitmapImage _originalImage;
    private readonly int _imgW, _imgH;
    private Tool _currentTool = Tool.Pencil;
    private WpfColor _strokeColor;
    private WpfColor _fillColor = WpfColor.FromArgb(0, 0, 0, 0);
    private double _strokeWidth;
    private string _fontFamily;
    private double _fontSize;
    private bool _textOutline;
    private bool _fillEnabled;
    private WpfColor _outlineColor = WpfColor.FromRgb(0, 0, 0);
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    private WpfPoint _shapeStart;
    private bool _isDrawingShape;
    private UIElement? _currentShapePreview;
    private TextBox? _activeTextBox;
    private readonly InkCanvas _inkCanvas;
    private readonly Canvas    _shapeCanvas;
    private readonly ScrollViewer _scrollViewer;
    private readonly Grid      _canvasHost;
    private readonly TextBlock _statusLabel;
    private readonly TextBlock _zoomLabel;
    private readonly Dictionary<Tool, WpfButton> _toolButtons = new();
    private WpfButton? _activeToolButton;
    private WpfRectangle _colorSwatch = null!;
    private WpfRectangle _fillSwatch = null!;
    private WpfRectangle _outlineSwatch = null!;
    private Slider  _strokeSlider = null!;
    private TextBlock _strokeValueLabel = null!;
    private ComboBox _fontCombo = null!;
    private TextBox  _fontSizeBox = null!;
    private CheckBox _outlineCheck = null!;
    private CheckBox _fillCheck = null!;
    private StackPanel _fontPanel = null!;
    private double _zoom = 1.0;
    private const double ZoomMin = 0.1;
    private const double ZoomMax = 5.0;
    private static readonly string[] PaletteColors = {
      "#FF0000", "#FF8000", "#FFD400", "#00FF00", "#00FFFF", "#0080FF",
      "#0000FF", "#8000FF", "#FF00FF", "#FFFFFF", "#808080", "#000000",
      "#800000", "#804000", "#808000", "#008000", "#008080", "#000080",
      "#400080", "#800080", "#C0C0C0", "#404040", "#FF8080", "#FFD080"
    };
    public ImageEditorWindow(string imagePath)
    {
      _imagePath = imagePath ?? throw new ArgumentNullException(nameof(imagePath));
      _originalImage = new BitmapImage();
      _originalImage.BeginInit();
      _originalImage.CacheOption = BitmapCacheOption.OnLoad;
      _originalImage.UriSource = new Uri(imagePath, UriKind.Absolute);
      _originalImage.EndInit();
      _originalImage.Freeze();
      _imgW = _originalImage.PixelWidth;
      _imgH = _originalImage.PixelHeight;
      LoadEditorPrefs();
      Title = $"Editeur d'image — {System.IO.Path.GetFileName(imagePath)}";
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      Width = 1280; Height = 800;
      MinWidth = 900; MinHeight = 600;
      ResizeMode = ResizeMode.CanResize;
      Background = B(BgColor);
      var rootGrid = new Grid();
      rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
      rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
      var toolbar = BuildToolbar();
      Grid.SetColumn(toolbar, 0);
      Grid.SetRow(toolbar, 0);
      rootGrid.Children.Add(toolbar);
      _canvasHost = new Grid
      {
        Width = _imgW,
        Height = _imgH,
        ClipToBounds = true
      };
      var bgImage = new WpfImage
      {
        Source = _originalImage,
        Width = _imgW,
        Height = _imgH,
        Stretch = Stretch.None
      };
      _canvasHost.Children.Add(bgImage);
      _inkCanvas = new InkCanvas
      {
        Width = _imgW,
        Height = _imgH,
        Background = Brushes.Transparent,
        EditingMode = InkCanvasEditingMode.Ink
      };
      ApplyInkAttributes();
      _inkCanvas.StrokeCollected += OnStrokeCollected;
      _inkCanvas.StrokeErased    += OnStrokeErased;
      _canvasHost.Children.Add(_inkCanvas);
      _shapeCanvas = new Canvas
      {
        Width = _imgW,
        Height = _imgH,
        Background = Brushes.Transparent,
        IsHitTestVisible = false
      };
      _canvasHost.Children.Add(_shapeCanvas);
      _scrollViewer = new ScrollViewer
      {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = B("#050506"),
        Content = _canvasHost
      };
      _scrollViewer.PreviewMouseWheel += OnCanvasMouseWheel;
      Grid.SetColumn(_scrollViewer, 1);
      Grid.SetRow(_scrollViewer, 0);
      rootGrid.Children.Add(_scrollViewer);
      var statusBar = new Border
      {
        Background = B(Bg2Color),
        BorderBrush = B(LineColor),
        BorderThickness = new Thickness(0, 1, 0, 0)
      };
      var statusPanel = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
      _statusLabel = new TextBlock
      {
        Text = $"{_imgW} x {_imgH} px",
        Foreground = B(Fg2Color),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11
      };
      DockPanel.SetDock(_statusLabel, Dock.Left);
      statusPanel.Children.Add(_statusLabel);
      _zoomLabel = new TextBlock
      {
        Text = "100%",
        Foreground = B(Fg2Color),
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
      };
      DockPanel.SetDock(_zoomLabel, Dock.Right);
      statusPanel.Children.Add(_zoomLabel);
      statusBar.Child = statusPanel;
      Grid.SetColumn(statusBar, 0);
      Grid.SetColumnSpan(statusBar, 2);
      Grid.SetRow(statusBar, 1);
      rootGrid.Children.Add(statusBar);
      Content = rootGrid;
      KeyDown += OnKeyDown;
      SetTool(Tool.Pencil);
      Loaded += (_, _) => ZoomToFit();
      Logger.Info($"[ImageEditor] Opened: {imagePath} ({_imgW}x{_imgH})");
    }
    private Border BuildToolbar()
    {
      var border = new Border
      {
        Background = B(Bg2Color),
        BorderBrush = B(LineColor),
        BorderThickness = new Thickness(0, 0, 1, 0),
        Padding = new Thickness(8)
      };
      var scroll = new ScrollViewer
      {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
      };
      var stack = new StackPanel();
      stack.Children.Add(SectionHeader("OUTILS"));
      var toolGrid = new System.Windows.Controls.Primitives.UniformGrid
      {
        Columns = 2,
        Margin = new Thickness(0, 2, 0, 4)
      };
      toolGrid.Children.Add(MakeToolButton(Tool.Pencil,    "\u270F\uFE0F"));
      toolGrid.Children.Add(MakeToolButton(Tool.Text,      "A"));
      toolGrid.Children.Add(MakeToolButton(Tool.Rectangle, "\u25AD"));
      toolGrid.Children.Add(MakeToolButton(Tool.Ellipse,   "\u25CB"));
      toolGrid.Children.Add(MakeToolButton(Tool.Line,      "\u2215"));
      toolGrid.Children.Add(MakeToolButton(Tool.Arrow,     "\u2192"));
      toolGrid.Children.Add(MakeToolButton(Tool.Eraser,    "\u2716"));
      stack.Children.Add(toolGrid);
      stack.Children.Add(SectionSep());
      var colorRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
      var colorLabel = new TextBlock { Text = "Trait", Foreground = B(Fg2Color), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
      DockPanel.SetDock(colorLabel, Dock.Left);
      colorRow.Children.Add(colorLabel);
      _colorSwatch = new WpfRectangle
      {
        Width = 24, Height = 24,
        Fill = new SolidColorBrush(_strokeColor),
        Stroke = B(FgColor), StrokeThickness = 1,
        RadiusX = 3, RadiusY = 3,
        Cursor = Cursors.Hand,
        Margin = new Thickness(0, 0, 6, 0)
      };
      _colorSwatch.MouseLeftButtonDown += OnColorSwatchClick;
      DockPanel.SetDock(_colorSwatch, Dock.Left);
      colorRow.Children.Add(_colorSwatch);
      var hexBox = new TextBox
      {
        Text = ColorToHex(_strokeColor),
        Width = 60, FontSize = 11,
        FontFamily = new WpfFontFamily("Consolas"),
        Background = B("#111113"),
        Foreground = B(FgColor),
        BorderBrush = B(LineColor),
        Padding = new Thickness(4, 2, 4, 2),
        VerticalContentAlignment = VerticalAlignment.Center
      };
      hexBox.Tag = "stroke";
      hexBox.LostFocus += OnHexBoxLostFocus;
      colorRow.Children.Add(hexBox);
      stack.Children.Add(colorRow);
      var palette = new System.Windows.Controls.Primitives.UniformGrid
      {
        Columns = 8,
        Margin = new Thickness(0, 2, 0, 4)
      };
      foreach (var hex in PaletteColors)
      {
        var swatch = new WpfRectangle
        {
          Width = 16, Height = 16,
          Fill = B(hex),
          Stroke = B("#333"),
          StrokeThickness = 0.5,
          RadiusX = 2, RadiusY = 2,
          Margin = new Thickness(1),
          Cursor = Cursors.Hand,
          Tag = hex
        };
        swatch.MouseLeftButtonDown += OnPaletteClick;
        palette.Children.Add(swatch);
      }
      stack.Children.Add(palette);
      var fillRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
      var fillLabel = new TextBlock { Text = "Remplir", Foreground = B(Fg2Color), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
      DockPanel.SetDock(fillLabel, Dock.Left);
      fillRow.Children.Add(fillLabel);
      _fillCheck = new CheckBox
      {
        IsChecked = _fillEnabled,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 4, 0),
        ToolTip = "Remplir les formes (Rectangle, Ellipse)"
      };
      _fillCheck.Checked   += (_, _) => { _fillEnabled = true; SaveEditorPrefs(); };
      _fillCheck.Unchecked += (_, _) => { _fillEnabled = false; _fillColor = WpfColor.FromArgb(0, 0, 0, 0); SaveEditorPrefs(); };
      DockPanel.SetDock(_fillCheck, Dock.Left);
      fillRow.Children.Add(_fillCheck);
      _fillSwatch = new WpfRectangle
      {
        Width = 24, Height = 24,
        Fill = _fillEnabled ? new SolidColorBrush(_fillColor) : Brushes.Transparent,
        Stroke = B(FgColor), StrokeThickness = 1,
        RadiusX = 3, RadiusY = 3,
        Cursor = Cursors.Hand
      };
      _fillSwatch.MouseLeftButtonDown += OnFillSwatchClick;
      fillRow.Children.Add(_fillSwatch);
      stack.Children.Add(fillRow);
      var thickRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
      var thickLabel = new TextBlock { Text = "Taille", Foreground = B(Fg2Color), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Width = 32 };
      DockPanel.SetDock(thickLabel, Dock.Left);
      thickRow.Children.Add(thickLabel);
      _strokeValueLabel = new TextBlock
      {
        Text = _strokeWidth.ToString("F0"),
        Foreground = B(FgColor),
        FontSize = 11,
        Width = 20,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
      };
      DockPanel.SetDock(_strokeValueLabel, Dock.Right);
      thickRow.Children.Add(_strokeValueLabel);
      _strokeSlider = new Slider
      {
        Minimum = 1, Maximum = 40,
        Value = _strokeWidth,
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
        VerticalAlignment = VerticalAlignment.Center
      };
      _strokeSlider.ValueChanged += OnStrokeWidthChanged;
      thickRow.Children.Add(_strokeSlider);
      stack.Children.Add(thickRow);
      stack.Children.Add(SectionSep());
      _fontPanel = new StackPanel();
      var fontRow = new DockPanel { Margin = new Thickness(0, 3, 0, 2) };
      _fontSizeBox = new TextBox
      {
        Text = _fontSize.ToString("F0"),
        Width = 34, FontSize = 10,
        FontFamily = new WpfFontFamily("Consolas"),
        Background = B("#111113"),
        Foreground = B(FgColor),
        BorderBrush = B(LineColor),
        Padding = new Thickness(2, 1, 2, 1),
        VerticalContentAlignment = VerticalAlignment.Center,
        ToolTip = "Taille police"
      };
      _fontSizeBox.LostFocus += OnFontSizeChanged;
      DockPanel.SetDock(_fontSizeBox, Dock.Right);
      fontRow.Children.Add(_fontSizeBox);
      _fontCombo = new ComboBox
      {
        FontSize = 10,
        VerticalContentAlignment = VerticalAlignment.Center,
        MaxWidth = 105
      };
      var fonts = new[] { "Arial", "Consolas", "Comic Sans MS", "Courier New", "Georgia",
                          "Impact", "Segoe UI", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana" };
      foreach (var f in fonts) _fontCombo.Items.Add(f);
      _fontCombo.SelectedItem = fonts.Contains(_fontFamily) ? _fontFamily : "Arial";
      _fontCombo.SelectionChanged += OnFontFamilyChanged;
      fontRow.Children.Add(_fontCombo);
      _fontPanel.Children.Add(fontRow);
      var outlineRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
      _outlineCheck = new CheckBox
      {
        Content = "Contour",
        IsChecked = _textOutline,
        Foreground = B(FgColor),
        FontSize = 10,
        VerticalContentAlignment = VerticalAlignment.Center
      };
      _outlineCheck.Checked   += (_, _) => { _textOutline = true; SaveEditorPrefs(); };
      _outlineCheck.Unchecked += (_, _) => { _textOutline = false; SaveEditorPrefs(); };
      DockPanel.SetDock(_outlineCheck, Dock.Left);
      outlineRow.Children.Add(_outlineCheck);
      _outlineSwatch = new WpfRectangle
      {
        Width = 20, Height = 20,
        Fill = new SolidColorBrush(_outlineColor),
        Stroke = B(FgColor), StrokeThickness = 1,
        RadiusX = 3, RadiusY = 3,
        Cursor = Cursors.Hand,
        Margin = new Thickness(6, 0, 0, 0)
      };
      _outlineSwatch.MouseLeftButtonDown += (_, _2) =>
      {
        var c = ShowSimpleColorDialog(_outlineColor);
        if (c != null) { _outlineColor = c.Value; _outlineSwatch.Fill = new SolidColorBrush(_outlineColor); SaveEditorPrefs(); }
      };
      DockPanel.SetDock(_outlineSwatch, Dock.Left);
      outlineRow.Children.Add(_outlineSwatch);
      _fontPanel.Children.Add(outlineRow);
      stack.Children.Add(_fontPanel);
      stack.Children.Add(SectionHeader("ACTIONS"));
      var actionsPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
      var undoBtn = MakeActionButton("\u21A9 Annuler (Ctrl+Z)");
      undoBtn.Click += (_, _) => PerformUndo();
      actionsPanel.Children.Add(undoBtn);
      var redoBtn = MakeActionButton("\u21AA Refaire (Ctrl+Y)");
      redoBtn.Click += (_, _) => PerformRedo();
      actionsPanel.Children.Add(redoBtn);
      var clearBtn = MakeActionButton("\u2716 Tout effacer");
      clearBtn.Click += (_, _) => ClearAll();
      actionsPanel.Children.Add(clearBtn);
      actionsPanel.Children.Add(new Border { Height = 8 });
      var saveBtn = MakeActionButton("\u2713 Sauvegarder (Ctrl+S)");
      saveBtn.FontWeight = FontWeights.SemiBold;
      saveBtn.Foreground = B(AccColor);
      saveBtn.Click += (_, _) => SaveImage();
      actionsPanel.Children.Add(saveBtn);
      var saveAsBtn = MakeActionButton("\U0001F4BE Enregistrer sous...");
      saveAsBtn.Click += (_, _) => SaveImageAs();
      actionsPanel.Children.Add(saveAsBtn);
      var cancelBtn = MakeActionButton("Annuler et fermer (Esc)");
      cancelBtn.Click += (_, _) => Close();
      actionsPanel.Children.Add(cancelBtn);
      stack.Children.Add(actionsPanel);
      scroll.Content = stack;
      border.Child = scroll;
      return border;
    }
    private void SetTool(Tool tool)
    {
      FinalizeActiveTextBox();
      _currentTool = tool;
      if (_activeToolButton != null)
        _activeToolButton.Background = B(Bg2Color);
      if (_toolButtons.TryGetValue(tool, out var btn))
      {
        btn.Background = new SolidColorBrush(WpfColor.FromArgb(0x55, 0xFF, 0x80, 0x00));
        _activeToolButton = btn;
      }
      switch (tool)
      {
        case Tool.Pencil:
          _inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Pen;
          ApplyInkAttributes();
          _canvasHost.MouseLeftButtonDown -= OnShapeMouseDown;
          _canvasHost.MouseMove           -= OnShapeMouseMove;
          _canvasHost.MouseLeftButtonUp   -= OnShapeMouseUp;
          break;
        case Tool.Eraser:
          _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Cross;
          _canvasHost.MouseLeftButtonDown -= OnShapeMouseDown;
          _canvasHost.MouseMove           -= OnShapeMouseMove;
          _canvasHost.MouseLeftButtonUp   -= OnShapeMouseUp;
          break;
        case Tool.Text:
        case Tool.Rectangle:
        case Tool.Ellipse:
        case Tool.Line:
        case Tool.Arrow:
          _inkCanvas.EditingMode = InkCanvasEditingMode.None;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Cross;
          _canvasHost.MouseLeftButtonDown -= OnShapeMouseDown;
          _canvasHost.MouseMove           -= OnShapeMouseMove;
          _canvasHost.MouseLeftButtonUp   -= OnShapeMouseUp;
          _canvasHost.MouseLeftButtonDown += OnShapeMouseDown;
          _canvasHost.MouseMove           += OnShapeMouseMove;
          _canvasHost.MouseLeftButtonUp   += OnShapeMouseUp;
          break;
      }
      _statusLabel.Text = $"{_imgW} x {_imgH} px  |  {GetToolName(tool)}";
      Logger.Info($"[ImageEditor] Tool changed to {_currentTool}");
    }
    private static string GetToolName(Tool t) => t switch
    {
      Tool.Pencil    => "Crayon",
      Tool.Text      => "Texte",
      Tool.Rectangle => "Rectangle",
      Tool.Ellipse   => "Ellipse",
      Tool.Line      => "Ligne",
      Tool.Arrow     => "Fleche",
      Tool.Eraser    => "Gomme",
      _              => ""
    };
    private void ApplyInkAttributes()
    {
      _inkCanvas.DefaultDrawingAttributes = new DrawingAttributes
      {
        Color = _strokeColor,
        Width = _strokeWidth,
        Height = _strokeWidth,
        FitToCurve = true,
        StylusTip = StylusTip.Ellipse
      };
    }
    private void OnStrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
      var action = new StrokeAdded(e.Stroke);
      _undoStack.Push(action);
      _redoStack.Clear();
    }
    private void OnStrokeErased(object? sender, RoutedEventArgs e)
    {
    }
    private void OnShapeMouseDown(object sender, MouseButtonEventArgs e)
    {
      if (_currentTool == Tool.Text)
      {
        OnTextClick(e);
        e.Handled = true;
        return;
      }
      if (_currentTool is not (Tool.Rectangle or Tool.Ellipse or Tool.Line or Tool.Arrow))
        return;
      _shapeStart = e.GetPosition(_canvasHost);
      _isDrawingShape = true;
      _canvasHost.CaptureMouse();
      e.Handled = true;
      Logger.Info($"[ImageEditor] Shape start at ({_shapeStart.X:F0}, {_shapeStart.Y:F0}) tool={_currentTool}");
    }
    private void OnShapeMouseMove(object sender, MouseEventArgs e)
    {
      if (!_isDrawingShape) return;
      var pos = e.GetPosition(_canvasHost);
      if (_currentShapePreview != null)
        _shapeCanvas.Children.Remove(_currentShapePreview);
      _currentShapePreview = CreateShapeElement(_shapeStart, pos, isPreview: true);
      if (_currentShapePreview != null)
        _shapeCanvas.Children.Add(_currentShapePreview);
    }
    private void OnShapeMouseUp(object sender, MouseButtonEventArgs e)
    {
      if (!_isDrawingShape) return;
      _isDrawingShape = false;
      _canvasHost.ReleaseMouseCapture();
      var pos = e.GetPosition(_canvasHost);
      if (_currentShapePreview != null)
      {
        _shapeCanvas.Children.Remove(_currentShapePreview);
        _currentShapePreview = null;
      }
      if (Math.Abs(pos.X - _shapeStart.X) < 3 && Math.Abs(pos.Y - _shapeStart.Y) < 3)
        return;
      var shape = CreateShapeElement(_shapeStart, pos, isPreview: false);
      if (shape != null)
      {
        _shapeCanvas.Children.Add(shape);
        var action = new ElementAdded(shape);
        _undoStack.Push(action);
        _redoStack.Clear();
      }
    }
    private UIElement? CreateShapeElement(WpfPoint p1, WpfPoint p2, bool isPreview)
    {
      var stroke = new SolidColorBrush(_strokeColor);
      if (isPreview) stroke.Opacity = 0.6;
      stroke.Freeze();
      WpfBrush? fill = null;
      if (_fillEnabled && _fillColor.A > 0)
      {
        fill = new SolidColorBrush(_fillColor);
        if (isPreview) ((SolidColorBrush)fill).Opacity = 0.4;
        ((SolidColorBrush)fill).Freeze();
      }
      double x = Math.Min(p1.X, p2.X);
      double y = Math.Min(p1.Y, p2.Y);
      double w = Math.Abs(p2.X - p1.X);
      double h = Math.Abs(p2.Y - p1.Y);
      switch (_currentTool)
      {
        case Tool.Rectangle:
        {
          var rect = new WpfRectangle
          {
            Width = w, Height = h,
            Stroke = stroke, StrokeThickness = _strokeWidth,
            Fill = fill ?? Brushes.Transparent
          };
          Canvas.SetLeft(rect, x);
          Canvas.SetTop(rect, y);
          return rect;
        }
        case Tool.Ellipse:
        {
          var ell = new Ellipse
          {
            Width = w, Height = h,
            Stroke = stroke, StrokeThickness = _strokeWidth,
            Fill = fill ?? Brushes.Transparent
          };
          Canvas.SetLeft(ell, x);
          Canvas.SetTop(ell, y);
          return ell;
        }
        case Tool.Line:
        {
          var line = new Line
          {
            X1 = p1.X, Y1 = p1.Y,
            X2 = p2.X, Y2 = p2.Y,
            Stroke = stroke, StrokeThickness = _strokeWidth,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
          };
          return line;
        }
        case Tool.Arrow:
        {
          return CreateArrow(p1, p2, stroke, isPreview);
        }
      }
      return null;
    }
    private UIElement CreateArrow(WpfPoint from, WpfPoint to, WpfBrush stroke, bool isPreview)
    {
      var canvas = new Canvas();
      var line = new Line
      {
        X1 = from.X, Y1 = from.Y,
        X2 = to.X, Y2 = to.Y,
        Stroke = stroke, StrokeThickness = _strokeWidth,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
      };
      canvas.Children.Add(line);
      double dx = to.X - from.X;
      double dy = to.Y - from.Y;
      double len = Math.Sqrt(dx * dx + dy * dy);
      if (len < 1) return canvas;
      double headLen = Math.Min(20 + _strokeWidth * 2, len * 0.4);
      double headAngle = Math.PI / 6;
      double angle = Math.Atan2(dy, dx);
      double ax1 = to.X - headLen * Math.Cos(angle - headAngle);
      double ay1 = to.Y - headLen * Math.Sin(angle - headAngle);
      double ax2 = to.X - headLen * Math.Cos(angle + headAngle);
      double ay2 = to.Y - headLen * Math.Sin(angle + headAngle);
      var head = new Polygon
      {
        Points = new PointCollection { to, new WpfPoint(ax1, ay1), new WpfPoint(ax2, ay2) },
        Fill = stroke,
        Stroke = stroke,
        StrokeThickness = 1
      };
      canvas.Children.Add(head);
      return canvas;
    }
    private void OnTextClick(MouseButtonEventArgs e)
    {
      FinalizeActiveTextBox();
      var pos = e.GetPosition(_canvasHost);
      _activeTextBox = new TextBox
      {
        Text = "",
        FontSize = _fontSize,
        FontFamily = new WpfFontFamily(_fontFamily),
        Foreground = new SolidColorBrush(_strokeColor),
        Background = new SolidColorBrush(WpfColor.FromArgb(0x40, 0, 0, 0)),
        BorderBrush = B(AccColor),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(4, 2, 4, 2),
        MinWidth = 100,
        MinHeight = 30,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap
      };
      Canvas.SetLeft(_activeTextBox, pos.X);
      Canvas.SetTop(_activeTextBox, pos.Y);
      _shapeCanvas.IsHitTestVisible = true;
      _shapeCanvas.Children.Add(_activeTextBox);
      Logger.Info($"[ImageEditor] TextBox placed at ({pos.X:F0}, {pos.Y:F0}), shapeCanvas.IsHitTestVisible={_shapeCanvas.IsHitTestVisible}");
      Dispatcher.BeginInvoke(() =>
      {
        if (_activeTextBox != null)
        {
          _activeTextBox.Focus();
          Keyboard.Focus(_activeTextBox);
        }
      }, System.Windows.Threading.DispatcherPriority.Input);
      _activeTextBox.LostFocus += OnTextBoxLostFocus;
      _activeTextBox.KeyDown   += OnTextBoxKeyDown;
    }
    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
      {
        if (_activeTextBox != null)
        {
          _shapeCanvas.Children.Remove(_activeTextBox);
          _shapeCanvas.IsHitTestVisible = false;
          _activeTextBox = null;
        }
        e.Handled = true;
      }
      else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
      {
        FinalizeActiveTextBox();
        e.Handled = true;
      }
    }
    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
      Dispatcher.BeginInvoke(new Action(() =>
      {
        if (_activeTextBox != null && !_activeTextBox.IsFocused)
          FinalizeActiveTextBox();
      }), System.Windows.Threading.DispatcherPriority.Background);
    }
    private void FinalizeActiveTextBox()
    {
      if (_activeTextBox == null) return;
      var text = _activeTextBox.Text?.Trim();
      double left = Canvas.GetLeft(_activeTextBox);
      double top  = Canvas.GetTop(_activeTextBox);
      _shapeCanvas.Children.Remove(_activeTextBox);
      _shapeCanvas.IsHitTestVisible = false;
      _activeTextBox = null;
      if (string.IsNullOrWhiteSpace(text)) return;
      UIElement element;
      if (_textOutline)
      {
        element = CreateOutlinedText(text, left, top);
      }
      else
      {
        var tb = new TextBlock
        {
          Text = text,
          FontSize = _fontSize,
          FontFamily = new WpfFontFamily(_fontFamily),
          Foreground = new SolidColorBrush(_strokeColor),
          FontWeight = FontWeights.Bold
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        element = tb;
      }
      _shapeCanvas.Children.Add(element);
      var action = new ElementAdded(element);
      _undoStack.Push(action);
      _redoStack.Clear();
    }
    private UIElement CreateOutlinedText(string text, double left, double top)
    {
      var typeface = new Typeface(
        new WpfFontFamily(_fontFamily),
        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
      var formatted = new FormattedText(
        text,
        CultureInfo.InvariantCulture,
        System.Windows.FlowDirection.LeftToRight,
        typeface,
        _fontSize,
        new SolidColorBrush(_strokeColor),
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
      var geometry = formatted.BuildGeometry(new WpfPoint(0, 0));
      var path = new System.Windows.Shapes.Path
      {
        Data = geometry,
        Fill = new SolidColorBrush(_strokeColor),
        Stroke = new SolidColorBrush(_outlineColor),
        StrokeThickness = Math.Max(2, _fontSize / 12)
      };
      Canvas.SetLeft(path, left);
      Canvas.SetTop(path, top);
      return path;
    }
    private void PerformUndo()
    {
      if (_undoStack.Count == 0) return;
      var action = _undoStack.Pop();
      switch (action)
      {
        case StrokeAdded sa:
          _inkCanvas.Strokes.Remove(sa.Stroke);
          break;
        case ElementAdded ea:
          _shapeCanvas.Children.Remove(ea.Element);
          break;
        case StrokesErased se:
          _inkCanvas.Strokes.Add(se.Strokes);
          break;
      }
      _redoStack.Push(action);
    }
    private void PerformRedo()
    {
      if (_redoStack.Count == 0) return;
      var action = _redoStack.Pop();
      switch (action)
      {
        case StrokeAdded sa:
          _inkCanvas.Strokes.Add(sa.Stroke);
          break;
        case ElementAdded ea:
          _shapeCanvas.Children.Add(ea.Element);
          break;
        case StrokesErased se:
          _inkCanvas.Strokes.Remove(se.Strokes);
          break;
      }
      _undoStack.Push(action);
    }
    private void ClearAll()
    {
      var result = MessageBox.Show(this,
        "Effacer toutes les annotations ?",
        "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
      if (result != MessageBoxResult.Yes) return;
      _inkCanvas.Strokes.Clear();
      _shapeCanvas.Children.Clear();
      _undoStack.Clear();
      _redoStack.Clear();
    }
    private void SaveImage()
    {
      FinalizeActiveTextBox();
      var bakPath = _imagePath + ".bak";
      if (!File.Exists(bakPath))
      {
        try { File.Copy(_imagePath, bakPath, overwrite: false); }
        catch (Exception ex) { Logger.Warn($"[ImageEditor] Backup failed: {ex.Message}"); }
      }
      FlattenAndSave(_imagePath);
      _statusLabel.Text = $"Sauvegarde OK — {System.IO.Path.GetFileName(_imagePath)}";
      Logger.Info($"[ImageEditor] Saved: {_imagePath}");
    }
    private void SaveImageAs()
    {
      FinalizeActiveTextBox();
      var dlg = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "PNG|*.png|JPEG|*.jpg",
        FileName = System.IO.Path.GetFileName(_imagePath),
        InitialDirectory = System.IO.Path.GetDirectoryName(_imagePath)
      };
      if (dlg.ShowDialog(this) == true)
      {
        FlattenAndSave(dlg.FileName);
        _statusLabel.Text = $"Enregistre sous: {dlg.FileName}";
        Logger.Info($"[ImageEditor] SaveAs: {dlg.FileName}");
      }
    }
    private void FlattenAndSave(string outputPath)
    {
      Logger.Info($"[ImageEditor] FlattenAndSave: strokes={_inkCanvas.Strokes.Count}, shapes={_shapeCanvas.Children.Count}");
      var rtb = new RenderTargetBitmap(_imgW, _imgH, 96, 96, PixelFormats.Pbgra32);
      var dv = new DrawingVisual();
      using (var ctx = dv.RenderOpen())
      {
        ctx.DrawImage(_originalImage, new Rect(0, 0, _imgW, _imgH));
      }
      rtb.Render(dv);
      if (_inkCanvas.Strokes.Count > 0)
      {
        var strokeDv = new DrawingVisual();
        using (var ctx = strokeDv.RenderOpen())
        {
          _inkCanvas.Strokes.Draw(ctx);
        }
        rtb.Render(strokeDv);
      }
      if (_shapeCanvas.Children.Count > 0)
      {
        _shapeCanvas.Measure(new Size(_imgW, _imgH));
        _shapeCanvas.Arrange(new Rect(0, 0, _imgW, _imgH));
        _shapeCanvas.UpdateLayout();
        rtb.Render(_shapeCanvas);
      }
      var ext = System.IO.Path.GetExtension(outputPath).ToLowerInvariant();
      BitmapEncoder encoder;
      if (ext == ".jpg" || ext == ".jpeg")
        encoder = new JpegBitmapEncoder { QualityLevel = 95 };
      else
        encoder = new PngBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(rtb));
      using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
      encoder.Save(fs);
    }
    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
      if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
      double factor = e.Delta > 0 ? 1.1 : 0.9;
      _zoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
      var transform = new ScaleTransform(_zoom, _zoom);
      _canvasHost.LayoutTransform = transform;
      _zoomLabel.Text = $"{(_zoom * 100):F0}%";
      e.Handled = true;
    }
    private void ZoomToFit()
    {
      var viewW = _scrollViewer.ActualWidth - 20;
      var viewH = _scrollViewer.ActualHeight - 20;
      if (viewW <= 0 || viewH <= 0) return;
      double scaleX = viewW / _imgW;
      double scaleY = viewH / _imgH;
      _zoom = Math.Min(scaleX, scaleY);
      _zoom = Math.Clamp(_zoom, ZoomMin, ZoomMax);
      _canvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
      _zoomLabel.Text = $"{(_zoom * 100):F0}%";
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
      if (_activeTextBox != null && _activeTextBox.IsFocused) return;
      if (e.Key == Key.Escape)
      {
        Close();
        e.Handled = true;
      }
      else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        PerformUndo();
        e.Handled = true;
      }
      else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        PerformRedo();
        e.Handled = true;
      }
      else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        SaveImage();
        e.Handled = true;
      }
      else if (e.Key == Key.P) SetTool(Tool.Pencil);
      else if (e.Key == Key.T) SetTool(Tool.Text);
      else if (e.Key == Key.R) SetTool(Tool.Rectangle);
      else if (e.Key == Key.C) SetTool(Tool.Ellipse);
      else if (e.Key == Key.L) SetTool(Tool.Line);
      else if (e.Key == Key.A) SetTool(Tool.Arrow);
      else if (e.Key == Key.E) SetTool(Tool.Eraser);
      else if (e.Key == Key.OemPlus && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        _zoom = Math.Clamp(_zoom * 1.2, ZoomMin, ZoomMax);
        _canvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        _zoomLabel.Text = $"{(_zoom * 100):F0}%";
        e.Handled = true;
      }
      else if (e.Key == Key.OemMinus && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        _zoom = Math.Clamp(_zoom * 0.8, ZoomMin, ZoomMax);
        _canvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        _zoomLabel.Text = $"{(_zoom * 100):F0}%";
        e.Handled = true;
      }
      else if (e.Key == Key.D0 && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        ZoomToFit();
        e.Handled = true;
      }
    }
    private void OnStrokeWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      _strokeWidth = e.NewValue;
      _strokeValueLabel.Text = _strokeWidth.ToString("F0");
      ApplyInkAttributes();
      SaveEditorPrefs();
    }
    private void OnPaletteClick(object sender, MouseButtonEventArgs e)
    {
      if (sender is WpfRectangle rect && rect.Tag is string hex)
      {
        _strokeColor = (WpfColor)ColorConverter.ConvertFromString(hex);
        _colorSwatch.Fill = new SolidColorBrush(_strokeColor);
        ApplyInkAttributes();
        SaveEditorPrefs();
      }
    }
    private void OnColorSwatchClick(object sender, MouseButtonEventArgs e)
    {
      var hex = ShowSimpleColorDialog(_strokeColor);
      if (hex != null)
      {
        _strokeColor = hex.Value;
        _colorSwatch.Fill = new SolidColorBrush(_strokeColor);
        ApplyInkAttributes();
        SaveEditorPrefs();
      }
    }
    private void OnFillSwatchClick(object sender, MouseButtonEventArgs e)
    {
      if (!_fillEnabled) return;
      var hex = ShowSimpleColorDialog(_fillColor);
      if (hex != null)
      {
        _fillColor = hex.Value;
        _fillSwatch.Fill = new SolidColorBrush(_fillColor);
        SaveEditorPrefs();
      }
    }
    private void OnHexBoxLostFocus(object sender, RoutedEventArgs e)
    {
      if (sender is not TextBox tb) return;
      try
      {
        var color = (WpfColor)ColorConverter.ConvertFromString(tb.Text);
        if ((string?)tb.Tag == "stroke")
        {
          _strokeColor = color;
          _colorSwatch.Fill = new SolidColorBrush(color);
          ApplyInkAttributes();
        }
        SaveEditorPrefs();
      }
      catch { tb.Text = ColorToHex(_strokeColor); }
    }
    private void OnFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_fontCombo.SelectedItem is string f)
      {
        _fontFamily = f;
        SaveEditorPrefs();
      }
    }
    private void OnFontSizeChanged(object sender, RoutedEventArgs e)
    {
      if (double.TryParse(_fontSizeBox.Text, out var size) && size >= 6 && size <= 300)
      {
        _fontSize = size;
        SaveEditorPrefs();
      }
      else
      {
        _fontSizeBox.Text = _fontSize.ToString("F0");
      }
    }
    private WpfColor? ShowSimpleColorDialog(WpfColor current)
    {
      using var dlg = new System.Windows.Forms.ColorDialog
      {
        FullOpen = true,
        Color = System.Drawing.Color.FromArgb(current.A, current.R, current.G, current.B)
      };
      if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
      {
        return WpfColor.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
      }
      return null;
    }
    private void LoadEditorPrefs()
    {
      _strokeColor  = ParseColor(UserPrefs.ImageEditorColor, WpfColor.FromRgb(0xFF, 0x00, 0x00));
      _strokeWidth  = UserPrefs.ImageEditorStrokeWidth > 0 ? UserPrefs.ImageEditorStrokeWidth : 3;
      _fontFamily   = string.IsNullOrWhiteSpace(UserPrefs.ImageEditorFontFamily)
                      ? "Arial" : UserPrefs.ImageEditorFontFamily!;
      _fontSize     = UserPrefs.ImageEditorFontSize > 0 ? UserPrefs.ImageEditorFontSize : 32;
      _textOutline  = UserPrefs.ImageEditorTextOutline;
      _fillEnabled  = UserPrefs.ImageEditorFillEnabled;
      _fillColor    = ParseColor(UserPrefs.ImageEditorFillColor, WpfColor.FromArgb(0, 0, 0, 0));
      _outlineColor = ParseColor(UserPrefs.ImageEditorOutlineColor, WpfColor.FromRgb(0, 0, 0));
    }
    private void SaveEditorPrefs()
    {
      UserPrefs.ImageEditorColor       = ColorToHex(_strokeColor);
      UserPrefs.ImageEditorStrokeWidth = _strokeWidth;
      UserPrefs.ImageEditorFontFamily  = _fontFamily;
      UserPrefs.ImageEditorFontSize    = _fontSize;
      UserPrefs.ImageEditorTextOutline = _textOutline;
      UserPrefs.ImageEditorFillEnabled = _fillEnabled;
      UserPrefs.ImageEditorFillColor   = ColorToHex(_fillColor);
      UserPrefs.ImageEditorOutlineColor = ColorToHex(_outlineColor);
      UserPrefs.Save();
    }
    private static WpfColor ParseColor(string? hex, WpfColor fallback)
    {
      if (string.IsNullOrWhiteSpace(hex)) return fallback;
      try { return (WpfColor)ColorConverter.ConvertFromString(hex); }
      catch { return fallback; }
    }
    private static string ColorToHex(WpfColor c)
      => c.A < 255 ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private WpfButton MakeToolButton(Tool tool, string label)
    {
      var btn = new WpfButton
      {
        Content = label,
        Height = 28,
        Margin = new Thickness(1),
        FontSize = 13,
        Foreground = B(FgColor),
        Background = B(Bg2Color),
        BorderBrush = B(LineColor),
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand,
        Tag = tool,
        ToolTip = GetToolName(tool)
      };
      btn.Click += (_, _) => SetTool(tool);
      _toolButtons[tool] = btn;
      return btn;
    }
    private static WpfButton MakeActionButton(string label)
    {
      return new WpfButton
      {
        Content = label,
        Height = 22,
        Margin = new Thickness(0, 1, 0, 1),
        FontSize = 10,
        Foreground = B(FgColor),
        Background = B(Bg2Color),
        BorderBrush = B(LineColor),
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand,
        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
        Padding = new Thickness(6, 0, 6, 0)
      };
    }
    private static TextBlock SectionHeader(string text)
    {
      return new TextBlock
      {
        Text = text,
        FontSize = 9,
        FontWeight = FontWeights.SemiBold,
        Foreground = B(Fg2Color),
        Margin = new Thickness(0, 4, 0, 0)
      };
    }
    private static Border SectionSep()
    {
      return new Border
      {
        Height = 1,
        Background = B(LineColor),
        Margin = new Thickness(0, 4, 0, 4),
        Opacity = 0.5
      };
    }
    private static SolidColorBrush B(string hex)
      => new SolidColorBrush((WpfColor)ColorConverter.ConvertFromString(hex));
    public bool WasSaved { get; private set; }
    protected override void OnClosed(EventArgs e)
    {
      base.OnClosed(e);
      Logger.Info($"[ImageEditor] Closed: {_imagePath}");
    }
  }
}