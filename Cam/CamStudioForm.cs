using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace RoleplayOverlay.Cam;
internal sealed class CamStudioForm : Form
{
    private sealed class MediaSlot
    {
        public string Path = "";
        public string? SourcePath;
        public CamForm.Shape Shape = CamForm.Shape.Rectangle;
        public string BorderColor = "#FFFFFF";
        public int BorderPx = 6;
        public double WidthFrac = 0.92, CxFrac = 0.5, CyFrac = 0.5;
        public double? Aspect = 0.8;
        public double PanX, PanY;
        public CamForm? Form;
        public VideoFileReader? VideoReader;
        public Bitmap? ImageBitmap;
        public Panel? Tile;
        public bool IsShown => Form != null;
    }
    private sealed class MediaSlotDto
    {
        public string Path = "";
        public string? SourcePath;
        public int Shape;
        public string BorderColor = "#FFFFFF";
        public int BorderPx = 6;
        public double WidthFrac = 0.92, CxFrac = 0.5, CyFrac = 0.5;
        public double? Aspect = 0.8;
        public double PanX, PanY;
    }
    private WebcamReader? _cam;
    private CamForm? _camForm;
    private TeleprompterForm? _teleForm;
    private BackdropForm? _backdrop;
    private readonly List<MediaSlot> _mediaSlots = new();
    private readonly HashSet<MediaSlot> _selectedSlots = new();
    private readonly MediaProxyService _thumbs = new();
    private readonly Dictionary<string, MediaSlot> _pendingThumbs = new();
    private readonly VideoRecorder _video = new();
    private string? _slidesDir;
    private double _zoneAspect = 9.0 / 16.0;
    private double DefaultMediaAspect => _zoneAspect >= 1.0 ? 16.0 / 9.0 : 0.8;
    private readonly ComboBox _deviceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _presetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _shapeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _scriptBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _slideBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _mirror = new() { Text = "Miroir", AutoSize = true };
    private readonly TrackBar _sizeBar = new() { Minimum = 15, Maximum = 100, TickStyle = TickStyle.None };
    private readonly Label _sizeValueLabel = new() { Text = "", AutoSize = false, Width = 38,
        TextAlign = ContentAlignment.MiddleRight };
    private readonly Button _camBtn = new() { Text = "Caméra : ouvrir" };
    private readonly Button _teleBtn = new() { Text = "Téléprompteur : ouvrir" };
    private readonly Button _slideBtn = new() { Text = "Fond diapo : afficher" };
    private readonly FlowLayoutPanel _mediaGallery = new()
    {
        FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
    };
    private readonly Button _importMediaBtn = new() { Text = "+ Importer un média…" };
    private readonly Button _autoArrangeMediaBtn = new() { Text = "Arranger auto (grille)" };
    private readonly Button _deleteSelectedBtn = new() { Text = "🗑 Supprimer la sélection" };
    private readonly CheckBox _countdownCheck = new() { Text = "Décompte 3, 2, 1 avant la vidéo", AutoSize = true };
    private readonly CheckBox _showCursorCheck = new() { Text = "Afficher le curseur souris", AutoSize = true, Checked = true };
    private readonly CheckBox _hideWmCheck = new() { Text = "Masquer le filigrane « Activer Windows » (paysage)", AutoSize = true, Checked = true };
    private readonly Button _recBtn = new() { Text = "Enregistrer" };
    private readonly Button _scriptFolderBtn = new() { Text = "Dossier scripts" };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 40 };
    private bool _countingDown;
    private CountdownForm? _countdownForm;
    private FileSystemWatcher? _scriptWatcher;
    private FileSystemWatcher? _slidesWatcher;
    private FileSystemWatcher? _composedWatcher;
    private readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 250 };
    private bool _reloadScriptPending;
    private bool _reloadSlidesPending;
    private readonly System.Windows.Forms.Timer _zOrderTimer = new() { Interval = 400 };
    private double _camWidthFrac;
    private double _camCxFrac;
    private double _camCyFrac;
    private static readonly Color Bg = Color.FromArgb(24, 24, 28);
    private static readonly Color Panel = Color.FromArgb(34, 34, 40);
    private static readonly Color TileActive = Color.FromArgb(40, 110, 60);
    public CamStudioForm(string? slidesDir = null)
    {
        _slidesDir = slidesDir;
        Text = "Studio caméra";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = new Size(320, 340);
        AutoScroll = true;
        BackColor = Bg;
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        Rectangle scr = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int defaultH = Math.Min(620, scr.Height - 80);
        int panelW = UserPrefs.CamPanelWidth  > 0 ? UserPrefs.CamPanelWidth  : 350;
        int panelH = UserPrefs.CamPanelHeight > 0 ? UserPrefs.CamPanelHeight : defaultH;
        ClientSize = new Size(panelW, panelH);
        Location = PanelLocation(panelH);
        BuildLayout();
        LoadDevices();
        LoadScripts();
        LoadSlides();
        RestoreMediaSlots();
        RefreshGalleryUi();
        _thumbs.SnapshotReady += OnThumbnailReady;
        _presetBox.Items.AddRange(CameraFilter.PresetNames);
        _shapeBox.Items.AddRange(new object[] { "Rectangle", "Arrondi", "Cercle (PiP rond)" });
        _presetBox.SelectedIndex = Math.Clamp(UserPrefs.CamPresetIndex, 0, _presetBox.Items.Count - 1);
        _shapeBox.SelectedIndex  = Math.Clamp(UserPrefs.CamShapeIndex,  0, _shapeBox.Items.Count - 1);
        _mirror.Checked = UserPrefs.CamMirror;
        _countdownCheck.Checked = UserPrefs.CamCountdownEnabled;
        _showCursorCheck.Checked = UserPrefs.CamShowCursor;
        _hideWmCheck.Checked = UserPrefs.CamHideWinWatermark;
        SelectByText(_deviceBox, UserPrefs.CamDeviceName);
        SelectByText(_scriptBox, UserPrefs.CamLastScript);
        SelectByText(_slideBox,  UserPrefs.CamLastSlide);
        _camWidthFrac = UserPrefs.CamWidthFrac;
        _camCxFrac    = UserPrefs.CamCenterXFrac;
        _camCyFrac    = UserPrefs.CamCenterYFrac;
        _sizeBar.Value = Math.Clamp((int)Math.Round(_camWidthFrac * 100), _sizeBar.Minimum, _sizeBar.Maximum);
        UpdateSizeLabel();
        _camBtn.Click += (_, _) => ToggleCamera();
        _teleBtn.Click += (_, _) => ToggleTeleprompter();
        _slideBtn.Click += (_, _) => ToggleSlideBackdrop();
        _importMediaBtn.Click += (_, _) => AddMediaFromFiles();
        _autoArrangeMediaBtn.Click += (_, _) => AutoArrangeMedia();
        _deleteSelectedBtn.Click += (_, _) => DeleteSelectedSlots();
        _recBtn.Click += (_, _) => ToggleRecording();
        _scriptFolderBtn.Click += (_, _) => Teleprompter.OpenFolder();
        _presetBox.SelectedIndexChanged += (_, _) => _camForm?.SetFilter(CameraFilter.GetPreset(_presetBox.SelectedIndex));
        _presetBox.SelectedIndexChanged += (_, _) => { UserPrefs.CamPresetIndex = _presetBox.SelectedIndex; UserPrefs.Save(); };
        _shapeBox.SelectedIndexChanged += (_, _) => _camForm?.SetShape((CamForm.Shape)_shapeBox.SelectedIndex);
        _shapeBox.SelectedIndexChanged += (_, _) => { UserPrefs.CamShapeIndex = _shapeBox.SelectedIndex; UserPrefs.Save(); };
        _mirror.CheckedChanged += (_, _) => { if (_camForm != null) _camForm.Mirror = _mirror.Checked; };
        _mirror.CheckedChanged += (_, _) => { UserPrefs.CamMirror = _mirror.Checked; UserPrefs.Save(); };
        _countdownCheck.CheckedChanged += (_, _) => { UserPrefs.CamCountdownEnabled = _countdownCheck.Checked; UserPrefs.Save(); };
        _showCursorCheck.CheckedChanged += (_, _) => { UserPrefs.CamShowCursor = _showCursorCheck.Checked; UserPrefs.Save(); };
        _hideWmCheck.CheckedChanged += (_, _) => { UserPrefs.CamHideWinWatermark = _hideWmCheck.Checked; UserPrefs.Save(); };
        _deviceBox.SelectedIndexChanged += (_, _) =>
        {
          if (_deviceBox.SelectedItem is string d && !d.StartsWith("(")) { UserPrefs.CamDeviceName = d; UserPrefs.Save(); }
        };
        _scriptBox.SelectedIndexChanged += (_, _) =>
        {
          if (_scriptBox.SelectedItem is string s && !s.StartsWith("(")) { UserPrefs.CamLastScript = s; UserPrefs.Save(); }
        };
        _scriptBox.SelectedIndexChanged += (_, _) => LoadTeleprompterSegment();
        _scriptBox.SelectedIndexChanged += (_, _) => OnReelChanged(_scriptBox.SelectedItem as string);
        _slideBox.SelectedIndexChanged += (_, _) => { if (_backdrop != null) LoadSlideIntoBackdrop(); };
        _slideBox.SelectedIndexChanged += (_, _) =>
        {
          if (_slideBox.SelectedItem is string s && !s.StartsWith("(")) { UserPrefs.CamLastSlide = s; UserPrefs.Save(); }
        };
        _slideBox.SelectedIndexChanged += (_, _) => LoadTeleprompterSegment();
        _sizeBar.Scroll += (_, _) => ApplyCamSizeFromSlider();
        _zOrderTimer.Tick += (_, _) => ReassertZOrder();
        _zOrderTimer.Start();
        _reloadDebounce.Tick += (_, _) => OnReloadDebounceTick();
        SetupScriptWatcher();
        SetupComposedWatcher();
        ResizeEnd += (_, _) =>
        {
            UserPrefs.CamPanelWidth = ClientSize.Width;
            UserPrefs.CamPanelHeight = ClientSize.Height;
            UserPrefs.Save();
        };
        FormClosed += (_, _) => CloseAll();
        OnReelChanged(_scriptBox.SelectedItem as string);
        UpdateSlidesWatcher();
        SyncCamSizeToFormat();
        SetStatus("Choisis ta caméra puis Ouvrir. Le téléprompteur n'apparaît pas dans l'enregistrement.");
    }
    private static void SelectByText(ComboBox box, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is string s && string.Equals(s, text, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
    }
    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12, 12, 12, 6),
            BackColor = Bg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control c)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) };
            c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 3, 0, 3);
            root.Controls.Add(l); root.Controls.Add(c);
        }
        Row("Caméra", _deviceBox);
        Row("Filtre", _presetBox);
        Row("Forme", _shapeBox);
        Row("", _mirror);
        var sizeRow = new Panel { Height = 24 };
        _sizeValueLabel.Dock = DockStyle.Right;
        sizeRow.Controls.Add(_sizeValueLabel);
        sizeRow.Controls.Add(_sizeBar);
        _sizeBar.Dock = DockStyle.Fill;
        Row("Taille", sizeRow);
        Row("", _camBtn);
        Row("Script", _scriptBox);
        Row("", _teleBtn);
        Row("", _scriptFolderBtn);
        Row("Diapo", _slideBox);
        Row("", _slideBtn);
        _mediaGallery.BackColor = Panel;
        _mediaGallery.Padding = new Padding(4);
        Row("Médias", _mediaGallery);
        Row("", _importMediaBtn);
        Row("", _autoArrangeMediaBtn);
        Row("", _deleteSelectedBtn);
        Row("", _countdownCheck);
        Row("", _showCursorCheck);
        Row("", _hideWmCheck);
        Row("", _recBtn);
        StyleButton(_camBtn); StyleButton(_teleBtn); StyleButton(_recBtn); StyleButton(_scriptFolderBtn);
        StyleButton(_slideBtn); StyleButton(_importMediaBtn); StyleButton(_autoArrangeMediaBtn); StyleButton(_deleteSelectedBtn);
        _status.BackColor = Panel; _status.ForeColor = Color.Gainsboro;
        _status.TextAlign = ContentAlignment.MiddleLeft; _status.Padding = new Padding(8, 0, 8, 0);
        Controls.Add(root);
        Controls.Add(_status);
    }
    private static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(48, 48, 56);
        b.ForeColor = Color.White; b.FlatAppearance.BorderSize = 0; b.Height = 28; b.Cursor = Cursors.Hand;
    }
    private void LoadDevices()
    {
        _deviceBox.Items.Clear();
        try
        {
            string[] cams = WebcamReader.ListDevices();
            if (cams.Length == 0) _deviceBox.Items.Add("(aucune caméra)");
            else _deviceBox.Items.AddRange(cams);
        }
        catch { _deviceBox.Items.Add("(énumération impossible)"); }
        _deviceBox.SelectedIndex = 0;
    }
    private void LoadScripts()
    {
        _scriptBox.Items.Clear();
        string[] names = Teleprompter.List();
        if (names.Length == 0) _scriptBox.Items.Add("(aucun script .txt)");
        else _scriptBox.Items.AddRange(names);
        _scriptBox.SelectedIndex = 0;
    }
    private void LoadSlides()
    {
        _slideBox.Items.Clear();
        string[] files = Array.Empty<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(_slidesDir) && Directory.Exists(_slidesDir))
                files = Directory.GetFiles(_slidesDir, "slide_*.png")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch {  }
        if (files.Length == 0) _slideBox.Items.Add("(aucune diapo)");
        else _slideBox.Items.AddRange(files.Select(Path.GetFileName).ToArray());
        _slideBox.SelectedIndex = 0;
        UpdateZoneAspectFromSlides(files);
    }
    private void UpdateZoneAspectFromSlides(string[] files)
    {
        double a = 9.0 / 16.0;
        try
        {
            if (files.Length > 0 && File.Exists(files[0]))
            {
                using var fs = new FileStream(files[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                using var img = System.Drawing.Image.FromStream(fs, false, false);
                if (img.Height > 0) a = (double)img.Width / img.Height;
            }
        }
        catch {  }
        _zoneAspect = a;
    }
    private static string? SlidesDirForScript(string? scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName) || scriptName.StartsWith("(")) return null;
        try
        {
            var reel = Path.GetFileNameWithoutExtension(scriptName);
            var root = Directory.GetParent(Teleprompter.ScriptsDir)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return null;
            return Path.Combine(root, "slides", reel);
        }
        catch { return null; }
    }
    private void OnReelChanged(string? scriptName)
    {
        var dir = SlidesDirForScript(scriptName);
        if (dir == null) return;
        if (string.Equals(dir, _slidesDir, StringComparison.OrdinalIgnoreCase)) return;
        _slidesDir = dir;
        LoadSlides();
        UpdateSlidesWatcher();
        SyncCamSizeToFormat();
        if (_backdrop != null) _backdrop.ShowOver(PortraitZone());
        UpdateCamBounds();
        foreach (var s in _mediaSlots.ToList()) HideMediaSlot(s);
        _selectedSlots.Clear();
        _mediaSlots.Clear();
        RestoreMediaSlots();
        RefreshGalleryUi();
        SetStatus("Reel : " + Path.GetFileName(dir));
    }
    private Rectangle PortraitZone()
    {
        Rectangle scr = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int h = (int)(scr.Height * 0.82);
        int w = (int)Math.Round(h * _zoneAspect);
        int maxW = (int)(scr.Width * 0.92);
        if (w > maxW) { w = maxW; h = (int)Math.Round(w / _zoneAspect); }
        int x = scr.X + (scr.Width - w) / 2;
        int y = scr.Y + (scr.Height - h) / 2;
        return new Rectangle(x, y, w, h);
    }
    private Point PanelLocation(int panelH)
    {
        Rectangle scr  = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Rectangle zone = PortraitZone();
        const int panelW = 370, margin = 20, chrome = 40;
        int x = zone.X - panelW - margin;
        if (x < scr.X)
        {
            x = zone.Right + margin;
            if (x + panelW > scr.Right) x = Math.Max(scr.X, scr.Right - panelW);
        }
        int y = Math.Max(scr.Y, zone.Y);
        y = Math.Min(y, Math.Max(scr.Y, scr.Bottom - panelH - chrome));
        return new Point(x, y);
    }
    private void ToggleCamera()
    {
        if (_camForm == null)
        {
            if (_deviceBox.SelectedItem is not string dev || dev.StartsWith("("))
            {
                SetStatus("Aucune caméra détectée.");
                return;
            }
            var reader = new WebcamReader();
            if (!reader.Start(Math.Max(0, _deviceBox.SelectedIndex)))
            {
                SetStatus("Caméra indisponible : " + (reader.LastError ?? "aucune caméra"));
                reader.Stop();
                return;
            }
            _cam = reader;
            var filter = CameraFilter.GetPreset(_presetBox.SelectedIndex);
            _camForm = new CamForm(reader, (CamForm.Shape)Math.Max(0, _shapeBox.SelectedIndex), _mirror.Checked, filter);
            _camForm.CloseRequested += (_, _) => BeginInvoke(new Action(StopCamera));
            _camForm.GeometryChanged += (_, _) => OnCamGeometryChanged();
            if (ComposedActive) _camForm.AllowedBounds = PortraitZone();
            _camForm.Show();
            _camForm.SetFilter(filter);
            _camForm.ApplyGeometry(PortraitZone(), _camWidthFrac, _camCxFrac, _camCyFrac);
            ReassertZOrder();
            _camBtn.Text = "Caméra : fermer";
            SetStatus("Caméra ouverte. Glisser/redimensionner le PiP. Clic droit ferme.");
        }
        else StopCamera();
    }
    private void StopCamera()
    {
        if (_video.Recording) ToggleRecording();
        if (_camForm != null)
        {
            CamForm f = _camForm; _camForm = null;
            f.Close(); f.Dispose();
        }
        if (_cam != null) { _cam.Stop(); _cam = null; }
        ReassertZOrder();
        _camBtn.Text = "Caméra : ouvrir";
        SetStatus("Caméra fermée.");
    }
    private void ToggleSlideBackdrop()
    {
        if (_backdrop == null)
        {
            if (_slideBox.SelectedItem is not string name || name.StartsWith("("))
            {
                SetStatus("Aucune diapo trouvée pour ce projet.");
                return;
            }
            _backdrop = new BackdropForm();
            _backdrop.CloseRequested += (_, _) => BeginInvoke(new Action(() => { if (_backdrop != null) ToggleSlideBackdrop(); }));
            _backdrop.ShowOver(PortraitZone());
            LoadSlideIntoBackdrop();
            UpdateCamBounds();
            ReassertZOrder();
            _slideBtn.Text = "Fond diapo : masquer";
            SetStatus("Fond diapo affiché (clic droit dessus pour le refermer). L'enregistrement capturera diapo + cam ensemble.");
        }
        else
        {
            var b = _backdrop; _backdrop = null;
            b.Close(); b.Dispose();
            UpdateCamBounds();
            ReassertZOrder();
            _slideBtn.Text = "Fond diapo : afficher";
            SetStatus("Fond diapo masqué.");
        }
    }
    private void LoadSlideIntoBackdrop()
    {
        if (_backdrop == null || string.IsNullOrWhiteSpace(_slidesDir)) return;
        if (_slideBox.SelectedItem is not string name || name.StartsWith("(")) return;
        try
        {
            using var src = new Bitmap(Path.Combine(_slidesDir, name));
            _backdrop.SetSlide(new Bitmap(src));
        }
        catch (Exception ex) { SetStatus("Diapo illisible : " + ex.Message); }
    }
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    private static readonly string[] VideoExtensions  = { ".mp4", ".mov", ".mkv", ".avi" };
    private static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    private void RestoreMediaSlots()
    {
        var known = new List<MediaSlotDto>();
        try
        {
            var lp = MediaLayersPath();
            if (lp != null && File.Exists(lp))
            {
                var list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<MediaSlotDto>>(File.ReadAllText(lp));
                if (list != null) known = list;
            }
        }
        catch {  }
        foreach (var dto in known)
        {
            if (!File.Exists(dto.Path)) continue;
            var shape = dto.Shape == (int)CamForm.Shape.Rounded ? CamForm.Shape.Rectangle : (CamForm.Shape)dto.Shape;
            var widthFrac = dto.WidthFrac <= 0 || Math.Abs(dto.WidthFrac - 0.4) < 0.001 ? 0.92 : dto.WidthFrac;
            _mediaSlots.Add(new MediaSlot
            {
                Path = dto.Path, SourcePath = dto.SourcePath, Shape = shape,
                BorderColor = string.IsNullOrWhiteSpace(dto.BorderColor) ? "#FFFFFF" : dto.BorderColor,
                BorderPx = dto.BorderPx, WidthFrac = widthFrac,
                CxFrac = dto.CxFrac > 0 ? dto.CxFrac : 0.5, CyFrac = dto.CyFrac > 0 ? dto.CyFrac : 0.5,
                Aspect = dto.Aspect, PanX = dto.PanX, PanY = dto.PanY,
            });
        }
        try
        {
            var mediaDir = AssetImporter.MediaDir(_slidesDir);
            var already = new HashSet<string>(_mediaSlots.Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(mediaDir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!ImageExtensions.Contains(ext) && !VideoExtensions.Contains(ext)) continue;
                if (already.Contains(file)) continue;
                _mediaSlots.Add(new MediaSlot { Path = file, Aspect = DefaultMediaAspect });
            }
        }
        catch {  }
    }
    private void PersistMediaSlots()
    {
        var dtos = _mediaSlots.Select(s => new MediaSlotDto
        {
            Path = s.Path, SourcePath = s.SourcePath, Shape = (int)s.Shape,
            BorderColor = s.BorderColor, BorderPx = s.BorderPx,
            WidthFrac = s.WidthFrac, CxFrac = s.CxFrac, CyFrac = s.CyFrac,
            Aspect = s.Aspect, PanX = s.PanX, PanY = s.PanY,
        }).ToList();
        try
        {
            var lp = MediaLayersPath();
            if (lp != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lp)!);
                File.WriteAllText(lp, Newtonsoft.Json.JsonConvert.SerializeObject(dtos));
            }
        }
        catch {  }
    }
    private string? MediaLayersPath()
    {
        try { return Path.Combine(AssetImporter.MediaDir(_slidesDir), "_camlayers.json"); }
        catch { return null; }
    }
    private void AddMediaFromFiles()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Importer un ou plusieurs médias",
            Filter = "Média (vidéo/image)|*.mp4;*.mov;*.mkv;*.avi;*.jpg;*.jpeg;*.png;*.bmp;*.gif|" +
                     "Vidéo|*.mp4;*.mov;*.mkv;*.avi|Image|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Tous|*.*",
            Multiselect = true,
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        int added = 0;
        foreach (var file in ofd.FileNames)
        {
            var destPath = AssetImporter.CopyIntoProjectMedia(_slidesDir, file);
            if (_mediaSlots.Any(s => string.Equals(s.Path, destPath, StringComparison.OrdinalIgnoreCase))) continue;
            _mediaSlots.Add(new MediaSlot { Path = destPath, SourcePath = file, Aspect = DefaultMediaAspect });
            added++;
        }
        RefreshGalleryUi();
        PersistMediaSlots();
        SetStatus(added > 0 ? $"{added} média(s) ajouté(s) à la galerie." : "Déjà dans la galerie.");
    }
    private void RemoveMediaSlot(MediaSlot slot)
    {
        HideMediaSlot(slot);
        _mediaSlots.Remove(slot);
        RefreshGalleryUi();
        PersistMediaSlots();
    }
    private void ShowMediaSlot(MediaSlot slot)
    {
        if (slot.IsShown) return;
        if (!File.Exists(slot.Path)) { SetStatus("Fichier introuvable : " + slot.Path); return; }
        IFrameSource source;
        if (IsImageFile(slot.Path))
        {
            try { slot.ImageBitmap = LoadImageDownscaled(slot.Path); }
            catch (Exception ex) { SetStatus("Image illisible : " + ex.Message); return; }
            source = new ImageFrameSource(slot.ImageBitmap);
        }
        else
        {
            var reader = new VideoFileReader();
            if (!reader.Start(slot.Path))
            {
                SetStatus("Clip illisible : " + (reader.LastError ?? "format non supporté"));
                reader.Stop();
                return;
            }
            slot.VideoReader = reader;
            source = reader;
        }
        slot.Form = new CamForm(source, slot.Shape, mirror: false, CameraFilter.GetPreset(CameraFilter.Preset.Aucun));
        slot.Form.SetBorder(HexToColor(slot.BorderColor), slot.BorderPx);
        slot.Form.SetAspectOverride(slot.Aspect, PortraitZone());
        slot.Form.SetPan(slot.PanX, slot.PanY);
        slot.Form.CloseRequested += (_, _) => BeginInvoke(new Action(() => HideMediaSlot(slot)));
        slot.Form.GeometryChanged += (_, _) => OnMediaSlotGeometryChanged(slot);
        slot.Form.PanChanged += (_, _) => { slot.PanX = slot.Form.PanX; slot.PanY = slot.Form.PanY; PersistMediaSlots(); };
        slot.Form.AllowedBounds = PortraitZone();
        slot.Form.Show();
        slot.Form.ApplyGeometry(PortraitZone(), slot.WidthFrac, slot.CxFrac, slot.CyFrac);
        UpdateCamBounds();
        ReassertZOrder();
        RefreshTileVisualState(slot);
        SetStatus("Média affiché : " + Path.GetFileName(slot.Path));
    }
    private static Bitmap LoadImageDownscaled(string path, int maxDim = 1600)
    {
        using var src = new Bitmap(path);
        if (src.Width <= maxDim && src.Height <= maxDim) return new Bitmap(src);
        double scale = Math.Min((double)maxDim / src.Width, (double)maxDim / src.Height);
        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
        int h = Math.Max(1, (int)Math.Round(src.Height * scale));
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        return dst;
    }
    private void HideMediaSlot(MediaSlot slot)
    {
        if (!slot.IsShown) return;
        var f = slot.Form!; slot.Form = null; f.Close(); f.Dispose();
        if (slot.VideoReader != null) { slot.VideoReader.Stop(); slot.VideoReader = null; }
        if (slot.ImageBitmap != null) { slot.ImageBitmap.Dispose(); slot.ImageBitmap = null; }
        UpdateCamBounds();
        ReassertZOrder();
        RefreshTileVisualState(slot);
        SetStatus("Média masqué.");
    }
    private void ToggleMediaSlot(MediaSlot slot)
    {
        if (slot.IsShown) HideMediaSlot(slot); else ShowMediaSlot(slot);
    }
    private void OnMediaSlotGeometryChanged(MediaSlot slot)
    {
        if (slot.Form == null) return;
        var zone = PortraitZone();
        var b = slot.Form.DesktopBounds;
        slot.WidthFrac = Math.Clamp((double)b.Width / zone.Width, 0.05, 1.0);
        slot.CxFrac    = Math.Clamp((b.X + b.Width  / 2.0 - zone.X) / zone.Width,  0.0, 1.0);
        slot.CyFrac    = Math.Clamp((b.Y + b.Height / 2.0 - zone.Y) / zone.Height, 0.0, 1.0);
        PersistMediaSlots();
    }
    private void SetSlotShape(MediaSlot slot, CamForm.Shape shape)
    {
        slot.Shape = shape;
        slot.Form?.SetShape(shape);
        PersistMediaSlots();
    }
    private void SetSlotBorder(MediaSlot slot, Color color, int px)
    {
        slot.BorderColor = ColorTranslator.ToHtml(color);
        slot.BorderPx = px;
        slot.Form?.SetBorder(color, px);
        PersistMediaSlots();
    }
    private void SetSlotAspect(MediaSlot slot, double? aspect)
    {
        slot.Aspect = aspect;
        slot.Form?.SetAspectOverride(aspect, PortraitZone());
        if (slot.Form != null) OnMediaSlotGeometryChanged(slot);
        else PersistMediaSlots();
    }
    private static Color HexToColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return Color.White; }
    }
    private void AutoArrangeMedia()
    {
        var shown = _mediaSlots.Where(s => s.IsShown).ToList();
        if (shown.Count < 1) { SetStatus("Aucun média affiché à arranger."); return; }
        var zone = PortraitZone();
        var cells = MosaicLayout.GetCells(shown.Count, zone.Width, zone.Height, gap: 12);
        for (int i = 0; i < shown.Count && i < cells.Count; i++)
        {
            var c = cells[i];
            var slot = shown[i];
            slot.WidthFrac = Math.Clamp((double)c.Width / zone.Width, 0.05, 1.0);
            slot.CxFrac    = (c.X + c.Width  / 2.0) / zone.Width;
            slot.CyFrac    = (c.Y + c.Height / 2.0) / zone.Height;
            slot.Form!.ApplyGeometry(zone, slot.WidthFrac, slot.CxFrac, slot.CyFrac);
        }
        ReassertZOrder();
        PersistMediaSlots();
        SetStatus(shown.Count > 6
            ? "Arrangé (les 6 premiers médias affichés ; MosaicLayout plafonne à 6, comme la mosaïque de l'éditeur)."
            : "Médias arrangés.");
    }
    private void RefreshGalleryUi()
    {
        _mediaGallery.SuspendLayout();
        foreach (Control c in _mediaGallery.Controls)
            foreach (var pic in c.Controls.OfType<PictureBox>()) pic.Image?.Dispose();
        _mediaGallery.Controls.Clear();
        foreach (var slot in _mediaSlots) _mediaGallery.Controls.Add(BuildGalleryTile(slot));
        _mediaGallery.ResumeLayout();
    }
    private Panel BuildGalleryTile(MediaSlot slot)
    {
        var host = new Panel { Width = 64, Height = 80, Margin = new Padding(3), Cursor = Cursors.Hand, BackColor = Panel };
        var pic = new PictureBox
        {
            Width = 58, Height = 54, Left = 3, Top = 3, SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(20, 20, 24), Cursor = Cursors.Hand,
        };
        var name = new Label
        {
            Text = Path.GetFileNameWithoutExtension(slot.Path), AutoEllipsis = true,
            Width = 60, Height = 18, Left = 2, Top = 60, Font = new Font("Segoe UI", 7f),
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Gainsboro,
        };
        slot.Tile = host;
        host.Controls.Add(pic);
        host.Controls.Add(name);
        pic.Click += (_, _) => TileClicked(slot);
        host.Click += (_, _) => TileClicked(slot);
        var menu = BuildTileContextMenu(slot);
        pic.ContextMenuStrip = menu;
        host.ContextMenuStrip = menu;
        RefreshTileVisualState(slot);
        LoadThumbnail(slot);
        return host;
    }
    private void RefreshTileVisualState(MediaSlot slot)
    {
        if (slot.Tile == null) return;
        if (_selectedSlots.Contains(slot)) slot.Tile.BackColor = Color.FromArgb(150, 110, 25);
        else slot.Tile.BackColor = slot.IsShown ? TileActive : Panel;
    }
    private void TileClicked(MediaSlot slot)
    {
        if ((Control.ModifierKeys & Keys.Control) != 0)
        {
            if (!_selectedSlots.Remove(slot)) _selectedSlots.Add(slot);
            RefreshTileVisualState(slot);
            UpdateDeleteBtn();
        }
        else ToggleMediaSlot(slot);
    }
    private void UpdateDeleteBtn()
    {
        _deleteSelectedBtn.Text = _selectedSlots.Count > 0
            ? $"🗑 Supprimer la sélection ({_selectedSlots.Count})"
            : "🗑 Supprimer la sélection";
    }
    private void DeleteSelectedSlots()
    {
        if (_selectedSlots.Count == 0)
        {
            SetStatus("Ctrl+clic sur des vignettes pour les sélectionner, puis Supprimer.");
            return;
        }
        if (MessageBox.Show(this,
                $"Supprimer définitivement {_selectedSlots.Count} média(s) du dossier de ce reel ?",
                "Supprimer la sélection", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        foreach (var s in _selectedSlots.ToList())
        {
            HideMediaSlot(s);
            _mediaSlots.Remove(s);
            try { if (File.Exists(s.Path)) File.Delete(s.Path); } catch { }
        }
        _selectedSlots.Clear();
        RefreshGalleryUi();
        PersistMediaSlots();
        UpdateDeleteBtn();
        SetStatus("Média(s) supprimé(s).");
    }
    private ContextMenuStrip BuildTileContextMenu(MediaSlot slot)
    {
        var menu = new ContextMenuStrip();
        var shapeMenu = new ToolStripMenuItem("Forme");
        shapeMenu.DropDownItems.Add("Rectangle", null, (_, _) => SetSlotShape(slot, CamForm.Shape.Rectangle));
        shapeMenu.DropDownItems.Add("Arrondi", null, (_, _) => SetSlotShape(slot, CamForm.Shape.Rounded));
        shapeMenu.DropDownItems.Add("Cercle", null, (_, _) => SetSlotShape(slot, CamForm.Shape.Circle));
        menu.Items.Add(shapeMenu);
        var aspectMenu = new ToolStripMenuItem("Format d'affichage");
        aspectMenu.DropDownItems.Add("Original (pas de recadrage)", null, (_, _) => SetSlotAspect(slot, null));
        aspectMenu.DropDownItems.Add("Paysage 16:9", null, (_, _) => SetSlotAspect(slot, 16.0 / 9.0));
        aspectMenu.DropDownItems.Add("Portrait 4:5", null, (_, _) => SetSlotAspect(slot, 4.0 / 5.0));
        aspectMenu.DropDownItems.Add("Portrait 9:16", null, (_, _) => SetSlotAspect(slot, 9.0 / 16.0));
        aspectMenu.DropDownItems.Add("Carré 1:1", null, (_, _) => SetSlotAspect(slot, 1.0));
        menu.Items.Add(aspectMenu);
        menu.Items.Add("Couleur de bordure…", null, (_, _) =>
        {
            using var cd = new ColorDialog { Color = HexToColor(slot.BorderColor) };
            if (cd.ShowDialog() == DialogResult.OK) SetSlotBorder(slot, cd.Color, slot.BorderPx);
        });
        var borderMenu = new ToolStripMenuItem("Épaisseur de bordure");
        foreach (int px in new[] { 0, 3, 6, 10 })
            borderMenu.DropDownItems.Add(px == 0 ? "Aucune" : $"{px}px", null,
                (_, _) => SetSlotBorder(slot, HexToColor(slot.BorderColor), px));
        menu.Items.Add(borderMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Voir la source", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(slot.SourcePath ?? slot.Path) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("Ouverture impossible : " + ex.Message); }
        });
        menu.Items.Add("Afficher dans l'explorateur", null, (_, _) =>
        {
            try { Process.Start("explorer.exe", $"/select,\"{slot.Path}\""); }
            catch (Exception ex) { SetStatus("Explorateur : " + ex.Message); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Retirer de la galerie", null, (_, _) => RemoveMediaSlot(slot));
        return menu;
    }
    private void LoadThumbnail(MediaSlot slot)
    {
        if (slot.Tile == null) return;
        var pic = slot.Tile.Controls.OfType<PictureBox>().FirstOrDefault();
        if (pic == null) return;
        if (IsImageFile(slot.Path))
        {
            try
            {
                using var fs = File.OpenRead(slot.Path);
                using var img = Image.FromStream(fs);
                pic.Image = new Bitmap(img, pic.Width, pic.Height);
            }
            catch {  }
        }
        else
        {
            var ffmpegDir = Directory.Exists(@"C:\ffmpeg\bin") ? @"C:\ffmpeg\bin" : null;
            const double ts = 0.2;
            var expected = $"snap_{Path.GetFileNameWithoutExtension(slot.Path)}_{ts:F1}.png";
            _pendingThumbs[expected] = slot;
            _thumbs.RequestSnapshot(slot.Path, ts, ffmpegDir);
        }
    }
    private void OnThumbnailReady(string pngPath)
    {
        var name = Path.GetFileName(pngPath);
        if (!_pendingThumbs.TryGetValue(name, out var slot) || slot.Tile == null) return;
        BeginInvoke(new Action(() =>
        {
            var pic = slot.Tile?.Controls.OfType<PictureBox>().FirstOrDefault();
            if (pic == null) return;
            try
            {
                using var fs = File.OpenRead(pngPath);
                using var img = Image.FromStream(fs);
                pic.Image = new Bitmap(img, pic.Width, pic.Height);
            }
            catch {  }
        }));
    }
    private void ToggleTeleprompter()
    {
        if (_teleForm == null)
        {
            if (_scriptBox.SelectedItem is not string name || name.StartsWith("("))
            {
                SetStatus("Aucun script : clique 'Dossier scripts' et écris un .txt.");
                return;
            }
            _teleForm = new TeleprompterForm();
            _teleForm.ApplySettings((float)UserPrefs.TelePromptSpeed, (float)UserPrefs.TelePromptFont,
                                    (float)UserPrefs.TelePromptAnchor);
            _teleForm.SettingsChanged += SaveTelePrefs;
            _teleForm.CloseRequested += (_, _) => CloseTeleprompter();
            _teleForm.FormClosed += (_, _) => { _teleForm = null; _teleBtn.Text = "Téléprompteur : ouvrir"; };
            _teleForm.Show();
            _teleForm.Opacity = Math.Clamp(UserPrefs.TelePromptOpacity, 0.25, 1.0);
            var savedBounds = TelePromptSavedBounds();
            if (savedBounds.HasValue) _teleForm.RestoreWindowBounds(savedBounds.Value);
            else _teleForm.PlaceNearTop(PortraitZone());
            LoadTeleprompterSegment();
            ReassertZOrder();
            _teleBtn.Text = "Téléprompteur : fermer";
            SetStatus("Téléprompteur ouvert (exclu de l'enregistrement).");
        }
        else CloseTeleprompter();
    }
    private int CurrentSlideIndex()
    {
        if (_slideBox.SelectedItem is not string name || name.StartsWith("(")) return -1;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+)");
        return m.Success && int.TryParse(m.Value, out int n) ? n - 1 : -1;
    }
    private void LoadTeleprompterSegment()
    {
        if (_teleForm == null) return;
        if (_scriptBox.SelectedItem is not string name || name.StartsWith("(")) return;
        var raw = Teleprompter.Read(name);
        var segments = Teleprompter.SplitSegments(raw);
        int idx = CurrentSlideIndex();
        _teleForm.SetScript(idx >= 0 && idx < segments.Count ? segments[idx] : raw);
    }
    private void CloseTeleprompter()
    {
        if (_teleForm != null) { SaveTelePrefs(); var t = _teleForm; _teleForm = null; t.Close(); t.Dispose(); }
        _teleBtn.Text = "Téléprompteur : ouvrir";
    }
    private void SaveTelePrefs()
    {
        if (_teleForm == null) return;
        UserPrefs.TelePromptSpeed   = _teleForm.SpeedValue;
        UserPrefs.TelePromptFont    = _teleForm.FontValue;
        UserPrefs.TelePromptAnchor  = _teleForm.AnchorValue;
        UserPrefs.TelePromptOpacity = _teleForm.Opacity;
        var b = _teleForm.DesktopBounds;
        if (b.Width > 0 && b.Height > 0)
        {
            UserPrefs.TelePromptX = b.X; UserPrefs.TelePromptY = b.Y;
            UserPrefs.TelePromptW = b.Width; UserPrefs.TelePromptH = b.Height;
        }
        UserPrefs.Save();
    }
    private static Rectangle? TelePromptSavedBounds()
    {
        if (UserPrefs.TelePromptW <= 0 || UserPrefs.TelePromptH <= 0) return null;
        var r = new Rectangle(UserPrefs.TelePromptX, UserPrefs.TelePromptY,
                              UserPrefs.TelePromptW, UserPrefs.TelePromptH);
        foreach (var scr in Screen.AllScreens)
            if (scr.Bounds.IntersectsWith(r)) return r;
        return null;
    }
    private void SetupScriptWatcher()
    {
        try
        {
            var dir = Teleprompter.ScriptsDir;
            if (!Directory.Exists(dir)) return;
            _scriptWatcher = new FileSystemWatcher(dir, "*.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler chg = (_, e) => QueueScriptReload(e.Name);
            _scriptWatcher.Changed += chg;
            _scriptWatcher.Created += chg;
            _scriptWatcher.Renamed += (_, e) => QueueScriptReload(e.Name);
        }
        catch (Exception ex) { Logger.Warn($"Script watcher: {ex.Message}"); }
    }
    private void UpdateSlidesWatcher()
    {
        try
        {
            _slidesWatcher?.Dispose();
            _slidesWatcher = null;
            if (string.IsNullOrWhiteSpace(_slidesDir) || !Directory.Exists(_slidesDir)) return;
            _slidesWatcher = new FileSystemWatcher(_slidesDir, "slide_*.png")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler chg = (_, _) => QueueSlidesReload();
            _slidesWatcher.Changed += chg;
            _slidesWatcher.Created += chg;
            _slidesWatcher.Deleted += chg;
            _slidesWatcher.Renamed += (_, _) => QueueSlidesReload();
        }
        catch (Exception ex) { Logger.Warn($"Slides watcher: {ex.Message}"); }
    }
    private void SetupComposedWatcher()
    {
        try
        {
            var dir = OutputDir(composed: true);
            _composedWatcher = new FileSystemWatcher(dir, "*.mp4")
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
            };
            _composedWatcher.Renamed += (_, e) => TryMoveSidecar(e.OldFullPath, e.FullPath);
            _composedWatcher.Deleted += (_, e) => OnComposedClipRemoved(e.FullPath);
        }
        catch (Exception ex) { Logger.Warn($"Composed watcher: {ex.Message}"); }
    }
    private static void TryMoveSidecar(string oldMp4, string newMp4)
    {
        try
        {
            var oldSide = CamGeoSidecar.PathFor(oldMp4);
            if (!File.Exists(oldSide)) return;
            var newSide = CamGeoSidecar.PathFor(newMp4);
            File.Move(oldSide, newSide, overwrite: true);
            Logger.Info($"Sidecar camgeo suit le renommage : {Path.GetFileName(oldMp4)} -> {Path.GetFileName(newMp4)}");
            NotifyShellRename(oldSide, newSide);
        }
        catch (Exception ex) { Logger.Warn("Renommage sidecar camgeo : " + ex.Message); }
    }
    private static void OnComposedClipRemoved(string oldMp4)
    {
        try
        {
            var oldSide = CamGeoSidecar.PathFor(oldMp4);
            if (!File.Exists(oldSide)) return;
            System.Threading.Thread.Sleep(60);
            var root = OutputDir(composed: true);
            var name = Path.GetFileName(oldMp4);
            string? movedTo = null;
            foreach (var f in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories))
            {
                if (!string.Equals(f, oldMp4, StringComparison.OrdinalIgnoreCase)) { movedTo = f; break; }
            }
            if (movedTo != null)
            {
                var newSide = CamGeoSidecar.PathFor(movedTo);
                File.Move(oldSide, newSide, overwrite: true);
                Logger.Info($"Sidecar camgeo suit le déplacement de la prise : {name}");
                NotifyShellRename(oldSide, newSide);
            }
            else
            {
                File.Delete(oldSide);
                Logger.Info("Sidecar camgeo supprimé avec la prise : " + Path.GetFileName(oldSide));
                NotifyShellDelete(oldSide);
            }
        }
        catch (Exception ex) { Logger.Warn("Sync sidecar (suppression/déplacement) : " + ex.Message); }
    }
    private const int SHCNE_RENAMEITEM = 0x00000001;
    private const int SHCNE_DELETE = 0x00000004;
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const uint SHCNF_PATHW = 0x0005;
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string dwItem1,
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string? dwItem2);
    private static void NotifyShellRename(string oldPath, string newPath)
    {
        try
        {
            SHChangeNotify(SHCNE_RENAMEITEM, SHCNF_PATHW, oldPath, newPath);
            var newDir = Path.GetDirectoryName(newPath);
            if (newDir != null) SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, newDir, null);
            var oldDir = Path.GetDirectoryName(oldPath);
            if (oldDir != null && !string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase))
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, oldDir, null);
        }
        catch (Exception ex) { Logger.Warn("SHChangeNotify (rename) : " + ex.Message); }
    }
    private static void NotifyShellDelete(string path)
    {
        try
        {
            SHChangeNotify(SHCNE_DELETE, SHCNF_PATHW, path, null);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, dir, null);
        }
        catch (Exception ex) { Logger.Warn("SHChangeNotify (delete) : " + ex.Message); }
    }
    private void QueueScriptReload(string? changedName)
    {
        if (_scriptBox.SelectedItem is not string sel) return;
        if (!string.IsNullOrEmpty(changedName) &&
            !string.Equals(Path.GetFileName(changedName), sel, StringComparison.OrdinalIgnoreCase))
            return;
        MarshalReload(scriptChanged: true);
    }
    private void QueueSlidesReload() => MarshalReload(scriptChanged: false);
    private void MarshalReload(bool scriptChanged)
    {
        try
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (scriptChanged) _reloadScriptPending = true; else _reloadSlidesPending = true;
                _reloadDebounce.Stop();
                _reloadDebounce.Start();
            }));
        }
        catch {  }
    }
    private void OnReloadDebounceTick()
    {
        _reloadDebounce.Stop();
        if (_reloadScriptPending)
        {
            _reloadScriptPending = false;
            LoadTeleprompterSegment();
            SyncScriptToJson();
            SetStatus("Script rechargé à chaud.");
        }
        if (_reloadSlidesPending)
        {
            _reloadSlidesPending = false;
            ReloadSlidesLive();
            SetStatus("Diapos rechargées à chaud.");
        }
    }
    private void ReloadSlidesLive()
    {
        var sel = _slideBox.SelectedItem as string;
        LoadSlides();
        if (sel != null) SelectByText(_slideBox, sel);
        if (_backdrop != null) { try { LoadSlideIntoBackdrop(); } catch {  } }
    }
    private void SyncScriptToJson()
    {
        try
        {
            if (_scriptBox.SelectedItem is not string sel || sel.StartsWith("(")) return;
            var reel = Path.GetFileNameWithoutExtension(sel);
            var root = Directory.GetParent(Teleprompter.ScriptsDir)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return;
            var jsonPath = Path.Combine(root, "json", reel + ".json");
            if (!File.Exists(jsonPath)) return;
            var spoken = SpokenBlocks(Teleprompter.Read(sel));
            if (spoken.Count == 0) return;
            var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(jsonPath));
            if (jo["scenes"] is not Newtonsoft.Json.Linq.JArray scenes || scenes.Count == 0) return;
            if (scenes[0]?["sequences"] is not Newtonsoft.Json.Linq.JArray seqs) return;
            if (seqs.Count != spoken.Count) return;
            bool changed = false;
            for (int i = 0; i < seqs.Count; i++)
                if ((string?)seqs[i]?["text"] != spoken[i]) { seqs[i]!["text"] = spoken[i]; changed = true; }
            if (!changed) return;
            File.WriteAllText(jsonPath, jo.ToString(Newtonsoft.Json.Formatting.Indented));
            Logger.Info($"Sync script->JSON : {reel}.json ({seqs.Count} séquences).");
        }
        catch (Exception ex) { Logger.Warn($"Sync script->JSON: {ex.Message}"); }
    }
    private static List<string> SpokenBlocks(string raw)
    {
        var result = new List<string>();
        foreach (var block in Teleprompter.SplitSegments(raw))
        {
            var lines = block.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("[") && !l.StartsWith("#") &&
                            !l.StartsWith(">") && !l.StartsWith("(") && !l.StartsWith("---") &&
                            !l.StartsWith("**"));
            var txt = string.Join(" ", lines).Trim();
            if (txt.Length > 0) result.Add(txt);
        }
        return result;
    }
    private bool ComposedActive => _backdrop != null || _mediaSlots.Any(s => s.IsShown);
    private void UpdateCamBounds()
    {
        if (_camForm == null) return;
        _camForm.AllowedBounds = ComposedActive ? PortraitZone() : null;
        if (ComposedActive) _camForm.ApplyGeometry(PortraitZone(), _camWidthFrac, _camCxFrac, _camCyFrac);
    }
    private void ReassertZOrder()
    {
        foreach (var slot in _mediaSlots)
            if (slot.IsShown) slot.Form!.BringToFrontTopmost();
        _camForm?.BringToFrontTopmost();
        _teleForm?.BringToTop();
        _countdownForm?.BringToTop();
    }
    private void OnCamGeometryChanged()
    {
        if (_camForm == null) return;
        var zone = PortraitZone();
        var b = _camForm.DesktopBounds;
        _camWidthFrac = Math.Clamp((double)b.Width / zone.Width, 0.05, 1.0);
        _camCxFrac    = Math.Clamp((b.X + b.Width  / 2.0 - zone.X) / zone.Width,  0.0, 1.0);
        _camCyFrac    = Math.Clamp((b.Y + b.Height / 2.0 - zone.Y) / zone.Height, 0.0, 1.0);
        _sizeBar.Value = Math.Clamp((int)Math.Round(_camWidthFrac * 100), _sizeBar.Minimum, _sizeBar.Maximum);
        UpdateSizeLabel();
        PersistCamPrefs();
    }
    private void ApplyCamSizeFromSlider()
    {
        _camWidthFrac = _sizeBar.Value / 100.0;
        UpdateSizeLabel();
        _camForm?.ApplyGeometry(PortraitZone(), _camWidthFrac, _camCxFrac, _camCyFrac);
        PersistCamPrefs();
    }
    private void UpdateSizeLabel() => _sizeValueLabel.Text = $"{_sizeBar.Value}%";
    private void PersistCamPrefs()
    {
        if (_zoneAspect >= 1.0) UserPrefs.CamWidthFracLandscape = _camWidthFrac;
        else                    UserPrefs.CamWidthFrac          = _camWidthFrac;
        UserPrefs.CamCenterXFrac = _camCxFrac;
        UserPrefs.CamCenterYFrac = _camCyFrac;
        UserPrefs.Save();
    }
    private bool? _camSizeFormat;
    private void SyncCamSizeToFormat()
    {
        bool landscape = _zoneAspect >= 1.0;
        if (_camSizeFormat == landscape) return;
        _camSizeFormat = landscape;
        _camWidthFrac = landscape ? UserPrefs.CamWidthFracLandscape : UserPrefs.CamWidthFrac;
        _sizeBar.Value = Math.Clamp((int)Math.Round(_camWidthFrac * 100), _sizeBar.Minimum, _sizeBar.Maximum);
        UpdateCamBounds();
    }
    private void ToggleRecording()
    {
        if (_video.Recording) { StopRecording(); return; }
        if (_countingDown) return;
        if (_camForm == null) { SetStatus("Ouvre d'abord la caméra."); return; }
        if (_countdownCheck.Checked)
        {
            _countingDown = true;
            _recBtn.Text = "Enregistrement : 3..2..1";
            _recBtn.ForeColor = Color.FromArgb(255, 180, 90);
            StartAudioPrewarm();
            Rectangle countdownZone = ComposedActive ? PortraitZone() : _camForm.DesktopBounds;
            var cd = new CountdownForm(countdownZone, 3, () =>
            {
                _countingDown = false;
                _countdownForm = null;
                _recBtn.ForeColor = Color.White;
                StartRecordingNow();
            });
            _countdownForm = cd;
            cd.Show();
            cd.BringToTop();
        }
        else StartRecordingNow();
    }
    private AudioCapture? _primedAudio;
    private bool _lastRecordComposed;
    private void StartAudioPrewarm()
    {
        var opts = AudioOptions.Default;
        opts.System = false;
        var audio = new AudioCapture();
        var t = new Thread(() =>
        {
            Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
            try { audio.Prime(opts); }
            finally { Wasapi.CoUninitialize(); }
        })
        { IsBackground = true, Name = "RoleplayOverlay.AudioPrime" };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        _primedAudio = audio;
    }
    private void StartRecordingNow()
    {
        var prewarmed = _primedAudio;
        _primedAudio = null;
        if (_camForm == null)
        {
            SetStatus("Ouvre d'abord la caméra.");
            prewarmed?.Stop();
            return;
        }
        bool composed = ComposedActive;
        _lastRecordComposed = composed;
        Rectangle zone = composed ? PortraitZone() : _camForm.DesktopBounds;
        string dir = OutputDir(composed);
        string prefix = composed ? "reel_" : "facecam_";
        string path = Path.Combine(dir, prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
        try
        {
            var audio = AudioOptions.Default;
            audio.System = false;
            _video.Start(zone, path, 30, audio, prewarmed, showCursor: _showCursorCheck.Checked,
                maskWinWatermark: composed && _hideWmCheck.Checked);
            _recBtn.Text = "Arrêter (REC)";
            _recBtn.BackColor = Color.FromArgb(170, 50, 50);
            SetStatus((composed ? "Enregistrement composé en cours : " : "Enregistrement en cours : ") + path);
        }
        catch (Exception ex) { SetStatus("Échec enregistrement : " + ex.Message); }
    }
    private void StopRecording()
    {
        string? outPath = _video.Stop();
        _recBtn.Text = "Enregistrer";
        _recBtn.BackColor = Color.FromArgb(48, 48, 56);
        if (outPath != null && _lastRecordComposed) WriteCamGeoSidecar(outPath);
        SetStatus(outPath != null ? "Clip enregistré : " + outPath : "Enregistrement arrêté.");
    }
    private void WriteCamGeoSidecar(string mp4Path)
    {
        try
        {
            var geo = new { camX = _camCxFrac, camY = _camCyFrac, camDiam = _camWidthFrac };
            File.WriteAllText(mp4Path + ".camgeo.json", Newtonsoft.Json.JsonConvert.SerializeObject(geo));
        }
        catch (Exception ex) { Logger.Warn("Sidecar camgeo non écrit : " + ex.Message); }
    }
    private static string OutputDir(bool composed = false)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "FaceCam");
        if (composed) dir = Path.Combine(dir, "Composed");
        Directory.CreateDirectory(dir);
        return dir;
    }
    private void CloseAll()
    {
        _zOrderTimer.Stop();
        _zOrderTimer.Dispose();
        _reloadDebounce.Stop();
        _reloadDebounce.Dispose();
        _scriptWatcher?.Dispose();
        _slidesWatcher?.Dispose();
        _composedWatcher?.Dispose();
        if (_video.Recording) _video.Stop();
        if (_primedAudio != null) { _primedAudio.Stop(); _primedAudio = null; }
        CloseTeleprompter();
        foreach (var slot in _mediaSlots.ToList()) HideMediaSlot(slot);
        if (_backdrop != null) { var b = _backdrop; _backdrop = null; b.Close(); b.Dispose(); }
        StopCamera();
        _thumbs.Dispose();
    }
    private void SetStatus(string s) => _status.Text = s;
}