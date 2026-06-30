using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal sealed class WebcamReader
{
    private Thread? _thread;
    private volatile bool _running;
    private readonly object _lock = new();
    private Bitmap? _frame;
    private Exception? _error;
    private int _deviceIndex;
    private string? _dsMfPath;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double Aspect => Height > 0 ? (double)Width / Height : 16.0 / 9.0;
    public string? LastError => _error?.Message;
    public event Action? FrameReady;
    private record struct DeviceInfo(int MfIdx, string DsPath);
    private static DeviceInfo[] _deviceMap = Array.Empty<DeviceInfo>();
    public static string[] ListDevices()
    {
        var mfNames  = new List<string>();
        var mfMap    = new List<DeviceInfo>();
        Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
        bool mfStarted = false;
        try
        {
            if (Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL) >= 0)
            {
                mfStarted = true;
                if (Mf.MFCreateAttributes(out IMFAttributes da, 1) >= 0)
                {
                    Guid kType = Mf.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
                    Guid vidcap = Mf.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
                    da.SetGUID(ref kType, ref vidcap);
                    if (Mf.MFEnumDeviceSources(da, out IntPtr ppDevices, out int count) >= 0)
                    {
                        Guid kName = Mf.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME;
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr p = Marshal.ReadIntPtr(ppDevices, i * IntPtr.Size);
                            var act = (IMFActivate)Marshal.GetObjectForIUnknown(p);
                            string name = $"Caméra {i + 1}";
                            try { if (act.GetAllocatedString(ref kName, out string n, out _) >= 0 && !string.IsNullOrWhiteSpace(n)) name = n; }
                            catch { }
                            mfNames.Add(name);
                            mfMap.Add(new DeviceInfo(i, ""));
                            Marshal.ReleaseComObject(act);
                            Marshal.Release(p);
                        }
                        Marshal.FreeCoTaskMem(ppDevices);
                    }
                    Marshal.ReleaseComObject(da);
                }
            }
        }
        catch { }
        finally
        {
            if (mfStarted) { try { Mf.MFShutdown(); } catch { } }
            Wasapi.CoUninitialize();
        }
        var dsDevices = Ds.ListVideoDevices();
        var mfSet = new HashSet<string>(mfNames, StringComparer.OrdinalIgnoreCase);
        foreach (var d in dsDevices)
            if (!mfSet.Contains(d.Name) && !string.IsNullOrEmpty(d.Name))
            {
                mfNames.Add(d.Name);
                mfMap.Add(new DeviceInfo(-1, d.MonikerName));
            }
        _deviceMap = mfMap.ToArray();
        return mfNames.ToArray();
    }
    public bool Start(int deviceIndex = 0)
    {
        if (_running) return true;
        _dsMfPath = null;
        if (deviceIndex < _deviceMap.Length && _deviceMap[deviceIndex].MfIdx < 0)
        {
            _dsMfPath = _deviceMap[deviceIndex].DsPath;
            _deviceIndex = 0;
        }
        else
        {
            _deviceIndex = deviceIndex < _deviceMap.Length
                ? _deviceMap[deviceIndex].MfIdx
                : Math.Max(0, deviceIndex);
        }
        var ready = new ManualResetEventSlim(false);
        bool ok = false;
        _thread = new Thread(() => Loop(ready, v => ok = v))
        {
            IsBackground = true,
            Name = "VertiMask.Cam",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _running = true;
        _thread.Start();
        ready.Wait(4000);
        if (!ok) { _running = false; _thread?.Join(1000); _thread = null; }
        return ok;
    }
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(3000);
        _thread = null;
        lock (_lock) { _frame?.Dispose(); _frame = null; }
    }
    public void DrawTo(Graphics g, Rectangle dest, ImageAttributes? attrs = null)
    {
        lock (_lock)
        {
            if (_frame == null) return;
            if (attrs == null) g.DrawImage(_frame, dest);
            else g.DrawImage(_frame, dest, 0, 0, _frame.Width, _frame.Height, GraphicsUnit.Pixel, attrs);
        }
    }
    public void DrawCenteredSquare(Graphics g, Rectangle dest, ImageAttributes? attrs = null)
    {
        lock (_lock)
        {
            if (_frame == null) return;
            int side = Math.Min(_frame.Width, _frame.Height);
            int sx = (_frame.Width - side) / 2;
            int sy = (_frame.Height - side) / 2;
            if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
            else g.DrawImage(_frame, dest, sx, sy, side, side, GraphicsUnit.Pixel, attrs);
        }
    }
    public void DrawCover(Graphics g, Rectangle dest, ImageAttributes? attrs = null)
    {
        lock (_lock)
        {
            if (_frame == null || dest.Height <= 0) return;
            double target = (double)dest.Width / dest.Height;
            double frame = (double)_frame.Width / _frame.Height;
            int sw, sh;
            if (frame > target) { sh = _frame.Height; sw = (int)Math.Round(sh * target); }
            else { sw = _frame.Width; sh = (int)Math.Round(sw / target); }
            int sx = (_frame.Width - sw) / 2;
            int sy = (_frame.Height - sh) / 2;
            if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            else g.DrawImage(_frame, dest, sx, sy, sw, sh, GraphicsUnit.Pixel, attrs);
        }
    }
    private static bool IsBlack(IntPtr p, int len)
    {
        if (p == IntPtr.Zero || len <= 0) return true;
        for (int i = 0; i < len; i += 1021)
            if (Marshal.ReadByte(p, i) != 0) return false;
        return true;
    }
    private void Loop(ManualResetEventSlim ready, Action<bool> setOk)
    {
        Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
        bool mfStarted = false;
        IMFSourceReader? reader = null;
        object? source = null;
        try
        {
            if (Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL) < 0) { ready.Set(); return; }
            mfStarted = true;
            if (_dsMfPath != null)
            {
                using var cap = new DsCapture();
                using var firstFrame = new ManualResetEventSlim(false);
                cap.FrameArrived += (p, len) =>
                {
                    lock (_lock)
                    {
                        if (_frame == null) return;
                        var bd = _frame.LockBits(new Rectangle(0, 0, Width, Height),
                            ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        int rowBytes = Width * 4;
                        Mf.MFCopyImage(bd.Scan0, bd.Stride, p + (Height - 1) * rowBytes, -rowBytes, rowBytes, Height);
                        _frame.UnlockBits(bd);
                    }
                    firstFrame.Set();
                    FrameReady?.Invoke();
                };
                if (!cap.Start(_dsMfPath))
                {
                    _error = new Exception("ouverture DirectShow impossible");
                    ready.Set();
                    return;
                }
                Width = cap.Width;
                Height = cap.Height;
                lock (_lock) _frame = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
                bool live = firstFrame.Wait(3000);
                if (!live) _error = new Exception("source inactive : demarrez OBS / NVIDIA Broadcast puis reessayez");
                setOk(live);
                ready.Set();
                if (!live) return;
                while (_running) Thread.Sleep(30);
                return;
            }
            else
            {
                if (Mf.MFCreateAttributes(out IMFAttributes da, 1) < 0) { ready.Set(); return; }
                Guid kType = Mf.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE;
                Guid vidcap = Mf.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID;
                da.SetGUID(ref kType, ref vidcap);
                if (Mf.MFEnumDeviceSources(da, out IntPtr ppDevices, out int count) < 0 || count == 0)
                {
                    Marshal.ReleaseComObject(da);
                    ready.Set();
                    return;
                }
                int target = Math.Min(_deviceIndex, count - 1);
                IMFActivate? act = null;
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(ppDevices, i * IntPtr.Size);
                    if (i == target) act = (IMFActivate)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                }
                Marshal.FreeCoTaskMem(ppDevices);
                Marshal.ReleaseComObject(da);
                if (act == null) { ready.Set(); return; }
                Guid iidSource = Mf.IID_IMFMediaSource;
                if (act.ActivateObject(ref iidSource, out source) < 0) { ready.Set(); return; }
                Marshal.ReleaseComObject(act);
            }
            Mf.MFCreateAttributes(out IMFAttributes ra, 1);
            Guid kProc = Mf.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
            ra.SetUINT32(ref kProc, 1);
            if (Mf.MFCreateSourceReaderFromMediaSource(source, ra, out reader) < 0) { ready.Set(); return; }
            Marshal.ReleaseComObject(ra);
            int stream = Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
            reader.SetStreamSelection(stream, true);
            Mf.MFCreateMediaType(out IMFMediaType outType);
            Guid major = Mf.MF_MT_MAJOR_TYPE, vid = Mf.MFMediaType_Video;
            Guid sub = Mf.MF_MT_SUBTYPE, rgb = Mf.MFVideoFormat_RGB32;
            outType.SetGUID(ref major, ref vid);
            outType.SetGUID(ref sub, ref rgb);
            if (reader.SetCurrentMediaType(stream, IntPtr.Zero, outType) < 0) { Marshal.ReleaseComObject(outType); ready.Set(); return; }
            Marshal.ReleaseComObject(outType);
            if (reader.GetCurrentMediaType(stream, out IMFMediaType cur) < 0) { ready.Set(); return; }
            Guid kSize = Mf.MF_MT_FRAME_SIZE;
            cur.GetUINT64(ref kSize, out ulong packed);
            Marshal.ReleaseComObject(cur);
            Width = (int)(packed >> 32);
            Height = (int)(packed & 0xFFFFFFFF);
            if (Width <= 0 || Height <= 0) { ready.Set(); return; }
            lock (_lock) _frame = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            bool imageOk = false;
            int blackDeadline = Environment.TickCount + 2500;
            while (_running)
            {
                int hr = reader.ReadSample(stream, 0, out _, out _, out _, out IMFSample? sample);
                if (hr < 0) break;
                if (sample == null)
                {
                    if (!imageOk && Environment.TickCount > blackDeadline) break;
                    Thread.Sleep(5);
                    continue;
                }
                try
                {
                    if (sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer) >= 0)
                    {
                        buffer.Lock(out IntPtr pData, out _, out int cb);
                        if (!imageOk && !IsBlack(pData, cb > 0 ? cb : Width * 4 * Height))
                        {
                            imageOk = true;
                            setOk(true);
                            ready.Set();
                        }
                        lock (_lock)
                        {
                            if (_frame != null)
                            {
                                var bd = _frame.LockBits(new Rectangle(0, 0, Width, Height),
                                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                                Mf.MFCopyImage(bd.Scan0, bd.Stride, pData, Width * 4, Width * 4, Height);
                                _frame.UnlockBits(bd);
                            }
                        }
                        buffer.Unlock();
                        Marshal.ReleaseComObject(buffer);
                    }
                    if (imageOk) FrameReady?.Invoke();
                    else if (Environment.TickCount > blackDeadline) break;
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
            if (!imageOk)
            {
                _error ??= new Exception("Caméra occupée ou indisponible (une autre application comme NVIDIA Broadcast l'utilise ?). Fermez-la puis réessayez.");
                setOk(false);
                ready.Set();
            }
        }
        catch (Exception ex) { _error = ex; ready.Set(); }
        finally
        {
            if (reader != null) Marshal.ReleaseComObject(reader);
            if (source != null) Marshal.ReleaseComObject(source);
            if (mfStarted) { try { Mf.MFShutdown(); } catch { } }
            Wasapi.CoUninitialize();
        }
    }
}
internal sealed class CamForm : Form
{
    public enum Shape { Rectangle, Rounded, Circle }
    private readonly WebcamReader _reader;
    private readonly double _camAspect;
    private Shape _shape;
    private bool _mirror;
    private bool _fill;
    private CameraFilter.Params _filterParams = CameraFilter.GetPreset(CameraFilter.Preset.Aucun);
    private const int Grip = 22;
    private const int Radius = 28;
    public event EventHandler? CloseRequested;
    public event EventHandler? GeometryChanged;
    public bool Mirror
    {
        get => _mirror;
        set { if (_mirror != value) { _mirror = value; Invalidate(); } }
    }
    public CamForm(WebcamReader reader, Shape shape = Shape.Rectangle, bool mirror = false,
                   CameraFilter.Params? filter = null)
    {
        _reader = reader;
        _camAspect = reader.Aspect;
        _shape = shape;
        _mirror = mirror;
        if (filter.HasValue) _filterParams = filter.Value;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        TopMost = true;
        DoubleBuffered = true;
        Text = "VertiMask_Cam";
        MinimumSize = new Size(120, 90);
        KeyPreview = true;
        _reader.FrameReady += OnFrameReady;
    }
    protected override bool ShowWithoutActivation => false;
    private double TargetAspect => _shape == Shape.Circle ? 1.0 : _camAspect;
    public void FillZone(Rectangle zone)
    {
        _fill = true;
        Region = null;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, zone.X, zone.Y, zone.Width, zone.Height,
            Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);
        Invalidate();
    }
    public void ExitFill(Rectangle zone)
    {
        _fill = false;
        PlaceInZone(zone);
        Invalidate();
    }
    public void BringToFrontTopmost()
    {
        if (!IsHandleCreated) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);
    }
    public void SetFilter(CameraFilter.Params p) { _filterParams = p; Invalidate(); }
    public void SetShape(Shape shape)
    {
        if (_shape == shape) return;
        _shape = shape;
        int w = Width;
        int h = (int)Math.Round(w / TargetAspect);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, w, h,
            Native.SWP_NOACTIVATE);
        UpdateRegion();
        Invalidate();
    }
    public void PlaceInZone(Rectangle zone)
    {
        int w = (int)Math.Round(zone.Width * 0.6);
        int h = (int)Math.Round(w / TargetAspect);
        if (h > zone.Height * 0.5) { h = (int)Math.Round(zone.Height * 0.5); w = (int)Math.Round(h * TargetAspect); }
        int x = zone.X + (zone.Width - w) / 2;
        int y = zone.Bottom - h - (int)Math.Round(zone.Height * 0.06);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_SHOWWINDOW);
        UpdateRegion();
    }
    public void ApplyGeometry(Rectangle zone, double widthFrac, double cxFrac, double cyFrac)
    {
        int w = (int)Math.Round(zone.Width * widthFrac);
        w = Math.Clamp(w, MinimumSize.Width, zone.Width);
        int h = (int)Math.Round(w / TargetAspect);
        if (h > zone.Height) { h = zone.Height; w = (int)Math.Round(h * TargetAspect); }
        int cx = zone.X + (int)Math.Round(zone.Width * cxFrac);
        int cy = zone.Y + (int)Math.Round(zone.Height * cyFrac);
        int x = Math.Clamp(cx - w / 2, zone.X, zone.Right - w);
        int y = Math.Clamp(cy - h / 2, zone.Y, zone.Bottom - h);
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_SHOWWINDOW);
        UpdateRegion();
    }
    private void UpdateRegion()
    {
        if (!IsHandleCreated) return;
        switch (_shape)
        {
            case Shape.Circle:
            {
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
                break;
            }
            case Shape.Rounded:
                Region = new Region(RoundedPath(Width, Height, Radius));
                break;
            default:
                Region = null;
                break;
        }
    }
    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(int w, int h, int r)
    {
        int d = r * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(0, 0, d, d, 180, 90);
        p.AddArc(w - d, 0, d, d, 270, 90);
        p.AddArc(w - d, h - d, d, d, 0, 90);
        p.AddArc(0, h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    private void OnFrameReady()
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke(new Action(Invalidate)); } catch {  }
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        var cr = ClientRectangle;
        var state = g.Save();
        if (_mirror) { g.TranslateTransform(Width, 0); g.ScaleTransform(-1, 1); }
        if (_filterParams.IsIdentity)
        {
            if (_fill) _reader.DrawCover(g, cr);
            else if (_shape == Shape.Circle) _reader.DrawCenteredSquare(g, cr);
            else _reader.DrawTo(g, cr);
        }
        else if (_filterParams.Smoothing > 0 || _filterParams.Sharpness > 0f)
        {
            using var tmp = new Bitmap(cr.Width, cr.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var g2 = Graphics.FromImage(tmp))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                if (_fill) _reader.DrawCover(g2, cr);
                else if (_shape == Shape.Circle) _reader.DrawCenteredSquare(g2, cr);
                else _reader.DrawTo(g2, cr);
            }
            if (_filterParams.Sharpness > 0f) CameraFilter.ApplyUnsharpMask(tmp, _filterParams.Sharpness);
            if (_filterParams.Smoothing > 0) CameraFilter.ApplySmoothing(tmp, _filterParams.Smoothing);
            using var attrs = CameraFilter.BuildAttributes(_filterParams);
            g.DrawImage(tmp, cr, 0, 0, cr.Width, cr.Height, GraphicsUnit.Pixel, attrs);
        }
        else
        {
            using var attrs = CameraFilter.BuildAttributes(_filterParams);
            if (_fill) _reader.DrawCover(g, cr, attrs);
            else if (_shape == Shape.Circle) _reader.DrawCenteredSquare(g, cr, attrs);
            else _reader.DrawTo(g, cr, attrs);
        }
        g.Restore(state);
        if (_filterParams.Vignette > 0f) CameraFilter.DrawVignette(g, cr, _filterParams.Vignette);
        if (_fill) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 3);
        switch (_shape)
        {
            case Shape.Circle: g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3); break;
            case Shape.Rounded: using (var p = RoundedPath(Width - 2, Height - 2, Radius)) g.DrawPath(pen, p); break;
            default: g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3); break;
        }
        if (_shape != Shape.Circle)
        {
            using var grip = new Pen(Color.FromArgb(160, 255, 255, 255), 2);
            for (int i = 1; i <= 3; i++)
                g.DrawLine(grip, Width - i * 6, Height - 2, Width - 2, Height - i * 6);
        }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) CloseRequested?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
    }
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int WM_SIZING = 0x0214;
        const int WM_EXITSIZEMOVE = 0x0232;
        const int HTCAPTION = 2, HTBOTTOMRIGHT = 17;
        if (m.Msg == WM_EXITSIZEMOVE && !_fill)
        {
            GeometryChanged?.Invoke(this, EventArgs.Empty);
            base.WndProc(ref m);
            return;
        }
        if (m.Msg == WM_NCHITTEST && !_fill)
        {
            Point pt = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
            m.Result = (pt.X >= Width - Grip && pt.Y >= Height - Grip)
                ? new IntPtr(HTBOTTOMRIGHT)
                : new IntPtr(HTCAPTION);
            return;
        }
        if (m.Msg == WM_SIZING && !_fill)
        {
            var rc = Marshal.PtrToStructure<Native.RECT>(m.LParam);
            int w = rc.Right - rc.Left;
            int h = (int)Math.Round(w / TargetAspect);
            rc.Bottom = rc.Top + h;
            Marshal.StructureToPtr(rc, m.LParam, false);
            m.Result = new IntPtr(1);
            return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _reader.FrameReady -= OnFrameReady;
        base.OnFormClosed(e);
    }
}
internal sealed class CamCloseButton : Form
{
    public event EventHandler? Clicked;
    private const int S = 52;
    private bool _hover;
    public CamCloseButton()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(20, 20, 24);
        TopMost = true;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        Text = "VertiMask_CamClose";
        Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
            return cp;
        }
    }
    public void ShowAt(Rectangle monitor, Rectangle zone)
    {
        Show();
        int x = zone.Right + 10;
        if (x + S > monitor.Right) x = monitor.Right - S - 6;
        int y = zone.Top + 12;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, S, S,
            Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, S, S);
        Region = new Region(path);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(_hover ? Color.FromArgb(220, 70, 70) : Color.FromArgb(185, 60, 60)))
            g.FillEllipse(b, 0, 0, S, S);
        using var pen = new Pen(Color.White, 3);
        g.DrawLine(pen, 14, 14, S - 14, S - 14);
        g.DrawLine(pen, S - 14, 14, 14, S - 14);
    }
}
internal sealed class BackdropForm : Form
{
    public BackdropForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        TopMost = true;
        Text = "VertiMask_Backdrop";
    }
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation => true;
    public void ShowOver(Rectangle zone)
    {
        Show();
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, zone.X, zone.Y, zone.Width, zone.Height,
            Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);
    }
}