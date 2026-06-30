using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
namespace RoleplayOverlay.Cam;
internal static class Teleprompter
{
    public static string ScriptsDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string cand = Path.Combine(dir.FullName, "scripts");
                if (Directory.Exists(cand)) return cand;
            }
            string local = Path.Combine(AppContext.BaseDirectory, "Scripts");
            Directory.CreateDirectory(local);
            return local;
        }
    }
    public static string[] List()
    {
        try
        {
            return Directory.GetFiles(ScriptsDir, "*.txt")
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(n => n)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
    public static string Read(string name)
    {
        try
        {
            string path = Path.Combine(ScriptsDir, name);
            string text = File.ReadAllText(path);
            if (text.Contains('�'))
                text = File.ReadAllText(path, System.Text.Encoding.Latin1);
            return text;
        }
        catch { return ""; }
    }
    public static void OpenFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", ScriptsDir) { UseShellExecute = true });
        }
        catch { }
    }
}
internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}
internal sealed class TeleprompterForm : Form
{
    public event EventHandler? CloseRequested;
    public event Action? SettingsChanged;
    public float SpeedValue => _speed;
    public float FontValue => _fontSize;
    public float AnchorValue => _anchorFrac;
    public void ApplySettings(float speed, float font, float anchor)
    {
        _speed = Math.Clamp(speed, 10f, 220f);
        _fontSize = Math.Clamp(font, 14f, 64f);
        _anchorFrac = Math.Clamp(anchor, 0.05f, 0.60f);
        BuildFonts();
        UpdateInfo();
        _view.Invalidate();
    }
    private readonly List<(string Text, bool Cue)> _paras = new();
    private readonly DoubleBufferedPanel _view = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private readonly Label _info = new();
    private readonly Button _playBtn = new();
    private Label _title = null!;
    private bool _clickThrough;
    private float _scroll;
    private float _speed = 45f;
    private float _fontSize = 30f;
    private float _anchorFrac = 0.16f;
    private bool _playing;
    private long _lastTick;
    private float _contentHeight;
    private Font _mainFont = null!, _cueFont = null!;
    private static readonly Color Bg = Color.FromArgb(16, 16, 20);
    private static readonly Color BarBg = Color.FromArgb(28, 28, 34);
    private static readonly Color Spoken = Color.White;
    private static readonly Color CueColor = Color.FromArgb(150, 175, 215);
    private static readonly Color Outline = Color.FromArgb(235, 0, 0, 0);
    public TeleprompterForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Bg;
        TopMost = true;
        KeyPreview = true;
        Opacity = 0.93;
        MinimumSize = new Size(260, 220);
        Text = "VertiMask_Teleprompter";
        BuildFonts();
        BuildChrome();
        _view.Paint += PaintText;
        _view.MouseWheel += (_, e) => { _scroll = Math.Max(0, _scroll - e.Delta / 2f); _view.Invalidate(); };
        _view.MouseEnter += (_, _) => _view.Focus();
        _timer.Tick += OnTick;
        _lastTick = Environment.TickCount64;
        _timer.Start();
    }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOPMOST;
            return cp;
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
    }
    private void BuildFonts()
    {
        _mainFont?.Dispose();
        _cueFont?.Dispose();
        _mainFont = new Font("Segoe UI Semibold", _fontSize, FontStyle.Bold);
        _cueFont = new Font("Segoe UI", _fontSize * 0.55f, FontStyle.Italic);
    }
    private void BuildChrome()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = BarBg };
        _title = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
        };
        _title.MouseDown += DragFromCaption;
        top.MouseDown += DragFromCaption;
        var close = new Button
        {
            Text = "X",
            Dock = DockStyle.Right,
            Width = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(185, 60, 60),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => { CloseRequested?.Invoke(this, EventArgs.Empty); };
        top.Controls.Add(_title);
        top.Controls.Add(close);
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = BarBg };
        int bx = 6;
        _playBtn.SetBounds(bx, 6, 80, 28); StyleBtn(_playBtn, "Lire"); _playBtn.Click += (_, _) => TogglePlay();
        bottom.Controls.Add(_playBtn); bx += 86;
        AddBtn(bottom, ref bx, "Vit -", () => Speed(-10), 48);
        AddBtn(bottom, ref bx, "Vit +", () => Speed(+10), 48);
        AddBtn(bottom, ref bx, "A-", () => FontStep(-2), 38);
        AddBtn(bottom, ref bx, "A+", () => FontStep(+2), 38);
        AddBtn(bottom, ref bx, "Ligne haut", () => MoveLine(-0.04f), 78);
        AddBtn(bottom, ref bx, "Ligne bas", () => MoveLine(+0.04f), 74);
        AddBtn(bottom, ref bx, "Debut", () => { _scroll = 0; _view.Invalidate(); }, 50);
        AddBtn(bottom, ref bx, "Opac -", () => Opac(-0.08), 52);
        AddBtn(bottom, ref bx, "Opac +", () => Opac(+0.08), 52);
        AddBtn(bottom, ref bx, "Traversant", () => SetClickThrough(true), 88);
        _info.SetBounds(bx + 4, 6, 120, 28);
        _info.ForeColor = Color.Gray;
        _info.Font = new Font("Segoe UI", 8f);
        _info.TextAlign = ContentAlignment.MiddleLeft;
        bottom.Controls.Add(_info);
        var grip = new Label
        {
            Text = "⤡",
            Dock = DockStyle.Right,
            Width = 28,
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.SizeNWSE,
            Font = new Font("Segoe UI", 11f),
        };
        grip.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTBOTTOMRIGHT), IntPtr.Zero);
            }
        };
        bottom.Controls.Add(grip);
        _view.BackColor = Bg;
        _view.TabStop = true;
        Controls.Add(_view);
        Controls.Add(top);
        Controls.Add(bottom);
        UpdateInfo();
        UpdateTitle();
    }
    private void DragFromCaption(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
        }
    }
    private static void StyleBtn(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(55, 55, 62);
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI", 8.5f);
        b.Cursor = Cursors.Hand;
        b.FlatAppearance.BorderSize = 0;
    }
    private void AddBtn(Panel parent, ref int x, string text, Action onClick, int width = 54)
    {
        var b = new Button();
        b.SetBounds(x, 6, width, 28);
        StyleBtn(b, text);
        b.Click += (_, _) => onClick();
        parent.Controls.Add(b);
        x += width + 4;
    }
    private void MoveLine(float d)
    {
        _anchorFrac = Math.Clamp(_anchorFrac + d, 0.05f, 0.60f);
        _view.Invalidate();
        SettingsChanged?.Invoke();
    }
    private void Opac(double d) => Opacity = Math.Clamp(Opacity + d, 0.25, 1.0);
    public void ScrollStep(float delta)
    {
        _scroll = Math.Max(0, _scroll + delta);
        _view.Invalidate();
    }
    public void BringToTop()
    {
        if (!IsHandleCreated) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
    public bool ClickThrough => _clickThrough;
    public event Action<bool>? ClickThroughChanged;
    public bool ToggleClickThrough() { SetClickThrough(!_clickThrough); return _clickThrough; }
    public void SetClickThrough(bool on)
    {
        if (!IsHandleCreated) return;
        _clickThrough = on;
        long style = Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE).ToInt64();
        if (on) style |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
        else style &= ~(long)Native.WS_EX_TRANSPARENT;
        Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(style));
        UpdateTitle();
        ClickThroughChanged?.Invoke(on);
    }
    private void UpdateTitle()
    {
        if (_title == null) return;
        _title.Text = _clickThrough
            ? "  CLIC TRAVERSANT  -  Ctrl+Alt+T pour reprendre la main"
            : "  Teleprompteur  (glisser ici)";
        _title.ForeColor = _clickThrough ? Color.FromArgb(120, 220, 160) : Color.Gainsboro;
    }
    public void SetScript(string raw)
    {
        _paras.Clear();
        foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (t.Length == 0) { _paras.Add(("", false)); continue; }
            bool cue = t.StartsWith("#") || t.StartsWith(">") || t.StartsWith("[")
                       || t.StartsWith("(") || t.StartsWith("---") || t.StartsWith("**");
            t = t.Trim('#', '>', ' ', '-').Replace("**", "");
            if (t.Length == 0) continue;
            _paras.Add((t, cue));
        }
        _scroll = 0;
        _view.Invalidate();
    }
    public void PlaceNearTop(Rectangle zone)
    {
        Rectangle screen = Screen.FromRectangle(zone).Bounds;
        int w = Math.Min(screen.Width - 40, Math.Max(900, zone.Width));
        int h = (int)(zone.Height * 0.5);
        int xx = zone.X + (zone.Width - w) / 2;
        xx = Math.Clamp(xx, screen.Left + 8, Math.Max(screen.Left + 8, screen.Right - w - 8));
        int yy = zone.Y;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, xx, yy, w, h, Native.SWP_SHOWWINDOW);
    }
    private void TogglePlay()
    {
        _playing = !_playing;
        _playBtn.Text = _playing ? "Pause" : "Lire";
        _lastTick = Environment.TickCount64;
    }
    private void Speed(float d) { _speed = Math.Clamp(_speed + d, 10f, 220f); UpdateInfo(); SettingsChanged?.Invoke(); }
    private void FontStep(float d)
    {
        _fontSize = Math.Clamp(_fontSize + d, 14f, 64f);
        BuildFonts();
        UpdateInfo();
        _view.Invalidate();
        SettingsChanged?.Invoke();
    }
    private void UpdateInfo() => _info.Text = $"Vitesse {(int)_speed} · Taille {(int)_fontSize}";
    private void OnTick(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        float dt = (now - _lastTick) / 1000f;
        _lastTick = now;
        if (_playing)
        {
            _scroll += _speed * dt;
            if (_scroll >= _contentHeight && _contentHeight > 0)
            {
                _scroll = _contentHeight;
                _playing = false;
                _playBtn.Text = "Lire";
            }
            _view.Invalidate();
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Space: TogglePlay(); break;
            case Keys.Control | Keys.Up: MoveLine(-0.04f); break;
            case Keys.Control | Keys.Down: MoveLine(+0.04f); break;
            case Keys.Up: _scroll = Math.Max(0, _scroll - 40); break;
            case Keys.Down: _scroll += 40; break;
            case Keys.PageUp: _scroll = Math.Max(0, _scroll - 220); break;
            case Keys.PageDown: _scroll += 220; break;
            case Keys.Home: _scroll = 0; break;
            case Keys.Add: case Keys.Oemplus: Speed(+10); break;
            case Keys.Subtract: case Keys.OemMinus: Speed(-10); break;
            case Keys.OemOpenBrackets: FontStep(-2); break;
            case Keys.OemCloseBrackets: FontStep(+2); break;
            case Keys.Escape: CloseRequested?.Invoke(this, EventArgs.Empty); break;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
        _view.Invalidate();
        return true;
    }
    private static void DrawOutlined(Graphics g, string text, Font f, Brush fill, Brush outline, RectangleF rect, StringFormat fmt)
    {
        ReadOnlySpan<int> dx = stackalloc int[] { -2, 2, 0, 0, -2, 2, -2, 2 };
        ReadOnlySpan<int> dy = stackalloc int[] { 0, 0, -2, 2, -2, -2, 2, 2 };
        for (int i = 0; i < dx.Length; i++)
            g.DrawString(text, f, outline, new RectangleF(rect.X + dx[i], rect.Y + dy[i], rect.Width, rect.Height), fmt);
        g.DrawString(text, f, fill, rect, fmt);
    }
    private void PaintText(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Bg);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        int margin = 26;
        int tw = Math.Max(40, _view.Width - 2 * margin);
        float anchor = _view.Height * _anchorFrac;
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            FormatFlags = 0,
        };
        using var spokenBrush = new SolidBrush(Spoken);
        using var cueBrush = new SolidBrush(CueColor);
        using var outlineBrush = new SolidBrush(Outline);
        float y = anchor - _scroll;
        float start = y;
        foreach (var (text, cue) in _paras)
        {
            if (text.Length == 0) { y += _fontSize * 0.6f; continue; }
            Font f = cue ? _cueFont : _mainFont;
            SizeF sz = g.MeasureString(text, f, tw, fmt);
            if (y + sz.Height > 0 && y < _view.Height)
                DrawOutlined(g, text, f, cue ? cueBrush : spokenBrush, outlineBrush,
                    new RectangleF(margin, y, tw, sz.Height), fmt);
            y += sz.Height + (cue ? 6f : 16f);
        }
        _contentHeight = y - start;
        using var guide = new Pen(Color.FromArgb(120, 255, 110, 110), 2);
        g.DrawLine(guide, 8, anchor, _view.Width - 8, anchor);
        using var tri = new SolidBrush(Color.FromArgb(170, 255, 110, 110));
        g.FillPolygon(tri, new[] { new PointF(0, anchor - 7), new PointF(12, anchor), new PointF(0, anchor + 7) });
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _mainFont.Dispose();
        _cueFont.Dispose();
        base.OnFormClosed(e);
    }
}