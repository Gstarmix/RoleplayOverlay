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
using ListBox       = System.Windows.Controls.ListBox;
using ListBoxItem   = System.Windows.Controls.ListBoxItem;
using CheckBox      = System.Windows.Controls.CheckBox;
using KeyEventArgs    = System.Windows.Input.KeyEventArgs;
using MouseEventArgs  = System.Windows.Input.MouseEventArgs;
using Cursors         = System.Windows.Input.Cursors;
using WpfCursor       = System.Windows.Input.Cursor;
using Orientation     = System.Windows.Controls.Orientation;
using Size            = System.Windows.Size;
using ColorConverter  = System.Windows.Media.ColorConverter;
using MessageBox      = System.Windows.MessageBox;
using Brushes         = System.Windows.Media.Brushes;
namespace RoleplayOverlay
{
  public sealed class ImageEditorWindow : Window
  {
    private enum Tool { Select, Pencil, Text, Rectangle, Ellipse, Line, Arrow, Eraser }
    private abstract record UndoAction;
    private sealed record StrokeAdded(Stroke Stroke) : UndoAction;
    private sealed record StrokesErased(StrokeCollection Strokes) : UndoAction;
    private sealed record ElementAdded(UIElement Element) : UndoAction;
    private sealed record ElementMoved(UIElement Element, double OldLeft, double OldTop, double NewLeft, double NewTop) : UndoAction;
    private sealed record ElementResized(UIElement Element, Rect Old, Rect New) : UndoAction;
    private sealed record ElementRemoved(UIElement Element) : UndoAction;
    private sealed record ElementReplaced(UIElement Old, UIElement New) : UndoAction;
    private sealed record EditAction(Action Undo, Action Redo) : UndoAction;
    private sealed record TextMeta(string Text, string Font, double Size, WpfColor Color, bool Outline, WpfColor OutlineColor, string Align = "left", bool Bold = true, bool Italic = false, bool Underline = false, double LineSpacing = 1.0);
    private enum Handle { None, Move, TL, T, TR, R, BR, B, BL, L, P1, P2 }
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
    private string _textAlign = "left";
    private bool _fillEnabled;
    private WpfColor _outlineColor = WpfColor.FromRgb(0, 0, 0);
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    private WpfPoint _shapeStart;
    private bool _isDrawingShape;
    private UIElement? _currentShapePreview;
    private TextBox? _activeTextBox;
    private bool _activeTextOutline;
    private WpfColor _activeOutlineColor = WpfColor.FromRgb(0, 0, 0);
    private bool _finalizingForToolChange;
    private bool _suppressFontUi;
    private UIElement? _selected;
    private readonly List<UIElement> _selection = new();
    private readonly List<UIElement> _clipboard = new();
    private readonly Dictionary<UIElement, WpfPoint> _moveStarts = new();
    private readonly List<WpfRectangle> _selBoxes = new();
    private Handle _activeHandle = Handle.None;
    private WpfPoint _dragStartPt;
    private Rect _dragStartBounds;
    private double _dragStartLeft, _dragStartTop;
    private UIElement? _dragStartElement;
    private TextMeta? _dragStartMeta;
    private ElemState? _dragStartState;
    private int _dragMoveCount;
    private int _recaptures;
    private readonly Dictionary<Handle, Rect> _handleRects = new();
    private ListBox _layersList = null!;
    private readonly Dictionary<UIElement, string> _layerNames = new();
    private Slider _opacitySlider = null!;
    private bool _suppressLayerSel;
    private readonly List<WpfRectangle> _handleVisuals = new();
    private readonly List<Line> _guideVisuals = new();
    private const double HandleSize = 9;
    private const double SnapPx = 7;
    private readonly InkCanvas _inkCanvas;
    private readonly Canvas    _shapeCanvas;
    private readonly Canvas    _overlayCanvas;
    private Canvas _gridCanvas = null!;
    private bool _showGrid, _snapGrid;
    private double _gridSize = 20;
    private readonly ScrollViewer _scrollViewer;
    private Grid   _rootGrid = null!;
    private Canvas _popupLayer = null!;
    private Border _textPopup = null!;
    private TextBox  _popupSize = null!;
    private ComboBox _popupFont = null!;
    private WpfRectangle _popupColor = null!;
    private WpfRectangle _popupOutlineColor = null!;
    private WpfButton _popupBold = null!, _popupItalic = null!, _popupOutline = null!, _popupUnderline = null!;
    private StackPanel _popupTextGroup = null!, _popupShapeGroup = null!, _popupCorner = null!;
    private WpfRectangle _popupShapeStroke = null!, _popupShapeFill = null!;
    private WpfPoint _dragLayerStart;
    private ListBoxItem? _dragLayerItem;
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
      rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
      var toolbar = BuildToolbar();
      Grid.SetColumn(toolbar, 0);
      Grid.SetRow(toolbar, 0);
      rootGrid.Children.Add(toolbar);
      var layersPanel = BuildLayersPanel();
      Grid.SetColumn(layersPanel, 2);
      Grid.SetRow(layersPanel, 0);
      rootGrid.Children.Add(layersPanel);
      _canvasHost = new Grid
      {
        Width = _imgW,
        Height = _imgH,
        ClipToBounds = true
      };
      _canvasHost.AddHandler(UIElement.LostMouseCaptureEvent, new System.Windows.Input.MouseEventHandler((_, _) =>
      {
        bool dragging = _activeHandle != Handle.None || _isDrawingShape;
        if (!dragging || Mouse.LeftButton != MouseButtonState.Pressed) return;
        var thief = Mouse.Captured?.GetType().Name ?? "null";
        if (_recaptures < 40)
        {
          _recaptures++;
          Logger.Info($"[ImageEditor] LOST capture -> {thief} (drag={_activeHandle} forme={_isDrawingShape}) : re-capture #{_recaptures}");
          Dispatcher.BeginInvoke(new Action(() =>
          {
            if ((_activeHandle != Handle.None || _isDrawingShape)
                && Mouse.LeftButton == MouseButtonState.Pressed && !_canvasHost.IsMouseCaptured)
              _canvasHost.CaptureMouse();
          }), System.Windows.Threading.DispatcherPriority.Input);
        }
        else
          Logger.Info($"[ImageEditor] LOST capture -> {thief} (drag={_activeHandle} forme={_isDrawingShape}) : limite de re-captures atteinte ({_recaptures})");
      }), handledEventsToo: true);
      var bgImage = new WpfImage
      {
        Source = _originalImage,
        Width = _imgW,
        Height = _imgH,
        Stretch = Stretch.None
      };
      _canvasHost.Children.Add(bgImage);
      _gridCanvas = new Canvas { Width = _imgW, Height = _imgH, Background = null, IsHitTestVisible = false };
      _canvasHost.Children.Add(_gridCanvas);
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
      _overlayCanvas = new Canvas
      {
        Width = _imgW,
        Height = _imgH,
        Background = Brushes.Transparent,
        IsHitTestVisible = false
      };
      _canvasHost.Children.Add(_overlayCanvas);
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
      Grid.SetColumnSpan(statusBar, 3);
      Grid.SetRow(statusBar, 1);
      rootGrid.Children.Add(statusBar);
      _rootGrid = rootGrid;
      _popupLayer = new Canvas { Background = null, IsHitTestVisible = true };
      Grid.SetColumn(_popupLayer, 0); Grid.SetColumnSpan(_popupLayer, 3); Grid.SetRow(_popupLayer, 0);
      rootGrid.Children.Add(_popupLayer);
      BuildTextPopup();
      _scrollViewer.ScrollChanged += (_, _) => UpdateTextPopup();
      Content = rootGrid;
      KeyDown += OnKeyDown;
      SetTool(Tool.Select);
      RefreshLayers();
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
      toolGrid.Children.Add(MakeToolButton(Tool.Select,    "\u2196"));
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
      _fillCheck.Checked   += (_, _) => { _fillEnabled = true; SaveEditorPrefs(); ApplyFillEnabledToSelected(true); };
      _fillCheck.Unchecked += (_, _) => { _fillEnabled = false; _fillColor = WpfColor.FromArgb(0, 0, 0, 0); SaveEditorPrefs(); ApplyFillEnabledToSelected(false); };
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
      _fontSizeBox.KeyDown += (s, e2) => { if (e2.Key == Key.Enter) { OnFontSizeChanged(s, new RoutedEventArgs()); e2.Handled = true; } };
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
      _outlineCheck.Checked   += (_, _) => { _textOutline = true; SaveEditorPrefs(); if (!_suppressFontUi) ApplyTextMetaToSelected(m => m with { Outline = true }); };
      _outlineCheck.Unchecked += (_, _) => { _textOutline = false; SaveEditorPrefs(); if (!_suppressFontUi) ApplyTextMetaToSelected(m => m with { Outline = false }); };
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
        if (c != null) { _outlineColor = c.Value; _outlineSwatch.Fill = new SolidColorBrush(_outlineColor); SaveEditorPrefs(); ApplyOutlineColorToSelected(_outlineColor); }
      };
      DockPanel.SetDock(_outlineSwatch, Dock.Left);
      outlineRow.Children.Add(_outlineSwatch);
      _fontPanel.Children.Add(outlineRow);
      var alignRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 2, 0, 2) };
      alignRow.Children.Add(MakeAlignBtn("G", "Aligner à gauche", "left"));
      alignRow.Children.Add(MakeAlignBtn("C", "Centrer", "center"));
      alignRow.Children.Add(MakeAlignBtn("D", "Aligner à droite", "right"));
      alignRow.Children.Add(MakeAlignBtn("J", "Justifier", "justify"));
      _fontPanel.Children.Add(alignRow);
      stack.Children.Add(_fontPanel);
      stack.Children.Add(SectionSep());
      stack.Children.Add(SectionHeader("ALIGNER (multi)"));
      var alignGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 2) };
      alignGrid.Children.Add(MakeMiniBtn("⇤", "Aligner à gauche", () => AlignSelected("left")));
      alignGrid.Children.Add(MakeMiniBtn("⇔", "Centrer horizontalement", () => AlignSelected("hcenter")));
      alignGrid.Children.Add(MakeMiniBtn("⇥", "Aligner à droite", () => AlignSelected("right")));
      alignGrid.Children.Add(MakeMiniBtn("⤒", "Aligner en haut", () => AlignSelected("top")));
      alignGrid.Children.Add(MakeMiniBtn("⇕", "Centrer verticalement", () => AlignSelected("vcenter")));
      alignGrid.Children.Add(MakeMiniBtn("⤓", "Aligner en bas", () => AlignSelected("bottom")));
      stack.Children.Add(alignGrid);
      var distRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 2) };
      distRow.Children.Add(MakeMiniBtn("⇹ H", "Répartir horizontalement (3+)", () => DistributeSelected(true)));
      distRow.Children.Add(MakeMiniBtn("⇳ V", "Répartir verticalement (3+)", () => DistributeSelected(false)));
      stack.Children.Add(distRow);
      stack.Children.Add(SectionSep());
      stack.Children.Add(SectionHeader("GRILLE"));
      var gridChk = new CheckBox { Content = "Afficher", Foreground = B(FgColor), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
      gridChk.Checked   += (_, _) => { _showGrid = true;  DrawGrid(); };
      gridChk.Unchecked += (_, _) => { _showGrid = false; DrawGrid(); };
      stack.Children.Add(gridChk);
      var snapChk = new CheckBox { Content = "Aimanter", Foreground = B(FgColor), FontSize = 10 };
      snapChk.Checked   += (_, _) => _snapGrid = true;
      snapChk.Unchecked += (_, _) => _snapGrid = false;
      stack.Children.Add(snapChk);
      var gsRow = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
      var gsLabel = new TextBlock { Text = "Pas (px)", Foreground = B(Fg2Color), FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
      DockPanel.SetDock(gsLabel, Dock.Left); gsRow.Children.Add(gsLabel);
      var gsBox = new TextBox { Text = _gridSize.ToString("F0"), Width = 44, FontSize = 10, Background = B("#111113"), Foreground = B(FgColor), BorderBrush = B(LineColor), VerticalContentAlignment = VerticalAlignment.Center };
      DockPanel.SetDock(gsBox, Dock.Right);
      gsBox.LostFocus += (_, _) => { if (double.TryParse(gsBox.Text, out var g) && g >= 4 && g <= 300) { _gridSize = g; DrawGrid(); } else gsBox.Text = _gridSize.ToString("F0"); };
      gsRow.Children.Add(gsBox);
      stack.Children.Add(gsRow);
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
      var saveAsBtn = MakeActionButton("\U0001F4BE Enregistrer sous... (image)");
      saveAsBtn.Click += (_, _) => SaveImageAs();
      actionsPanel.Children.Add(saveAsBtn);
      var saveLayeredBtn = MakeActionButton("\U0001F5C2 Enregistrer avec calques (.roedit)");
      saveLayeredBtn.Click += (_, _) => SaveLayeredAs();
      actionsPanel.Children.Add(saveLayeredBtn);
      var openLayeredBtn = MakeActionButton("\U0001F4C2 Ouvrir un projet (.roedit)");
      openLayeredBtn.Click += (_, _) => OpenLayered();
      actionsPanel.Children.Add(openLayeredBtn);
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
      _finalizingForToolChange = true;
      FinalizeActiveTextBox();
      _finalizingForToolChange = false;
      DetachInteractionHandlers();
      if (tool != Tool.Select) ClearSelection();
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
        case Tool.Select:
          _inkCanvas.EditingMode = InkCanvasEditingMode.None;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Arrow;
          _canvasHost.MouseLeftButtonDown += OnSelectMouseDown;
          _canvasHost.MouseMove           += OnSelectMouseMove;
          _canvasHost.MouseLeftButtonUp   += OnSelectMouseUp;
          break;
        case Tool.Pencil:
          _inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Pen;
          ApplyInkAttributes();
          break;
        case Tool.Eraser:
          _inkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Cross;
          break;
        case Tool.Text:
        case Tool.Rectangle:
        case Tool.Ellipse:
        case Tool.Line:
        case Tool.Arrow:
          _inkCanvas.EditingMode = InkCanvasEditingMode.None;
          _shapeCanvas.IsHitTestVisible = false;
          _inkCanvas.Cursor = Cursors.Cross;
          _canvasHost.MouseLeftButtonDown += OnShapeMouseDown;
          _canvasHost.MouseMove           += OnShapeMouseMove;
          _canvasHost.MouseLeftButtonUp   += OnShapeMouseUp;
          break;
      }
      _statusLabel.Text = $"{_imgW} x {_imgH} px  |  {GetToolName(tool)}";
      Logger.Info($"[ImageEditor] Tool changed to {_currentTool}");
    }
    private void DetachInteractionHandlers()
    {
      _canvasHost.MouseLeftButtonDown -= OnShapeMouseDown;
      _canvasHost.MouseMove           -= OnShapeMouseMove;
      _canvasHost.MouseLeftButtonUp   -= OnShapeMouseUp;
      _canvasHost.MouseLeftButtonDown -= OnSelectMouseDown;
      _canvasHost.MouseMove           -= OnSelectMouseMove;
      _canvasHost.MouseLeftButtonUp   -= OnSelectMouseUp;
    }
    private static string GetToolName(Tool t) => t switch
    {
      Tool.Select    => "Sélection (déplacer / redimensionner / double-clic = éditer le texte)",
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
      _recaptures = 0;
      _canvasHost.CaptureMouse();
      _isDrawingShape = true;
      e.Handled = true;
      Logger.Info($"[ImageEditor] Shape start at ({_shapeStart.X:F0}, {_shapeStart.Y:F0}) tool={_currentTool}");
    }
    private void OnShapeMouseMove(object sender, MouseEventArgs e)
    {
      if (!_isDrawingShape) return;
      if (e.LeftButton == MouseButtonState.Pressed && !_canvasHost.IsMouseCaptured && _recaptures < 40)
      { _recaptures++; _canvasHost.CaptureMouse(); }
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
        _undoStack.Push(new ElementAdded(shape));
        _redoStack.Clear();
        RefreshLayers();
        SetTool(Tool.Select);
        Select(shape);
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
    private UIElement CreateArrow(WpfPoint from, WpfPoint to, WpfBrush stroke, bool isPreview, double? width = null)
    {
      double sw = width ?? _strokeWidth;
      var canvas = new Canvas();
      var line = new Line
      {
        X1 = from.X, Y1 = from.Y,
        X2 = to.X, Y2 = to.Y,
        Stroke = stroke, StrokeThickness = sw,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
      };
      canvas.Children.Add(line);
      double dx = to.X - from.X;
      double dy = to.Y - from.Y;
      double len = Math.Sqrt(dx * dx + dy * dy);
      if (len < 1) return canvas;
      double headLen = Math.Min(20 + sw * 2, len * 0.4);
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
    private void OnSelectMouseDown(object sender, MouseButtonEventArgs e)
    {
      var pos = e.GetPosition(_canvasHost);
      if (!_inkCanvas.IsHitTestVisible) _inkCanvas.IsHitTestVisible = true;
      if (_popupLayer != null && !_popupLayer.IsHitTestVisible) _popupLayer.IsHitTestVisible = true;
      if (e.ClickCount == 2)
      {
        var t = HitTestElement(pos);
        if (t is FrameworkElement fe && fe.Tag is TextMeta) { BeginEditText(t); e.Handled = true; return; }
      }
      bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
      if (_selection.Count == 1 && _selected != null && !ctrl)
      {
        var b = BoundsInCanvas(_selected);
        double m = (HandleSize + 5) / _zoom;
        var interior = new Rect(b.X + m, b.Y + m, Math.Max(0, b.Width - 2 * m), Math.Max(0, b.Height - 2 * m));
        bool clearlyInside = interior.Width > 1 && interior.Height > 1 && interior.Contains(pos);
        if (!clearlyInside)
        {
          var h = HitTestHandle(pos);
          if (h != Handle.None) { Logger.Info($"[ImageEditor] SelectDown -> poignée {h} (redim)"); StartDrag(h, pos); e.Handled = true; return; }
        }
      }
      var el = HitTestElement(pos);
      Logger.Info($"[ImageEditor] SelectDown pos=({pos.X:F0},{pos.Y:F0}) ctrl={ctrl} selCount={_selection.Count} hit={(el?.GetType().Name ?? "none")}");
      if (el != null)
      {
        if (ctrl) { ToggleSelect(el); e.Handled = true; return; }
        if (!_selection.Contains(el)) Select(el);
        StartDrag(Handle.Move, pos);
      }
      else if (!ctrl) ClearSelection();
      e.Handled = true;
    }
    private void StartDrag(Handle h, WpfPoint pos)
    {
      _dragStartPt = pos;
      _dragStartBounds = _selected != null ? BoundsInCanvas(_selected) : new Rect();
      _dragStartLeft = _selected != null ? GetLeft(_selected) : 0; _dragStartTop = _selected != null ? GetTop(_selected) : 0;
      _dragStartElement = _selected;
      _dragStartMeta = (_selected as FrameworkElement)?.Tag as TextMeta;
      _dragStartState = _selected is Line ? Capture(_selected!) : null;
      _moveStarts.Clear();
      foreach (var s in _selection) _moveStarts[s] = new WpfPoint(GetLeft(s), GetTop(s));
      _dragMoveCount = 0;
      if (_popupLayer != null) _popupLayer.IsHitTestVisible = false;
      _inkCanvas.IsHitTestVisible = false;
      _recaptures = 0;
      bool cap = _canvasHost.CaptureMouse();
      _activeHandle = h;
      Logger.Info($"[ImageEditor] StartDrag handle={h} capture={cap} startLeft={_dragStartLeft:F0} startTop={_dragStartTop:F0}");
    }
    private void OnSelectMouseMove(object sender, MouseEventArgs e)
    {
      var pos = e.GetPosition(_canvasHost);
      if (_activeHandle != Handle.None && e.LeftButton == MouseButtonState.Pressed
          && !_canvasHost.IsMouseCaptured && _recaptures < 40)
      { _recaptures++; _canvasHost.CaptureMouse(); }
      if (_activeHandle == Handle.None || _selected == null)
      {
        var h = _selected != null ? HitTestHandle(pos) : Handle.None;
        _inkCanvas.Cursor = h != Handle.None ? CursorForHandle(h)
                          : (HitTestElement(pos) != null ? Cursors.SizeAll : Cursors.Arrow);
        return;
      }
      ClearGuides();
      if (_activeHandle == Handle.Move)
      {
        double dx = pos.X - _dragStartPt.X, dy = pos.Y - _dragStartPt.Y;
        double sx = 0, sy = 0;
        if (_selected != null)
        {
          var moved = new Rect(_dragStartBounds.X + dx, _dragStartBounds.Y + dy, _dragStartBounds.Width, _dragStartBounds.Height);
          (sx, sy) = SnapMove(moved);
        }
        if (_selection.Count > 1)
          foreach (var s in _selection) if (_moveStarts.TryGetValue(s, out var st)) { SetLeft(s, st.X + dx + sx); SetTop(s, st.Y + dy + sy); }
        else if (_selected != null) { SetLeft(_selected, _dragStartLeft + dx + sx); SetTop(_selected, _dragStartTop + dy + sy); }
        _dragMoveCount++;
        if (_dragMoveCount <= 3 || _dragMoveCount % 10 == 0)
          Logger.Info($"[ImageEditor] move#{_dragMoveCount} dx={dx:F0} dy={dy:F0} captured={_canvasHost.IsMouseCaptured}");
      }
      else ResizeByHandle(pos);
      DrawSelection();
      ShowCoords();
    }
    private void OnSelectMouseUp(object sender, MouseButtonEventArgs e)
    {
      if (_activeHandle != Handle.None && _selected != null)
      {
        _canvasHost.ReleaseMouseCapture();
        if (_activeHandle == Handle.Move && _selection.Count > 1)
        {
          var starts = new Dictionary<UIElement, WpfPoint>(_moveStarts);
          var ends = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
          bool moved = starts.Any(kv => ends.TryGetValue(kv.Key, out var e2) && (Math.Abs(e2.X - kv.Value.X) > 0.01 || Math.Abs(e2.Y - kv.Value.Y) > 0.01));
          if (moved) PushEdit(
            () => { foreach (var kv in starts) { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); },
            () => { foreach (var kv in ends)   { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); });
        }
        else if (_activeHandle == Handle.Move)
        {
          double nl = GetLeft(_selected), nt = GetTop(_selected);
          if (Math.Abs(nl - _dragStartLeft) > 0.01 || Math.Abs(nt - _dragStartTop) > 0.01)
          { _undoStack.Push(new ElementMoved(_selected, _dragStartLeft, _dragStartTop, nl, nt)); _redoStack.Clear(); }
        }
        else if (_dragStartElement != null && !ReferenceEquals(_dragStartElement, _selected))
        {
          _undoStack.Push(new ElementReplaced(_dragStartElement, _selected)); _redoStack.Clear();
          RefreshLayers();
        }
        else if (_selected is Line && (_activeHandle == Handle.P1 || _activeHandle == Handle.P2) && _dragStartState != null)
        {
          var before = _dragStartState; var el = _selected; var after = Capture(el);
          if (before.X1 != after.X1 || before.Y1 != after.Y1 || before.X2 != after.X2 || before.Y2 != after.Y2)
            PushEdit(() => { Restore(el, before); ReselIfPresent(el); }, () => { Restore(el, after); ReselIfPresent(el); });
        }
        else if (IsResizable(_selected))
        {
          var nb = BoundsInCanvas(_selected);
          if (nb != _dragStartBounds) { _undoStack.Push(new ElementResized(_selected, _dragStartBounds, nb)); _redoStack.Clear(); }
        }
      }
      _activeHandle = Handle.None;
      _dragStartElement = null; _dragStartMeta = null; _dragStartState = null;
      if (_canvasHost.IsMouseCaptured) _canvasHost.ReleaseMouseCapture();
      if (_popupLayer != null) _popupLayer.IsHitTestVisible = true;
      _inkCanvas.IsHitTestVisible = true;
      Logger.Info($"[ImageEditor] SelectUp moves={_dragMoveCount}");
      ClearGuides();
      SyncLayerSelection();
      UpdateTextPopup();
    }
    private UIElement? HitTestElement(WpfPoint p)
    {
      for (int i = _shapeCanvas.Children.Count - 1; i >= 0; i--)
      {
        var el = _shapeCanvas.Children[i];
        if (ReferenceEquals(el, _activeTextBox)) continue;
        if (BoundsInCanvas(el).Contains(p)) return el;
      }
      return null;
    }
    private static double GetLeft(UIElement el) { double v = Canvas.GetLeft(el); return double.IsNaN(v) ? 0 : v; }
    private static double GetTop(UIElement el)  { double v = Canvas.GetTop(el);  return double.IsNaN(v) ? 0 : v; }
    private static void SetLeft(UIElement el, double v) => Canvas.SetLeft(el, v);
    private static void SetTop(UIElement el, double v)  => Canvas.SetTop(el, v);
    private Rect BoundsInCanvas(UIElement el)
    {
      var lb = LocalBounds(el);
      return new Rect(GetLeft(el) + lb.X, GetTop(el) + lb.Y, lb.Width, lb.Height);
    }
    private Rect LocalBounds(UIElement el)
    {
      if (el is System.Windows.Shapes.Path p && p.Data != null && !p.Data.Bounds.IsEmpty) return p.Data.Bounds;
      if (el is Line ln)
      {
        double s = ln.StrokeThickness / 2 + 1;
        return new Rect(Math.Min(ln.X1, ln.X2) - s, Math.Min(ln.Y1, ln.Y2) - s,
                        Math.Abs(ln.X2 - ln.X1) + 2 * s, Math.Abs(ln.Y2 - ln.Y1) + 2 * s);
      }
      if (el is Canvas c)
      {
        var b = VisualTreeHelper.GetDescendantBounds(c);
        return b.IsEmpty ? new Rect(0, 0, 12, 12) : b;
      }
      if (el is FrameworkElement fe)
      {
        double w = !double.IsNaN(fe.Width) ? fe.Width : (fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width);
        double h = !double.IsNaN(fe.Height) ? fe.Height : (fe.ActualHeight > 0 ? fe.ActualHeight : fe.DesiredSize.Height);
        if (w <= 0 || h <= 0) { fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); w = fe.DesiredSize.Width; h = fe.DesiredSize.Height; }
        return new Rect(0, 0, Math.Max(1, w), Math.Max(1, h));
      }
      return new Rect(0, 0, 12, 12);
    }
    private static bool IsResizable(UIElement el) => el is WpfRectangle || el is Ellipse;
    private static bool IsText(UIElement el) => el is FrameworkElement fe && fe.Tag is TextMeta;
    private void ResizeText(WpfPoint pos)
    {
      if (_selected == null || _dragStartMeta == null) return;
      var b = _dragStartBounds;
      double startH = Math.Max(1, b.Height);
      double newH = _activeHandle is Handle.BR or Handle.BL ? pos.Y - b.Top : b.Bottom - pos.Y;
      double scale = Math.Max(0.05, newH / startH);
      double newSize = Math.Clamp(_dragStartMeta.Size * scale, 6, 800);
      var m = _dragStartMeta with { Size = newSize };
      int idx = _shapeCanvas.Children.IndexOf(_selected);
      if (idx < 0) return;
      var el = CreateText(m, 0, 0);
      ((FrameworkElement)el).Tag = m;
      el.Visibility = _selected.Visibility;
      _shapeCanvas.Children.RemoveAt(idx);
      _shapeCanvas.Children.Insert(idx, el);
      _selected = el;
      var lb = LocalBounds(el);
      double nw = lb.Width, nh = lb.Height, vx, vy;
      switch (_activeHandle)
      {
        case Handle.BR: vx = b.Left;      vy = b.Top;         break;
        case Handle.TR: vx = b.Left;      vy = b.Bottom - nh; break;
        case Handle.BL: vx = b.Right - nw; vy = b.Top;        break;
        case Handle.TL: vx = b.Right - nw; vy = b.Bottom - nh; break;
        default:        vx = b.Left;      vy = b.Top;         break;
      }
      SetLeft(el, vx - lb.X); SetTop(el, vy - lb.Y);
    }
    private void ResizeByHandle(WpfPoint pos)
    {
      if (_selected == null) return;
      if (IsLineLike(_selected) && (_activeHandle == Handle.P1 || _activeHandle == Handle.P2)) { EditEndpoint(pos); return; }
      if (IsText(_selected)) { ResizeText(pos); return; }
      if (!IsResizable(_selected)) return;
      var b = _dragStartBounds;
      double left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
      double dx = pos.X - _dragStartPt.X, dy = pos.Y - _dragStartPt.Y;
      switch (_activeHandle)
      {
        case Handle.TL: left += dx; top += dy; break;
        case Handle.T:  top += dy; break;
        case Handle.TR: right += dx; top += dy; break;
        case Handle.R:  right += dx; break;
        case Handle.BR: right += dx; bottom += dy; break;
        case Handle.B:  bottom += dy; break;
        case Handle.BL: left += dx; bottom += dy; break;
        case Handle.L:  left += dx; break;
      }
      double nx = Math.Min(left, right), ny = Math.Min(top, bottom);
      double nw = Math.Max(4, Math.Abs(right - left)), nh = Math.Max(4, Math.Abs(bottom - top));
      if (_snapGrid && _gridSize >= 2)
      {
        double l2 = Math.Round(nx / _gridSize) * _gridSize, t2 = Math.Round(ny / _gridSize) * _gridSize;
        double r2 = Math.Round((nx + nw) / _gridSize) * _gridSize, b2 = Math.Round((ny + nh) / _gridSize) * _gridSize;
        nx = l2; ny = t2; nw = Math.Max(4, r2 - l2); nh = Math.Max(4, b2 - t2);
      }
      SetLeft(_selected!, nx); SetTop(_selected!, ny);
      ((FrameworkElement)_selected!).Width = nw;
      ((FrameworkElement)_selected!).Height = nh;
    }
    private void Select(UIElement el)
    {
      _selection.Clear(); _selection.Add(el); _selected = el;
      DrawSelection(); ShowCoords(); SyncLayerSelection(); UpdateOpacitySlider(); SyncFontControls(); UpdateTextPopup();
    }
    private void ToggleSelect(UIElement el)
    {
      if (_selection.Contains(el)) { _selection.Remove(el); if (ReferenceEquals(_selected, el)) _selected = _selection.Count > 0 ? _selection[^1] : null; }
      else { _selection.Add(el); _selected = el; }
      DrawSelection(); ShowCoords(); SyncLayerSelection(); UpdateOpacitySlider(); SyncFontControls(); UpdateTextPopup();
    }
    private void SyncFontControls()
    {
      if (_selected is not FrameworkElement fe || fe.Tag is not TextMeta m) return;
      _suppressFontUi = true;
      if (_fontSizeBox != null) _fontSizeBox.Text = m.Size.ToString("F0");
      if (_fontCombo != null && _fontCombo.Items.Contains(m.Font)) _fontCombo.SelectedItem = m.Font;
      if (_outlineCheck != null) _outlineCheck.IsChecked = m.Outline;
      _suppressFontUi = false;
    }
    private void BuildTextPopup()
    {
      _textPopup = new Border { Background = B("#1C1C20"), BorderBrush = B(AccColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3), Visibility = Visibility.Collapsed };
      var sp = new StackPanel { Orientation = Orientation.Horizontal };
      _popupTextGroup = new StackPanel { Orientation = Orientation.Horizontal };
      var tg = _popupTextGroup;
      tg.Children.Add(PopupLabel("T"));
      _popupSize = new TextBox { Width = 40, FontSize = 11, Background = B("#111113"), Foreground = B(FgColor), BorderBrush = B(LineColor), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
      _popupSize.LostFocus += (_, _) => CommitPopupSize();
      _popupSize.KeyDown   += (_, e) => { if (e.Key == Key.Enter) { CommitPopupSize(); e.Handled = true; } };
      tg.Children.Add(_popupSize);
      tg.Children.Add(PopupBtn("−", () => BumpPopupSize(-2)));
      tg.Children.Add(PopupBtn("+", () => BumpPopupSize(+2)));
      tg.Children.Add(PopupSep());
      _popupFont = new ComboBox { Width = 96, FontSize = 10, Margin = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center };
      foreach (var f in new[] { "Arial", "Consolas", "Comic Sans MS", "Courier New", "Georgia", "Impact", "Segoe UI", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana" }) _popupFont.Items.Add(f);
      _popupFont.SelectionChanged += (_, _) => { if (!_suppressFontUi && _popupFont.SelectedItem is string f) { _fontFamily = f; SaveEditorPrefs(); ApplyTextMetaToSelected(m => m with { Font = f }); } };
      tg.Children.Add(_popupFont);
      tg.Children.Add(PopupSep());
      _popupBold = PopupBtn("B", () => ApplyTextMetaToSelected(m => m with { Bold = !m.Bold })); _popupBold.ToolTip = "Gras";
      tg.Children.Add(_popupBold);
      _popupItalic = PopupBtn("I", () => ApplyTextMetaToSelected(m => m with { Italic = !m.Italic }));
      _popupItalic.FontStyle = FontStyles.Italic; _popupItalic.ToolTip = "Italique";
      tg.Children.Add(_popupItalic);
      _popupUnderline = PopupBtn("U", () => ApplyTextMetaToSelected(m => m with { Underline = !m.Underline }));
      _popupUnderline.ToolTip = "Souligné";
      tg.Children.Add(_popupUnderline);
      tg.Children.Add(PopupSep());
      tg.Children.Add(PopupBtn("G", () => PopupSetAlign("left")));
      tg.Children.Add(PopupBtn("C", () => PopupSetAlign("center")));
      tg.Children.Add(PopupBtn("D", () => PopupSetAlign("right")));
      tg.Children.Add(PopupBtn("J", () => PopupSetAlign("justify")));
      tg.Children.Add(PopupSep());
      var ls = PopupLabel("↕"); ls.ToolTip = "Interligne"; tg.Children.Add(ls);
      tg.Children.Add(PopupBtn("−", () => BumpLineSpacing(-0.1)));
      tg.Children.Add(PopupBtn("+", () => BumpLineSpacing(+0.1)));
      tg.Children.Add(PopupSep());
      _popupColor = new WpfRectangle { Width = 20, Height = 20, Stroke = B(FgColor), StrokeThickness = 1, RadiusX = 3, RadiusY = 3, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Couleur du texte" };
      _popupColor.MouseLeftButtonDown += (_, _) =>
      {
        var c = ShowSimpleColorDialog(_strokeColor);
        if (c != null) { _strokeColor = c.Value; _colorSwatch.Fill = new SolidColorBrush(_strokeColor); _popupColor.Fill = new SolidColorBrush(_strokeColor); ApplyInkAttributes(); SaveEditorPrefs(); ApplyStrokeColorToSelected(_strokeColor); }
      };
      tg.Children.Add(_popupColor);
      _popupOutline = PopupBtn("O", () => ApplyTextMetaToSelected(m => m with { Outline = !m.Outline })); _popupOutline.ToolTip = "Contour du texte";
      tg.Children.Add(_popupOutline);
      _popupOutlineColor = new WpfRectangle { Width = 16, Height = 16, Stroke = B(FgColor), StrokeThickness = 1, RadiusX = 2, RadiusY = 2, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), ToolTip = "Couleur du contour" };
      _popupOutlineColor.MouseLeftButtonDown += (_, _) =>
      {
        var c = ShowSimpleColorDialog(_outlineColor);
        if (c != null) { _outlineColor = c.Value; _outlineSwatch.Fill = new SolidColorBrush(_outlineColor); _popupOutlineColor.Fill = new SolidColorBrush(_outlineColor); SaveEditorPrefs(); ApplyOutlineColorToSelected(_outlineColor); }
      };
      tg.Children.Add(_popupOutlineColor);
      sp.Children.Add(_popupTextGroup);
      _popupShapeGroup = new StackPanel { Orientation = Orientation.Horizontal };
      var shg = _popupShapeGroup;
      shg.Children.Add(PopupLabel("Trait"));
      _popupShapeStroke = new WpfRectangle { Width = 20, Height = 20, Stroke = B(FgColor), StrokeThickness = 1, RadiusX = 3, RadiusY = 3, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Couleur du trait" };
      _popupShapeStroke.MouseLeftButtonDown += (_, _) =>
      {
        var c = ShowSimpleColorDialog(_strokeColor);
        if (c != null) { _strokeColor = c.Value; _colorSwatch.Fill = new SolidColorBrush(_strokeColor); _popupShapeStroke.Fill = new SolidColorBrush(_strokeColor); ApplyInkAttributes(); SaveEditorPrefs(); ApplyStrokeColorToSelected(_strokeColor); }
      };
      shg.Children.Add(_popupShapeStroke);
      shg.Children.Add(PopupBtn("−", () => BumpShapeWidth(-1)));
      shg.Children.Add(PopupBtn("+", () => BumpShapeWidth(+1)));
      shg.Children.Add(PopupSep());
      shg.Children.Add(PopupLabel("Fond"));
      _popupShapeFill = new WpfRectangle { Width = 20, Height = 20, Stroke = B(FgColor), StrokeThickness = 1, RadiusX = 3, RadiusY = 3, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Couleur de remplissage" };
      _popupShapeFill.MouseLeftButtonDown += (_, _) =>
      {
        var c = ShowSimpleColorDialog(_fillColor.A > 0 ? _fillColor : WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        if (c != null) { _fillColor = c.Value; _fillEnabled = true; _fillSwatch.Fill = new SolidColorBrush(_fillColor); _popupShapeFill.Fill = new SolidColorBrush(_fillColor); SaveEditorPrefs(); ApplyFillColorToSelected(_fillColor); }
      };
      shg.Children.Add(_popupShapeFill);
      _popupCorner = new StackPanel { Orientation = Orientation.Horizontal };
      _popupCorner.Children.Add(PopupSep());
      var cl = PopupLabel("⌜"); cl.ToolTip = "Coins arrondis"; _popupCorner.Children.Add(cl);
      _popupCorner.Children.Add(PopupBtn("−", () => BumpCorner(-4)));
      _popupCorner.Children.Add(PopupBtn("+", () => BumpCorner(+4)));
      shg.Children.Add(_popupCorner);
      sp.Children.Add(_popupShapeGroup);
      sp.Children.Add(PopupSep());
      var op = PopupLabel("◑"); op.ToolTip = "Opacité"; sp.Children.Add(op);
      sp.Children.Add(PopupBtn("−", () => BumpOpacity(-0.1)));
      sp.Children.Add(PopupBtn("+", () => BumpOpacity(+0.1)));
      sp.Children.Add(PopupSep());
      var dup = PopupBtn("⧉", DuplicateSelected); dup.ToolTip = "Dupliquer (Ctrl+J)"; sp.Children.Add(dup);
      var del = PopupBtn("✕", DeleteSelected); del.ToolTip = "Supprimer (Suppr)"; sp.Children.Add(del);
      _textPopup.Child = sp;
      _popupLayer.Children.Add(_textPopup);
    }
    private void BumpLineSpacing(double d)
    {
      if (_selected is not FrameworkElement fe || fe.Tag is not TextMeta) return;
      ApplyTextMetaToSelected(m => m with { LineSpacing = Math.Clamp(m.LineSpacing + d, 0.8, 3.0) });
    }
    private double SelectedStrokeWidth() => _selected switch
    {
      WpfRectangle r => r.StrokeThickness,
      Ellipse e => e.StrokeThickness,
      Line l => l.StrokeThickness,
      Canvas cv => cv.Children.OfType<Line>().FirstOrDefault()?.StrokeThickness ?? 2,
      _ => 2
    };
    private void BumpShapeWidth(double d)
    {
      if (_selected == null) return;
      var el = _selected; double w = Math.Clamp(ElWidth(el) + d, 1, 60);
      _strokeWidth = w; EditShape(el, () => SetElWidth(el, w));
    }
    private void BumpCorner(double d)
    {
      if (_selected is not WpfRectangle r) return;
      double max = Math.Min(r.Width, r.Height) / 2;
      double nr = Math.Clamp(r.RadiusX + d, 0, max);
      EditShape(r, () => { r.RadiusX = nr; r.RadiusY = nr; });
    }
    private void BumpOpacity(double d)
    {
      if (_selected == null) return;
      var el = _selected; double o = Math.Clamp(el.Opacity + d, 0.1, 1.0);
      EditShape(el, () => el.Opacity = o);
      UpdateOpacitySlider();
    }
    private void SetToggleState(WpfButton b, bool on)
      => b.Background = on ? new SolidColorBrush(WpfColor.FromArgb(0x99, 0xFF, 0x80, 0x00)) : B(Bg2Color);
    private static TextBlock PopupLabel(string t) => new TextBlock { Text = t, Foreground = B(Fg2Color), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
    private static Border PopupSep() => new Border { Width = 1, Background = B(LineColor), Margin = new Thickness(5, 1, 5, 1) };
    private WpfButton PopupBtn(string label, Action onClick)
    {
      var b = new WpfButton { Content = label, Width = 22, Height = 22, Margin = new Thickness(1, 0, 1, 0), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = B(FgColor), Background = B(Bg2Color), BorderBrush = B(LineColor), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
      b.Click += (_, _) => onClick();
      return b;
    }
    private void CommitPopupSize()
    {
      if (double.TryParse(_popupSize.Text, out var s) && s >= 6 && s <= 800)
      { _fontSize = s; SaveEditorPrefs(); ApplyTextMetaToSelected(m => m with { Size = s }); UpdateTextPopup(); }
      else if (_selected is FrameworkElement fe && fe.Tag is TextMeta m) _popupSize.Text = m.Size.ToString("F0");
    }
    private void BumpPopupSize(double d)
    {
      if (_selected is not FrameworkElement fe || fe.Tag is not TextMeta m) return;
      double s = Math.Clamp(m.Size + d, 6, 800);
      _fontSize = s; SaveEditorPrefs(); ApplyTextMetaToSelected(mm => mm with { Size = s }); UpdateTextPopup();
    }
    private void PopupSetAlign(string a)
    {
      _textAlign = a; SaveEditorPrefs();
      ApplyTextMetaToSelected(m => m with { Align = a }); UpdateTextPopup();
    }
    private void UpdateTextPopup()
    {
      if (_textPopup == null) return;
      if (_currentTool != Tool.Select || _selected == null)
      { _textPopup.Visibility = Visibility.Collapsed; return; }
      bool isText = _selected is FrameworkElement fe0 && fe0.Tag is TextMeta;
      _popupTextGroup.Visibility  = isText ? Visibility.Visible : Visibility.Collapsed;
      _popupShapeGroup.Visibility = isText ? Visibility.Collapsed : Visibility.Visible;
      _suppressFontUi = true;
      if (isText)
      {
        var m = (TextMeta)((FrameworkElement)_selected).Tag!;
        _popupSize.Text = m.Size.ToString("F0");
        if (_popupFont.Items.Contains(m.Font)) _popupFont.SelectedItem = m.Font;
        _popupColor.Fill = new SolidColorBrush(m.Color);
        _popupOutlineColor.Fill = new SolidColorBrush(m.OutlineColor);
        SetToggleState(_popupBold, m.Bold);
        SetToggleState(_popupItalic, m.Italic);
        SetToggleState(_popupUnderline, m.Underline);
        SetToggleState(_popupOutline, m.Outline);
      }
      else
      {
        _popupShapeStroke.Fill = SelectedBrush(true) ?? Brushes.Transparent;
        _popupShapeFill.Fill   = SelectedBrush(false) ?? Brushes.Transparent;
        _popupCorner.Visibility = _selected is WpfRectangle ? Visibility.Visible : Visibility.Collapsed;
      }
      _suppressFontUi = false;
      try
      {
        var b = BoundsInCanvas(_selected);
        var pt = _canvasHost.TransformToAncestor(_rootGrid).Transform(new WpfPoint(b.Left + b.Width / 2, b.Top));
        _textPopup.Visibility = Visibility.Visible;
        _textPopup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = _textPopup.DesiredSize.Width, ph = _textPopup.DesiredSize.Height;
        double x = pt.X - pw / 2, y = pt.Y - ph - 8;
        if (y < 4) y = pt.Y + 26;
        x = Math.Max(184, Math.Min(x, Math.Max(184, _rootGrid.ActualWidth - pw - 6)));
        Canvas.SetLeft(_textPopup, x);
        Canvas.SetTop(_textPopup, y);
      }
      catch { _textPopup.Visibility = Visibility.Collapsed; }
    }
    private WpfBrush? SelectedBrush(bool stroke)
    {
      if (stroke) return _selected switch
      {
        WpfRectangle r => r.Stroke, Ellipse e => e.Stroke, Line l => l.Stroke,
        Canvas cv => cv.Children.OfType<Line>().FirstOrDefault()?.Stroke, _ => null
      };
      return _selected switch { WpfRectangle r => r.Fill, Ellipse e => e.Fill, _ => null };
    }
    private void UpdateOpacitySlider()
    {
      if (_opacitySlider == null) return;
      _suppressLayerSel = true;
      _opacitySlider.Value = _selected != null ? Math.Round(_selected.Opacity * 100) : 100;
      _suppressLayerSel = false;
    }
    private void ClearSelection()
    {
      _selected = null; _selection.Clear(); _activeHandle = Handle.None;
      foreach (var sb in _selBoxes) _overlayCanvas.Children.Remove(sb); _selBoxes.Clear();
      foreach (var hv in _handleVisuals) _overlayCanvas.Children.Remove(hv);
      _handleVisuals.Clear(); _handleRects.Clear();
      ClearGuides();
      SyncLayerSelection();
      UpdateOpacitySlider();
      UpdateTextPopup();
      if (_statusLabel != null) _statusLabel.Text = $"{_imgW} x {_imgH} px  |  {GetToolName(_currentTool)}";
    }
    private void DrawSelection()
    {
      foreach (var sb in _selBoxes) _overlayCanvas.Children.Remove(sb); _selBoxes.Clear();
      foreach (var hv in _handleVisuals) _overlayCanvas.Children.Remove(hv);
      _handleVisuals.Clear(); _handleRects.Clear();
      if (_selection.Count == 0) return;
      foreach (var el in _selection)
      {
        var bb = BoundsInCanvas(el);
        var box = new WpfRectangle
        {
          Width = Math.Max(1, bb.Width), Height = Math.Max(1, bb.Height),
          Stroke = B(AccColor), StrokeThickness = 1.4 / _zoom,
          StrokeDashArray = new DoubleCollection { 4, 3 },
          Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        Canvas.SetLeft(box, bb.Left); Canvas.SetTop(box, bb.Top);
        _overlayCanvas.Children.Add(box); _selBoxes.Add(box);
      }
      if (_selection.Count != 1 || _selected == null) return;
      var b = BoundsInCanvas(_selected);
      if (IsResizable(_selected))
      {
        double cx = b.Left + b.Width / 2, cy = b.Top + b.Height / 2;
        AddHandle(Handle.TL, b.Left, b.Top); AddHandle(Handle.T, cx, b.Top); AddHandle(Handle.TR, b.Right, b.Top);
        AddHandle(Handle.R, b.Right, cy);    AddHandle(Handle.BR, b.Right, b.Bottom); AddHandle(Handle.B, cx, b.Bottom);
        AddHandle(Handle.BL, b.Left, b.Bottom); AddHandle(Handle.L, b.Left, cy);
      }
      else if (IsText(_selected))
      {
        AddHandle(Handle.TL, b.Left, b.Top);   AddHandle(Handle.TR, b.Right, b.Top);
        AddHandle(Handle.BL, b.Left, b.Bottom); AddHandle(Handle.BR, b.Right, b.Bottom);
      }
      else if (IsLineLike(_selected))
      {
        var (p1, p2) = LineEndpoints(_selected);
        AddHandle(Handle.P1, p1.X, p1.Y);
        AddHandle(Handle.P2, p2.X, p2.Y);
      }
    }
    private static bool IsLineLike(UIElement el) => el is Line || el is Canvas;
    private (WpfPoint, WpfPoint) LineEndpoints(UIElement el)
    {
      double lx = GetLeft(el), ly = GetTop(el);
      if (el is Line l) return (new WpfPoint(lx + l.X1, ly + l.Y1), new WpfPoint(lx + l.X2, ly + l.Y2));
      if (el is Canvas cv) { var ln = cv.Children.OfType<Line>().FirstOrDefault(); if (ln != null) return (new WpfPoint(lx + ln.X1, ly + ln.Y1), new WpfPoint(lx + ln.X2, ly + ln.Y2)); }
      return (new WpfPoint(lx, ly), new WpfPoint(lx, ly));
    }
    private void EditEndpoint(WpfPoint pos)
    {
      bool isP1 = _activeHandle == Handle.P1;
      if (_selected is Line l)
      {
        double lx = GetLeft(l), ly = GetTop(l);
        if (isP1) { l.X1 = pos.X - lx; l.Y1 = pos.Y - ly; } else { l.X2 = pos.X - lx; l.Y2 = pos.Y - ly; }
      }
      else if (_selected is Canvas cv)
      {
        var ln = cv.Children.OfType<Line>().FirstOrDefault(); if (ln == null) return;
        double lx = GetLeft(cv), ly = GetTop(cv);
        var from = new WpfPoint(lx + ln.X1, ly + ln.Y1);
        var to   = new WpfPoint(lx + ln.X2, ly + ln.Y2);
        if (isP1) from = pos; else to = pos;
        int idx = _shapeCanvas.Children.IndexOf(cv); if (idx < 0) return;
        var na = CreateArrow(from, to, ln.Stroke, false, ln.StrokeThickness);
        Canvas.SetLeft(na, 0); Canvas.SetTop(na, 0);
        na.Visibility = cv.Visibility; na.Opacity = cv.Opacity;
        _shapeCanvas.Children.RemoveAt(idx); _shapeCanvas.Children.Insert(idx, na);
        _selected = na;
      }
    }
    private void AddHandle(Handle h, double x, double y)
    {
      double s = HandleSize / _zoom;
      var r = new Rect(x - s / 2, y - s / 2, s, s);
      _handleRects[h] = r;
      var vis = new WpfRectangle { Width = s, Height = s, Fill = B(AccColor), Stroke = Brushes.White, StrokeThickness = 1 / _zoom, IsHitTestVisible = false };
      Canvas.SetLeft(vis, r.X); Canvas.SetTop(vis, r.Y);
      _overlayCanvas.Children.Add(vis); _handleVisuals.Add(vis);
    }
    private Handle HitTestHandle(WpfPoint p)
    {
      double pad = 3 / _zoom;
      foreach (var kv in _handleRects) { var r = kv.Value; r.Inflate(pad, pad); if (r.Contains(p)) return kv.Key; }
      return Handle.None;
    }
    private static WpfCursor CursorForHandle(Handle h) => h switch
    {
      Handle.TL or Handle.BR => Cursors.SizeNWSE,
      Handle.TR or Handle.BL => Cursors.SizeNESW,
      Handle.T or Handle.B   => Cursors.SizeNS,
      Handle.L or Handle.R   => Cursors.SizeWE,
      Handle.P1 or Handle.P2 => Cursors.Cross,
      _ => Cursors.SizeAll
    };
    private void DrawGrid()
    {
      if (_gridCanvas == null) return;
      _gridCanvas.Children.Clear();
      if (!_showGrid || _gridSize < 2) return;
      var brush = new SolidColorBrush(WpfColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)); brush.Freeze();
      for (double x = 0; x <= _imgW; x += _gridSize)
        _gridCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = _imgH, Stroke = brush, StrokeThickness = 0.5, IsHitTestVisible = false });
      for (double y = 0; y <= _imgH; y += _gridSize)
        _gridCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = _imgW, Y2 = y, Stroke = brush, StrokeThickness = 0.5, IsHitTestVisible = false });
    }
    private (double, double) SnapMove(Rect moved)
    {
      if (_snapGrid && _gridSize >= 2)
        return (Math.Round(moved.Left / _gridSize) * _gridSize - moved.Left,
                Math.Round(moved.Top / _gridSize) * _gridSize - moved.Top);
      double bestDX = 0, bestDY = 0, dMinX = SnapPx / _zoom, dMinY = SnapPx / _zoom;
      bool sX = false, sY = false; double gX = 0, gY = 0;
      double[] mX = { moved.Left, moved.Left + moved.Width / 2, moved.Right };
      double[] mY = { moved.Top, moved.Top + moved.Height / 2, moved.Bottom };
      foreach (var t in CollectTargets(true, moved))
        foreach (var m in mX) { double d = t - m; if (Math.Abs(d) < dMinX) { dMinX = Math.Abs(d); bestDX = d; sX = true; gX = t; } }
      foreach (var t in CollectTargets(false, moved))
        foreach (var m in mY) { double d = t - m; if (Math.Abs(d) < dMinY) { dMinY = Math.Abs(d); bestDY = d; sY = true; gY = t; } }
      if (sX) AddGuide(true, gX);
      if (sY) AddGuide(false, gY);
      return (bestDX, bestDY);
    }
    private List<double> CollectTargets(bool xAxis, Rect moved)
    {
      var list = new List<double> { 0, (xAxis ? _imgW : _imgH) / 2.0, xAxis ? _imgW : _imgH };
      foreach (var child in _shapeCanvas.Children)
      {
        var el = (UIElement)child;
        if (_selection.Contains(el) || ReferenceEquals(el, _activeTextBox)) continue;
        var b = BoundsInCanvas(el);
        var inter = Rect.Intersect(moved, b);
        if (!inter.IsEmpty)
        {
          double minArea = Math.Min(moved.Width * moved.Height, b.Width * b.Height);
          if (minArea > 0 && (inter.Width * inter.Height) / minArea > 0.6) continue;
        }
        if (xAxis) { list.Add(b.Left); list.Add(b.Left + b.Width / 2); list.Add(b.Right); }
        else       { list.Add(b.Top);  list.Add(b.Top + b.Height / 2);  list.Add(b.Bottom); }
      }
      return list;
    }
    private void AddGuide(bool vertical, double coord)
    {
      var ln = new Line { Stroke = B("#00E5FF"), StrokeThickness = 1 / _zoom, IsHitTestVisible = false, StrokeDashArray = new DoubleCollection { 3, 2 } };
      if (vertical) { ln.X1 = coord; ln.Y1 = 0; ln.X2 = coord; ln.Y2 = _imgH; }
      else          { ln.X1 = 0; ln.Y1 = coord; ln.X2 = _imgW; ln.Y2 = coord; }
      _overlayCanvas.Children.Add(ln); _guideVisuals.Add(ln);
    }
    private void ClearGuides()
    {
      foreach (var g in _guideVisuals) _overlayCanvas.Children.Remove(g);
      _guideVisuals.Clear();
    }
    private void ShowCoords()
    {
      if (_selected == null) return;
      var b = BoundsInCanvas(_selected);
      _statusLabel.Text = $"X {b.Left:F0}  Y {b.Top:F0}  L {b.Width:F0}  H {b.Height:F0}   |  {_imgW}x{_imgH}";
    }
    private void SelectMany(List<UIElement> els)
    {
      _selection.Clear(); _selection.AddRange(els); _selected = els.Count > 0 ? els[^1] : null;
      DrawSelection(); SyncLayerSelection(); UpdateOpacitySlider(); SyncFontControls(); UpdateTextPopup(); ShowCoords();
    }
    private void DeleteSelected()
    {
      if (_selection.Count == 0) return;
      var snap = _selection.Select(el => (el, idx: _shapeCanvas.Children.IndexOf(el))).OrderBy(t => t.idx).ToList();
      foreach (var (el, _) in snap) { _shapeCanvas.Children.Remove(el); }
      ClearSelection();
      PushEdit(
        () => { foreach (var (el, idx) in snap) _shapeCanvas.Children.Insert(Math.Clamp(idx, 0, _shapeCanvas.Children.Count), el); RefreshLayers(); },
        () => { foreach (var (el, _) in snap) _shapeCanvas.Children.Remove(el); RefreshLayers(); });
      RefreshLayers();
    }
    private void NudgeSelected(double dx, double dy)
    {
      if (_selection.Count == 0) return;
      var starts = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      foreach (var s in _selection) { SetLeft(s, GetLeft(s) + dx); SetTop(s, GetTop(s) + dy); }
      var ends = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      PushEdit(
        () => { foreach (var kv in starts) { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); },
        () => { foreach (var kv in ends)   { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); });
      DrawSelection(); ShowCoords(); UpdateTextPopup();
    }
    private void CopySelection()
    {
      _clipboard.Clear();
      foreach (var el in _selection) { var c = CloneElement(el); if (c != null) { c.Visibility = el.Visibility; c.Opacity = el.Opacity; _clipboard.Add(c); } }
    }
    private void PasteClipboard()
    {
      if (_clipboard.Count == 0) return;
      var pasted = new List<UIElement>();
      foreach (var src in _clipboard)
      {
        var c = CloneElement(src); if (c == null) continue;
        SetLeft(c, GetLeft(src) + 14); SetTop(c, GetTop(src) + 14);
        c.Visibility = src.Visibility; c.Opacity = src.Opacity;
        _shapeCanvas.Children.Add(c); pasted.Add(c);
      }
      if (pasted.Count == 0) return;
      PushEdit(
        () => { foreach (var c in pasted) _shapeCanvas.Children.Remove(c); ClearSelection(); RefreshLayers(); },
        () => { foreach (var c in pasted) if (!_shapeCanvas.Children.Contains(c)) _shapeCanvas.Children.Add(c); RefreshLayers(); });
      RefreshLayers();
      SelectMany(pasted);
    }
    private void AlignSelected(string mode)
    {
      if (_selection.Count < 2) return;
      var bounds = _selection.ToDictionary(el => el, el => BoundsInCanvas(el));
      double gl = bounds.Values.Min(b => b.Left), gr = bounds.Values.Max(b => b.Right);
      double gt = bounds.Values.Min(b => b.Top),  gb = bounds.Values.Max(b => b.Bottom);
      double gcx = (gl + gr) / 2, gcy = (gt + gb) / 2;
      var starts = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      foreach (var el in _selection)
      {
        var b = bounds[el]; double dx = 0, dy = 0;
        switch (mode)
        {
          case "left":   dx = gl - b.Left; break;
          case "hcenter":dx = gcx - (b.Left + b.Width / 2); break;
          case "right":  dx = gr - b.Right; break;
          case "top":    dy = gt - b.Top; break;
          case "vcenter":dy = gcy - (b.Top + b.Height / 2); break;
          case "bottom": dy = gb - b.Bottom; break;
        }
        SetLeft(el, GetLeft(el) + dx); SetTop(el, GetTop(el) + dy);
      }
      var ends = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      PushEdit(() => { foreach (var kv in starts) { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); },
               () => { foreach (var kv in ends)   { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); });
      DrawSelection(); UpdateTextPopup();
    }
    private void DistributeSelected(bool horizontal)
    {
      if (_selection.Count < 3) return;
      var starts = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      var items = _selection.Select(el => (el, b: BoundsInCanvas(el)))
                    .OrderBy(t => horizontal ? t.b.Left + t.b.Width / 2 : t.b.Top + t.b.Height / 2).ToList();
      double first = horizontal ? items[0].b.Left + items[0].b.Width / 2 : items[0].b.Top + items[0].b.Height / 2;
      double last  = horizontal ? items[^1].b.Left + items[^1].b.Width / 2 : items[^1].b.Top + items[^1].b.Height / 2;
      double step = (last - first) / (items.Count - 1);
      for (int i = 1; i < items.Count - 1; i++)
      {
        var (el, b) = items[i];
        double targetC = first + step * i;
        if (horizontal) { double dx = targetC - (b.Left + b.Width / 2); SetLeft(el, GetLeft(el) + dx); }
        else            { double dy = targetC - (b.Top + b.Height / 2); SetTop(el, GetTop(el) + dy); }
      }
      var ends = _selection.ToDictionary(s => s, s => new WpfPoint(GetLeft(s), GetTop(s)));
      PushEdit(() => { foreach (var kv in starts) { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); },
               () => { foreach (var kv in ends)   { Canvas.SetLeft(kv.Key, kv.Value.X); Canvas.SetTop(kv.Key, kv.Value.Y); } DrawSelection(); });
      DrawSelection(); UpdateTextPopup();
    }
    private void BeginEditText(UIElement el)
    {
      if (el is not FrameworkElement fe || fe.Tag is not TextMeta meta) return;
      double left = GetLeft(el), top = GetTop(el);
      _shapeCanvas.Children.Remove(el);
      _undoStack.Push(new ElementRemoved(el)); _redoStack.Clear();
      ClearSelection();
      RefreshLayers();
      StartTextEditAt(new WpfPoint(left, top), meta);
    }
    private static void ApplyBounds(UIElement el, Rect r)
    {
      Canvas.SetLeft(el, r.Left); Canvas.SetTop(el, r.Top);
      if (el is FrameworkElement fe && (el is WpfRectangle || el is Ellipse)) { fe.Width = r.Width; fe.Height = r.Height; }
    }
    private Border BuildLayersPanel()
    {
      var border = new Border { Background = B(Bg2Color), BorderBrush = B(LineColor), BorderThickness = new Thickness(1, 0, 0, 0) };
      var dock = new DockPanel { Margin = new Thickness(6) };
      var header = SectionHeader("CALQUES");
      DockPanel.SetDock(header, Dock.Top);
      dock.Children.Add(header);
      var btnRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(0, 2, 0, 4) };
      DockPanel.SetDock(btnRow, Dock.Top);
      btnRow.Children.Add(MakeMiniBtn("▲", "Monter (devant)", () => MoveLayer(+1)));
      btnRow.Children.Add(MakeMiniBtn("▼", "Descendre (derrière)", () => MoveLayer(-1)));
      btnRow.Children.Add(MakeMiniBtn("⧉", "Dupliquer (Ctrl+J)", DuplicateSelected));
      btnRow.Children.Add(MakeMiniBtn("◉", "Afficher / masquer", ToggleLayerVisibility));
      btnRow.Children.Add(MakeMiniBtn("✕", "Supprimer (Suppr)", DeleteSelected));
      dock.Children.Add(btnRow);
      var opRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
      DockPanel.SetDock(opRow, Dock.Bottom);
      var opLabel = new TextBlock { Text = "Opacité", Foreground = B(Fg2Color), FontSize = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
      DockPanel.SetDock(opLabel, Dock.Left);
      opRow.Children.Add(opLabel);
      _opacitySlider = new Slider { Minimum = 0, Maximum = 100, Value = 100, VerticalAlignment = VerticalAlignment.Center };
      _opacitySlider.ValueChanged += OnLayerOpacityChanged;
      opRow.Children.Add(_opacitySlider);
      dock.Children.Add(opRow);
      _layersList = new ListBox
      {
        Background = B("#0E0E10"),
        BorderThickness = new Thickness(0),
        Foreground = B(FgColor),
        FontSize = 11,
        SelectionMode = System.Windows.Controls.SelectionMode.Extended
      };
      _layersList.SelectionChanged += OnLayerListSelectionChanged;
      _layersList.AllowDrop = true;
      _layersList.PreviewMouseLeftButtonDown += LayersPreviewDown;
      _layersList.PreviewMouseMove += LayersPreviewMove;
      _layersList.Drop += LayersDrop;
      _layersList.MouseDoubleClick += LayersRename;
      dock.Children.Add(_layersList);
      border.Child = dock;
      return border;
    }
    private void OnLayerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
      if (_suppressLayerSel || _selected == null) return;
      _selected.Opacity = Math.Clamp(e.NewValue / 100.0, 0, 1);
    }
    private void DuplicateSelected()
    {
      if (_selection.Count == 0) return;
      var clones = new List<UIElement>();
      foreach (var el in _selection)
      {
        var c = CloneElement(el); if (c == null) continue;
        SetLeft(c, GetLeft(el) + 12); SetTop(c, GetTop(el) + 12);
        c.Visibility = el.Visibility; c.Opacity = el.Opacity;
        _shapeCanvas.Children.Add(c); clones.Add(c);
      }
      if (clones.Count == 0) { if (_statusLabel != null) _statusLabel.Text = "Duplication impossible."; return; }
      PushEdit(
        () => { foreach (var c in clones) _shapeCanvas.Children.Remove(c); ClearSelection(); RefreshLayers(); },
        () => { foreach (var c in clones) if (!_shapeCanvas.Children.Contains(c)) _shapeCanvas.Children.Add(c); RefreshLayers(); });
      RefreshLayers();
      SelectMany(clones);
    }
    private UIElement? CloneElement(UIElement el)
    {
      switch (el)
      {
        case WpfRectangle r:
          { var c = new WpfRectangle { Width = r.Width, Height = r.Height, Stroke = r.Stroke, StrokeThickness = r.StrokeThickness, Fill = r.Fill };
            Canvas.SetLeft(c, GetLeft(r)); Canvas.SetTop(c, GetTop(r)); return c; }
        case Ellipse el2:
          { var c = new Ellipse { Width = el2.Width, Height = el2.Height, Stroke = el2.Stroke, StrokeThickness = el2.StrokeThickness, Fill = el2.Fill };
            Canvas.SetLeft(c, GetLeft(el2)); Canvas.SetTop(c, GetTop(el2)); return c; }
        case Line l:
          { var c = new Line { X1 = l.X1, Y1 = l.Y1, X2 = l.X2, Y2 = l.Y2, Stroke = l.Stroke, StrokeThickness = l.StrokeThickness, StrokeStartLineCap = l.StrokeStartLineCap, StrokeEndLineCap = l.StrokeEndLineCap };
            Canvas.SetLeft(c, GetLeft(l)); Canvas.SetTop(c, GetTop(l)); return c; }
        case FrameworkElement fe when fe.Tag is TextMeta m:
          { var c = CreateText(m, GetLeft(el), GetTop(el)); ((FrameworkElement)c).Tag = m; return c; }
        default:
          try
          {
            var s = System.Windows.Markup.XamlWriter.Save(el);
            using var sr = new System.IO.StringReader(s);
            using var xr = System.Xml.XmlReader.Create(sr);
            return System.Windows.Markup.XamlReader.Load(xr) as UIElement;
          }
          catch { return null; }
      }
    }
    private WpfButton MakeAlignBtn(string label, string tip, string align)
    {
      var b = new WpfButton
      {
        Content = label, Height = 22, Margin = new Thickness(1), FontSize = 11, FontWeight = FontWeights.Bold,
        Foreground = B(FgColor), Background = B(Bg2Color), BorderBrush = B(LineColor),
        BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip
      };
      b.Click += (_, _) =>
      {
        _textAlign = align; SaveEditorPrefs();
        if (_activeTextBox != null) _activeTextBox.TextAlignment = ParseAlign(align);
        ApplyTextMetaToSelected(m => m with { Align = align });
      };
      return b;
    }
    private WpfButton MakeMiniBtn(string label, string tip, Action onClick)
    {
      var b = new WpfButton
      {
        Content = label, Height = 22, Margin = new Thickness(1), FontSize = 12,
        Foreground = B(FgColor), Background = B(Bg2Color), BorderBrush = B(LineColor),
        BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = tip
      };
      b.Click += (_, _) => onClick();
      return b;
    }
    private void RefreshLayers()
    {
      if (_layersList == null) return;
      _suppressLayerSel = true;
      _layersList.Items.Clear();
      for (int i = _shapeCanvas.Children.Count - 1; i >= 0; i--)
      {
        var el = _shapeCanvas.Children[i];
        if (ReferenceEquals(el, _activeTextBox)) continue;
        var item = new ListBoxItem
        {
          Content = LayerLabel(el),
          Tag = el,
          Foreground = B(FgColor),
          Padding = new Thickness(4, 2, 4, 2),
          IsSelected = _selection.Contains(el)
        };
        _layersList.Items.Add(item);
      }
      _suppressLayerSel = false;
    }
    private string LayerLabel(UIElement el)
    {
      string name;
      if (_layerNames.TryGetValue(el, out var custom) && !string.IsNullOrWhiteSpace(custom))
        name = custom;
      else
      {
        string Trunc(string s) => s.Length > 18 ? s.Substring(0, 17) + "…" : s;
        name = el switch
        {
          System.Windows.Shapes.Path p when p.Tag is TextMeta tm => "T  " + Trunc(tm.Text),
          TextBlock tb => "T  " + Trunc(tb.Text),
          WpfRectangle => "▭  Rectangle",
          Ellipse => "◯  Ellipse",
          Line => "／  Ligne",
          Canvas => "→  Flèche",
          _ => "Calque"
        };
      }
      if (el.Visibility != Visibility.Visible) name = "🚫 " + name;
      return name;
    }
    private void LayersRename(object sender, MouseButtonEventArgs e)
    {
      var item = LayerItemUnder(e.OriginalSource as DependencyObject);
      if (item?.Tag is not UIElement el) return;
      var tb = new TextBox { Text = item.Content?.ToString() ?? "", FontSize = 11, Background = B("#111113"), Foreground = B(FgColor), BorderBrush = B(AccColor), BorderThickness = new Thickness(1) };
      item.Content = tb;
      tb.Focus(); tb.SelectAll();
      bool done = false;
      void Commit()
      {
        if (done) return; done = true;
        var oldName = _layerNames.TryGetValue(el, out var on) ? on : null;
        var t = tb.Text.Trim();
        var newName = string.IsNullOrWhiteSpace(t) ? null : t;
        if (newName == oldName) { RefreshLayers(); return; }
        void Set(string? nm) { if (nm == null) _layerNames.Remove(el); else _layerNames[el] = nm; RefreshLayers(); }
        Set(newName);
        PushEdit(() => Set(oldName), () => Set(newName));
      }
      tb.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) { Commit(); ke.Handled = true; } else if (ke.Key == Key.Escape) { done = true; RefreshLayers(); } };
      tb.LostFocus += (_, _) => Commit();
    }
    private void OnLayerListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_suppressLayerSel) return;
      var els = _layersList.SelectedItems.OfType<ListBoxItem>().Select(li => li.Tag).OfType<UIElement>().ToList();
      if (els.Count == 0) return;
      if (_currentTool != Tool.Select) SetTool(Tool.Select);
      SelectMany(els);
    }
    private void SyncLayerSelection()
    {
      if (_layersList == null) return;
      _suppressLayerSel = true;
      foreach (var obj in _layersList.Items)
        if (obj is ListBoxItem li)
        {
          bool sel = li.Tag is UIElement el && _selection.Contains(el);
          li.IsSelected = sel;
          if (sel && ReferenceEquals(li.Tag, _selected)) _layersList.ScrollIntoView(li);
        }
      _suppressLayerSel = false;
    }
    private static ListBoxItem? LayerItemUnder(DependencyObject? o)
    {
      while (o != null && o is not ListBoxItem) o = VisualTreeHelper.GetParent(o);
      return o as ListBoxItem;
    }
    private void LayersPreviewDown(object sender, MouseButtonEventArgs e)
    {
      _dragLayerStart = e.GetPosition(_layersList);
      _dragLayerItem = LayerItemUnder(e.OriginalSource as DependencyObject);
    }
    private void LayersPreviewMove(object sender, MouseEventArgs e)
    {
      if (e.LeftButton != MouseButtonState.Pressed || _dragLayerItem == null) return;
      if (Math.Abs(e.GetPosition(_layersList).Y - _dragLayerStart.Y) < 6) return;
      System.Windows.DragDrop.DoDragDrop(_layersList, _dragLayerItem, System.Windows.DragDropEffects.Move);
    }
    private void LayersDrop(object sender, System.Windows.DragEventArgs e)
    {
      var dragItem = _dragLayerItem; _dragLayerItem = null;
      if (dragItem?.Tag is not UIElement dragged) return;
      var target = LayerItemUnder(e.OriginalSource as DependencyObject);
      if (target?.Tag is not UIElement targetEl || ReferenceEquals(dragged, targetEl)) return;
      _shapeCanvas.Children.Remove(dragged);
      int to = _shapeCanvas.Children.IndexOf(targetEl);
      if (to < 0) to = _shapeCanvas.Children.Count;
      _shapeCanvas.Children.Insert(Math.Clamp(to, 0, _shapeCanvas.Children.Count), dragged);
      RefreshLayers();
      Select(dragged);
    }
    private void MoveLayer(int dir)
    {
      if (_selected == null) return;
      int i = _shapeCanvas.Children.IndexOf(_selected);
      int j = i + dir;
      if (i < 0 || j < 0 || j >= _shapeCanvas.Children.Count) return;
      var el = _selected;
      _shapeCanvas.Children.Remove(el);
      _shapeCanvas.Children.Insert(j, el);
      RefreshLayers(); SyncLayerSelection(); DrawSelection();
    }
    private void ToggleLayerVisibility()
    {
      if (_selected == null) return;
      _selected.Visibility = _selected.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
      RefreshLayers(); SyncLayerSelection();
    }
    private void PushEdit(Action undo, Action redo) { _undoStack.Push(new EditAction(undo, redo)); _redoStack.Clear(); }
    private static WpfBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static WpfBrush? ElStroke(UIElement el) => el switch
    { WpfRectangle r => r.Stroke, Ellipse e => e.Stroke, Line l => l.Stroke, Canvas cv => cv.Children.OfType<Line>().FirstOrDefault()?.Stroke, _ => null };
    private static void SetElStroke(UIElement el, WpfBrush? b)
    {
      switch (el)
      {
        case WpfRectangle r: r.Stroke = b; break;
        case Ellipse e: e.Stroke = b; break;
        case Line l: l.Stroke = b; break;
        case Canvas cv: foreach (var ch in cv.Children) { if (ch is Line ln) ln.Stroke = b; else if (ch is Polygon pg) { pg.Fill = b; pg.Stroke = b; } } break;
      }
    }
    private static void SetElFill(UIElement el, WpfBrush? b) { if (el is WpfRectangle r) r.Fill = b ?? Brushes.Transparent; else if (el is Ellipse e) e.Fill = b ?? Brushes.Transparent; }
    private static double ElWidth(UIElement el) => el switch
    { WpfRectangle r => r.StrokeThickness, Ellipse e => e.StrokeThickness, Line l => l.StrokeThickness, Canvas cv => cv.Children.OfType<Line>().FirstOrDefault()?.StrokeThickness ?? 2, _ => 2 };
    private static void SetElWidth(UIElement el, double w)
    {
      switch (el) { case WpfRectangle r: r.StrokeThickness = w; break; case Ellipse e: e.StrokeThickness = w; break; case Line l: l.StrokeThickness = w; break; case Canvas cv: foreach (var ch in cv.Children) if (ch is Line ln) ln.StrokeThickness = w; break; }
    }
    private sealed class ElemState { public WpfBrush? Stroke, Fill; public double Width, Radius, Opacity, X1, Y1, X2, Y2; public bool IsLine; }
    private ElemState Capture(UIElement el)
    {
      var s = new ElemState { Opacity = el.Opacity, Stroke = ElStroke(el), Width = ElWidth(el) };
      if (el is WpfRectangle r) { s.Fill = r.Fill; s.Radius = r.RadiusX; }
      else if (el is Ellipse e) { s.Fill = e.Fill; }
      else if (el is Line l) { s.IsLine = true; s.X1 = l.X1; s.Y1 = l.Y1; s.X2 = l.X2; s.Y2 = l.Y2; }
      return s;
    }
    private void Restore(UIElement el, ElemState s)
    {
      el.Opacity = s.Opacity; SetElStroke(el, s.Stroke); SetElWidth(el, s.Width);
      if (el is WpfRectangle r) { r.Fill = s.Fill ?? Brushes.Transparent; r.RadiusX = s.Radius; r.RadiusY = s.Radius; }
      else if (el is Ellipse e) { e.Fill = s.Fill ?? Brushes.Transparent; }
      else if (el is Line l && s.IsLine) { l.X1 = s.X1; l.Y1 = s.Y1; l.X2 = s.X2; l.Y2 = s.Y2; }
    }
    private void ReselIfPresent(UIElement el)
    {
      if (!_shapeCanvas.Children.Contains(el)) return;
      _selected = el; DrawSelection(); UpdateTextPopup();
    }
    private void EditShape(UIElement el, Action mutate)
    {
      var before = Capture(el);
      mutate();
      var after = Capture(el);
      DrawSelection(); UpdateTextPopup();
      PushEdit(() => { Restore(el, before); ReselIfPresent(el); }, () => { Restore(el, after); ReselIfPresent(el); });
    }
    private void ApplyStrokeColorToSelected(WpfColor c)
    {
      if (_currentTool != Tool.Select || _selected == null) return;
      if (IsText(_selected)) { ApplyTextMetaToSelected(m => m with { Color = c }); return; }
      var el = _selected; var b = Freeze(new SolidColorBrush(c));
      EditShape(el, () => SetElStroke(el, b));
    }
    private void ApplyFillColorToSelected(WpfColor c)
    {
      if (_currentTool != Tool.Select || _selected is not (WpfRectangle or Ellipse)) return;
      var el = _selected; var b = Freeze(new SolidColorBrush(c));
      EditShape(el, () => SetElFill(el, b));
    }
    private void ApplyFillEnabledToSelected(bool on)
    {
      if (_currentTool != Tool.Select || _selected is not (WpfRectangle or Ellipse)) return;
      var el = _selected;
      WpfBrush fill = on ? Freeze(new SolidColorBrush(_fillColor.A > 0 ? _fillColor : WpfColor.FromRgb(0xFF, 0xFF, 0xFF))) : Brushes.Transparent;
      EditShape(el, () => SetElFill(el, fill));
    }
    private void ApplyStrokeWidthToSelected(double w)
    {
      if (_currentTool != Tool.Select || _selected == null) return;
      SetElWidth(_selected, w); DrawSelection();
    }
    private void ApplyOutlineColorToSelected(WpfColor c)
    {
      if (_currentTool != Tool.Select || _selected is not System.Windows.Shapes.Path p || p.Tag is not TextMeta) return;
      ApplyTextMetaToSelected(m => m with { OutlineColor = c });
    }
    private void ApplyTextMetaToSelected(Func<TextMeta, TextMeta> mutate)
    {
      if (_currentTool != Tool.Select || _selected is not FrameworkElement fe || fe.Tag is not TextMeta m) return;
      var nm = mutate(m);
      int idx = _shapeCanvas.Children.IndexOf(_selected); if (idx < 0) return;
      double left = GetLeft(_selected), top = GetTop(_selected);
      var old = _selected;
      var el = CreateText(nm, left, top);
      ((FrameworkElement)el).Tag = nm; el.Visibility = old.Visibility; el.Opacity = old.Opacity;
      _shapeCanvas.Children.RemoveAt(idx); _shapeCanvas.Children.Insert(idx, el);
      _selected = el;
      _undoStack.Push(new ElementReplaced(old, el)); _redoStack.Clear();
      DrawSelection(); RefreshLayers(); SyncLayerSelection(); UpdateTextPopup();
    }
    private void OnTextClick(MouseButtonEventArgs e)
      => StartTextEditAt(e.GetPosition(_canvasHost), null);
    private void StartTextEditAt(WpfPoint pos, TextMeta? meta)
    {
      FinalizeActiveTextBox();
      string font  = meta?.Font ?? _fontFamily;
      double size  = meta?.Size ?? _fontSize;
      WpfColor col = meta?.Color ?? _strokeColor;
      _activeTextOutline  = meta?.Outline ?? _textOutline;
      _activeOutlineColor = meta?.OutlineColor ?? _outlineColor;
      string align = meta?.Align ?? _textAlign;
      _activeTextBox = new TextBox
      {
        Text = meta?.Text ?? "",
        FontSize = size,
        FontFamily = new WpfFontFamily(font),
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(col),
        Background = new SolidColorBrush(WpfColor.FromArgb(0x40, 0, 0, 0)),
        BorderBrush = B(AccColor),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(4, 2, 4, 2),
        MinWidth = 100,
        MinHeight = 30,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        TextAlignment = ParseAlign(align)
      };
      Canvas.SetLeft(_activeTextBox, pos.X);
      Canvas.SetTop(_activeTextBox, pos.Y);
      _shapeCanvas.IsHitTestVisible = true;
      _shapeCanvas.Children.Add(_activeTextBox);
      Logger.Info($"[ImageEditor] Text edit at ({pos.X:F0}, {pos.Y:F0}) reedit={meta != null}");
      Dispatcher.BeginInvoke(() =>
      {
        if (_activeTextBox != null)
        {
          _activeTextBox.Focus();
          Keyboard.Focus(_activeTextBox);
          _activeTextBox.SelectAll();
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
      string font  = ((WpfFontFamily)_activeTextBox.FontFamily).Source;
      double size  = _activeTextBox.FontSize;
      WpfColor col = ((SolidColorBrush)_activeTextBox.Foreground).Color;
      bool outline = _activeTextOutline;
      WpfColor oc  = _activeOutlineColor;
      string align = AlignName(_activeTextBox.TextAlignment);
      _shapeCanvas.Children.Remove(_activeTextBox);
      _shapeCanvas.IsHitTestVisible = false;
      _activeTextBox = null;
      if (string.IsNullOrWhiteSpace(text)) return;
      var meta = new TextMeta(text!, font, size, col, outline, oc, align);
      UIElement element = CreateText(meta, left, top);
      ((FrameworkElement)element).Tag = meta;
      _shapeCanvas.Children.Add(element);
      _undoStack.Push(new ElementAdded(element));
      _redoStack.Clear();
      RefreshLayers();
      if (!_finalizingForToolChange)
      {
        if (_currentTool != Tool.Select) SetTool(Tool.Select);
        Select(element);
      }
    }
    private static TextAlignment ParseAlign(string? a) => a switch
    {
      "center" => TextAlignment.Center,
      "right" => TextAlignment.Right,
      "justify" => TextAlignment.Justify,
      _ => TextAlignment.Left
    };
    private static string AlignName(TextAlignment a) => a switch
    {
      TextAlignment.Center => "center",
      TextAlignment.Right => "right",
      TextAlignment.Justify => "justify",
      _ => "left"
    };
    private UIElement CreateText(TextMeta m, double left, double top) => m.Outline ? CreateOutlinedText(m, left, top) : CreatePlainText(m, left, top);
    private UIElement CreatePlainText(TextMeta m, double left, double top)
    {
      var tb = new TextBlock
      {
        Text = m.Text,
        FontSize = m.Size,
        FontFamily = new WpfFontFamily(m.Font),
        Foreground = new SolidColorBrush(m.Color),
        FontWeight = m.Bold ? FontWeights.Bold : FontWeights.Normal,
        FontStyle = m.Italic ? FontStyles.Italic : FontStyles.Normal,
        TextAlignment = ParseAlign(m.Align)
      };
      if (m.Underline) tb.TextDecorations = TextDecorations.Underline;
      if (m.LineSpacing > 0 && Math.Abs(m.LineSpacing - 1.0) > 0.001)
      { tb.LineHeight = m.Size * m.LineSpacing; tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
      Canvas.SetLeft(tb, left);
      Canvas.SetTop(tb, top);
      return tb;
    }
    private UIElement CreateOutlinedText(TextMeta m, double left, double top)
    {
      var typeface = new Typeface(
        new WpfFontFamily(m.Font),
        m.Italic ? FontStyles.Italic : FontStyles.Normal,
        m.Bold ? FontWeights.Bold : FontWeights.Normal,
        FontStretches.Normal);
      var formatted = new FormattedText(
        m.Text,
        CultureInfo.InvariantCulture,
        System.Windows.FlowDirection.LeftToRight,
        typeface,
        m.Size,
        new SolidColorBrush(m.Color),
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
      formatted.MaxTextWidth = Math.Max(1, formatted.WidthIncludingTrailingWhitespace);
      formatted.TextAlignment = ParseAlign(m.Align);
      if (m.LineSpacing > 0 && Math.Abs(m.LineSpacing - 1.0) > 0.001)
        formatted.LineHeight = m.Size * Math.Max(0.8, m.LineSpacing);
      Geometry geometry = formatted.BuildGeometry(new WpfPoint(0, 0));
      if (m.Underline)
      {
        var grp = new GeometryGroup();
        grp.Children.Add(geometry);
        double gy = formatted.Height - Math.Max(2, m.Size * 0.16);
        grp.Children.Add(new RectangleGeometry(new Rect(0, gy, Math.Max(1, formatted.Width), Math.Max(1.5, m.Size / 14))));
        geometry = grp;
      }
      var path = new System.Windows.Shapes.Path
      {
        Data = geometry,
        Fill = new SolidColorBrush(m.Color),
        Stroke = new SolidColorBrush(m.OutlineColor),
        StrokeThickness = Math.Max(2, m.Size / 12)
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
          if (ReferenceEquals(_selected, ea.Element)) ClearSelection();
          break;
        case StrokesErased se:
          _inkCanvas.Strokes.Add(se.Strokes);
          break;
        case ElementMoved em:
          Canvas.SetLeft(em.Element, em.OldLeft); Canvas.SetTop(em.Element, em.OldTop);
          if (ReferenceEquals(_selected, em.Element)) DrawSelection();
          break;
        case ElementResized er:
          ApplyBounds(er.Element, er.Old);
          if (ReferenceEquals(_selected, er.Element)) DrawSelection();
          break;
        case ElementRemoved rem:
          _shapeCanvas.Children.Add(rem.Element);
          break;
        case ElementReplaced er:
          { int i = _shapeCanvas.Children.IndexOf(er.New); if (i >= 0) { _shapeCanvas.Children.RemoveAt(i); _shapeCanvas.Children.Insert(i, er.Old); }
            if (ReferenceEquals(_selected, er.New)) { _selected = er.Old; DrawSelection(); } }
          break;
        case EditAction ed:
          ed.Undo();
          break;
      }
      _redoStack.Push(action);
      RefreshLayers();
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
        case ElementMoved em:
          Canvas.SetLeft(em.Element, em.NewLeft); Canvas.SetTop(em.Element, em.NewTop);
          if (ReferenceEquals(_selected, em.Element)) DrawSelection();
          break;
        case ElementResized er:
          ApplyBounds(er.Element, er.New);
          if (ReferenceEquals(_selected, er.Element)) DrawSelection();
          break;
        case ElementRemoved rem:
          _shapeCanvas.Children.Remove(rem.Element);
          if (ReferenceEquals(_selected, rem.Element)) ClearSelection();
          break;
        case ElementReplaced er:
          { int i = _shapeCanvas.Children.IndexOf(er.Old); if (i >= 0) { _shapeCanvas.Children.RemoveAt(i); _shapeCanvas.Children.Insert(i, er.New); }
            if (ReferenceEquals(_selected, er.Old)) { _selected = er.New; DrawSelection(); } }
          break;
        case EditAction ed:
          ed.Redo();
          break;
      }
      _undoStack.Push(action);
      RefreshLayers();
    }
    private void ClearAll()
    {
      var result = MessageBox.Show(this,
        "Effacer toutes les annotations ?",
        "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
      if (result != MessageBoxResult.Yes) return;
      _inkCanvas.Strokes.Clear();
      _shapeCanvas.Children.Clear();
      _layerNames.Clear();
      _undoStack.Clear();
      _redoStack.Clear();
      ClearSelection();
      RefreshLayers();
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
      WasSaved = true;
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
    private sealed class LayerDto
    {
      public string Type = "";
      public double Left, Top, Width, Height, X1, Y1, X2, Y2, StrokeWidth = 2, Opacity = 1, FontSize;
      public string? Stroke, Fill, Text, Font, OutlineColor, Name;
      public string Align = "left";
      public bool Outline, Hidden;
      public bool Bold = true, Italic, Underline;
      public double LineSpacing = 1.0;
    }
    private sealed class ProjectDto
    {
      public string? Background; public int W, H; public string? StrokesB64;
      public List<LayerDto> Layers = new();
    }
    private static string? BrushHex(WpfBrush? b) => b is SolidColorBrush s && s.Color.A > 0 ? ColorToHex(s.Color) : null;
    private LayerDto? ElementToDto(UIElement el)
    {
      var d = new LayerDto { Opacity = el.Opacity, Hidden = el.Visibility != Visibility.Visible, Left = GetLeft(el), Top = GetTop(el) };
      d.Name = _layerNames.TryGetValue(el, out var nm) ? nm : null;
      switch (el)
      {
        case WpfRectangle r: d.Type = "rect"; d.Width = r.Width; d.Height = r.Height; d.Stroke = BrushHex(r.Stroke); d.Fill = BrushHex(r.Fill); d.StrokeWidth = r.StrokeThickness; return d;
        case Ellipse e: d.Type = "ellipse"; d.Width = e.Width; d.Height = e.Height; d.Stroke = BrushHex(e.Stroke); d.Fill = BrushHex(e.Fill); d.StrokeWidth = e.StrokeThickness; return d;
        case Line l: d.Type = "line"; d.X1 = l.X1; d.Y1 = l.Y1; d.X2 = l.X2; d.Y2 = l.Y2; d.Stroke = BrushHex(l.Stroke); d.StrokeWidth = l.StrokeThickness; return d;
        case FrameworkElement fe when fe.Tag is TextMeta m:
          d.Type = "text"; d.Text = m.Text; d.Font = m.Font; d.FontSize = m.Size; d.Stroke = ColorToHex(m.Color); d.Outline = m.Outline; d.OutlineColor = ColorToHex(m.OutlineColor); d.Align = m.Align; d.Bold = m.Bold; d.Italic = m.Italic; d.Underline = m.Underline; d.LineSpacing = m.LineSpacing; return d;
        case Canvas cv:
          d.Type = "arrow";
          foreach (var ch in cv.Children) if (ch is Line ln) { d.X1 = ln.X1; d.Y1 = ln.Y1; d.X2 = ln.X2; d.Y2 = ln.Y2; d.Stroke = BrushHex(ln.Stroke); d.StrokeWidth = ln.StrokeThickness; }
          return d;
      }
      return null;
    }
    private UIElement? DtoToElement(LayerDto d)
    {
      WpfBrush Stroke() => new SolidColorBrush(ParseColor(d.Stroke, WpfColor.FromRgb(0xFF, 0, 0)));
      WpfBrush Fill() => d.Fill != null ? new SolidColorBrush(ParseColor(d.Fill, WpfColor.FromArgb(0, 0, 0, 0))) : Brushes.Transparent;
      UIElement? el = null;
      switch (d.Type)
      {
        case "rect": { var r = new WpfRectangle { Width = d.Width, Height = d.Height, Stroke = Stroke(), StrokeThickness = d.StrokeWidth, Fill = Fill() }; Canvas.SetLeft(r, d.Left); Canvas.SetTop(r, d.Top); el = r; break; }
        case "ellipse": { var e = new Ellipse { Width = d.Width, Height = d.Height, Stroke = Stroke(), StrokeThickness = d.StrokeWidth, Fill = Fill() }; Canvas.SetLeft(e, d.Left); Canvas.SetTop(e, d.Top); el = e; break; }
        case "line": { var l = new Line { X1 = d.X1, Y1 = d.Y1, X2 = d.X2, Y2 = d.Y2, Stroke = Stroke(), StrokeThickness = d.StrokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; Canvas.SetLeft(l, d.Left); Canvas.SetTop(l, d.Top); el = l; break; }
        case "arrow": { var a = CreateArrow(new WpfPoint(d.X1, d.Y1), new WpfPoint(d.X2, d.Y2), Stroke(), false, d.StrokeWidth); Canvas.SetLeft(a, d.Left); Canvas.SetTop(a, d.Top); el = a; break; }
        case "text":
          {
            var m = new TextMeta(d.Text ?? "", d.Font ?? "Arial", d.FontSize > 0 ? d.FontSize : 32,
              ParseColor(d.Stroke, WpfColor.FromRgb(0xFF, 0, 0)), d.Outline, ParseColor(d.OutlineColor, WpfColor.FromRgb(0, 0, 0)), d.Align ?? "left", d.Bold, d.Italic, d.Underline, d.LineSpacing > 0 ? d.LineSpacing : 1.0);
            el = CreateText(m, d.Left, d.Top);
            ((FrameworkElement)el).Tag = m;
            break;
          }
      }
      if (el != null)
      {
        el.Opacity = d.Opacity;
        el.Visibility = d.Hidden ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(d.Name)) _layerNames[el] = d.Name!;
      }
      return el;
    }
    private void SaveLayeredAs()
    {
      FinalizeActiveTextBox();
      var dlg = new Microsoft.Win32.SaveFileDialog
      {
        Filter = "Projet éditable RoleplayOverlay (*.roedit)|*.roedit",
        FileName = System.IO.Path.GetFileNameWithoutExtension(_imagePath) + ".roedit",
        InitialDirectory = System.IO.Path.GetDirectoryName(_imagePath)
      };
      if (dlg.ShowDialog(this) != true) return;
      try
      {
        var proj = new ProjectDto { Background = _imagePath, W = _imgW, H = _imgH };
        foreach (var ch in _shapeCanvas.Children)
          if (ch is UIElement el && !ReferenceEquals(el, _activeTextBox)) { var d = ElementToDto(el); if (d != null) proj.Layers.Add(d); }
        if (_inkCanvas.Strokes.Count > 0)
        {
          using var ms = new MemoryStream();
          _inkCanvas.Strokes.Save(ms);
          proj.StrokesB64 = Convert.ToBase64String(ms.ToArray());
        }
        File.WriteAllText(dlg.FileName, Newtonsoft.Json.JsonConvert.SerializeObject(proj, Newtonsoft.Json.Formatting.Indented));
        WasSaved = true;
        _statusLabel.Text = "Projet (calques conservés) enregistré : " + System.IO.Path.GetFileName(dlg.FileName);
        Logger.Info($"[ImageEditor] SaveLayered: {dlg.FileName} ({proj.Layers.Count} calques)");
      }
      catch (Exception ex) { _statusLabel.Text = "Échec projet : " + ex.Message; }
    }
    private void OpenLayered()
    {
      var dlg = new Microsoft.Win32.OpenFileDialog
      {
        Filter = "Projet éditable RoleplayOverlay (*.roedit)|*.roedit",
        InitialDirectory = System.IO.Path.GetDirectoryName(_imagePath)
      };
      if (dlg.ShowDialog(this) != true) return;
      try
      {
        var proj = Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectDto>(File.ReadAllText(dlg.FileName));
        if (proj == null) return;
        FinalizeActiveTextBox();
        ClearSelection();
        _shapeCanvas.Children.Clear();
        _inkCanvas.Strokes.Clear();
        if (!string.IsNullOrEmpty(proj.StrokesB64))
        {
          using var ms = new MemoryStream(Convert.FromBase64String(proj.StrokesB64));
          _inkCanvas.Strokes.Add(new StrokeCollection(ms));
        }
        foreach (var d in proj.Layers) { var el = DtoToElement(d); if (el != null) _shapeCanvas.Children.Add(el); }
        _undoStack.Clear(); _redoStack.Clear();
        RefreshLayers();
        _statusLabel.Text = (proj.W != _imgW || proj.H != _imgH)
          ? $"Projet chargé — ⚠ dimensions {proj.W}x{proj.H} ≠ image {_imgW}x{_imgH}"
          : $"Projet chargé ({proj.Layers.Count} calques).";
        Logger.Info($"[ImageEditor] OpenLayered: {dlg.FileName}");
      }
      catch (Exception ex) { _statusLabel.Text = "Ouverture projet échouée : " + ex.Message; }
    }
    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
      if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
      double factor = e.Delta > 0 ? 1.1 : 0.9;
      _zoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
      var transform = new ScaleTransform(_zoom, _zoom);
      _canvasHost.LayoutTransform = transform;
      _zoomLabel.Text = $"{(_zoom * 100):F0}%";
      UpdateTextPopup();
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
      if (_currentTool == Tool.Select && _selected != null)
      {
        if (e.Key == Key.Delete || e.Key == Key.Back) { DeleteSelected(); e.Handled = true; return; }
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
        switch (e.Key)
        {
          case Key.Left:  NudgeSelected(-step, 0); e.Handled = true; return;
          case Key.Right: NudgeSelected(step, 0);  e.Handled = true; return;
          case Key.Up:    NudgeSelected(0, -step); e.Handled = true; return;
          case Key.Down:  NudgeSelected(0, step);  e.Handled = true; return;
        }
      }
      if (e.Key == Key.Escape)
      {
        if (_selected != null) { ClearSelection(); e.Handled = true; return; }
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
      else if (e.Key == Key.J && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        DuplicateSelected();
        e.Handled = true;
      }
      else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        CopySelection();
        e.Handled = true;
      }
      else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        PasteClipboard();
        e.Handled = true;
      }
      else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
      {
        if (_currentTool != Tool.Select) SetTool(Tool.Select);
        SelectMany(_shapeCanvas.Children.OfType<UIElement>().Where(x => !ReferenceEquals(x, _activeTextBox)).ToList());
        e.Handled = true;
      }
      else if (e.Key == Key.V) SetTool(Tool.Select);
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
      ApplyStrokeWidthToSelected(_strokeWidth);
    }
    private void OnPaletteClick(object sender, MouseButtonEventArgs e)
    {
      if (sender is WpfRectangle rect && rect.Tag is string hex)
      {
        _strokeColor = (WpfColor)ColorConverter.ConvertFromString(hex);
        _colorSwatch.Fill = new SolidColorBrush(_strokeColor);
        ApplyInkAttributes();
        SaveEditorPrefs();
        ApplyStrokeColorToSelected(_strokeColor);
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
        ApplyStrokeColorToSelected(_strokeColor);
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
        ApplyFillColorToSelected(_fillColor);
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
          ApplyStrokeColorToSelected(color);
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
        if (!_suppressFontUi) ApplyTextMetaToSelected(m => m with { Font = _fontFamily });
      }
    }
    private void OnFontSizeChanged(object sender, RoutedEventArgs e)
    {
      if (double.TryParse(_fontSizeBox.Text, out var size) && size >= 6 && size <= 300)
      {
        _fontSize = size;
        SaveEditorPrefs();
        if (!_suppressFontUi) ApplyTextMetaToSelected(m => m with { Size = _fontSize });
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
      _textAlign    = string.IsNullOrWhiteSpace(UserPrefs.ImageEditorTextAlign) ? "left" : UserPrefs.ImageEditorTextAlign!;
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
      UserPrefs.ImageEditorTextAlign   = _textAlign;
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