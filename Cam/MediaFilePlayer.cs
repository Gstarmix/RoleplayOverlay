using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal sealed class ImageFrameSource : IFrameSource
{
    private readonly Bitmap _frame;
    public ImageFrameSource(Bitmap frame) => _frame = frame;
    public int Width => _frame.Width;
    public int Height => _frame.Height;
    public double Aspect => Height > 0 ? (double)Width / Height : 16.0 / 9.0;
    public event Action? FrameReady;
    public void DrawTo(Graphics g, Rectangle dest, ImageAttributes? attrs = null)
    {
        if (attrs == null) g.DrawImage(_frame, dest);
        else g.DrawImage(_frame, dest, 0, 0, _frame.Width, _frame.Height, GraphicsUnit.Pixel, attrs);
    }
    public void DrawCenteredSquare(Graphics g, Rectangle dest, double panX = 0, double panY = 0, ImageAttributes? attrs = null)
    {
        int side = Math.Min(_frame.Width, _frame.Height);
        var (sx, sy) = WebcamReader.PannedOrigin(_frame.Width, _frame.Height, side, side, panX, panY);
        if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
        else g.DrawImage(_frame, dest, sx, sy, side, side, GraphicsUnit.Pixel, attrs);
    }
    public void DrawCover(Graphics g, Rectangle dest, double panX = 0, double panY = 0, ImageAttributes? attrs = null)
    {
        if (dest.Height <= 0) return;
        double target = (double)dest.Width / dest.Height;
        double frame = (double)_frame.Width / _frame.Height;
        int sw, sh;
        if (frame > target) { sh = _frame.Height; sw = (int)Math.Round(sh * target); }
        else { sw = _frame.Width; sh = (int)Math.Round(sw / target); }
        var (sx, sy) = WebcamReader.PannedOrigin(_frame.Width, _frame.Height, sw, sh, panX, panY);
        if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
        else g.DrawImage(_frame, dest, sx, sy, sw, sh, GraphicsUnit.Pixel, attrs);
    }
}
internal sealed class VideoFileReader : IFrameSource
{
    private Thread? _thread;
    private volatile bool _running;
    private readonly object _lock = new();
    private Bitmap? _frame;
    private Exception? _error;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double Aspect => Height > 0 ? (double)Width / Height : 16.0 / 9.0;
    public string? LastError => _error?.Message;
    public event Action? FrameReady;
    public bool Start(string path, double startSec = 0)
    {
        if (_running) return true;
        var ready = new ManualResetEventSlim(false);
        bool ok = false;
        _thread = new Thread(() => Loop(path, startSec, ready, v => ok = v))
        {
            IsBackground = true,
            Name = "RoleplayOverlay.MediaFile",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _running = true;
        _thread.Start();
        ready.Wait(5000);
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
    public void DrawCenteredSquare(Graphics g, Rectangle dest, double panX = 0, double panY = 0, ImageAttributes? attrs = null)
    {
        lock (_lock)
        {
            if (_frame == null) return;
            int side = Math.Min(_frame.Width, _frame.Height);
            var (sx, sy) = WebcamReader.PannedOrigin(_frame.Width, _frame.Height, side, side, panX, panY);
            if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
            else g.DrawImage(_frame, dest, sx, sy, side, side, GraphicsUnit.Pixel, attrs);
        }
    }
    public void DrawCover(Graphics g, Rectangle dest, double panX = 0, double panY = 0, ImageAttributes? attrs = null)
    {
        lock (_lock)
        {
            if (_frame == null || dest.Height <= 0) return;
            double target = (double)dest.Width / dest.Height;
            double frame = (double)_frame.Width / _frame.Height;
            int sw, sh;
            if (frame > target) { sh = _frame.Height; sw = (int)Math.Round(sh * target); }
            else { sw = _frame.Width; sh = (int)Math.Round(sw / target); }
            var (sx, sy) = WebcamReader.PannedOrigin(_frame.Width, _frame.Height, sw, sh, panX, panY);
            if (attrs == null) g.DrawImage(_frame, dest, new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            else g.DrawImage(_frame, dest, sx, sy, sw, sh, GraphicsUnit.Pixel, attrs);
        }
    }
    private void Loop(string path, double startSec, ManualResetEventSlim ready, Action<bool> setOk)
    {
        Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
        bool mfStarted = false;
        IMFSourceReader? reader = null;
        try
        {
            if (Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL) < 0) { ready.Set(); return; }
            mfStarted = true;
            Mf.MFCreateAttributes(out IMFAttributes ra, 1);
            Guid kProc = Mf.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
            ra.SetUINT32(ref kProc, 1);
            if (Mf.MFCreateSourceReaderFromURL(path, ra, out reader) < 0)
            {
                _error = new Exception("fichier illisible ou codec non supporte");
                ready.Set();
                return;
            }
            Marshal.ReleaseComObject(ra);
            int stream = Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
            reader.SetStreamSelection(Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(stream, true);
            Mf.MFCreateMediaType(out IMFMediaType outType);
            Guid major = Mf.MF_MT_MAJOR_TYPE, vid = Mf.MFMediaType_Video;
            Guid sub = Mf.MF_MT_SUBTYPE, rgb = Mf.MFVideoFormat_RGB32;
            outType.SetGUID(ref major, ref vid);
            outType.SetGUID(ref sub, ref rgb);
            if (reader.SetCurrentMediaType(stream, IntPtr.Zero, outType) < 0)
            {
                _error = new Exception("conversion RGB32 impossible pour ce fichier");
                Marshal.ReleaseComObject(outType); ready.Set(); return;
            }
            Marshal.ReleaseComObject(outType);
            if (reader.GetCurrentMediaType(stream, out IMFMediaType cur) < 0) { ready.Set(); return; }
            Guid kSize = Mf.MF_MT_FRAME_SIZE;
            cur.GetUINT64(ref kSize, out ulong packed);
            Width = (int)(packed >> 32);
            Height = (int)(packed & 0xFFFFFFFF);
            Guid kStride = Mf.MF_MT_DEFAULT_STRIDE;
            int mfStride = (Width > 0 && cur.GetUINT32(ref kStride, out int ds) >= 0 && ds != 0) ? Math.Abs(ds) : 0;
            Marshal.ReleaseComObject(cur);
            if (Width <= 0 || Height <= 0) { ready.Set(); return; }
            lock (_lock) _frame = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
            if (startSec > 0.01)
            {
                var sfmt = Guid.Empty;
                var spos = new PROPVARIANT { vt = Mf.VT_I8, data = (long)(startSec * 10_000_000) };
                reader.SetCurrentPosition(ref sfmt, ref spos);
            }
            setOk(true);
            ready.Set();
            MfImage.DebugNextCopy = true;
            long loopStartTick = Environment.TickCount64;
            long firstSampleTs = -1;
            while (_running)
            {
                int hr = reader.ReadSample(stream, 0, out _, out int flags, out long ts, out IMFSample? sample);
                if (hr < 0) break;
                if (sample != null)
                {
                    if (firstSampleTs < 0) { firstSampleTs = ts; loopStartTick = Environment.TickCount64; }
                    long targetMs = (ts - firstSampleTs) / 10_000;
                    long waitMs = targetMs - (Environment.TickCount64 - loopStartTick);
                    if (waitMs > 2 && waitMs < 2000) Thread.Sleep((int)waitMs);
                    try
                    {
                        lock (_lock)
                        {
                            if (_frame != null)
                                MfImage.CopySample(sample, _frame, Width, Height, mfStride);
                        }
                        FrameReady?.Invoke();
                    }
                    finally { Marshal.ReleaseComObject(sample); }
                }
                bool eos = (flags & Mf.MF_SOURCE_READERF_ENDOFSTREAM) != 0;
                if (eos)
                {
                    var fmt = Guid.Empty;
                    var pos = new PROPVARIANT { vt = Mf.VT_I8, data = 0 };
                    reader.SetCurrentPosition(ref fmt, ref pos);
                    firstSampleTs = -1;
                }
            }
        }
        catch (Exception ex) { _error = ex; ready.Set(); }
        finally
        {
            if (reader != null) Marshal.ReleaseComObject(reader);
            if (mfStarted) { try { Mf.MFShutdown(); } catch { } }
            Wasapi.CoUninitialize();
        }
    }
}
internal sealed class VideoClip
{
    private readonly Bitmap[] _frames;
    private readonly int[] _startMs;
    public int TotalMs { get; }
    public long MemoryBytes { get; }
    private VideoClip(Bitmap[] frames, int[] startMs, int totalMs)
    {
        _frames = frames; _startMs = startMs; TotalMs = totalMs;
        long b = 0;
        foreach (var f in frames) b += (long)f.Width * f.Height * 4;
        MemoryBytes = b;
    }
    public static VideoClip? Load(string path, int decodeWidth = 720, int maxFrames = 300)
    {
        Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
        bool mfStarted = false;
        IMFSourceReader? reader = null;
        Bitmap? native = null;
        var frames = new List<Bitmap>();
        var starts = new List<int>();
        int lastTs = 0;
        try
        {
            if (Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL) < 0) return null;
            mfStarted = true;
            Mf.MFCreateAttributes(out IMFAttributes ra, 1);
            Guid kProc = Mf.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING;
            ra.SetUINT32(ref kProc, 1);
            if (Mf.MFCreateSourceReaderFromURL(path, ra, out reader) < 0) { Marshal.ReleaseComObject(ra); return null; }
            Marshal.ReleaseComObject(ra);
            int stream = Mf.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
            reader.SetStreamSelection(Mf.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(stream, true);
            Mf.MFCreateMediaType(out IMFMediaType outType);
            Guid major = Mf.MF_MT_MAJOR_TYPE, vid = Mf.MFMediaType_Video;
            Guid sub = Mf.MF_MT_SUBTYPE, rgb = Mf.MFVideoFormat_RGB32;
            outType.SetGUID(ref major, ref vid);
            outType.SetGUID(ref sub, ref rgb);
            if (reader.SetCurrentMediaType(stream, IntPtr.Zero, outType) < 0) { Marshal.ReleaseComObject(outType); return null; }
            Marshal.ReleaseComObject(outType);
            if (reader.GetCurrentMediaType(stream, out IMFMediaType cur) < 0) return null;
            Guid kSize = Mf.MF_MT_FRAME_SIZE;
            cur.GetUINT64(ref kSize, out ulong packed);
            int width = (int)(packed >> 32);
            int height = (int)(packed & 0xFFFFFFFF);
            Guid kStride = Mf.MF_MT_DEFAULT_STRIDE;
            int mfStride = (width > 0 && cur.GetUINT32(ref kStride, out int ds) >= 0 && ds != 0) ? Math.Abs(ds) : 0;
            Marshal.ReleaseComObject(cur);
            if (width <= 0 || height <= 0) return null;
            int dw = Math.Min(decodeWidth, width);
            int dh = Math.Max(1, (int)Math.Round((double)height / width * dw));
            native = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            MfImage.DebugNextCopy = true;
            int stride = 1;
            int index = 0;
            while (true)
            {
                int hr = reader.ReadSample(stream, 0, out _, out int flags, out long ts, out IMFSample? sample);
                if (hr < 0) break;
                if (sample != null)
                {
                    if ((index % stride) == 0)
                    {
                        if (MfImage.CopySample(sample, native, width, height, mfStride))
                        {
                            var scaled = new Bitmap(dw, dh, PixelFormat.Format32bppRgb);
                            using (var gsc = Graphics.FromImage(scaled))
                            {
                                gsc.InterpolationMode = dw >= 600
                                    ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
                                    : System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                gsc.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                                gsc.DrawImage(native, new Rectangle(0, 0, dw, dh), 0, 0, width, height, GraphicsUnit.Pixel);
                            }
                            frames.Add(scaled);
                            starts.Add((int)(ts / 10_000));
                        }
                    }
                    lastTs = (int)(ts / 10_000);
                    Marshal.ReleaseComObject(sample);
                    index++;
                    if (frames.Count >= maxFrames * 2)
                    {
                        int w = 0;
                        for (int i = 0; i < frames.Count; i++)
                        {
                            if ((i & 1) == 0) { frames[w] = frames[i]; starts[w] = starts[i]; w++; }
                            else frames[i].Dispose();
                        }
                        frames.RemoveRange(w, frames.Count - w);
                        starts.RemoveRange(w, starts.Count - w);
                        stride *= 2;
                    }
                }
                if ((flags & Mf.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                if (index > 200_000) break;
            }
            if (frames.Count == 0) return null;
            int span = frames.Count > 1 ? Math.Max(1, (starts[^1] - starts[0]) / Math.Max(1, frames.Count - 1)) : 40;
            int total = Math.Max(lastTs, starts[^1]) + span;
            return new VideoClip(frames.ToArray(), starts.ToArray(), Math.Max(total, starts[^1] + 1));
        }
        catch { foreach (var f in frames) f.Dispose(); return null; }
        finally
        {
            native?.Dispose();
            if (reader != null) Marshal.ReleaseComObject(reader);
            if (mfStarted) { try { Mf.MFShutdown(); } catch { } }
            Wasapi.CoUninitialize();
        }
    }
    public Bitmap FrameAt(int ms, int windowStartMs, int windowEndMs, bool loop)
    {
        int a = Math.Clamp(windowStartMs, 0, Math.Max(0, TotalMs - 1));
        int b = Math.Clamp(windowEndMs, a + 1, TotalMs);
        int span = b - a;
        int local = loop
            ? (span > 0 ? a + (((ms % span) + span) % span) : a)
            : a + Math.Clamp(ms, 0, Math.Max(0, span - 1));
        int idx = 0;
        for (int i = 0; i < _startMs.Length; i++)
        {
            if (_startMs[i] <= local) idx = i; else break;
        }
        return _frames[idx];
    }
    public void Dispose() { foreach (var f in _frames) f.Dispose(); }
}