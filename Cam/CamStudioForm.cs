using System.IO;
using System.Windows.Forms;
namespace RoleplayOverlay.Cam;
internal sealed class CamStudioForm : Form
{
    private WebcamReader? _cam;
    private CamForm? _camForm;
    private TeleprompterForm? _teleForm;
    private readonly VideoRecorder _video = new();
    private readonly ComboBox _deviceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _presetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _shapeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _scriptBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _mirror = new() { Text = "Miroir", AutoSize = true };
    private readonly Button _camBtn = new() { Text = "Caméra : ouvrir" };
    private readonly Button _teleBtn = new() { Text = "Téléprompteur : ouvrir" };
    private readonly Button _recBtn = new() { Text = "Enregistrer" };
    private readonly Button _scriptFolderBtn = new() { Text = "Dossier scripts" };
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Bottom, Height = 40 };
    private static readonly Color Bg = Color.FromArgb(24, 24, 28);
    private static readonly Color Panel = Color.FromArgb(34, 34, 40);
    public CamStudioForm()
    {
        Text = "Studio caméra";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        BackColor = Bg;
        ForeColor = Color.Gainsboro;
        ClientSize = new Size(320, 360);
        Font = new Font("Segoe UI", 9f);
        BuildLayout();
        LoadDevices();
        LoadScripts();
        _presetBox.Items.AddRange(CameraFilter.PresetNames);
        _presetBox.SelectedIndex = 1;
        _shapeBox.Items.AddRange(new object[] { "Rectangle", "Arrondi", "Cercle (PiP rond)" });
        _shapeBox.SelectedIndex = 2;
        _camBtn.Click += (_, _) => ToggleCamera();
        _teleBtn.Click += (_, _) => ToggleTeleprompter();
        _recBtn.Click += (_, _) => ToggleRecording();
        _scriptFolderBtn.Click += (_, _) => Teleprompter.OpenFolder();
        _presetBox.SelectedIndexChanged += (_, _) => _camForm?.SetFilter(CameraFilter.GetPreset(_presetBox.SelectedIndex));
        _shapeBox.SelectedIndexChanged += (_, _) => _camForm?.SetShape((CamForm.Shape)_shapeBox.SelectedIndex);
        _mirror.CheckedChanged += (_, _) => { if (_camForm != null) _camForm.Mirror = _mirror.Checked; };
        FormClosed += (_, _) => CloseAll();
        SetStatus("Choisis ta caméra puis Ouvrir. Le téléprompteur n'apparaît pas dans l'enregistrement.");
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
        Row("", _camBtn);
        Row("Script", _scriptBox);
        Row("", _teleBtn);
        Row("", _scriptFolderBtn);
        Row("", _recBtn);
        StyleButton(_camBtn); StyleButton(_teleBtn); StyleButton(_recBtn); StyleButton(_scriptFolderBtn);
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
    private static Rectangle PortraitZone()
    {
        Rectangle scr = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int h = (int)(scr.Height * 0.82);
        int w = h * 9 / 16;
        int x = scr.X + (scr.Width - w) / 2;
        int y = scr.Y + (scr.Height - h) / 2;
        return new Rectangle(x, y, w, h);
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
            _camForm.Show();
            _camForm.SetFilter(filter);
            _camForm.PlaceInZone(PortraitZone());
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
        _camBtn.Text = "Caméra : ouvrir";
        SetStatus("Caméra fermée.");
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
            _teleForm.CloseRequested += (_, _) => CloseTeleprompter();
            _teleForm.FormClosed += (_, _) => { _teleForm = null; _teleBtn.Text = "Téléprompteur : ouvrir"; };
            _teleForm.Show();
            _teleForm.PlaceNearTop(PortraitZone());
            _teleForm.SetScript(Teleprompter.Read(name));
            _teleBtn.Text = "Téléprompteur : fermer";
            SetStatus("Téléprompteur ouvert (exclu de l'enregistrement).");
        }
        else CloseTeleprompter();
    }
    private void CloseTeleprompter()
    {
        if (_teleForm != null) { var t = _teleForm; _teleForm = null; t.Close(); t.Dispose(); }
        _teleBtn.Text = "Téléprompteur : ouvrir";
    }
    private void ToggleRecording()
    {
        if (!_video.Recording)
        {
            if (_camForm == null) { SetStatus("Ouvre d'abord la caméra."); return; }
            Rectangle zone = _camForm.DesktopBounds;
            string dir = OutputDir();
            string path = Path.Combine(dir, "facecam_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            try
            {
                var audio = AudioOptions.Default;
                audio.System = false;
                _video.Start(zone, path, 30, audio);
                _recBtn.Text = "Arrêter (REC)";
                _recBtn.BackColor = Color.FromArgb(170, 50, 50);
                SetStatus("Enregistrement en cours : " + path);
            }
            catch (Exception ex) { SetStatus("Échec enregistrement : " + ex.Message); }
        }
        else
        {
            string? outPath = _video.Stop();
            _recBtn.Text = "Enregistrer";
            _recBtn.BackColor = Color.FromArgb(48, 48, 56);
            SetStatus(outPath != null ? "Clip enregistré : " + outPath : "Enregistrement arrêté.");
        }
    }
    private static string OutputDir()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "FaceCam");
        Directory.CreateDirectory(dir);
        return dir;
    }
    private void CloseAll()
    {
        if (_video.Recording) _video.Stop();
        CloseTeleprompter();
        StopCamera();
    }
    private void SetStatus(string s) => _status.Text = s;
}