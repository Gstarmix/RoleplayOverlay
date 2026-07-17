using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RoleplayOverlay.Cam;
namespace RoleplayOverlay
{
  internal sealed class TlPanel : Panel { public TlPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
  internal sealed class MediaTimelineForm : Form, IMessageFilter
  {
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Sequence _seq;
    private readonly string   _clipPath;
    private readonly string   _slidesDir;
    private readonly string   _ffmpegExe;
    private readonly double   _clipDur;
    private List<MediaItem> Items => _seq.MediaItems!;
    private readonly TlPanel _preview = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 12, 16) };
    private readonly TlPanel _track   = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 22, 28) };
    private readonly Label   _info    = new();
    private readonly Dictionary<string, Bitmap> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GifClip?> _gifs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_vidLock = new();
    private static readonly Dictionary<string, VideoClip?> s_videoClips = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> s_videoLru = new();
    private static readonly HashSet<string> s_videoDecoding = new(StringComparer.OrdinalIgnoreCase);
    private const long VideoCacheBudget = 1_200_000_000;
    private Bitmap? _frame;
    private double  _playhead;
    private MediaItem? _selected;
    private readonly List<MediaItem> _multiSel = new();
    private static readonly List<MediaItem> s_layerClipboard = new();
    private static MediaItem? s_paramClipboard;
    private static readonly HashSet<string> s_paramGroups = new();
    private static readonly (string key, string label)[] ParamGroupDefs =
    {
      ("pos",    "Position"),
      ("size",   "Taille"),
      ("crop",   "Rognage & pan"),
      ("anim",   "Animation"),
      ("speed",  "Vitesse & boucle"),
      ("style",  "Cadre & entier"),
      ("window", "Fenêtre (début + durée)"),
    };
    private readonly Dictionary<MediaItem, (double x, double y)> _dragOrigPos = new();
    private readonly Dictionary<MediaItem, double> _dragOrigAt = new();
    private readonly Dictionary<MediaItem, double> _dragOrigDurs = new();
    private readonly Dictionary<MediaItem, double> _dragOrigScales = new();
    private PointF _marqueeCur;
    private bool _marqueeActive;
    private readonly System.Collections.Generic.IList<Sequence>? _allSeqs;
    private Button _playBtn = null!;
    private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 33 };
    private VideoFileReader? _player;
    private bool _playing;
    private bool _endReached;
    private long _playStartMs;
    private bool _scrubbing;
    private readonly List<HistState> _history = new();
    private int  _histIdx = -1;
    private bool _restoring;
    private long _lastCommitTick;
    private string _lastCommitLabel = "";
    private Button? _undoBtn, _redoBtn, _histBtn;
    private NAudio.Wave.WaveOutEvent? _wave;
    private NAudio.Wave.MediaFoundationReader? _audio;
    private double _playOrigin;
    private string AudioSourcePath => _hasCam ? (_camClipPath ?? _clipPath) : _clipPath;
    private ComboBox _animCombo = null!;
    private bool _suppressAnim;
    private static readonly string[] AnimKeys = { "none", "slide", "zoom", "float", "scrolldown", "scrollup" };
    private NumericUpDown _speedBox = null!;
    private CheckBox _loopCheck = null!;
    private NumericUpDown _trimInBox = null!;
    private NumericUpDown _trimOutBox = null!;
    private CheckBox _borderCheck = null!;
    private CheckBox _containCheck = null!;
    private CheckBox _panAnimCheck = null!;
    private Button _panABBtn = null!;
    private NumericUpDown _panT1Box = null!;
    private NumericUpDown _panDurBox = null!;
    private bool _panEditB;
    private bool _suppressMediaOpts;
    private readonly System.Windows.Forms.Timer _animPrevTimer = new() { Interval = 33 };
    private bool _animPreview;
    private double _animPrevT;
    private long _animPrevStartMs;
    private readonly string? _camClipPath;
    private bool _hasCam;
    private readonly bool _composedBase;
    private bool _camRelift;
    private Button? _reliftBtn;
    private Label _banner = null!;
    private bool CamEditable => _hasCam || (_composedBase && _camRelift);
    private Bitmap? _camThumb;
    private VideoFileReader? _camPlayer;
    private bool _camSelected;
    private double _dragOrigCamX, _dragOrigCamY, _dragOrigCamDiam;
    private enum Drag { None, Move, Resize, BlockMove, BlockLeft, BlockRight, Playhead, CamMove, CamResize, Pan,
                        CropL, CropT, CropR, CropB, TrimLeft, TrimRight, Marquee, PanM1, PanM2 }
    private Drag _drag = Drag.None;
    private PointF _dragStart;
    private bool _dragEngaged;
    private double _dragOrigA, _dragOrigDur, _dragOrigX, _dragOrigY, _dragOrigScale;
    private double _dragOrigPanX, _dragOrigPanY;
    private double _dragOrigSpeed, _dragOrigCropFL, _dragOrigCropFT, _dragOrigCropFR, _dragOrigCropFB;
    private double _dragOrigTrimIn, _dragOrigTrimOut;
    private readonly Dictionary<string, double> _durCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cropMode;
    private Button _cropBtn = null!;
    private readonly Dictionary<string, (double[] xs, double[] ys)> _edgeLines = new(StringComparer.OrdinalIgnoreCase);
    private bool _cropSnapped;
    private CheckBox _linkSpeedCheck = null!;
    private CheckBox _snapCheck = null!;
    private double _snapGuideT = double.NaN;
    private const float SnapPx = 8f;
    private float _snapGuideVx = float.NaN, _snapGuideVy = float.NaN;
    private Panel _trackHost = null!;
    private VScrollBar _trackScroll = null!;
    private int _rowScroll;
    private const int TrackPadB = 12;
    private readonly int _canvasW, _canvasH;
    public bool Changed { get; private set; }
    public MediaTimelineForm(Sequence seq, string clipPath, string slidesDir, string ffmpegDir, double explicitDur = 0, string? camClipPath = null, bool composedBase = false, System.Collections.Generic.IList<Sequence>? allSequences = null, VideoAspect outputAspect = VideoAspect.Portrait)
    {
      _seq = seq;
      (_canvasW, _canvasH) = VideoAspectInfo.Dimensions(outputAspect);
      _allSeqs = allSequences;
      _clipPath = clipPath;
      _camClipPath = camClipPath;
      _hasCam = !string.IsNullOrWhiteSpace(camClipPath) && File.Exists(camClipPath);
      _composedBase = composedBase;
      _camRelift = seq.CamRelift;
      if ((_hasCam || (_composedBase && _camRelift)) && CamAtExactDefault())
      {
        if (!(_composedBase && CamGeoSidecar.ApplyTo(_seq, clipPath)))
          SeedCamFromPrefsIfDefault();
      }
      _slidesDir = slidesDir;
      _ffmpegExe = File.Exists(Path.Combine(ffmpegDir, "ffmpeg.exe"))
        ? Path.Combine(ffmpegDir, "ffmpeg.exe")
        : (File.Exists(@"C:\ffmpeg\bin\ffmpeg.exe") ? @"C:\ffmpeg\bin\ffmpeg.exe" : "ffmpeg");
      seq.MediaItems ??= new List<MediaItem>();
      if (Items.Count == 0 && !string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath))
      {
        Items.Add(new MediaItem { Path = seq.MediaPath, SourcePath = seq.MediaSourcePath, AppearAt = 0, AppearDur = 0, PosX = 0.5, PosY = 0.5, Scale = Math.Clamp(seq.MediaScale, 0.05f, 1.0f) });
      }
      foreach (var it in Items)
        if (Math.Abs(it.Scale - 0.5) < 1e-6) { it.Scale = 0.80; Changed = true; }
      _clipDur = explicitDur > 0.1 ? explicitDur : Math.Max(0.5, ProbeDuration(clipPath));
      foreach (var it in Items)
      {
        if (it.AppearAt > _clipDur - 0.2) { it.AppearAt = Math.Max(0, _clipDur - 0.2); Changed = true; }
        if (it.AppearDur > 0.01 && it.AppearAt + it.AppearDur > _clipDur + 0.01)
        { it.AppearDur = Math.Max(0.2, _clipDur - it.AppearAt); Changed = true; }
        if (IsAnimatedMedia(it)) SourceDur(it);
      }
      Logger.Info($"[Montage] Ouverture — {(_seq.Note ?? _seq.Id ?? "?")} · clip {Path.GetFileName(clipPath)} ({_clipDur:0.0}s) · {Items.Count} média(s)");
      Text = "Montage média — " + Path.GetFileName(clipPath);
      StartPosition = FormStartPosition.CenterParent;
      Size = new Size(980, 900);
      MinimumSize = new Size(720, 640);
      BackColor = Color.FromArgb(18, 18, 22);
      ForeColor = Color.Gainsboro;
      Font = new Font("Segoe UI", 9f);
      BuildUi();
      UpdateTrackLayout();
      Application.AddMessageFilter(this);
      FormClosed += (_, _) => Application.RemoveMessageFilter(this);
      if (_hasCam) _camThumb = ExtractFrameOf(_camClipPath!, 0.3);
      SetPlayhead(0);
      PushInitialHistory();
    }
    private const int WM_MOUSEWHEEL = 0x020A;
    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
      if (m.Msg != WM_MOUSEWHEEL || IsDisposed || !_trackScroll.Visible) return false;
      var pt = _track.PointToClient(Cursor.Position);
      if (!_track.ClientRectangle.Contains(pt)) return false;
      int delta = unchecked((short)((long)m.WParam >> 16));
      SetRowScroll(_rowScroll - Math.Sign(delta));
      return true;
    }
    private RectangleF CamRect(RectangleF fr)
    {
      float diam = (float)Math.Clamp(_seq.CamDiam, 0.10, 1.0) * fr.Width;
      float cx = fr.X + (float)Math.Clamp(_seq.CamX, 0, 1) * fr.Width;
      float cy = fr.Y + (float)Math.Clamp(_seq.CamY, 0, 1) * fr.Height;
      return new RectangleF(cx - diam / 2f, cy - diam / 2f, diam, diam);
    }
    private void BuildUi()
    {
      var top = new FlowLayoutPanel
      {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true, FlowDirection = FlowDirection.LeftToRight,
        BackColor = Color.FromArgb(28, 28, 34), Padding = new Padding(6, 5, 6, 6),
      };
      Button FlowBtn(string text, EventHandler onClick, Color? back = null, int w = 0)
      {
        var btn = new Button
        {
          Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
          BackColor = back ?? Color.FromArgb(55, 55, 62), Height = 28, AutoSize = false,
          Margin = new Padding(3, 3, 3, 3),
          Width = w > 0 ? w : Math.Max(40, TextRenderer.MeasureText(text, Font).Width + 22),
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += onClick;
        return btn;
      }
      FlowLayoutPanel Group(params Control[] cs)
      {
        var p = new FlowLayoutPanel
        {
          AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
          FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(2, 0, 2, 0),
        };
        foreach (var c in cs) { c.Margin = new Padding(2, c is Label ? 8 : 3, 2, 3); p.Controls.Add(c); }
        return p;
      }
      top.Controls.Add(FlowBtn("+ Image", (_, _) => AddMedia(false)));
      top.Controls.Add(FlowBtn("+ Vidéo", (_, _) => AddMedia(true)));
      top.Controls.Add(FlowBtn("+ Texte", (_, _) => AddText()));
      top.Controls.Add(FlowBtn("Supprimer", (_, _) => DeleteSelected()));
      top.Controls.Add(FlowBtn("✏ Éditer", (_, _) => EditSelected()));
      top.Controls.Add(FlowBtn("⧉ Dupliquer", (_, _) => DuplicateSelected()));
      top.Controls.Add(FlowBtn("⎘ Copier", (_, _) => CopySelection()));
      top.Controls.Add(FlowBtn("📋 Coller", (_, _) => PasteClipboard()));
      top.Controls.Add(FlowBtn("⬆ 1er plan", (_, _) => MoveLayer(-1)));
      top.Controls.Add(FlowBtn("⬇ Fond", (_, _) => MoveLayer(+1)));
      var tips = new ToolTip();
      Button AlignBtn(string text, char where, bool content, string tip, int w = 34)
      {
        var btn = FlowBtn(text, (_, _) => AlignSelection(where, content), w: w);
        tips.SetToolTip(btn, tip);
        return btn;
      }
      top.Controls.Add(Group(
        new Label { Text = "Boîte :", ForeColor = Color.Gainsboro, AutoSize = true },
        AlignBtn("▣", 'C', false, "Centrer la boîte dans le cadre (X et Y)", 40),
        AlignBtn("⇤", 'L', false, "Coller la boîte au bord gauche du cadre"),
        AlignBtn("⇥", 'R', false, "Coller la boîte au bord droit du cadre"),
        AlignBtn("⤒", 'T', false, "Coller la boîte en haut du cadre"),
        AlignBtn("⤓", 'B', false, "Coller la boîte en bas du cadre")));
      top.Controls.Add(Group(
        new Label { Text = "Contenu :", ForeColor = Color.Gainsboro, AutoSize = true },
        AlignBtn("▣", 'C', true, "Recentrer l'image/vidéo dans sa boîte (pan 0/0)", 40),
        AlignBtn("⇤", 'L', true, "Montrer le bord gauche de l'image/vidéo"),
        AlignBtn("⇥", 'R', true, "Montrer le bord droit de l'image/vidéo"),
        AlignBtn("⤒", 'T', true, "Montrer le haut de l'image/vidéo"),
        AlignBtn("⤓", 'B', true, "Montrer le bas de l'image/vidéo")));
      _panAnimCheck = new CheckBox { Text = "🎬 Pan A→B", ForeColor = Color.Gainsboro, AutoSize = true, Enabled = false };
      _panAnimCheck.CheckedChanged += OnPanAnimChanged;
      tips.SetToolTip(_panAnimCheck, "Anime le recadrage interne : glisse du cadrage A (départ) au cadrage B (arrivée)");
      _panABBtn = FlowBtn("Édite : A", (_, _) => TogglePanEdit(), w: 84);
      _panABBtn.Enabled = false;
      tips.SetToolTip(_panABBtn, "Bascule l'état édité par les gestes de pan (clic droit, flèches, boutons Contenu)");
      _panT1Box = new NumericUpDown { DecimalPlaces = 2, Increment = 0.1M, Minimum = 0, Maximum = 3600, Width = 56, Enabled = false };
      _panDurBox = new NumericUpDown { DecimalPlaces = 2, Increment = 0.1M, Minimum = 0, Maximum = 3600, Width = 56, Enabled = false };
      _panT1Box.ValueChanged += OnPanTimingChanged;
      _panDurBox.ValueChanged += OnPanTimingChanged;
      tips.SetToolTip(_panT1Box, "Début de la transition (s après l'apparition du média)");
      tips.SetToolTip(_panDurBox, "Durée de la transition (s) — 0 = jusqu'à la fin de la fenêtre");
      top.Controls.Add(Group(_panAnimCheck, _panABBtn,
        new Label { Text = "de", ForeColor = Color.Gainsboro, AutoSize = true }, _panT1Box,
        new Label { Text = "durée", ForeColor = Color.Gainsboro, AutoSize = true }, _panDurBox));
      _playBtn = FlowBtn("▶ Lire", (_, _) => TogglePlay(), w: 84);
      top.Controls.Add(_playBtn);
      _playTimer.Tick += PlayTick;
      _animPrevTimer.Tick += AnimPrevTick;
      _undoBtn = FlowBtn("⟲", (_, _) => Undo(), w: 40);
      _redoBtn = FlowBtn("↻", (_, _) => Redo(), w: 40);
      _histBtn = FlowBtn("⟲ Historique", (_, _) => ShowHistoryMenu(), w: 150);
      top.Controls.Add(_undoBtn);
      top.Controls.Add(_redoBtn);
      top.Controls.Add(_histBtn);
      _animCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Enabled = false, Width = 130 };
      _animCombo.Items.AddRange(new object[] { "Aucune", "Glissement", "Zoom lent", "Flottement", "Défilement ↓", "Défilement ↑" });
      _animCombo.SelectedIndex = 0;
      _animCombo.SelectedIndexChanged += OnAnimChanged;
      top.Controls.Add(Group(new Label { Text = "Anim :", ForeColor = Color.Gainsboro, AutoSize = true }, _animCombo));
      _speedBox = new NumericUpDown { DecimalPlaces = 2, Increment = 0.05M, Minimum = 0.10M, Maximum = 4.00M, Value = 1.00M, Enabled = false, Width = 60 };
      _speedBox.ValueChanged += OnSpeedChanged;
      top.Controls.Add(Group(new Label { Text = "Vitesse :", ForeColor = Color.Gainsboro, AutoSize = true }, _speedBox));
      _loopCheck = new CheckBox { Text = "Boucle", ForeColor = Color.Gainsboro, AutoSize = true, Checked = true, Enabled = false };
      _loopCheck.CheckedChanged += OnLoopChanged;
      top.Controls.Add(Group(_loopCheck));
      _borderCheck = new CheckBox { Text = "Cadre", ForeColor = Color.Gainsboro, AutoSize = true, Checked = true, Enabled = false };
      _borderCheck.CheckedChanged += OnBorderChanged;
      _containCheck = new CheckBox { Text = "🧩 Entier", ForeColor = Color.Gainsboro, AutoSize = true, Enabled = false };
      _containCheck.CheckedChanged += OnContainChanged;
      top.Controls.Add(Group(_borderCheck, _containCheck));
      _trimInBox = new NumericUpDown { DecimalPlaces = 2, Increment = 0.1M, Minimum = 0, Maximum = 3600, Enabled = false, Width = 58 };
      _trimInBox.ValueChanged += OnTrimChanged;
      _trimOutBox = new NumericUpDown { DecimalPlaces = 2, Increment = 0.1M, Minimum = 0, Maximum = 3600, Enabled = false, Width = 58 };
      _trimOutBox.ValueChanged += OnTrimChanged;
      top.Controls.Add(Group(
        new Label { Text = "Rogner s :", ForeColor = Color.Gainsboro, AutoSize = true }, _trimInBox,
        new Label { Text = "→", ForeColor = Color.Gainsboro, AutoSize = true }, _trimOutBox));
      _cropBtn = FlowBtn("✂ Rogner", (_, _) => ToggleCropMode(), w: 92);
      _cropBtn.Enabled = false;
      top.Controls.Add(_cropBtn);
      _linkSpeedCheck = new CheckBox { Text = "Vit.∝1/taille", ForeColor = Color.Gainsboro, AutoSize = true, Checked = false };
      top.Controls.Add(Group(_linkSpeedCheck));
      _snapCheck = new CheckBox { Text = "🧲 Aimant", ForeColor = Color.Gainsboro, AutoSize = true, Checked = true };
      top.Controls.Add(Group(_snapCheck));
      if (_composedBase && !_hasCam)
      {
        _reliftBtn = FlowBtn("", (_, _) => ToggleRelift(), w: 150);
        StyleReliftBtn();
        top.Controls.Add(_reliftBtn);
        top.Controls.Add(FlowBtn("📍 Recaler sur la cam", (_, _) => ForceCamFromStudio(), w: 168));
      }
      top.Controls.Add(FlowBtn("Fermer", (_, _) => Close(), w: 84));
      _info.AutoSize = false;
      _info.Dock = DockStyle.Fill;
      _info.TextAlign = ContentAlignment.MiddleLeft;
      _info.AutoEllipsis = true;
      _info.ForeColor = Color.Gray;
      var infoStrip = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.FromArgb(26, 26, 32), Padding = new Padding(10, 0, 8, 0) };
      infoStrip.Controls.Add(_info);
      _trackHost = new Panel { Dock = DockStyle.Bottom, Height = 190, BackColor = Color.FromArgb(22, 22, 28), Padding = new Padding(0) };
      _track.Paint += PaintTrack;
      _track.MouseDown += TrackMouseDown;
      _track.MouseMove += TrackMouseMove;
      _track.MouseUp += (_, _) => EndDrag();
      _trackScroll = new VScrollBar { Dock = DockStyle.Right, Visible = false, Minimum = 0, SmallChange = 1, LargeChange = 1 };
      _trackScroll.ValueChanged += (_, _) => { if (_rowScroll != _trackScroll.Value) { _rowScroll = _trackScroll.Value; _track.Invalidate(); } };
      _trackHost.Controls.Add(_track);
      _trackHost.Controls.Add(_trackScroll);
      var trackHost = _trackHost;
      Resize += (_, _) => UpdateTrackLayout();
      _preview.Paint += PaintPreview;
      _preview.MouseDown += PreviewMouseDown;
      _preview.MouseMove += PreviewMouseMove;
      _preview.MouseUp += (_, _) => EndDrag();
      _preview.MouseDoubleClick += (_, _) => { if (_selected?.Text != null) EditSelectedText(); };
      _banner = new Label
      {
        Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(10, 0, 8, 0), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
      };
      UpdateBanner();
      Controls.Add(_preview);
      Controls.Add(trackHost);
      Controls.Add(infoStrip);
      Controls.Add(_banner);
      Controls.Add(top);
    }
    private void UpdateBanner()
    {
      if (_hasCam)
      {
        _banner.Text = "● Clip cam séparé — les médias passent SOUS la cam. Clique la CAM pour la placer/redimensionner.";
        _banner.BackColor = Color.FromArgb(30, 70, 45);
        _banner.ForeColor = Color.FromArgb(150, 235, 175);
      }
      else if (_composedBase && _camRelift)
      {
        _banner.Text = "● Cam relevée — les médias passent SOUS la cam. Aligne le cercle CAM pointillé sur ta cam filmée (glisse / poignée / flèches).";
        _banner.BackColor = Color.FromArgb(30, 70, 45);
        _banner.ForeColor = Color.FromArgb(150, 235, 175);
      }
      else if (_composedBase)
      {
        _banner.Text = "▲ Clip composé — les médias passent AU-DESSUS de la cam (déjà cuite). "
                     + "Clique « Cam au-dessus » pour relever ta cam par-dessus les médias (sans re-filmer).";
        _banner.BackColor = Color.FromArgb(70, 55, 25);
        _banner.ForeColor = Color.FromArgb(240, 200, 120);
      }
      else
      {
        _banner.Text = "Les médias passent au-dessus du clip de fond.";
        _banner.BackColor = Color.FromArgb(40, 40, 48);
        _banner.ForeColor = Color.Gainsboro;
      }
    }
    private void StyleReliftBtn()
    {
      if (_reliftBtn == null) return;
      _reliftBtn.Text = _camRelift ? "Cam au-dessus : ON" : "Cam au-dessus : OFF";
      _reliftBtn.BackColor = _camRelift ? Color.FromArgb(40, 110, 60) : Color.FromArgb(55, 55, 62);
    }
    private bool CamAtExactDefault() =>
      Math.Abs(_seq.CamX - 0.80) < 1e-6 && Math.Abs(_seq.CamY - 0.84) < 1e-6 && Math.Abs(_seq.CamDiam - 0.34) < 1e-6;
    private double CamWidthPrefForFormat => _canvasW >= _canvasH ? UserPrefs.CamWidthFracLandscape : UserPrefs.CamWidthFrac;
    private void SeedCamFromPrefsIfDefault()
    {
      if (CamAtExactDefault())
      {
        _seq.CamX = Math.Clamp(UserPrefs.CamCenterXFrac, 0, 1);
        _seq.CamY = Math.Clamp(UserPrefs.CamCenterYFrac, 0, 1);
        _seq.CamDiam = Math.Clamp(CamWidthPrefForFormat, 0.10, 1.0);
      }
    }
    private void ForceCamFromStudio()
    {
      if (!_camRelift) { _camRelift = true; _seq.CamRelift = true; StyleReliftBtn(); }
      _seq.CamX = Math.Clamp(UserPrefs.CamCenterXFrac, 0, 1);
      _seq.CamY = Math.Clamp(UserPrefs.CamCenterYFrac, 0, 1);
      _seq.CamDiam = Math.Clamp(CamWidthPrefForFormat, 0.10, 1.0);
      SelectCam();
      UpdateBanner();
      Commit("Recaler cam");
      _preview.Invalidate();
    }
    private void ToggleRelift()
    {
      _camRelift = !_camRelift;
      _seq.CamRelift = _camRelift;
      if (_camRelift) SeedCamFromPrefsIfDefault();
      StyleReliftBtn();
      if (_camRelift) SelectCam(); else _camSelected = false;
      UpdateBanner();
      Commit("Cam au-dessus");
    }
    private RectangleF FrameRect()
    {
      var cr = _preview.ClientRectangle;
      cr.Inflate(-12, -12);
      float aspect = (float)_canvasW / _canvasH;
      float w = cr.Width, h = w / aspect;
      if (h > cr.Height) { h = cr.Height; w = h * aspect; }
      float px = cr.X + (cr.Width - w) / 2f;
      float py = cr.Y + (cr.Height - h) / 2f;
      return new RectangleF(px, py, w, h);
    }
    private RectangleF MediaRect(MediaItem it, RectangleF fr)
    {
      float sc = (float)Math.Clamp(it.Scale, 0.05, 1.0);
      float wPx = sc * fr.Width;
      float hPx = sc * fr.Height;
      float cx = fr.X + (float)it.PosX * fr.Width;
      float cy = fr.Y + (float)it.PosY * fr.Height;
      return new RectangleF(cx - wPx / 2f, cy - hPx / 2f, wPx, hPx);
    }
    private static Rectangle CoverSrc(Bitmap th, RectangleF box, double panX = 0, double panY = 0,
      double cropL = 0, double cropT = 0, double cropR = 0, double cropB = 0, double zoom = 1.0)
    {
      cropL = Math.Clamp(cropL, 0, 0.9); cropT = Math.Clamp(cropT, 0, 0.9);
      cropR = Math.Clamp(cropR, 0, 0.9); cropB = Math.Clamp(cropB, 0, 0.9);
      int kx = (int)Math.Round(th.Width * cropL), ky = (int)Math.Round(th.Height * cropT);
      int kw = Math.Max(1, th.Width  - kx - (int)Math.Round(th.Width  * cropR));
      int kh = Math.Max(1, th.Height - ky - (int)Math.Round(th.Height * cropB));
      float boxA = box.Width / Math.Max(1f, box.Height);
      float imgA = kw / (float)Math.Max(1, kh);
      int sw, sh;
      if (imgA > boxA) { sh = kh; sw = (int)Math.Round(sh * boxA); }
      else            { sw = kw; sh = (int)Math.Round(sw / boxA); }
      if (zoom > 1.0001) { sw = (int)Math.Round(sw / zoom); sh = (int)Math.Round(sh / zoom); }
      sw = Math.Clamp(sw, 1, kw); sh = Math.Clamp(sh, 1, kh);
      int exX = Math.Max(0, kw - sw), exY = Math.Max(0, kh - sh);
      int sx = kx + Math.Clamp((int)Math.Round(exX / 2.0 * (1 + panX)), 0, exX);
      int sy = ky + Math.Clamp((int)Math.Round(exY / 2.0 * (1 + panY)), 0, exY);
      return new Rectangle(sx, sy, sw, sh);
    }
    private static (Rectangle src, RectangleF dst) ContainSrcDst(Bitmap th, RectangleF box,
      double cropL = 0, double cropT = 0, double cropR = 0, double cropB = 0)
    {
      cropL = Math.Clamp(cropL, 0, 0.9); cropT = Math.Clamp(cropT, 0, 0.9);
      cropR = Math.Clamp(cropR, 0, 0.9); cropB = Math.Clamp(cropB, 0, 0.9);
      int kx = (int)Math.Round(th.Width * cropL), ky = (int)Math.Round(th.Height * cropT);
      int kw = Math.Max(1, th.Width  - kx - (int)Math.Round(th.Width  * cropR));
      int kh = Math.Max(1, th.Height - ky - (int)Math.Round(th.Height * cropB));
      float sc = Math.Min(box.Width / kw, box.Height / kh);
      float dw = Math.Max(1f, kw * sc), dh = Math.Max(1f, kh * sc);
      var dst = new RectangleF(box.X + (box.Width - dw) / 2f, box.Y + (box.Height - dh) / 2f, dw, dh);
      return (new Rectangle(kx, ky, kw, kh), dst);
    }
    private const float CropHandle = 9f;
    private static RectangleF[] CropHandleRects(RectangleF mr)
    {
      float hs = CropHandle, cx = mr.Left + mr.Width / 2f, cy = mr.Top + mr.Height / 2f;
      return new[]
      {
        new RectangleF(mr.Left  - hs, cy - hs, hs * 2, hs * 2),
        new RectangleF(cx - hs, mr.Top - hs, hs * 2, hs * 2),
        new RectangleF(mr.Right - hs, cy - hs, hs * 2, hs * 2),
        new RectangleF(cx - hs, mr.Bottom - hs, hs * 2, hs * 2)
      };
    }
    private static bool IsAnimatedMedia(MediaItem it)
      => !string.IsNullOrWhiteSpace(it.Path)
         && System.IO.Path.GetExtension(it.Path).ToLowerInvariant() is ".gif" or ".mp4" or ".mov" or ".webm" or ".mkv";
    private static double ClampSpeed(float s) => Math.Clamp(s <= 0 ? 1.0 : s, 0.10, 4.00);
    private double SourceDur(MediaItem it)
    {
      if (string.IsNullOrWhiteSpace(it.Path)) return 0;
      if (_durCache.TryGetValue(it.Path!, out var d)) return d;
      var gif = GetGif(it);
      d = gif != null ? gif.TotalMs / 1000.0 : ProbeDuration(it.Path!);
      _durCache[it.Path!] = d;
      return d;
    }
    private double TrimmedDur(MediaItem it)
    {
      double sd = SourceDur(it);
      double tin = Math.Clamp(it.TrimIn ?? 0f, 0, Math.Max(0, sd - 0.1));
      double tout = it.TrimOut is > 0 ? Math.Min(it.TrimOut.Value, sd) : sd;
      return Math.Max(0.1, tout - tin);
    }
    private double PlayDur(MediaItem it) => TrimmedDur(it) / ClampSpeed(it.Speed);
    private double WindowDur(MediaItem it)
      => it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
    private bool IsTruncated(MediaItem it)
      => IsAnimatedMedia(it) && PlayDur(it) > WindowDur(it) + 0.05;
    private void FitWindowToAnim(MediaItem it, bool allowSpeedUp)
    {
      if (!IsAnimatedMedia(it)) return;
      double remaining = Math.Max(0.2, _clipDur - it.AppearAt);
      double pd = PlayDur(it);
      if (pd > remaining + 0.01 && allowSpeedUp)
      {
        it.Speed = (float)Math.Clamp(TrimmedDur(it) / remaining, 0.10, 4.00);
        pd = PlayDur(it);
      }
      it.AppearDur = Math.Round(Math.Min(pd, remaining), 2);
    }
    private void PaintPreview(object? s, PaintEventArgs e)
    {
      var g = e.Graphics;
      g.InterpolationMode = _playing ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
      var fr = FrameRect();
      if (_playing && !_scrubbing && _player != null) _player.DrawCover(g, Rectangle.Round(fr));
      else if (_frame != null) g.DrawImage(_frame, fr);
      else { using var bb = new SolidBrush(Color.Black); g.FillRectangle(bb, fr); }
      using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawRectangle(pen, fr.X, fr.Y, fr.Width, fr.Height);
      for (int li = Items.Count - 1; li >= 0; li--)
      {
        var it = Items[li];
        bool visible = _playhead >= it.AppearAt - 1e-6 &&
                       (it.AppearDur <= 0.01 || _playhead <= it.AppearAt + it.AppearDur + 1e-6);
        bool sel = ReferenceEquals(it, _selected);
        if (!visible && !(sel && !_playing)) continue;
        var mr = MediaRect(it, fr);
        bool animP = _animPreview && sel;
        string anim = (it.Anim ?? "none").ToLowerInvariant();
        double lt = _playing ? (_playhead - it.AppearAt) : (animP ? _animPrevT : double.NaN);
        double wdA = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
        var draw = mr;
        var th = GetFrameFor(it);
        float alpha = (visible || animP) ? 1f : 0.30f;
        RectangleF dst = draw;
        if (th != null)
        {
          Rectangle src;
          if (it.Contain)
            (src, dst) = ContainSrcDst(th, draw, it.CropFL, it.CropFT, it.CropFR, it.CropFB);
          else
          {
            var (ePX, ePY) = EffectivePan(it, animP);
            double apx = ePX, apy = ePY, az = 1.0;
            if (!double.IsNaN(lt) && anim != "none" && lt >= -1e-6 && lt <= wdA + 0.01)
            {
              var (tpx, tpy, tz) = AnimTransform(anim, lt, wdA);
              apx = tpx; apy = tpy; az = tz;
            }
            src = CoverSrc(th, draw, apx, apy, it.CropFL, it.CropFT, it.CropFR, it.CropFB, az);
          }
          if (alpha >= 0.999f)
            g.DrawImage(th, Rectangle.Round(dst), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
          else
          {
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
            ia.SetColorMatrix(cm);
            g.DrawImage(th, Rectangle.Round(dst), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
          }
        }
        else { using var bb = new SolidBrush(Color.FromArgb((int)(alpha * 120), 120, 120, 120)); g.FillRectangle(bb, draw); }
        if (it.Path != null && IsVideoDecoding(it.Path))
        {
          using var df = new Font("Segoe UI", 8.5f, FontStyle.Bold);
          const string dmsg = "⏳ décodage vidéo…";
          var dsz = g.MeasureString(dmsg, df);
          float dxc = draw.X + (draw.Width - dsz.Width) / 2f, dyc = draw.Y + (draw.Height - dsz.Height) / 2f;
          using (var db = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            g.FillRectangle(db, dxc - 6, dyc - 3, dsz.Width + 12, dsz.Height + 6);
          using var dw2 = new SolidBrush(Color.White);
          g.DrawString(dmsg, df, dw2, dxc, dyc);
        }
        if (_seq.MediaBorderPx > 0 && it.Border)
        {
          float bw = Math.Max(1f, _seq.MediaBorderPx * fr.Width / _canvasW);
          using var bpen = new Pen(ParseColor(_seq.MediaBorderColor, Color.White), bw);
          var brd = dst; brd.Inflate(bw / 2f, bw / 2f);
          g.DrawRectangle(bpen, brd.X, brd.Y, brd.Width, brd.Height);
        }
        if (sel)
        {
          using var bp = new Pen(Color.Gold, 2.5f);
          if (!visible) bp.DashStyle = DashStyle.Dash;
          g.DrawRectangle(bp, draw.X, draw.Y, draw.Width, draw.Height);
        }
        else if (_multiSel.Contains(it) && !_playing)
        {
          using var mp = new Pen(Color.Gold, 1.5f) { DashStyle = DashStyle.Dot };
          g.DrawRectangle(mp, draw.X, draw.Y, draw.Width, draw.Height);
        }
        if (sel && !_playing)
        {
          if (_cropMode)
          {
            using (var cp = new Pen(Color.FromArgb(0, 229, 255), 2f) { DashStyle = DashStyle.Dash })
              g.DrawRectangle(cp, mr.X, mr.Y, mr.Width, mr.Height);
            using var cb = new SolidBrush(Color.FromArgb(0, 229, 255));
            foreach (var hr in CropHandleRects(mr)) g.FillRectangle(cb, hr);
            if (_cropSnapped)
            {
              using var sp2 = new Pen(Color.Gold, 3f);
              switch (_drag)
              {
                case Drag.CropL: g.DrawLine(sp2, mr.Left, mr.Top, mr.Left, mr.Bottom); break;
                case Drag.CropR: g.DrawLine(sp2, mr.Right, mr.Top, mr.Right, mr.Bottom); break;
                case Drag.CropT: g.DrawLine(sp2, mr.Left, mr.Top, mr.Right, mr.Top); break;
                case Drag.CropB: g.DrawLine(sp2, mr.Left, mr.Bottom, mr.Right, mr.Bottom); break;
              }
            }
          }
          else
          {
            using var hb = new SolidBrush(Color.Gold);
            g.FillRectangle(hb, mr.Right - 7, mr.Bottom - 7, 12, 12);
          }
        }
      }
      if (CamEditable)
      {
        var cr = CamRect(fr);
        if (_hasCam)
        {
          using var gp = new GraphicsPath();
          gp.AddEllipse(cr);
          var oldClip = g.Clip;
          g.SetClip(gp);
          if (_playing && !_scrubbing && _camPlayer != null)
            _camPlayer.DrawCover(g, Rectangle.Round(cr));
          else if (_camThumb != null)
          {
            var src = CoverSrc(_camThumb, cr);
            g.DrawImage(_camThumb, Rectangle.Round(cr), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
          }
          else { using var bb = new SolidBrush(Color.FromArgb(70, 120, 120, 120)); g.FillEllipse(bb, cr); }
          g.Clip = oldClip;
        }
        using (var cp = new Pen(_camSelected ? Color.Gold : Color.White, _camSelected ? 3f : 2f))
        {
          if (!_hasCam) cp.DashStyle = DashStyle.Dash;
          g.DrawEllipse(cp, cr);
        }
        using (var lf = new Font("Segoe UI", 8f, FontStyle.Bold))
        using (var lb = new SolidBrush(Color.White))
          g.DrawString(_hasCam ? "CAM" : "CAM (aligne)", lf, lb, cr.X + 6, cr.Y + 6);
        if (_camSelected && !_playing) { using var hb = new SolidBrush(Color.Gold); g.FillRectangle(hb, cr.Right - 9, cr.Bottom - 9, 15, 15); }
      }
      if (!float.IsNaN(_snapGuideVx) || !float.IsNaN(_snapGuideVy))
      {
        using var gpen = new Pen(Color.FromArgb(0, 229, 255), 1.6f) { DashStyle = DashStyle.Dash };
        if (!float.IsNaN(_snapGuideVx)) g.DrawLine(gpen, _snapGuideVx, fr.Top, _snapGuideVx, fr.Bottom);
        if (!float.IsNaN(_snapGuideVy)) g.DrawLine(gpen, fr.Left, _snapGuideVy, fr.Right, _snapGuideVy);
      }
    }
    private void PreviewMouseDown(object? s, MouseEventArgs e)
    {
      var fr = FrameRect();
      _dragEngaged = false;
      if (_cropMode && _selected != null && e.Button == MouseButtons.Left)
      {
        var mr = MediaRect(_selected, fr);
        var hs = CropHandleRects(mr);
        Drag pick = hs[0].Contains(e.Location) ? Drag.CropL
                  : hs[1].Contains(e.Location) ? Drag.CropT
                  : hs[2].Contains(e.Location) ? Drag.CropR
                  : hs[3].Contains(e.Location) ? Drag.CropB : Drag.None;
        if (pick != Drag.None)
        {
          _drag = pick; _dragStart = e.Location;
          _dragOrigCropFL = _selected.CropFL; _dragOrigCropFT = _selected.CropFT;
          _dragOrigCropFR = _selected.CropFR; _dragOrigCropFB = _selected.CropFB;
          return;
        }
      }
      if (e.Button == MouseButtons.Right)
      {
        if (_selected != null && MediaRect(_selected, fr).Contains(e.Location))
        {
          _drag = Drag.Pan; _dragStart = e.Location;
          (_dragOrigPanX, _dragOrigPanY) = GetPan(_selected);
          return;
        }
        for (int i = 0; i < Items.Count; i++)
          if (MediaRect(Items[i], fr).Contains(e.Location))
          {
            Select(Items[i]);
            _drag = Drag.Pan; _dragStart = e.Location;
            (_dragOrigPanX, _dragOrigPanY) = GetPan(Items[i]);
            return;
          }
        return;
      }
      if ((ModifierKeys & Keys.Control) == Keys.Control)
      {
        if (_selected != null && MediaRect(_selected, fr).Contains(e.Location)) { ToggleSelect(_selected); return; }
        for (int i = 0; i < Items.Count; i++)
          if (MediaRect(Items[i], fr).Contains(e.Location)) { ToggleSelect(Items[i]); return; }
        return;
      }
      if (CamEditable)
      {
        var cr = CamRect(fr);
        var handle = new RectangleF(cr.Right - 10, cr.Bottom - 10, 20, 20);
        if (handle.Contains(e.Location)) { SelectCam(); _drag = Drag.CamResize; _dragStart = e.Location; _dragOrigCamDiam = _seq.CamDiam; return; }
        float ccx = cr.X + cr.Width / 2f, ccy = cr.Y + cr.Height / 2f;
        if (Math.Sqrt((e.X - ccx) * (e.X - ccx) + (e.Y - ccy) * (e.Y - ccy)) <= cr.Width / 2f)
        { SelectCam(); _drag = Drag.CamMove; _dragStart = e.Location; _dragOrigCamX = _seq.CamX; _dragOrigCamY = _seq.CamY; return; }
      }
      if (_selected != null)
      {
        var smr = MediaRect(_selected, fr);
        var sHandle = new RectangleF(smr.Right - 8, smr.Bottom - 8, 16, 16);
        if (sHandle.Contains(e.Location))
        { _drag = Drag.Resize; _dragStart = e.Location; _dragOrigScale = _selected.Scale; _dragOrigSpeed = _selected.Speed; CaptureGroupDragOrigins(); return; }
        if (smr.Contains(e.Location))
        { _drag = Drag.Move; _dragStart = e.Location; _dragOrigX = _selected.PosX; _dragOrigY = _selected.PosY; CaptureGroupDragOrigins(); return; }
      }
      for (int i = 0; i < Items.Count; i++)
      {
        var it = Items[i];
        var mr = MediaRect(it, fr);
        var handle = new RectangleF(mr.Right - 8, mr.Bottom - 8, 16, 16);
        if (handle.Contains(e.Location)) { PromoteInGroup(it); _drag = Drag.Resize; _dragStart = e.Location; _dragOrigScale = it.Scale; _dragOrigSpeed = it.Speed; CaptureGroupDragOrigins(); return; }
        if (mr.Contains(e.Location)) { PromoteInGroup(it); _drag = Drag.Move; _dragStart = e.Location; _dragOrigX = it.PosX; _dragOrigY = it.PosY; CaptureGroupDragOrigins(); return; }
      }
      Select(null);
    }
    private void PreviewMouseMove(object? s, MouseEventArgs e)
    {
      if (_drag == Drag.None) { _preview.Cursor = PreviewCursorAt(e.Location); return; }
      var fr = FrameRect();
      if (_drag == Drag.CamMove)
      {
        double dx = (e.X - _dragStart.X) / fr.Width, dy = (e.Y - _dragStart.Y) / fr.Height;
        _seq.CamX = Math.Clamp(_dragOrigCamX + dx, 0, 1);
        _seq.CamY = Math.Clamp(_dragOrigCamY + dy, 0, 1);
        Touch(); return;
      }
      if (_drag == Drag.CamResize)
      {
        double dd = (e.X - _dragStart.X) / fr.Width;
        _seq.CamDiam = Math.Clamp(_dragOrigCamDiam + dd * 2, 0.10, 1.0);
        Touch(); return;
      }
      if (_drag == Drag.Pan)
      {
        if (_selected == null) return;
        double dx = -(e.X - _dragStart.X) / (fr.Width * 0.5);
        double dy = -(e.Y - _dragStart.Y) / (fr.Height * 0.5);
        SetPan(_selected, _dragOrigPanX + dx, _dragOrigPanY + dy);
        Touch(); return;
      }
      if (_selected == null) return;
      if (_drag is Drag.CropL or Drag.CropT or Drag.CropR or Drag.CropB)
      {
        var mr = MediaRect(_selected, fr);
        double dxF = (e.X - _dragStart.X) / Math.Max(1f, mr.Width);
        double dyF = (e.Y - _dragStart.Y) / Math.Max(1f, mr.Height);
        _cropSnapped = false;
        switch (_drag)
        {
          case Drag.CropL: _selected.CropFL = SnapCrop(_selected, _dragOrigCropFL + dxF, 0.9 - _selected.CropFR, axisX: true,  fromStart: true,  mr); break;
          case Drag.CropR: _selected.CropFR = SnapCrop(_selected, _dragOrigCropFR - dxF, 0.9 - _selected.CropFL, axisX: true,  fromStart: false, mr); break;
          case Drag.CropT: _selected.CropFT = SnapCrop(_selected, _dragOrigCropFT + dyF, 0.9 - _selected.CropFB, axisX: false, fromStart: true,  mr); break;
          case Drag.CropB: _selected.CropFB = SnapCrop(_selected, _dragOrigCropFB - dyF, 0.9 - _selected.CropFT, axisX: false, fromStart: false, mr); break;
        }
        Touch(); return;
      }
      if (_drag == Drag.Move)
      {
        if (!_dragEngaged)
        {
          if (Math.Abs(e.X - _dragStart.X) <= 3 && Math.Abs(e.Y - _dragStart.Y) <= 3) return;
          _dragEngaged = true;
        }
        double dx = (e.X - _dragStart.X) / fr.Width;
        double dy = (e.Y - _dragStart.Y) / fr.Height;
        var (nx, ny) = SnapPos(_dragOrigX + dx, _dragOrigY + dy, _selected, fr);
        nx = Math.Clamp(nx, 0, 1); ny = Math.Clamp(ny, 0, 1);
        _selected.PosX = nx;
        _selected.PosY = ny;
        double gdx = nx - _dragOrigX, gdy = ny - _dragOrigY;
        foreach (var kv in _dragOrigPos)
        {
          kv.Key.PosX = Math.Clamp(kv.Value.x + gdx, 0, 1);
          kv.Key.PosY = Math.Clamp(kv.Value.y + gdy, 0, 1);
        }
        Touch();
      }
      else if (_drag == Drag.Resize)
      {
        double dw = (e.X - _dragStart.X) / fr.Width;
        double newScale = Math.Clamp(_dragOrigScale + dw * 2, 0.05, 1.0);
        _selected.Scale = newScale;
        double dsc = newScale - _dragOrigScale;
        foreach (var kv in _dragOrigScales)
          kv.Key.Scale = Math.Clamp(kv.Value + dsc, 0.05, 1.0);
        if (_linkSpeedCheck.Checked && IsAnimatedMedia(_selected) && newScale > 0.001)
        {
          _selected.Speed = (float)Math.Clamp(_dragOrigSpeed * _dragOrigScale / newScale, 0.10, 4.00);
          _suppressMediaOpts = true; _speedBox.Value = (decimal)_selected.Speed; _suppressMediaOpts = false;
          FitWindowToAnim(_selected, allowSpeedUp: false);
        }
        Touch();
      }
    }
    private Cursor PreviewCursorAt(Point p)
    {
      var fr = FrameRect();
      if (_cropMode && _selected != null)
      {
        var hs = CropHandleRects(MediaRect(_selected, fr));
        if (hs[0].Contains(p) || hs[2].Contains(p)) return Cursors.SizeWE;
        if (hs[1].Contains(p) || hs[3].Contains(p)) return Cursors.SizeNS;
      }
      if (CamEditable)
      {
        var cr = CamRect(fr);
        if (new RectangleF(cr.Right - 10, cr.Bottom - 10, 20, 20).Contains(p)) return Cursors.SizeNWSE;
        float ccx = cr.X + cr.Width / 2f, ccy = cr.Y + cr.Height / 2f;
        if (Math.Sqrt((p.X - ccx) * (p.X - ccx) + (p.Y - ccy) * (p.Y - ccy)) <= cr.Width / 2f) return Cursors.SizeAll;
      }
      if (_selected != null)
      {
        var smr = MediaRect(_selected, fr);
        if (new RectangleF(smr.Right - 8, smr.Bottom - 8, 16, 16).Contains(p)) return Cursors.SizeNWSE;
        if (smr.Contains(p)) return Cursors.SizeAll;
      }
      for (int i = 0; i < Items.Count; i++)
      {
        var mr = MediaRect(Items[i], fr);
        if (new RectangleF(mr.Right - 8, mr.Bottom - 8, 16, 16).Contains(p)) return Cursors.SizeNWSE;
        if (mr.Contains(p)) return Cursors.SizeAll;
      }
      return Cursors.Default;
    }
    private const int TrackPadL = 12, TrackPadR = 12, RulerH = 26, RowH = 30, RowTop = 34;
    private float TimeToX(double t)
    {
      float w = _track.ClientSize.Width - TrackPadL - TrackPadR;
      return TrackPadL + (float)(t / _clipDur) * w;
    }
    private double XToTime(float x)
    {
      float w = _track.ClientSize.Width - TrackPadL - TrackPadR;
      return Math.Clamp((x - TrackPadL) / w, 0, 1) * _clipDur;
    }
    private RectangleF BlockRect(MediaItem it, int row)
    {
      double dur = it.AppearDur > 0.01 ? it.AppearDur : (_clipDur - it.AppearAt);
      double end = Math.Min(it.AppearAt + dur, _clipDur);
      float x1 = TimeToX(it.AppearAt), x2 = TimeToX(end);
      float y = RowTop + (row - _rowScroll) * RowH;
      return new RectangleF(x1, y, Math.Max(6, x2 - x1), RowH - 8);
    }
    private int VisibleRows() => Math.Max(1, (_trackHost.Height - RowTop - TrackPadB) / RowH);
    private void UpdateTrackLayout()
    {
      if (_trackHost == null || _trackScroll == null) return;
      int rows = Math.Max(1, Items.Count);
      int desired = RowTop + rows * RowH + TrackPadB;
      int maxH = Math.Max(190, (int)(ClientSize.Height * 0.40));
      int h = Math.Clamp(desired, 190, maxH);
      if (_trackHost.Height != h) _trackHost.Height = h;
      int overflow = Math.Max(0, rows - VisibleRows());
      _rowScroll = Math.Min(_rowScroll, overflow);
      _trackScroll.Visible = overflow > 0;
      if (overflow > 0)
      {
        _trackScroll.Maximum = overflow;
        if (_trackScroll.Value != _rowScroll) _trackScroll.Value = _rowScroll;
      }
    }
    private void SetRowScroll(int v)
    {
      int overflow = Math.Max(0, Math.Max(1, Items.Count) - VisibleRows());
      v = Math.Clamp(v, 0, overflow);
      if (v == _rowScroll) return;
      _rowScroll = v;
      if (_trackScroll.Visible) { try { _trackScroll.Value = v; } catch { } }
      _track.Invalidate();
    }
    private void EnsureRowVisible(MediaItem it)
    {
      int i = Items.IndexOf(it);
      if (i < 0) return;
      if (i < _rowScroll) SetRowScroll(i);
      else if (i >= _rowScroll + VisibleRows()) SetRowScroll(i - VisibleRows() + 1);
    }
    private void PaintTrack(object? s, PaintEventArgs e)
    {
      var g = e.Graphics;
      var cr = _track.ClientRectangle;
      float wallX = TimeToX(_clipDur);
      if (wallX < cr.Width)
      {
        using var forbid = new System.Drawing.Drawing2D.HatchBrush(
          System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal,
          Color.FromArgb(70, 200, 70, 70), Color.FromArgb(28, 24, 24));
        g.FillRectangle(forbid, wallX, 0, cr.Width - wallX, cr.Height);
      }
      using (var rb = new SolidBrush(Color.FromArgb(34, 34, 42))) g.FillRectangle(rb, 0, 0, wallX, RulerH);
      using (var tp = new Pen(Color.FromArgb(80, 80, 90)))
      using (var tf = new Font("Segoe UI", 7.5f))
      using (var tb = new SolidBrush(Color.Gray))
      {
        double step = _clipDur <= 6 ? 1 : (_clipDur <= 20 ? 2 : 5);
        for (double t = 0; t <= _clipDur + 1e-6; t += step)
        {
          float x = TimeToX(t);
          g.DrawLine(tp, x, 0, x, RulerH);
          g.DrawString(t.ToString("0.#", Inv) + "s", tf, tb, x + 2, 8);
        }
      }
      var rowClip = g.Clip;
      g.SetClip(new Rectangle(0, RulerH, cr.Width, Math.Max(0, cr.Height - RulerH)));
      var colors = new[] { Color.FromArgb(80, 150, 255), Color.FromArgb(90, 200, 130), Color.FromArgb(230, 160, 60), Color.FromArgb(200, 110, 200), Color.FromArgb(220, 90, 90), Color.FromArgb(120, 200, 200) };
      for (int i = 0; i < Items.Count; i++)
      {
        var it = Items[i];
        var br = BlockRect(it, i);
        if (br.Bottom < RulerH || br.Top > cr.Height) continue;
        var col = colors[i % colors.Length];
        bool prim = ReferenceEquals(it, _selected);
        bool inSel = !prim && _multiSel.Contains(it);
        using (var bb = new SolidBrush(Color.FromArgb(prim || inSel ? 235 : 180, col))) g.FillRectangle(bb, br);
        using (var bp = new Pen(prim || inSel ? Color.Gold : Color.FromArgb(30, 30, 30), prim ? 2f : 1.5f))
        {
          if (inSel) bp.DashStyle = DashStyle.Dot;
          g.DrawRectangle(bp, br.X, br.Y, br.Width, br.Height);
        }
        var name = (it.PanAnim ? "🎬 " : "") + Path.GetFileName(it.Path ?? "média");
        bool cut = IsTruncated(it);
        if (cut) { using var wpn = new Pen(Color.FromArgb(200, 40, 40), 2f) { DashStyle = DashStyle.Dash }; g.DrawRectangle(wpn, br.X, br.Y, br.Width, br.Height); }
        using var nf = new Font("Segoe UI", 7.5f, cut ? FontStyle.Bold : FontStyle.Regular);
        using var nb = new SolidBrush(cut ? Color.FromArgb(120, 0, 0) : Color.Black);
        var clip = g.Clip; g.SetClip(RectangleF.Intersect(br, new RectangleF(0, RulerH, cr.Width, cr.Height - RulerH)));
        g.DrawString(cut ? "⚠ coupé · " + name : name, nf, nb, br.X + 4, br.Y + 4);
        g.Clip = clip;
        if (ReferenceEquals(it, _selected) && it.PanAnim)
        {
          var (pt1, pt2) = PanMarkerTimes(it);
          float x1 = TimeToX(pt1), x2 = TimeToX(pt2);
          using var band = new SolidBrush(Color.FromArgb(110, 255, 215, 0));
          g.FillRectangle(band, Math.Min(x1, x2), br.Bottom - 5, Math.Max(2, Math.Abs(x2 - x1)), 4);
          using var mpen = new Pen(Color.Gold, 2f);
          g.DrawLine(mpen, x1, br.Top - 2, x1, br.Bottom + 2);
          g.DrawLine(mpen, x2, br.Top - 2, x2, br.Bottom + 2);
          using var mf2 = new Font("Segoe UI", 7f, FontStyle.Bold);
          using var mgb = new SolidBrush(Color.Gold);
          g.DrawString("A", mf2, mgb, x1 - 11, br.Top - 2);
          g.DrawString("B", mf2, mgb, x2 + 2, br.Top - 2);
        }
      }
      g.Clip = rowClip;
      using (var wp = new Pen(Color.FromArgb(235, 90, 90), 2f)) g.DrawLine(wp, wallX, 0, wallX, cr.Height);
      using (var wf = new Font("Segoe UI", 7.5f, FontStyle.Bold))
      using (var wb = new SolidBrush(Color.FromArgb(235, 120, 120)))
      {
        var lbl = "fin " + _clipDur.ToString("0.#", Inv) + "s";
        var sz = g.MeasureString(lbl, wf);
        float lx = Math.Min(wallX + 3, cr.Width - sz.Width - 2);
        g.DrawString(lbl, wf, wb, lx, RulerH + 2);
      }
      if (!double.IsNaN(_snapGuideT))
      {
        float gx = TimeToX(_snapGuideT);
        using var gpen = new Pen(Color.FromArgb(0, 229, 255), 1.6f) { DashStyle = DashStyle.Dash };
        g.DrawLine(gpen, gx, 0, gx, cr.Height);
      }
      float px = TimeToX(_playhead);
      using (var pp = new Pen(Color.OrangeRed, 2f)) g.DrawLine(pp, px, 0, px, cr.Height);
      using (var ph = new SolidBrush(Color.OrangeRed)) g.FillPolygon(ph, new[] { new PointF(px - 6, 0), new PointF(px + 6, 0), new PointF(px, 10) });
      if (_drag == Drag.Marquee && _marqueeActive)
      {
        var mr2 = MarqueeRect();
        using var mf = new SolidBrush(Color.FromArgb(36, 0, 229, 255));
        using var mp = new Pen(Color.FromArgb(0, 229, 255), 1.2f) { DashStyle = DashStyle.Dash };
        g.FillRectangle(mf, mr2);
        g.DrawRectangle(mp, mr2.X, mr2.Y, mr2.Width, mr2.Height);
      }
    }
    private void TrackMouseDown(object? s, MouseEventArgs e)
    {
      if (e.Button == MouseButtons.Right) { ShowTrackContextMenu(e.Location); return; }
      if (e.Button != MouseButtons.Left) return;
      _dragEngaged = false;
      if (Math.Abs(e.X - TimeToX(_playhead)) <= 7f)
      {
        _drag = Drag.Playhead;
        if (_playing && !_scrubbing) { _scrubbing = true; _playTimer.Stop(); try { _wave?.Pause(); } catch { } }
        SetPlayhead(SnapPlayhead(XToTime(e.X)), reextract: !_playing);
        return;
      }
      if (_selected != null && _selected.PanAnim)
      {
        int si = Items.IndexOf(_selected);
        if (si >= 0)
        {
          var sbr = BlockRect(_selected, si);
          if (e.Y >= sbr.Top - 5 && e.Y <= sbr.Bottom + 5 && sbr.Bottom >= RulerH)
          {
            var (pt1, pt2) = PanMarkerTimes(_selected);
            if (Math.Abs(e.X - TimeToX(pt2)) <= 6) { _drag = Drag.PanM2; _dragStart = e.Location; return; }
            if (Math.Abs(e.X - TimeToX(pt1)) <= 6) { _drag = Drag.PanM1; _dragStart = e.Location; return; }
          }
        }
      }
      for (int i = Items.Count - 1; i >= 0 && e.Y > RulerH; i--)
      {
        var it = Items[i];
        var br = BlockRect(it, i);
        if (br.Bottom < RulerH) continue;
        if (br.Contains(e.Location))
        {
          if ((ModifierKeys & Keys.Control) == Keys.Control) { ToggleSelect(it); return; }
          PromoteInGroup(it);
          CaptureGroupDragOrigins();
          _dragStart = e.Location; _dragOrigA = it.AppearAt;
          _dragOrigDur = it.AppearDur > 0.01 ? it.AppearDur : (_clipDur - it.AppearAt);
          _dragOrigSpeed = ClampSpeed(it.Speed);
          _dragOrigTrimIn = it.TrimIn ?? 0f;
          _dragOrigTrimOut = it.TrimOut is > 0 ? it.TrimOut.Value : SourceDur(it);
          bool alt = (ModifierKeys & Keys.Alt) == Keys.Alt && IsAnimatedMedia(it);
          if (e.X <= br.X + 6) _drag = alt ? Drag.TrimLeft : Drag.BlockLeft;
          else if (e.X >= br.Right - 6) _drag = alt ? Drag.TrimRight : Drag.BlockRight;
          else _drag = Drag.BlockMove;
          return;
        }
      }
      if (!_playing && e.Y > RulerH)
      {
        _drag = Drag.Marquee;
        _dragStart = e.Location;
        _marqueeCur = e.Location;
        _marqueeActive = false;
        return;
      }
      _drag = Drag.Playhead;
      if (_playing && !_scrubbing) { _scrubbing = true; _playTimer.Stop(); try { _wave?.Pause(); } catch { } }
      SetPlayhead(SnapPlayhead(XToTime(e.X)), reextract: !_playing);
    }
    private double SnapPlayhead(double t)
    {
      _snapGuideT = double.NaN;
      if (!SnapActive()) return t;
      double best = t, target = double.NaN;
      float bestDx = SnapPx + 0.5f, x = TimeToX(t);
      void Consider(double tg)
      {
        if (tg < -1e-6 || tg > _clipDur + 1e-6) return;
        float dx = Math.Abs(TimeToX(tg) - x);
        if (dx <= SnapPx && dx < bestDx) { bestDx = dx; best = tg; target = tg; }
      }
      Consider(0);
      Consider(_clipDur);
      foreach (var it in Items) { Consider(it.AppearAt); Consider(EffEnd(it)); }
      Consider(Math.Clamp(Math.Round(t / GridStep()) * GridStep(), 0, _clipDur));
      _snapGuideT = target;
      return best;
    }
    private bool CanSplitAtPlayhead(MediaItem it)
      => _playhead > it.AppearAt + 0.05 && _playhead < it.AppearAt + WindowDur(it) - 0.05;
    private void SplitAtPlayhead(bool keepBefore)
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord un média à couper."; return; }
      var it = _selected;
      double a = it.AppearAt;
      double end = a + WindowDur(it);
      double p = _playhead;
      if (!CanSplitAtPlayhead(it))
      { _info.Text = "Place la tête de lecture À L'INTÉRIEUR du bloc, puis coupe."; return; }
      bool anim = IsAnimatedMedia(it);
      double s = ClampSpeed(it.Speed);
      double ti = it.TrimIn ?? 0f;
      double srcAtP = ti + (p - a) * s;
      if (keepBefore)
      {
        if (anim)
        {
          double sd = SourceDur(it);
          double tout = Math.Clamp(srcAtP, ti + 0.1, sd);
          it.TrimOut = tout < sd - 0.001 ? (float?)(float)tout : null;
        }
        it.AppearDur = Math.Round(p - a, 2);
      }
      else
      {
        if (anim)
        {
          double sd = SourceDur(it);
          double tin = Math.Clamp(srcAtP, 0, sd - 0.1);
          it.TrimIn = tin > 0.001 ? (float?)(float)tin : null;
        }
        it.AppearAt = Math.Round(p, 2);
        it.AppearDur = Math.Round(end - p, 2);
      }
      SyncMediaOpts(it);
      Commit(keepBefore ? "Couper après la tête" : "Couper avant la tête");
    }
    private void ShowTrackContextMenu(Point at)
    {
      for (int i = Items.Count - 1; i >= 0 && at.Y > RulerH; i--)
      {
        var br = BlockRect(Items[i], i);
        if (br.Bottom < RulerH || !br.Contains(at)) continue;
        if (!IsSel(Items[i])) Select(Items[i]);
        break;
      }
      int nSel = SelList().Count;
      var menu = new ContextMenuStrip();
      var copy  = menu.Items.Add($"⎘ Copier ({nSel})", null, (_, _) => CopySelection());
      copy.Enabled = nSel > 0;
      var paste = menu.Items.Add($"📋 Coller ({s_layerClipboard.Count})", null, (_, _) => PasteClipboard());
      paste.Enabled = s_layerClipboard.Count > 0;
      var dup   = menu.Items.Add("⧉ Dupliquer", null, (_, _) => DuplicateSelected());
      dup.Enabled = nSel > 0;
      var del   = menu.Items.Add("Supprimer", null, (_, _) => DeleteSelected());
      del.Enabled = nSel > 0;
      menu.Items.Add(new ToolStripSeparator());
      bool canSplit = _selected != null && nSel == 1 && CanSplitAtPlayhead(_selected);
      var cutBefore = menu.Items.Add("✂ Couper AVANT la tête (garder la suite)", null, (_, _) => SplitAtPlayhead(keepBefore: false));
      cutBefore.Enabled = canSplit;
      var cutAfter = menu.Items.Add("✂ Couper APRÈS la tête (garder le début)", null, (_, _) => SplitAtPlayhead(keepBefore: true));
      cutAfter.Enabled = canSplit;
      menu.Items.Add(new ToolStripSeparator());
      var cpp = menu.Items.Add("🎛 Copier les paramètres", null, (_, _) => CopyParams(null));
      cpp.Enabled = _selected != null;
      var cps = menu.Items.Add("🎛 Copier les paramètres…", null, (_, _) => CopyParamsWithPicker());
      cps.Enabled = _selected != null;
      var pps = menu.Items.Add(
        s_paramClipboard != null ? $"🎛 Coller les paramètres ({s_paramGroups.Count} groupe(s))" : "🎛 Coller les paramètres",
        null, (_, _) => PasteParams());
      pps.Enabled = s_paramClipboard != null && nSel > 0;
      var targets = _allSeqs?.Where(q => !ReferenceEquals(q, _seq)).ToList();
      if (targets != null && targets.Count > 0)
      {
        menu.Items.Add(new ToolStripSeparator());
        var copyTo = new ToolStripMenuItem("⎘ Copier vers")     { Enabled = nSel > 0 };
        var moveTo = new ToolStripMenuItem("➜ Déplacer vers") { Enabled = nSel > 0 };
        foreach (var t in targets)
        {
          var tgt = t;
          copyTo.DropDownItems.Add(SeqLabel(tgt), null, (_, _) => TransferSelection(tgt, move: false));
          moveTo.DropDownItems.Add(SeqLabel(tgt), null, (_, _) => TransferSelection(tgt, move: true));
        }
        menu.Items.Add(copyTo);
        menu.Items.Add(moveTo);
      }
      menu.Show(_track, at);
    }
    private static string SeqLabel(Sequence s)
      => !string.IsNullOrWhiteSpace(s.Note) ? s.Note! : (s.Id ?? "séquence");
    private void CopyParams(HashSet<string>? groups)
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord un calque."; return; }
      s_paramClipboard = _selected.Clone();
      s_paramGroups.Clear();
      foreach (var g in groups ?? ParamGroupDefs.Select(d => d.key).Where(k => k != "window").ToHashSet())
        s_paramGroups.Add(g);
      _info.Text = $"Paramètres copiés ({s_paramGroups.Count} groupe(s)) — clic droit sur un calque (toute slide) → « Coller les paramètres ».";
    }
    private void CopyParamsWithPicker()
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord un calque."; return; }
      using var dlg = new Form
      {
        Text = "Paramètres à copier", FormBorderStyle = FormBorderStyle.FixedDialog,
        StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(250, 30 * ParamGroupDefs.Length + 58),
        MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
        BackColor = Color.FromArgb(24, 24, 30), ForeColor = Color.Gainsboro,
      };
      var boxes = new List<CheckBox>();
      int y = 12;
      foreach (var (key, label) in ParamGroupDefs)
      {
        var cb = new CheckBox
        {
          Text = label, Tag = key, AutoSize = true, Location = new Point(16, y), ForeColor = Color.Gainsboro,
          Checked = s_paramGroups.Count == 0 ? key != "window" : s_paramGroups.Contains(key),
        };
        boxes.Add(cb); dlg.Controls.Add(cb); y += 30;
      }
      var ok = new Button { Text = "Copier", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 110, 60), ForeColor = Color.White, Bounds = new Rectangle(138, y + 8, 96, 28) };
      var ko = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(55, 55, 62), ForeColor = Color.White, Bounds = new Rectangle(30, y + 8, 96, 28) };
      dlg.Controls.Add(ok); dlg.Controls.Add(ko);
      dlg.AcceptButton = ok; dlg.CancelButton = ko;
      if (dlg.ShowDialog(this) != DialogResult.OK) return;
      var groups = boxes.Where(b => b.Checked).Select(b => (string)b.Tag!).ToHashSet();
      if (groups.Count == 0) { _info.Text = "Aucun paramètre coché : rien n'a été copié."; return; }
      CopyParams(groups);
    }
    private void PasteParams()
    {
      if (s_paramClipboard == null) { _info.Text = "Copie d'abord des paramètres (clic droit → « Copier les paramètres »)."; return; }
      var sel = SelList();
      if (sel.Count == 0) { _info.Text = "Sélectionne le(s) calque(s) qui doivent recevoir les paramètres."; return; }
      var src = s_paramClipboard;
      foreach (var d in sel)
      {
        if (s_paramGroups.Contains("pos"))   { d.PosX = src.PosX; d.PosY = src.PosY; }
        if (s_paramGroups.Contains("size"))  d.Scale = src.Scale;
        if (s_paramGroups.Contains("crop"))  { d.CropFL = src.CropFL; d.CropFT = src.CropFT; d.CropFR = src.CropFR; d.CropFB = src.CropFB; d.PanX = src.PanX; d.PanY = src.PanY; }
        if (s_paramGroups.Contains("anim"))  d.Anim = src.Anim;
        if (s_paramGroups.Contains("speed")) { d.Speed = src.Speed; d.Loop = src.Loop; if (IsAnimatedMedia(d)) FitWindowToAnim(d, allowSpeedUp: false); }
        if (s_paramGroups.Contains("style")) { d.Border = src.Border; d.Contain = src.Contain; }
        if (s_paramGroups.Contains("window"))
        {
          d.AppearAt = Math.Clamp(src.AppearAt, 0, Math.Max(0, _clipDur - 0.2));
          d.AppearDur = src.AppearDur > 0.01 ? Math.Clamp(src.AppearDur, 0.2, Math.Max(0.2, _clipDur - d.AppearAt)) : 0;
        }
      }
      SyncMediaOpts(_selected);
      Commit(sel.Count > 1 ? $"Coller les paramètres ({sel.Count} calques)" : "Coller les paramètres");
      _info.Text = $"Paramètres appliqués à {sel.Count} calque(s).";
    }
    private void TransferSelection(Sequence target, bool move)
    {
      var sel = SelList();
      if (sel.Count == 0) return;
      target.MediaItems ??= new List<MediaItem>();
      for (int i = sel.Count - 1; i >= 0; i--) target.MediaItems.Insert(0, sel[i].Clone());
      if (move)
      {
        foreach (var it in sel) Items.Remove(it);
        _multiSel.Clear();
        _selected = null;
        Commit(sel.Count > 1 ? $"Déplacer {sel.Count} calques vers une autre slide" : "Déplacer un calque vers une autre slide");
      }
      else
      {
        Changed = true;
        Touch();
      }
      _info.Text = $"{sel.Count} calque(s) {(move ? "déplacés" : "copiés")} vers « {SeqLabel(target)} ».";
    }
    private void TrackMouseMove(object? s, MouseEventArgs e)
    {
      if (_drag == Drag.None) { _track.Cursor = TrackCursorAt(e.Location); return; }
      if (_drag == Drag.Playhead) { SetPlayhead(SnapPlayhead(XToTime(e.X)), reextract: false); return; }
      if (_drag == Drag.Marquee)
      {
        _marqueeCur = e.Location;
        if (!_marqueeActive && (Math.Abs(e.X - _dragStart.X) > 4 || Math.Abs(e.Y - _dragStart.Y) > 4))
          _marqueeActive = true;
        if (_marqueeActive) ApplyMarquee(MarqueeRect());
        _track.Invalidate();
        return;
      }
      if (_selected == null) return;
      if (_drag is Drag.PanM1 or Drag.PanM2)
      {
        var it = _selected;
        double wd = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
        var (pt1, pt2) = PanMarkerTimes(it);
        double t = Math.Clamp(SnapPlayhead(XToTime(e.X)), it.AppearAt, it.AppearAt + wd);
        if (_drag == Drag.PanM1)
        {
          double nt1 = Math.Min(t, pt2 - 0.1);
          it.PanT1 = Math.Max(0, nt1 - it.AppearAt);
          it.PanDur = Math.Max(0.1, pt2 - nt1);
        }
        else
        {
          it.PanDur = Math.Clamp(t - pt1, 0.1, Math.Max(0.1, it.AppearAt + wd - pt1));
        }
        SyncMediaOpts(it);
        Touch();
        return;
      }
      if (_drag is Drag.BlockMove or Drag.BlockLeft or Drag.BlockRight && !_dragEngaged)
      {
        if (Math.Abs(e.X - _dragStart.X) <= 3 && Math.Abs(e.Y - _dragStart.Y) <= 3) return;
        _dragEngaged = true;
      }
      double dt = XToTime(e.X) - XToTime(_dragStart.X);
      if (_drag == Drag.BlockMove)
      {
        double blkDur = _selected.AppearDur > 0.01 ? _selected.AppearDur : (_clipDur - _dragOrigA);
        double na = Math.Clamp(_dragOrigA + dt, 0, Math.Max(0, _clipDur - blkDur));
        na = Math.Clamp(SnapMove(na, blkDur, _selected), 0, Math.Max(0, _clipDur - blkDur));
        _selected.AppearAt = na;
        double gdt = na - _dragOrigA;
        foreach (var kv in _dragOrigAt)
        {
          var o = kv.Key;
          double od = o.AppearDur > 0.01 ? o.AppearDur : Math.Max(0.1, _clipDur - kv.Value);
          o.AppearAt = Math.Clamp(kv.Value + gdt, 0, Math.Max(0, _clipDur - od));
        }
        if (_multiSel.Count == 0)
        {
          int cur = Items.IndexOf(_selected);
          int targetRow = Math.Clamp((int)Math.Floor((e.Y - RowTop) / (double)RowH) + _rowScroll, 0, Items.Count - 1);
          if (cur >= 0 && targetRow != cur)
          {
            Items.RemoveAt(cur);
            Items.Insert(targetRow, _selected);
          }
        }
        Touch();
      }
      else if (_drag == Drag.BlockLeft)
      {
        double na = Math.Clamp(_dragOrigA + dt, 0, _dragOrigA + _dragOrigDur - 0.2);
        na = Math.Clamp(SnapEdge(na, _selected), 0, _dragOrigA + _dragOrigDur - 0.2);
        _selected.AppearDur = (_dragOrigA + _dragOrigDur) - na;
        _selected.AppearAt = na;
        SpeedFromWindow(_selected);
        double dl = na - _dragOrigA;
        foreach (var kv in _dragOrigAt)
        {
          var o = kv.Key;
          double oEnd = kv.Value + _dragOrigDurs[o];
          double oNa = Math.Clamp(kv.Value + dl, 0, oEnd - 0.2);
          o.AppearAt = oNa;
          o.AppearDur = oEnd - oNa;
          SpeedFromWindow(o);
        }
        Touch();
      }
      else if (_drag == Drag.BlockRight)
      {
        double nd = Math.Clamp(_dragOrigDur + dt, 0.2, _clipDur - _selected.AppearAt);
        double snEnd = SnapEdge(_selected.AppearAt + nd, _selected);
        nd = Math.Clamp(snEnd - _selected.AppearAt, 0.2, _clipDur - _selected.AppearAt);
        _selected.AppearDur = nd;
        SpeedFromWindow(_selected);
        double ddur = nd - _dragOrigDur;
        foreach (var kv in _dragOrigDurs)
        {
          var o = kv.Key;
          o.AppearDur = Math.Clamp(kv.Value + ddur, 0.2, Math.Max(0.2, _clipDur - o.AppearAt));
          SpeedFromWindow(o);
        }
        Touch();
      }
      else if (_drag == Drag.TrimLeft)
      {
        double ntin = Math.Clamp(_dragOrigTrimIn + dt * _dragOrigSpeed, 0, _dragOrigTrimOut - 0.1);
        _selected.TrimIn = ntin > 0.001 ? (float?)(float)ntin : null;
        _selected.AppearAt = Math.Clamp(_dragOrigA + (ntin - _dragOrigTrimIn) / _dragOrigSpeed, 0, Math.Max(0, _clipDur - 0.2));
        FitWindowToAnim(_selected, allowSpeedUp: false);
        SyncMediaOpts(_selected);
        Touch();
      }
      else if (_drag == Drag.TrimRight)
      {
        double sd = SourceDur(_selected);
        double ntout = Math.Clamp(_dragOrigTrimOut + dt * _dragOrigSpeed, (_selected.TrimIn ?? 0f) + 0.1, sd);
        _selected.TrimOut = ntout < sd - 0.001 ? (float?)(float)ntout : null;
        FitWindowToAnim(_selected, allowSpeedUp: false);
        SyncMediaOpts(_selected);
        Touch();
      }
    }
    private void SpeedFromWindow(MediaItem it)
    {
      if (!IsAnimatedMedia(it)) return;
      it.Speed = (float)Math.Clamp(TrimmedDur(it) / WindowDur(it), 0.10, 4.00);
      _suppressMediaOpts = true;
      _speedBox.Value = (decimal)Math.Clamp(it.Speed, 0.10f, 4.00f);
      _suppressMediaOpts = false;
    }
    private double EffEnd(MediaItem it)
    {
      double dur = it.AppearDur > 0.01 ? it.AppearDur : (_clipDur - it.AppearAt);
      return Math.Min(it.AppearAt + dur, _clipDur);
    }
    private bool SnapActive() => (_snapCheck?.Checked ?? true) && (ModifierKeys & Keys.Control) != Keys.Control;
    private IEnumerable<double> SnapTargets(MediaItem? exclude)
    {
      yield return 0;
      yield return _clipDur;
      yield return _playhead;
      foreach (var it in Items)
      {
        if (ReferenceEquals(it, exclude) || _multiSel.Contains(it)) continue;
        yield return it.AppearAt;
        yield return EffEnd(it);
      }
    }
    private double GridStep() => _clipDur <= 6 ? 1 : (_clipDur <= 20 ? 2 : 5);
    private double SnapEdge(double t, MediaItem? exclude)
    {
      _snapGuideT = double.NaN;
      if (!SnapActive()) return t;
      double best = t, target = double.NaN;
      float bestDx = SnapPx + 0.5f, x = TimeToX(t);
      foreach (var tg in SnapTargets(exclude))
      {
        if (tg < -1e-6 || tg > _clipDur + 1e-6) continue;
        float dx = Math.Abs(TimeToX(tg) - x);
        if (dx <= SnapPx && dx < bestDx) { bestDx = dx; best = tg; target = tg; }
      }
      double gt = Math.Clamp(Math.Round(t / GridStep()) * GridStep(), 0, _clipDur);
      float dg = Math.Abs(TimeToX(gt) - x);
      if (dg <= SnapPx && dg < bestDx) { best = gt; target = gt; }
      _snapGuideT = target;
      return best;
    }
    private double SnapMove(double start, double dur, MediaItem? exclude)
    {
      _snapGuideT = double.NaN;
      if (!SnapActive()) return start;
      double end = start + dur, bestStart = start, target = double.NaN;
      float bestDx = SnapPx + 0.5f, sx = TimeToX(start), ex = TimeToX(end);
      void Consider(double tg)
      {
        if (tg < -1e-6 || tg > _clipDur + 1e-6) return;
        float tx = TimeToX(tg);
        float dxs = Math.Abs(tx - sx);
        if (dxs <= SnapPx && dxs < bestDx) { bestDx = dxs; bestStart = tg;       target = tg; }
        float dxe = Math.Abs(tx - ex);
        if (dxe <= SnapPx && dxe < bestDx) { bestDx = dxe; bestStart = tg - dur; target = tg; }
      }
      foreach (var tg in SnapTargets(exclude)) Consider(tg);
      Consider(Math.Clamp(Math.Round(start / GridStep()) * GridStep(), 0, _clipDur));
      Consider(Math.Clamp(Math.Round(end   / GridStep()) * GridStep(), 0, _clipDur));
      _snapGuideT = target;
      return bestStart;
    }
    private (double x, double y) SnapPos(double nx, double ny, MediaItem it, RectangleF fr)
    {
      _snapGuideVx = _snapGuideVy = float.NaN;
      if (!SnapActive()) return (nx, ny);
      double half = Math.Clamp(it.Scale, 0.05, 1.0) / 2.0;
      var xs = new List<double> { 0, 0.5, 1 };
      var ys = new List<double> { 0, 0.5, 1 };
      foreach (var o in Items)
      {
        if (ReferenceEquals(o, it) || _multiSel.Contains(o)) continue;
        double oh = Math.Clamp(o.Scale, 0.05, 1.0) / 2.0;
        xs.Add(o.PosX); xs.Add(o.PosX - oh); xs.Add(o.PosX + oh);
        ys.Add(o.PosY); ys.Add(o.PosY - oh); ys.Add(o.PosY + oh);
      }
      if (CamEditable) { xs.Add(_seq.CamX); ys.Add(_seq.CamY); }
      double bx = nx;
      float bestPx = SnapPx + 0.5f;
      foreach (var t in xs)
        foreach (var cand in new[] { t, t + half, t - half })
        {
          float d = (float)Math.Abs((cand - nx) * fr.Width);
          if (d <= SnapPx && d < bestPx) { bestPx = d; bx = cand; _snapGuideVx = fr.X + (float)t * fr.Width; }
        }
      double by = ny;
      bestPx = SnapPx + 0.5f;
      foreach (var t in ys)
        foreach (var cand in new[] { t, t + half, t - half })
        {
          float d = (float)Math.Abs((cand - ny) * fr.Height);
          if (d <= SnapPx && d < bestPx) { bestPx = d; by = cand; _snapGuideVy = fr.Y + (float)t * fr.Height; }
        }
      return (bx, by);
    }
    private double SnapCrop(MediaItem it, double raw, double max, bool axisX, bool fromStart, RectangleF mr)
    {
      raw = Math.Clamp(raw, 0, max);
      if (!SnapActive()) return raw;
      var (xs, ys) = GetContentEdges(it);
      var lines = axisX ? xs : ys;
      float axisPx = Math.Max(1f, axisX ? mr.Width : mr.Height);
      double best = raw;
      float bestD = SnapPx + 0.5f;
      bool found = false;
      foreach (var l in lines)
      {
        double frac = fromStart ? l : 1 - l;
        if (frac < -1e-9 || frac > max + 1e-9) continue;
        float d = (float)(Math.Abs(frac - raw) * axisPx);
        if (d <= SnapPx && d < bestD) { bestD = d; best = frac; found = true; }
      }
      _cropSnapped = found;
      return Math.Clamp(best, 0, max);
    }
    private (double[] xs, double[] ys) GetContentEdges(MediaItem it)
    {
      var path = it.Path;
      if (string.IsNullOrWhiteSpace(path)) return (Array.Empty<double>(), Array.Empty<double>());
      if (_edgeLines.TryGetValue(path!, out var cached)) return cached;
      var th = GetThumb(it);
      var res = th == null ? (Array.Empty<double>(), Array.Empty<double>()) : DetectColorEdges(th);
      _edgeLines[path!] = res;
      return res;
    }
    private static (double[] xs, double[] ys) DetectColorEdges(Bitmap src)
    {
      const int A = 320;
      int aw = Math.Min(A, src.Width), ah = Math.Min(A, src.Height);
      if (aw < 4 || ah < 4) return (Array.Empty<double>(), Array.Empty<double>());
      using var small = new Bitmap(aw, ah, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      using (var g = Graphics.FromImage(small))
      {
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.DrawImage(src, new Rectangle(0, 0, aw, ah), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
      }
      var bd = small.LockBits(new Rectangle(0, 0, aw, ah),
        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
      var px = new byte[bd.Stride * ah];
      System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, px, 0, px.Length);
      small.UnlockBits(bd);
      double[] rowDiff = new double[ah - 1];
      double[] colDiff = new double[aw - 1];
      for (int y = 0; y < ah; y++)
      {
        int o = y * bd.Stride;
        for (int x = 0; x < aw; x++)
        {
          int p = o + x * 3;
          if (y < ah - 1)
          {
            int q = p + bd.Stride;
            rowDiff[y] += Math.Abs(px[p] - px[q]) + Math.Abs(px[p + 1] - px[q + 1]) + Math.Abs(px[p + 2] - px[q + 2]);
          }
          if (x < aw - 1)
          {
            int q = p + 3;
            colDiff[x] += Math.Abs(px[p] - px[q]) + Math.Abs(px[p + 1] - px[q + 1]) + Math.Abs(px[p + 2] - px[q + 2]);
          }
        }
      }
      for (int i = 0; i < rowDiff.Length; i++) rowDiff[i] /= aw;
      for (int i = 0; i < colDiff.Length; i++) colDiff[i] /= ah;
      static double[] Peaks(double[] d, int n)
      {
        double mean = d.Length > 0 ? d.Average() : 0;
        double floor = Math.Max(24, mean * 4);
        return d.Select((v, i) => (v, i))
                .Where(t => t.v >= floor
                            && (t.i == 0 || d[t.i - 1] <= t.v)
                            && (t.i == d.Length - 1 || d[t.i + 1] <= t.v))
                .OrderByDescending(t => t.v)
                .Take(12)
                .Select(t => (t.i + 1.0) / (d.Length + 1.0))
                .OrderBy(f => f)
                .ToArray();
      }
      return (Peaks(colDiff, aw), Peaks(rowDiff, ah));
    }
    private RectangleF MarqueeRect()
    {
      float x = Math.Min(_dragStart.X, _marqueeCur.X), y = Math.Min(_dragStart.Y, _marqueeCur.Y);
      return new RectangleF(x, y, Math.Abs(_marqueeCur.X - _dragStart.X), Math.Abs(_marqueeCur.Y - _dragStart.Y));
    }
    private void ApplyMarquee(RectangleF rect)
    {
      var hit = new List<MediaItem>();
      for (int i = 0; i < Items.Count; i++)
      {
        var br = BlockRect(Items[i], i);
        if (br.Bottom < RulerH) continue;
        if (br.IntersectsWith(rect)) hit.Add(Items[i]);
      }
      var cur = SelList();
      if (hit.Count == cur.Count && !hit.Where((t, i) => !ReferenceEquals(t, cur[i])).Any()) return;
      _multiSel.Clear();
      for (int i = 1; i < hit.Count; i++) _multiSel.Add(hit[i]);
      SetPrimary(hit.Count > 0 ? hit[0] : null);
    }
    private Cursor TrackCursorAt(Point p)
    {
      if (Math.Abs(p.X - TimeToX(_playhead)) <= 7f) return Cursors.SizeWE;
      if (_selected != null && _selected.PanAnim)
      {
        int si = Items.IndexOf(_selected);
        if (si >= 0)
        {
          var sbr = BlockRect(_selected, si);
          if (p.Y >= sbr.Top - 5 && p.Y <= sbr.Bottom + 5)
          {
            var (pt1, pt2) = PanMarkerTimes(_selected);
            if (Math.Abs(p.X - TimeToX(pt1)) <= 6 || Math.Abs(p.X - TimeToX(pt2)) <= 6) return Cursors.SizeWE;
          }
        }
      }
      for (int i = Items.Count - 1; i >= 0 && p.Y > RulerH; i--)
      {
        var br = BlockRect(Items[i], i);
        if (br.Bottom < RulerH) continue;
        if (br.Contains(p))
          return (p.X <= br.X + 6 || p.X >= br.Right - 6) ? Cursors.SizeWE : Cursors.SizeAll;
      }
      return Cursors.Default;
    }
    private void TrackMouseUpReextract() { }
    private void AddMedia(bool video)
    {
      var ofd = new OpenFileDialog
      {
        Title = video ? "Ajouter une vidéo" : "Ajouter une image",
        Filter = video ? "Vidéo|*.mp4;*.mov;*.webm;*.mkv;*.gif|Tous|*.*" : "Image|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tous|*.*",
      };
      if (ofd.ShowDialog(this) != DialogResult.OK) return;
      string path = ofd.FileName;
      try
      {
        var copied = AssetImporter.CopyIntoProjectMedia(_slidesDir, path, "timeline");
        if (!string.IsNullOrWhiteSpace(copied) && File.Exists(copied)) path = copied;
      }
      catch { }
      double remaining = Math.Max(0.5, _clipDur - _playhead);
      var it = new MediaItem
      {
        Path = path,
        SourcePath = ofd.FileName,
        AppearAt = Math.Round(_playhead, 2),
        AppearDur = Math.Round(Math.Min(3.0, Math.Max(1.0, remaining)), 2),
        PosX = 0.5, PosY = 0.5, Scale = 0.80,
        Speed = 1f,
      };
      FitWindowToAnim(it, allowSpeedUp: true);
      Items.Add(it);
      Select(it);
      Commit("Ajouter média");
    }
    private void DeleteSelected()
    {
      var sel = SelList();
      if (sel.Count == 0) return;
      foreach (var it in sel) Items.Remove(it);
      _multiSel.Clear();
      _selected = null;
      Commit(sel.Count > 1 ? $"Supprimer {sel.Count} calques" : "Supprimer");
    }
    private void DuplicateSelected()
    {
      var sel = SelList();
      if (sel.Count == 0) { _info.Text = "Sélectionne d'abord un média."; return; }
      var clones = new List<MediaItem>();
      foreach (var orig in sel)
      {
        var copy = orig.Clone();
        Items.Insert(Math.Max(0, Items.IndexOf(orig)), copy);
        clones.Add(copy);
      }
      _multiSel.Clear();
      for (int i = 1; i < clones.Count; i++) _multiSel.Add(clones[i]);
      SetPrimary(clones[0]);
      Commit(clones.Count > 1 ? $"Dupliquer {clones.Count} calques" : "Dupliquer");
      _info.Text = clones.Count > 1
        ? $"{clones.Count} copies créées au même endroit (sélectionnées) : glisse pour placer le groupe."
        : "Copie créée au même endroit (sélectionnée) : glisse-la pour la placer.";
    }
    private void AlignSelection(char where, bool content)
    {
      var sel = SelList();
      if (sel.Count == 0) { _info.Text = "Sélectionne d'abord un ou plusieurs calques."; return; }
      foreach (var it in sel)
      {
        if (content)
        {
          var p = GetPan(it);
          switch (where)
          {
            case 'C': SetPan(it, 0, 0); break;
            case 'L': SetPan(it, -1, p.y); break;
            case 'R': SetPan(it, 1, p.y); break;
            case 'T': SetPan(it, p.x, -1); break;
            case 'B': SetPan(it, p.x, 1); break;
          }
        }
        else
        {
          double half = Math.Clamp(it.Scale, 0.05, 1.0) / 2.0;
          switch (where)
          {
            case 'C': it.PosX = 0.5; it.PosY = 0.5; break;
            case 'L': it.PosX = half; break;
            case 'R': it.PosX = 1 - half; break;
            case 'T': it.PosY = half; break;
            case 'B': it.PosY = 1 - half; break;
          }
        }
      }
      string obj = content ? "contenu" : "boîte";
      Commit(where switch { 'C' => $"Centrer ({obj})", 'L' => $"Aligner à gauche ({obj})", 'R' => $"Aligner à droite ({obj})", 'T' => $"Aligner en haut ({obj})", _ => $"Aligner en bas ({obj})" });
    }
    private void MoveLayer(int dir)
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord un média."; return; }
      int i = Items.IndexOf(_selected);
      int j = Math.Clamp(i + dir, 0, Items.Count - 1);
      if (i < 0 || j == i) return;
      Items.RemoveAt(i);
      Items.Insert(j, _selected);
      Commit(dir < 0 ? "Calque : 1er plan" : "Calque : fond");
    }
    private void EditSelectedImage()
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord une image."; return; }
      var it = _selected;
      var path = it.Path;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { _info.Text = "Image introuvable."; return; }
      var ext = Path.GetExtension(path).ToLowerInvariant();
      if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp"))
      { _info.Text = "L'éditeur ne gère que les images (pas les vidéos)."; return; }
      try
      {
        string oldPath = path;
        string editPath;
        using (var src = LoadDetached(path))
        {
          int W = src.Width, H = src.Height;
          double targetA = (double)_canvasW / _canvasH, imgA = (double)W / H;
          int cw, ch;
          if (imgA > targetA) { ch = H; cw = (int)Math.Round(H * targetA); }
          else                { cw = W; ch = (int)Math.Round(W / targetA); }
          cw = Math.Clamp(cw, 1, W); ch = Math.Clamp(ch, 1, H);
          if (cw == W && ch == H)
            editPath = path;
          else
          {
            int exX = W - cw, exY = H - ch;
            int cx = Math.Clamp((int)Math.Round(exX / 2.0 * (1 + it.PanX)), 0, exX);
            int cy = Math.Clamp((int)Math.Round(exY / 2.0 * (1 + it.PanY)), 0, exY);
            var dir = AssetImporter.MediaDir(_slidesDir);
            Directory.CreateDirectory(dir);
            editPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_edit.png");
            using var dst = new System.Drawing.Bitmap(cw, ch);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
              g.InterpolationMode = InterpolationMode.HighQualityBicubic;
              g.DrawImage(src, new System.Drawing.Rectangle(0, 0, cw, ch),
                               new System.Drawing.Rectangle(cx, cy, cw, ch), System.Drawing.GraphicsUnit.Pixel);
            }
            dst.Save(editPath, System.Drawing.Imaging.ImageFormat.Png);
          }
        }
        var win = new ImageEditorWindow(editPath);
        win.ShowDialog();
        if (win.WasSaved)
        {
          it.Path = editPath;
          it.PanX = 0; it.PanY = 0;
          DropThumb(oldPath); DropThumb(editPath);
          Touch();
          _info.Text = "Image éditée.";
        }
      }
      catch (Exception ex) { _info.Text = "Édition impossible : " + ex.Message; }
    }
    private void DropThumb(string? p)
    {
      if (string.IsNullOrEmpty(p)) return;
      if (_thumbs.TryGetValue(p!, out var b)) { b.Dispose(); _thumbs.Remove(p!); }
      if (_gifs.TryGetValue(p!, out var gc)) { gc?.Dispose(); _gifs.Remove(p!); }
    }
    private sealed class HistState
    {
      public List<MediaItem> Items = new();
      public double CamX, CamY, CamDiam;
      public bool   CamRelift;
      public int    SelIndex = -1;
      public string Label = "";
    }
    private HistState Snapshot(string label) => new HistState
    {
      Items     = Items.Select(i => i.Clone()).ToList(),
      CamX      = _seq.CamX, CamY = _seq.CamY, CamDiam = _seq.CamDiam,
      CamRelift = _camRelift,
      SelIndex  = _selected != null ? Items.IndexOf(_selected) : -1,
      Label     = label,
    };
    private void Commit(string label, bool coalesce = false)
    {
      Touch();
      if (_restoring) return;
      long now = Environment.TickCount64;
      if (coalesce && _histIdx >= 0 && _histIdx == _history.Count - 1
          && _lastCommitLabel == label && now - _lastCommitTick < 900)
      {
        _history[_histIdx] = Snapshot(label);
      }
      else
      {
        if (_histIdx < _history.Count - 1)
          _history.RemoveRange(_histIdx + 1, _history.Count - _histIdx - 1);
        _history.Add(Snapshot(label));
        _histIdx = _history.Count - 1;
        if (_history.Count > 120) { _history.RemoveAt(0); _histIdx--; }
        string what = _selected?.Path != null ? Path.GetFileName(_selected.Path) : (_selected?.Text != null ? "texte" : "—");
        Logger.Info($"[Montage] {label} · sél: {what} · {Items.Count} média(s)");
      }
      _lastCommitTick = now; _lastCommitLabel = label;
      UpdateHistoryBtn();
    }
    private void PushInitialHistory()
    {
      _history.Clear();
      _history.Add(Snapshot("État initial"));
      _histIdx = 0;
      UpdateHistoryBtn();
    }
    private void Undo() { if (_histIdx > 0) RestoreTo(_histIdx - 1); }
    private void Redo() { if (_histIdx < _history.Count - 1) RestoreTo(_histIdx + 1); }
    private void RestoreTo(int idx)
    {
      if (idx < 0 || idx >= _history.Count) return;
      var s = _history[idx];
      _restoring = true;
      StopAnimPreview();
      Items.Clear();
      foreach (var i in s.Items) Items.Add(i.Clone());
      _seq.CamX = s.CamX; _seq.CamY = s.CamY; _seq.CamDiam = s.CamDiam;
      _camRelift = s.CamRelift; _seq.CamRelift = s.CamRelift;
      _selected = (s.SelIndex >= 0 && s.SelIndex < Items.Count) ? Items[s.SelIndex] : null;
      _multiSel.Clear();
      _camSelected = false;
      _histIdx = idx;
      _restoring = false;
      Changed = true;
      StyleReliftBtn();
      Select(_selected);
      UpdateHistoryBtn();
    }
    private void UpdateHistoryBtn()
    {
      if (_undoBtn != null) _undoBtn.Enabled = _histIdx > 0;
      if (_redoBtn != null) _redoBtn.Enabled = _histIdx < _history.Count - 1;
      if (_histBtn != null) _histBtn.Text = $"⟲ Historique ({_histIdx + 1}/{_history.Count})";
    }
    private void ShowHistoryMenu()
    {
      if (_histBtn == null) return;
      var menu = new ContextMenuStrip { ShowImageMargin = false };
      for (int i = _history.Count - 1; i >= 0; i--)
      {
        int idx = i;
        var item = new ToolStripMenuItem($"{i + 1}. {_history[i].Label}")
        {
          Checked = (i == _histIdx),
          CheckOnClick = false,
        };
        item.Click += (_, _) => RestoreTo(idx);
        menu.Items.Add(item);
      }
      menu.Show(_histBtn, new Point(0, _histBtn.Height));
    }
    private int TxtCanvasW => _canvasW;
    private int TxtCanvasH => _canvasH;
    private string RenderTextPng(MediaText t, string? reusePath)
    {
      var dir = AssetImporter.MediaDir(_slidesDir);
      Directory.CreateDirectory(dir);
      string path = reusePath ?? Path.Combine(dir, "text_" + Guid.NewGuid().ToString("N") + ".png");
      var bmp = new Bitmap(TxtCanvasW, TxtCanvasH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
      try
      {
        using (var g = Graphics.FromImage(bmp))
        {
          g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
          g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
          g.Clear(Color.Transparent);
          int fontPx = Math.Clamp(t.FontSizePx, 8, 600);
          var style = t.Bold ? FontStyle.Bold : FontStyle.Regular;
          using var font = new Font("Segoe UI", fontPx, style, GraphicsUnit.Pixel);
          var align = (t.Align ?? "center").ToLowerInvariant() switch
          {
            "left"  => StringAlignment.Near,
            "right" => StringAlignment.Far,
            _       => StringAlignment.Center,
          };
          using var fmt = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center };
          float margin = TxtCanvasW * 0.06f;
          var rect = new RectangleF(margin, margin, TxtCanvasW - 2 * margin, TxtCanvasH - 2 * margin);
          var color = ParseColor(t.ColorHex, Color.White);
          string content = string.IsNullOrEmpty(t.Content) ? " " : t.Content;
          if (t.Outline)
          {
            int ow = Math.Max(2, fontPx / 16);
            using (var sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
              g.DrawString(content, font, sh, new RectangleF(rect.X + ow, rect.Y + ow * 2, rect.Width, rect.Height), fmt);
            using var ob = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
            for (int dx = -ow; dx <= ow; dx += ow)
              for (int dy = -ow; dy <= ow; dy += ow)
                if (dx != 0 || dy != 0)
                  g.DrawString(content, font, ob, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), fmt);
          }
          using var fill = new SolidBrush(color);
          g.DrawString(content, font, fill, rect, fmt);
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
      }
      finally { bmp.Dispose(); }
      return path;
    }
    private static Color ParseColor(string? hex, Color fallback)
    {
      try
      {
        if (!string.IsNullOrWhiteSpace(hex))
          return ColorTranslator.FromHtml(hex!.StartsWith("#") ? hex! : "#" + hex);
      }
      catch { }
      return fallback;
    }
    private void AddText()
    {
      var spec = new MediaText { Content = "Texte", FontSizePx = 90, ColorHex = "#FFFFFF", Bold = true, Outline = true, Align = "center" };
      using var dlg = new TextEditDialog(spec);
      if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(spec.Content)) return;
      string png;
      try { png = RenderTextPng(spec, null); }
      catch (Exception ex) { _info.Text = "Texte impossible : " + ex.Message; return; }
      double remaining = Math.Max(0.5, _clipDur - _playhead);
      var it = new MediaItem
      {
        Path = png, Text = spec,
        AppearAt = Math.Round(_playhead, 2),
        AppearDur = Math.Round(Math.Min(3.0, Math.Max(1.0, remaining)), 2),
        PosX = 0.5, PosY = 0.5, Scale = 0.80,
        Border = false,
      };
      Items.Insert(0, it);
      Select(it);
      Commit("Ajouter texte");
    }
    private void EditSelected()
    {
      if (_selected == null) { _info.Text = "Sélectionne d'abord un média."; return; }
      if (_selected.Text != null) { EditSelectedText(); return; }
      EditSelectedImage();
    }
    private void EditSelectedText()
    {
      if (_selected?.Text == null) return;
      var it = _selected;
      var spec = it.Text.Clone();
      using var dlg = new TextEditDialog(spec);
      if (dlg.ShowDialog(this) != DialogResult.OK) return;
      try
      {
        string png = RenderTextPng(spec, null);
        it.Text = spec;
        it.Path = png;
        Commit("Modifier texte");
        _info.Text = "Texte modifié.";
      }
      catch (Exception ex) { _info.Text = "Texte impossible : " + ex.Message; }
    }
    private void RestartPlayersAt(double at)
    {
      if (_hasCam)
      {
        _camPlayer?.Stop(); _camPlayer = null;
        var cp = new VideoFileReader();
        if (cp.Start(_camClipPath!, at)) _camPlayer = cp; else cp.Stop();
      }
      else
      {
        _player?.Stop(); _player = null;
        var pl = new VideoFileReader();
        if (pl.Start(_clipPath, at)) _player = pl; else pl.Stop();
      }
    }
    private void EndDrag()
    {
      var d = _drag;
      _drag = Drag.None;
      if (!double.IsNaN(_snapGuideT)) { _snapGuideT = double.NaN; _track.Invalidate(); }
      if (!float.IsNaN(_snapGuideVx) || !float.IsNaN(_snapGuideVy))
      { _snapGuideVx = _snapGuideVy = float.NaN; _preview.Invalidate(); }
      if (_cropSnapped) { _cropSnapped = false; _preview.Invalidate(); }
      if (d == Drag.Marquee)
      {
        if (!_marqueeActive)
        {
          Select(null);
          SetPlayhead(XToTime(_dragStart.X));
        }
        _marqueeActive = false;
        _track.Invalidate();
        return;
      }
      if (d == Drag.Playhead)
      {
        if (_scrubbing)
        {
          _scrubbing = false;
          double at = _playhead;
          try
          {
            if (_audio != null)
            {
              double maxT = Math.Max(0, _audio.TotalTime.TotalSeconds - 0.1);
              _audio.CurrentTime = TimeSpan.FromSeconds(Math.Min(at, maxT));
            }
          }
          catch { }
          RestartPlayersAt(at);
          _playOrigin = at;
          _playStartMs = Environment.TickCount64;
          try { _wave?.Play(); } catch { }
          _playTimer.Start();
        }
        return;
      }
      bool mutating = d is Drag.Move or Drag.Resize or Drag.Pan
        or Drag.BlockMove or Drag.BlockLeft or Drag.BlockRight
        or Drag.TrimLeft or Drag.TrimRight
        or Drag.CropL or Drag.CropT or Drag.CropR or Drag.CropB
        or Drag.CamMove or Drag.CamResize
        or Drag.PanM1 or Drag.PanM2;
      if (mutating) Commit(LabelForDrag(d));
    }
    private static string LabelForDrag(Drag d) => d switch
    {
      Drag.Move       => "Déplacer",
      Drag.Resize     => "Redimensionner",
      Drag.Pan        => "Recadrer (pan)",
      Drag.BlockMove  => "Déplacer dans le temps",
      Drag.BlockLeft or Drag.BlockRight => "Durée",
      Drag.TrimLeft or Drag.TrimRight   => "Rogner la source",
      Drag.CropL or Drag.CropT or Drag.CropR or Drag.CropB => "Rogner",
      Drag.CamMove    => "Déplacer la cam",
      Drag.CamResize  => "Taille cam",
      Drag.PanM1 or Drag.PanM2 => "Pan A→B (timing)",
      _               => "Édition",
    };
    private void Select(MediaItem? it)
    {
      _multiSel.Clear();
      SetPrimary(it);
    }
    private void SetPrimary(MediaItem? it)
    {
      StopAnimPreview();
      _camSelected = false;
      if (!ReferenceEquals(it, _selected)) _panEditB = false;
      _selected = it;
      _suppressAnim = true;
      _animCombo.SelectedIndex = it == null ? 0 : Math.Max(0, Array.IndexOf(AnimKeys, (it.Anim ?? "none").ToLowerInvariant()));
      _animCombo.Enabled = it != null;
      _suppressAnim = false;
      SyncMediaOpts(it);
      if (it != null) EnsureRowVisible(it);
      _preview.Invalidate(); _track.Invalidate(); UpdateInfo();
    }
    private bool IsSel(MediaItem it) => ReferenceEquals(it, _selected) || _multiSel.Contains(it);
    private List<MediaItem> SelList()
    {
      var l = new List<MediaItem>();
      foreach (var it in Items) if (IsSel(it)) l.Add(it);
      return l;
    }
    private void ToggleSelect(MediaItem it)
    {
      if (ReferenceEquals(it, _selected))
      {
        if (_multiSel.Count > 0) { var nxt = _multiSel[0]; _multiSel.RemoveAt(0); SetPrimary(nxt); }
        else Select(null);
        return;
      }
      if (_multiSel.Remove(it)) { _preview.Invalidate(); _track.Invalidate(); UpdateInfo(); return; }
      if (_selected != null) _multiSel.Add(_selected);
      SetPrimary(it);
    }
    private void PromoteInGroup(MediaItem it)
    {
      if (_multiSel.Remove(it)) { if (_selected != null) _multiSel.Add(_selected); SetPrimary(it); }
      else Select(it);
    }
    private void CaptureGroupDragOrigins()
    {
      _dragOrigPos.Clear(); _dragOrigAt.Clear(); _dragOrigDurs.Clear(); _dragOrigScales.Clear();
      foreach (var it in _multiSel)
      {
        _dragOrigPos[it] = (it.PosX, it.PosY);
        _dragOrigAt[it] = it.AppearAt;
        _dragOrigDurs[it] = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
        _dragOrigScales[it] = Math.Clamp(it.Scale, 0.05, 1.0);
      }
    }
    private void CopySelection()
    {
      var sel = SelList();
      if (sel.Count == 0) { _info.Text = "Rien à copier : sélectionne un ou plusieurs calques (Ctrl+clic)."; return; }
      s_layerClipboard.Clear();
      foreach (var it in sel) s_layerClipboard.Add(it.Clone());
      _info.Text = $"{sel.Count} calque(s) copié(s) — Ctrl+V ici ou dans le montage d'une autre slide.";
    }
    private void PasteClipboard()
    {
      if (s_layerClipboard.Count == 0) { _info.Text = "Presse-papiers vide : Ctrl+C sur des calques d'abord."; return; }
      var clones = new List<MediaItem>();
      foreach (var src in s_layerClipboard) clones.Add(src.Clone());
      foreach (var it in clones)
      {
        if (it.AppearAt > _clipDur - 0.2) it.AppearAt = Math.Max(0, _clipDur - 0.2);
        if (it.AppearDur > 0.01 && it.AppearAt + it.AppearDur > _clipDur + 0.01)
          it.AppearDur = Math.Max(0.2, _clipDur - it.AppearAt);
      }
      for (int i = clones.Count - 1; i >= 0; i--) Items.Insert(0, clones[i]);
      _multiSel.Clear();
      for (int i = 1; i < clones.Count; i++) _multiSel.Add(clones[i]);
      SetPrimary(clones[0]);
      Commit(clones.Count > 1 ? $"Coller {clones.Count} calques" : "Coller");
      _info.Text = $"{clones.Count} calque(s) collé(s) (sélectionnés : glisse pour placer le groupe).";
    }
    private void SyncMediaOpts(MediaItem? it)
    {
      _suppressMediaOpts = true;
      if (it == null)
      {
        _speedBox.Enabled = _loopCheck.Enabled = _trimInBox.Enabled = _trimOutBox.Enabled = false;
        _cropBtn.Enabled = false;
        _borderCheck.Enabled = _containCheck.Enabled = false;
        _panAnimCheck.Enabled = false; _panAnimCheck.Checked = false;
        _panABBtn.Enabled = false; _panT1Box.Enabled = _panDurBox.Enabled = false;
        _panT1Box.Value = 0; _panDurBox.Value = 0;
        if (_cropMode) { _cropMode = false; StyleCropBtn(); }
        _speedBox.Value = 1.00M; _loopCheck.Checked = true; _trimInBox.Value = 0; _trimOutBox.Value = 0;
        _borderCheck.Checked = true; _containCheck.Checked = false;
      }
      else
      {
        bool media = it.Text == null;
        _speedBox.Enabled = _loopCheck.Enabled = _trimInBox.Enabled = _trimOutBox.Enabled = media;
        _cropBtn.Enabled = media;
        _borderCheck.Enabled = _containCheck.Enabled = true;
        if (!media && _cropMode) { _cropMode = false; StyleCropBtn(); }
        _speedBox.Value   = (decimal)Math.Clamp(it.Speed <= 0 ? 1f : it.Speed, 0.10f, 4.00f);
        _loopCheck.Checked = it.Loop;
        _trimInBox.Value  = (decimal)Math.Clamp(it.TrimIn ?? 0f, 0f, 3600f);
        _trimOutBox.Value = (decimal)Math.Clamp(it.TrimOut ?? 0f, 0f, 3600f);
        _borderCheck.Checked = it.Border;
        _containCheck.Checked = it.Contain;
        bool panOk = media && !it.Contain;
        _panAnimCheck.Enabled = panOk;
        _panAnimCheck.Checked = it.PanAnim;
        _panABBtn.Enabled = panOk && it.PanAnim;
        _panT1Box.Enabled = _panDurBox.Enabled = panOk && it.PanAnim;
        _panT1Box.Value = (decimal)Math.Clamp(it.PanT1, 0, 3600);
        _panDurBox.Value = (decimal)Math.Clamp(it.PanDur, 0, 3600);
        StylePanBtn();
      }
      _suppressMediaOpts = false;
    }
    private void SelectCam()
    {
      StopAnimPreview();
      _selected = null;
      _camSelected = true;
      _suppressAnim = true; _animCombo.SelectedIndex = 0; _animCombo.Enabled = false; _suppressAnim = false;
      SyncMediaOpts(null);
      _preview.Invalidate(); _track.Invalidate(); UpdateInfo();
    }
    private void OnAnimChanged(object? s, EventArgs e)
    {
      if (_suppressAnim || _selected == null) return;
      int i = _animCombo.SelectedIndex; if (i < 0) return;
      _selected.Anim = AnimKeys[Math.Clamp(i, 0, AnimKeys.Length - 1)];
      Commit("Animation");
      StartAnimPreview();
    }
    private void OnSpeedChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      _selected.Speed = (float)Math.Clamp((double)_speedBox.Value, 0.10, 4.00);
      FitWindowToAnim(_selected, allowSpeedUp: false);
      Commit("Vitesse", coalesce: true);
    }
    private void OnLoopChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      _selected.Loop = _loopCheck.Checked;
      Commit("Boucle");
      _preview.Invalidate();
    }
    private void OnBorderChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      _selected.Border = _borderCheck.Checked;
      Commit("Cadre");
      _preview.Invalidate();
    }
    private void OnContainChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      _selected.Contain = _containCheck.Checked;
      Commit("Entier");
      _preview.Invalidate();
    }
    private void OnPanAnimChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      var it = _selected;
      it.PanAnim = _panAnimCheck.Checked;
      if (it.PanAnim)
      {
        it.PanX2 = it.PanX; it.PanY2 = it.PanY;
        it.PanT1 = 0; it.PanDur = 0;
        _panEditB = true;
        _info.Text = "Pan A→B activé : règle le cadrage d'ARRIVÉE (clic droit / flèches / boutons Contenu), puis « Édite : A » pour revoir le départ.";
      }
      else _panEditB = false;
      StylePanBtn();
      SyncMediaOpts(it);
      Commit(it.PanAnim ? "Pan A→B : ON" : "Pan A→B : OFF");
    }
    private void TogglePanEdit()
    {
      if (_selected == null || !_selected.PanAnim) return;
      _panEditB = !_panEditB;
      StylePanBtn();
      _info.Text = _panEditB ? "Édition du cadrage d'ARRIVÉE (B)." : "Édition du cadrage de DÉPART (A).";
      _preview.Invalidate();
    }
    private void StylePanBtn()
    {
      _panABBtn.Text = _panEditB ? "Édite : B" : "Édite : A";
      _panABBtn.BackColor = _panEditB ? Color.FromArgb(40, 110, 60) : Color.FromArgb(55, 55, 62);
    }
    private void OnPanTimingChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      _selected.PanT1 = (double)_panT1Box.Value;
      _selected.PanDur = (double)_panDurBox.Value;
      Commit("Pan A→B (timing)", coalesce: true);
    }
    private (double x, double y) GetPan(MediaItem it)
      => _panEditB && it.PanAnim ? (it.PanX2, it.PanY2) : (it.PanX, it.PanY);
    private void SetPan(MediaItem it, double px, double py)
    {
      px = Math.Clamp(px, -1, 1); py = Math.Clamp(py, -1, 1);
      if (_panEditB && it.PanAnim) { it.PanX2 = px; it.PanY2 = py; }
      else { it.PanX = px; it.PanY = py; }
    }
    private (double x, double y) EffectivePan(MediaItem it, bool animPrevActive)
    {
      if (!it.PanAnim) return (it.PanX, it.PanY);
      if (!_playing && !animPrevActive && ReferenceEquals(it, _selected))
        return _panEditB ? (it.PanX2, it.PanY2) : (it.PanX, it.PanY);
      double t = animPrevActive ? it.AppearAt + _animPrevT : _playhead;
      return PanAtTime(it, t);
    }
    private (double t1, double t2) PanMarkerTimes(MediaItem it)
    {
      double wd = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
      double pt1 = Math.Clamp(it.PanT1, 0, Math.Max(0, wd - 0.1));
      double t1 = it.AppearAt + pt1;
      double d = it.PanDur > 0.01 ? it.PanDur : Math.Max(0.05, wd - pt1);
      double t2 = Math.Min(t1 + d, it.AppearAt + wd);
      return (t1, t2);
    }
    private (double x, double y) PanAtTime(MediaItem it, double segT)
    {
      double wd = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, _clipDur - it.AppearAt);
      double pt1 = Math.Max(0, it.PanT1);
      double t1 = it.AppearAt + pt1;
      double d = it.PanDur > 0.01 ? it.PanDur : Math.Max(0.05, wd - pt1);
      double f = Math.Clamp((segT - t1) / d, 0, 1);
      return (it.PanX + (it.PanX2 - it.PanX) * f, it.PanY + (it.PanY2 - it.PanY) * f);
    }
    private void ToggleCropMode()
    {
      if (_selected == null) return;
      _cropMode = !_cropMode;
      StyleCropBtn();
      _info.Text = _cropMode
        ? "Rognage : tire les bords du média pour couper ; la zone gardée remplit la boîte. Reclique « ✂ Rogner » pour finir."
        : "";
      _preview.Invalidate();
    }
    private void StyleCropBtn()
    {
      _cropBtn.BackColor = _cropMode ? Color.FromArgb(40, 110, 60) : Color.FromArgb(55, 55, 62);
      _cropBtn.Text = _cropMode ? "✂ Rogner : ON" : "✂ Rogner";
    }
    private void OnTrimChanged(object? s, EventArgs e)
    {
      if (_suppressMediaOpts || _selected == null) return;
      float tin  = (float)_trimInBox.Value;
      float tout = (float)_trimOutBox.Value;
      _selected.TrimIn  = tin  > 0.001f ? tin  : (float?)null;
      _selected.TrimOut = tout > 0.001f ? tout : (float?)null;
      FitWindowToAnim(_selected, allowSpeedUp: false);
      Commit("Rogner", coalesce: true);
    }
    private void StartAnimPreview()
    {
      if (_playing || _selected == null || (_selected.Anim ?? "none").ToLowerInvariant() == "none")
      { StopAnimPreview(); return; }
      _animPreview = true;
      _animPrevStartMs = Environment.TickCount64;
      _animPrevTimer.Start();
    }
    private void StopAnimPreview()
    {
      if (!_animPreview && !_animPrevTimer.Enabled) return;
      _animPreview = false;
      _animPrevTimer.Stop();
      _preview.Invalidate();
    }
    private void AnimPrevTick(object? s, EventArgs e)
    {
      if (!_animPreview || _selected == null) { StopAnimPreview(); return; }
      double wd = _selected.AppearDur > 0.01 ? _selected.AppearDur : Math.Max(0.1, _clipDur - _selected.AppearAt);
      _animPrevT = ((Environment.TickCount64 - _animPrevStartMs) / 1000.0) % Math.Max(1.0, wd);
      _preview.Invalidate();
    }
    private static (double px, double py, double zoom) AnimTransform(string anim, double lt, double wd)
    {
      double f = Math.Clamp(wd > 0.01 ? lt / wd : 0, 0, 1);
      return anim switch
      {
        "slide"      => ((f * 2 - 1) * 0.8, 0.0, 1.12),
        "float"      => (0.0, Math.Sin(lt * 1.8) * 0.5, 1.12),
        "zoom"       => (0.0, 0.0, Math.Min(1 + 0.05 * lt, 1.18)),
        "scrolldown" => (0.0, f * 2 - 1, 1.0),
        "scrollup"   => (0.0, 1 - f * 2, 1.0),
        _            => (0.0, 0.0, 1.0),
      };
    }
    private void Touch()
    {
      Changed = true;
      UpdateTrackLayout();
      _preview.Invalidate();
      _track.Invalidate();
      UpdateInfo();
    }
    private void UpdateInfo()
    {
      if (_camSelected) { _info.Text = $"Cam : pos {_seq.CamX:0.00}/{_seq.CamY:0.00} · taille {_seq.CamDiam:0.00}  (flèches pour ajuster)"; return; }
      if (_selected != null && _multiSel.Count > 0)
      {
        _info.Text = $"{_multiSel.Count + 1} calques sélectionnés · glisser = déplacer le groupe · Ctrl+clic = ajouter/retirer · Ctrl+C/Ctrl+V = copier/coller (autre slide OK)";
        return;
      }
      if (_selected == null) { _info.Text = $"{Items.Count} média(s) · clip {_clipDur:0.0}s · tête {_playhead:0.0}s" + (_hasCam ? " · clique la CAM pour la placer" : ""); return; }
      double dur = _selected.AppearDur > 0.01 ? _selected.AppearDur : (_clipDur - _selected.AppearAt);
      if (IsAnimatedMedia(_selected))
      {
        string warn = IsTruncated(_selected) ? "⚠ anim coupée (accélère ou rogne) · " : "";
        _info.Text = warn + $"Sél. : {_selected.AppearAt:0.0}s · fenêtre {dur:0.0}s · anim {PlayDur(_selected):0.0}s à ×{ClampSpeed(_selected.Speed):0.00}  (bords du bloc = vitesse · Alt+bord = rogner la source)";
        return;
      }
      _info.Text = $"Sél. : {_selected.AppearAt:0.0}s · {dur:0.0}s · taille {_selected.Scale:0.00} · pan {_selected.PanX:0.0}/{_selected.PanY:0.0}  (flèches = recadrer l'image · clic droit glisser aussi · Ctrl+flèches = déplacer)";
    }
    private void SetPlayhead(double t, bool reextract = true)
    {
      _endReached = false;
      _playhead = Math.Clamp(t, 0, _clipDur);
      if (reextract) { var nf = ExtractFrame(_playhead); if (nf != null) { _frame?.Dispose(); _frame = nf; } }
      _preview.Invalidate();
      _track.Invalidate();
      UpdateInfo();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
      base.OnMouseUp(e);
      if (_playing) return;
      if (_playhead >= 0) { var nf = ExtractFrame(_playhead); if (nf != null) { _frame?.Dispose(); _frame = nf; } _preview.Invalidate(); }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      bool ctrl = (keyData & Keys.Control) == Keys.Control;
      bool shift = (keyData & Keys.Shift) == Keys.Shift;
      var kc = keyData & Keys.KeyCode;
      if (ctrl && kc == Keys.Z) { if (shift) Redo(); else Undo(); return true; }
      if (ctrl && kc == Keys.Y) { Redo(); return true; }
      if (ctrl && kc == Keys.C) { CopySelection(); return true; }
      if (ctrl && kc == Keys.V) { PasteClipboard(); return true; }
      if (ctrl && kc == Keys.D) { DuplicateSelected(); return true; }
      if (_camSelected)
      {
        double s2 = shift ? 0.02 : 0.004;
        switch (kc)
        {
          case Keys.Left:  _seq.CamX = Math.Clamp(_seq.CamX - s2, 0, 1); Commit("Déplacer la cam", true); return true;
          case Keys.Right: _seq.CamX = Math.Clamp(_seq.CamX + s2, 0, 1); Commit("Déplacer la cam", true); return true;
          case Keys.Up:    _seq.CamY = Math.Clamp(_seq.CamY - s2, 0, 1); Commit("Déplacer la cam", true); return true;
          case Keys.Down:  _seq.CamY = Math.Clamp(_seq.CamY + s2, 0, 1); Commit("Déplacer la cam", true); return true;
        }
      }
      if (_selected != null)
      {
        double panStep = shift ? 0.08 : 0.02;
        double posStep = shift ? 0.02 : 0.004;
        switch (kc)
        {
          case Keys.Left:  if (ctrl) _selected.PosX = Math.Clamp(_selected.PosX - posStep, 0, 1); else { var p = GetPan(_selected); SetPan(_selected, p.x - panStep, p.y); } Commit(ctrl ? "Déplacer" : "Recadrer", true); return true;
          case Keys.Right: if (ctrl) _selected.PosX = Math.Clamp(_selected.PosX + posStep, 0, 1); else { var p = GetPan(_selected); SetPan(_selected, p.x + panStep, p.y); } Commit(ctrl ? "Déplacer" : "Recadrer", true); return true;
          case Keys.Up:    if (ctrl) _selected.PosY = Math.Clamp(_selected.PosY - posStep, 0, 1); else { var p = GetPan(_selected); SetPan(_selected, p.x, p.y - panStep); } Commit(ctrl ? "Déplacer" : "Recadrer", true); return true;
          case Keys.Down:  if (ctrl) _selected.PosY = Math.Clamp(_selected.PosY + posStep, 0, 1); else { var p = GetPan(_selected); SetPan(_selected, p.x, p.y + panStep); } Commit(ctrl ? "Déplacer" : "Recadrer", true); return true;
          case Keys.Delete: DeleteSelected(); return true;
        }
      }
      return base.ProcessCmdKey(ref msg, keyData);
    }
    private void TogglePlay()
    {
      if (_playing) { StopPlay(); return; }
      StopAnimPreview();
      double startAt = (_endReached || _playhead >= _clipDur - 0.05) ? 0 : Math.Max(0, _playhead);
      _endReached = false;
      try
      {
        _audio = new NAudio.Wave.MediaFoundationReader(AudioSourcePath);
        double maxStart = Math.Max(0, _audio.TotalTime.TotalSeconds - 0.1);
        _audio.CurrentTime = TimeSpan.FromSeconds(Math.Min(startAt, maxStart));
        _wave = new NAudio.Wave.WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        _wave.Init(_audio);
        _wave.Play();
      }
      catch { DisposeAudio(); }
      if (_hasCam)
      {
        var cp = new VideoFileReader();
        if (cp.Start(_camClipPath!, startAt)) _camPlayer = cp; else cp.Stop();
      }
      else
      {
        var pl = new VideoFileReader();
        if (pl.Start(_clipPath, startAt)) _player = pl;
        else
        {
          pl.Stop();
          if (_audio == null) { MessageBox.Show(this, "Lecture impossible pour ce clip (codec non supporté).", "Montage"); return; }
        }
      }
      _playOrigin = startAt;
      _playStartMs = Environment.TickCount64;
      _playing = true;
      _playBtn.Text = "⏸ Pause";
      _playTimer.Start();
    }
    private void PlayTick(object? s, EventArgs e)
    {
      if (!_playing) return;
      if (_audio != null)
      {
        if ((_wave != null && _wave.PlaybackState == NAudio.Wave.PlaybackState.Stopped)
            || _audio.CurrentTime >= _audio.TotalTime - TimeSpan.FromMilliseconds(60))
        { StopPlay(); _endReached = true; return; }
        _playhead = Math.Min(_clipDur, _audio.CurrentTime.TotalSeconds);
      }
      else
      {
        _playhead = _playOrigin + (Environment.TickCount64 - _playStartMs) / 1000.0;
        if (_playhead >= _clipDur) { StopPlay(); _endReached = true; return; }
      }
      _preview.Invalidate();
      _track.Invalidate();
      UpdateInfo();
    }
    private void StopPlay()
    {
      _playTimer.Stop();
      _playing = false;
      _scrubbing = false;
      _playBtn.Text = "▶ Lire";
      DisposeAudio();
      if (_player != null) { _player.Stop(); _player = null; }
      if (_camPlayer != null) { _camPlayer.Stop(); _camPlayer = null; }
      SetPlayhead(_playhead);
    }
    private void DisposeAudio()
    {
      try { _wave?.Stop(); } catch { }
      try { _wave?.Dispose(); } catch { }
      try { _audio?.Dispose(); } catch { }
      _wave = null; _audio = null;
    }
    private Bitmap? GetFrameFor(MediaItem it)
    {
      double speed = Math.Clamp(it.Speed <= 0 ? 1f : it.Speed, 0.1f, 4.0f);
      double baseMs = Math.Max(0, _playhead - it.AppearAt) * 1000.0;
      int ms = (int)Math.Round(baseMs * speed);
      int inMs = (int)Math.Max(0, (it.TrimIn ?? 0f) * 1000f);
      var gif = GetGif(it);
      if (gif != null)
      {
        int outMs = it.TrimOut is > 0 ? (int)(it.TrimOut.Value * 1000f) : gif.TotalMs;
        return gif.FrameAt(ms, inMs, outMs, it.Loop) ?? GetThumb(it);
      }
      var vid = GetVideoClip(it);
      if (vid != null)
      {
        int outMs = it.TrimOut is > 0 ? (int)(it.TrimOut.Value * 1000f) : vid.TotalMs;
        return vid.FrameAt(ms, inMs, outMs, it.Loop) ?? GetThumb(it);
      }
      return GetThumb(it);
    }
    private VideoClip? GetVideoClip(MediaItem it)
    {
      if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path)) return null;
      var ext = Path.GetExtension(it.Path).ToLowerInvariant();
      if (ext is not (".mp4" or ".mov" or ".webm" or ".mkv" or ".avi" or ".m4v")) return null;
      var path = it.Path!;
      lock (s_vidLock)
      {
        if (s_videoClips.TryGetValue(path, out var vc)) { TouchVideoLru(path); return vc; }
        if (!s_videoDecoding.Add(path)) return null;
      }
      double srcDur = SourceDur(it);
      int firstW = srcDur > 30 ? 360 : 720;
      StartVideoDecode(path, firstW, upgradeToW: firstW < 720 ? 720 : 0);
      return null;
    }
    private static bool IsVideoDecoding(string path)
    { lock (s_vidLock) return s_videoDecoding.Contains(path); }
    private static void TouchVideoLru(string path)
    {
      s_videoLru.Remove(path);
      s_videoLru.Add(path);
    }
    private static void EvictVideoCacheIfNeeded()
    {
      long total = 0;
      foreach (var c in s_videoClips.Values) total += c?.MemoryBytes ?? 0;
      for (int i = 0; total > VideoCacheBudget && i < s_videoLru.Count - 1;)
      {
        var victim = s_videoLru[i];
        if (s_videoClips.TryGetValue(victim, out var c) && c != null)
        {
          total -= c.MemoryBytes;
          c.Dispose();
          s_videoClips.Remove(victim);
          s_videoLru.RemoveAt(i);
        }
        else i++;
      }
    }
    private void StartVideoDecode(string path, int decodeW, int upgradeToW)
    {
      var t = new System.Threading.Thread(() =>
      {
        VideoClip? clip = null;
        try { clip = VideoClip.Load(path, decodeW, 300); } catch { }
        if (clip == null) Logger.Warn($"[Montage] Décodage vidéo d'aperçu échoué ({decodeW}px) : {path}");
        VideoClip? toDispose = null;
        lock (s_vidLock)
        {
          if (s_videoClips.TryGetValue(path, out var old) && old != null && !ReferenceEquals(old, clip))
            toDispose = old;
          s_videoClips[path] = clip;
          s_videoDecoding.Remove(path);
          TouchVideoLru(path);
          EvictVideoCacheIfNeeded();
        }
        try
        {
          if (!IsDisposed) BeginInvoke((Action)(() =>
          {
            toDispose?.Dispose();
            if (!IsDisposed) _preview.Invalidate();
          }));
          else toDispose?.Dispose();
        }
        catch { toDispose?.Dispose(); }
        if (clip != null && upgradeToW > decodeW) StartVideoDecode(path, upgradeToW, 0);
      })
      { IsBackground = true, Name = "RoleplayOverlay.VideoClipDecode" };
      t.SetApartmentState(System.Threading.ApartmentState.MTA);
      t.Start();
    }
    private GifClip? GetGif(MediaItem it)
    {
      if (string.IsNullOrWhiteSpace(it.Path)) return null;
      if (_gifs.TryGetValue(it.Path!, out var g)) return g;
      GifClip? clip = null;
      try
      {
        if (Path.GetExtension(it.Path).ToLowerInvariant() == ".gif" && File.Exists(it.Path))
          clip = GifClip.Load(it.Path!);
      }
      catch { }
      _gifs[it.Path!] = clip;
      return clip;
    }
    private Bitmap? GetThumb(MediaItem it)
    {
      if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path)) return null;
      if (_thumbs.TryGetValue(it.Path, out var b)) return b;
      Bitmap? bmp = null;
      try
      {
        var ext = Path.GetExtension(it.Path).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
          bmp = LoadDetached(it.Path);
        else
          bmp = ExtractFrameOf(it.Path, 0.0, 1280);
      }
      catch { }
      if (bmp != null) _thumbs[it.Path] = bmp;
      return bmp;
    }
    private static Bitmap LoadDetached(string path)
    {
      using var s = File.OpenRead(path);
      using var tmp = new Bitmap(s);
      return new Bitmap(tmp);
    }
    private Bitmap? ExtractFrame(double t) => ExtractFrameOf(_clipPath, Math.Min(t, Math.Max(0, _clipDur - 0.05)));
    private Bitmap? ExtractFrameOf(string path, double t, int width = 540)
    {
      try
      {
        var dir = Path.Combine(Path.GetTempPath(), "ro_timeline");
        Directory.CreateDirectory(dir);
        var outp = Path.Combine(dir, "f_" + Guid.NewGuid().ToString("N") + ".png");
        var args = $"-y -ss {t.ToString("F2", Inv)} -i \"{path}\" -frames:v 1 -vf scale={width}:-2 \"{outp}\"";
        RunFfmpeg(args);
        if (File.Exists(outp))
        {
          var bmp = LoadDetached(outp);
          try { File.Delete(outp); } catch { }
          return bmp;
        }
      }
      catch { }
      return null;
    }
    private void RunFfmpeg(string args)
    {
      try
      {
        var psi = new ProcessStartInfo { FileName = _ffmpegExe, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        using var p = Process.Start(psi);
        if (p == null) return;
        p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
      }
      catch { }
    }
    private double ProbeDuration(string path)
    {
      try
      {
        var probe = _ffmpegExe.Replace("ffmpeg.exe", "ffprobe.exe");
        var psi = new ProcessStartInfo { FileName = probe, Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{path}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi);
        if (p == null) return 5;
        var outp = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(10000);
        if (double.TryParse(outp, NumberStyles.Float, Inv, out var d) && d > 0) return d;
      }
      catch { }
      return 5;
    }
    private sealed class GifClip
    {
      private readonly Bitmap[] _frames;
      private readonly int[] _cumEndMs;
      public int TotalMs { get; }
      private GifClip(Bitmap[] frames, int[] cumEndMs, int total)
      { _frames = frames; _cumEndMs = cumEndMs; TotalMs = total; }
      public static GifClip? Load(string path)
      {
        using var s = File.OpenRead(path);
        using var img = Image.FromStream(s, false, false);
        var dim = System.Drawing.Imaging.FrameDimension.Time;
        int count = img.GetFrameCount(dim);
        if (count <= 1) return null;
        var delays = new int[count];
        try
        {
          var prop = img.GetPropertyItem(0x5100);
          for (int i = 0; i < count; i++)
          {
            int d = (prop?.Value != null && i * 4 + 4 <= prop.Value.Length)
              ? BitConverter.ToInt32(prop.Value, i * 4) * 10 : 100;
            delays[i] = d < 20 ? 100 : d;
          }
        }
        catch { for (int i = 0; i < count; i++) delays[i] = 100; }
        var frames = new Bitmap[count];
        var cum = new int[count];
        int t = 0;
        for (int i = 0; i < count; i++)
        {
          img.SelectActiveFrame(dim, i);
          frames[i] = new Bitmap(img);
          t += delays[i];
          cum[i] = t;
        }
        return new GifClip(frames, cum, t);
      }
      public Bitmap FrameAt(int ms, int windowStartMs, int windowEndMs, bool loop)
      {
        int a = Math.Clamp(windowStartMs, 0, Math.Max(0, TotalMs - 1));
        int b = Math.Clamp(windowEndMs, a + 1, TotalMs);
        int span = b - a;
        int local = loop
          ? (span > 0 ? a + (((ms % span) + span) % span) : a)
          : a + Math.Clamp(ms, 0, Math.Max(0, span - 1));
        for (int i = 0; i < _cumEndMs.Length; i++)
          if (local < _cumEndMs[i]) return _frames[i];
        return _frames[_frames.Length - 1];
      }
      public void Dispose() { foreach (var f in _frames) f.Dispose(); }
    }
    private sealed class TextEditDialog : Form
    {
      private readonly MediaText _spec;
      private readonly TextBox _txt;
      private readonly NumericUpDown _size;
      private readonly CheckBox _bold, _outline;
      private readonly ComboBox _align;
      private Color _color;
      private readonly Button _colorBtn;
      public TextEditDialog(MediaText spec)
      {
        _spec = spec;
        Text = "Texte";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(440, 322);
        BackColor = Color.FromArgb(24, 24, 30);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        Controls.Add(new Label { Text = "Texte :", AutoSize = true, Location = new Point(12, 12) });
        _txt = new TextBox
        {
          Multiline = true, ScrollBars = ScrollBars.Vertical, Text = spec.Content,
          BackColor = Color.FromArgb(36, 36, 44), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
          Bounds = new Rectangle(12, 34, 416, 120),
        };
        Controls.Add(_txt);
        int y = 166;
        Controls.Add(new Label { Text = "Taille :", AutoSize = true, Location = new Point(12, y + 3) });
        _size = new NumericUpDown
        {
          Minimum = 20, Maximum = 400, Value = Math.Clamp(spec.FontSizePx, 20, 400),
          Bounds = new Rectangle(70, y, 70, 24), BackColor = Color.FromArgb(36, 36, 44), ForeColor = Color.White,
        };
        Controls.Add(_size);
        _bold = new CheckBox { Text = "Gras", Checked = spec.Bold, AutoSize = true, Location = new Point(160, y + 2) };
        Controls.Add(_bold);
        _outline = new CheckBox { Text = "Contour", Checked = spec.Outline, AutoSize = true, Location = new Point(232, y + 2) };
        Controls.Add(_outline);
        _color = ParseColor(spec.ColorHex, Color.White);
        _colorBtn = new Button { Text = "Couleur…", FlatStyle = FlatStyle.Flat, Bounds = new Rectangle(320, y, 100, 26), ForeColor = Color.White, BackColor = _color };
        _colorBtn.FlatAppearance.BorderSize = 1;
        _colorBtn.Click += (_, _) =>
        {
          using var cd = new ColorDialog { Color = _color, FullOpen = true };
          if (cd.ShowDialog(this) == DialogResult.OK) { _color = cd.Color; _colorBtn.BackColor = _color; }
        };
        Controls.Add(_colorBtn);
        y += 40;
        Controls.Add(new Label { Text = "Alignement :", AutoSize = true, Location = new Point(12, y + 3) });
        _align = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(100, y, 120, 24) };
        _align.Items.AddRange(new object[] { "Gauche", "Centré", "Droite" });
        _align.SelectedIndex = (spec.Align ?? "center").ToLowerInvariant() switch { "left" => 0, "right" => 2, _ => 1 };
        Controls.Add(_align);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, Bounds = new Rectangle(236, 284, 90, 28), BackColor = Color.FromArgb(40, 110, 60), ForeColor = Color.White };
        var cancel = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, Bounds = new Rectangle(332, 284, 90, 28), BackColor = Color.FromArgb(55, 55, 62), ForeColor = Color.White };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        FormClosing += (_, _) =>
        {
          if (DialogResult != DialogResult.OK) return;
          _spec.Content    = _txt.Text;
          _spec.FontSizePx = (int)_size.Value;
          _spec.Bold       = _bold.Checked;
          _spec.Outline    = _outline.Checked;
          _spec.ColorHex   = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
          _spec.Align      = _align.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" };
        };
      }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
      Logger.Info($"[Montage] Fermeture — {(_seq.Note ?? _seq.Id ?? "?")} · modifié: {(Changed ? "oui" : "non")} · {Items.Count} média(s)");
      _playTimer.Stop();
      _animPrevTimer.Stop();
      _animPrevTimer.Dispose();
      DisposeAudio();
      if (_player != null) { _player.Stop(); _player = null; }
      if (_camPlayer != null) { _camPlayer.Stop(); _camPlayer = null; }
      _camThumb?.Dispose();
      _frame?.Dispose();
      foreach (var b in _thumbs.Values) b.Dispose();
      _thumbs.Clear();
      foreach (var gc in _gifs.Values) gc?.Dispose();
      _gifs.Clear();
      base.OnFormClosed(e);
    }
  }
}