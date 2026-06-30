using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal sealed class VideoRecorder
{
    private Thread? _thread;
    private volatile bool _running;
    private Rectangle _zone;
    private string _path = "";
    private int _fps;
    private AudioOptions _audio = AudioOptions.Default;
    private Exception? _error;
    private readonly object _writeLock = new();
    public bool Recording => _running;
    public string? LastError => _error?.Message;
    public void Start(Rectangle zone, string path, int fps = 30, AudioOptions? audio = null)
    {
        if (_running) return;
        int w = zone.Width & ~1;
        int h = zone.Height & ~1;
        _zone = new Rectangle(zone.X, zone.Y, w, h);
        _path = path;
        _fps = Math.Clamp(fps, 10, 60);
        _audio = audio ?? AudioOptions.Default;
        _error = null;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "VertiMask.Video" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }
    public string? Stop()
    {
        if (!_running) return null;
        _running = false;
        _thread?.Join(8000);
        _thread = null;
        return _error == null ? _path : null;
    }
    private void Loop()
    {
        IMFSinkWriter? writer = null;
        IMFMediaType? outType = null, inType = null;
        IMFAttributes? attr = null;
        bool mfStarted = false;
        try
        {
            Check(Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL), "MFStartup");
            mfStarted = true;
            int w = _zone.Width, h = _zone.Height;
            int bitrate = Math.Max(1_000_000, (int)((long)w * h * _fps / 10));
            Check(Mf.MFCreateAttributes(out attr, 2), "MFCreateAttributes");
            Guid kHw = Mf.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
            Guid kThr = Mf.MF_SINK_WRITER_DISABLE_THROTTLING;
            attr.SetUINT32(ref kHw, 1);
            attr.SetUINT32(ref kThr, 1);
            Check(Mf.MFCreateSinkWriterFromURL(_path, IntPtr.Zero, attr, out IntPtr pWriter), "MFCreateSinkWriterFromURL");
            writer = (IMFSinkWriter)Marshal.GetObjectForIUnknown(pWriter);
            Marshal.Release(pWriter);
            Check(Mf.MFCreateMediaType(out outType), "MFCreateMediaType(out)");
            SetGuid(outType, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Video);
            SetGuid(outType, Mf.MF_MT_SUBTYPE, Mf.MFVideoFormat_H264);
            SetU32(outType, Mf.MF_MT_AVG_BITRATE, bitrate);
            SetU32(outType, Mf.MF_MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            SetU64(outType, Mf.MF_MT_FRAME_SIZE, Mf.Pack2((uint)w, (uint)h));
            SetU64(outType, Mf.MF_MT_FRAME_RATE, Mf.Pack2((uint)_fps, 1));
            SetU64(outType, Mf.MF_MT_PIXEL_ASPECT_RATIO, Mf.Pack2(1, 1));
            Check(writer.AddStream(outType, out int videoIndex), "AddStream(video)");
            Check(Mf.MFCreateMediaType(out inType), "MFCreateMediaType(in)");
            SetGuid(inType, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Video);
            SetGuid(inType, Mf.MF_MT_SUBTYPE, Mf.MFVideoFormat_RGB32);
            SetU32(inType, Mf.MF_MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
            SetU32(inType, Mf.MF_MT_DEFAULT_STRIDE, w * 4);
            SetU64(inType, Mf.MF_MT_FRAME_SIZE, Mf.Pack2((uint)w, (uint)h));
            SetU64(inType, Mf.MF_MT_FRAME_RATE, Mf.Pack2((uint)_fps, 1));
            SetU64(inType, Mf.MF_MT_PIXEL_ASPECT_RATIO, Mf.Pack2(1, 1));
            Check(writer.SetInputMediaType(videoIndex, inType, null), "SetInputMediaType(video)");
            int audioIndex = -1;
            if (_audio.System || _audio.Mic)
            {
                try { audioIndex = AddAudioStream(writer); }
                catch { audioIndex = -1; }
            }
            Check(writer.BeginWriting(), "BeginWriting");
            long frameDuration = 10_000_000L / _fps;
            int bufferSize = w * h * 4;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            var sw = Stopwatch.StartNew();
            AudioCapture? audio = null;
            Thread? audioInit = null;
            if (audioIndex >= 0)
            {
                audio = new AudioCapture();
                int ai = audioIndex;
                AudioCapture ac = audio;
                audioInit = new Thread(() => ac.Start(sw, (pcm, t) => WriteAudioFrame(writer, ai, pcm, t), _audio))
                {
                    IsBackground = true,
                    Name = "VertiMask.AudioInit",
                };
                audioInit.SetApartmentState(ApartmentState.MTA);
                audioInit.Start();
            }
            long nextDueMs = 0;
            int periodMs = 1000 / _fps;
            while (_running)
            {
                g.CopyFromScreen(_zone.X, _zone.Y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                long timeHns = sw.ElapsedTicks * 10_000_000L / Stopwatch.Frequency;
                WriteVideoFrame(writer, videoIndex, bmp, bufferSize, timeHns, frameDuration, w, h);
                nextDueMs += periodMs;
                long sleep = nextDueMs - sw.ElapsedMilliseconds;
                if (sleep > 1) Thread.Sleep((int)sleep);
            }
            audioInit?.Join(3000);
            audio?.Stop();
            Check(writer.DoFinalize(), "Finalize");
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally
        {
            if (inType != null) Marshal.ReleaseComObject(inType);
            if (outType != null) Marshal.ReleaseComObject(outType);
            if (attr != null) Marshal.ReleaseComObject(attr);
            if (writer != null) Marshal.ReleaseComObject(writer);
            if (mfStarted) { try { Mf.MFShutdown(); } catch {  } }
        }
    }
    private void WriteVideoFrame(IMFSinkWriter writer, int index, Bitmap bmp, int bufferSize,
        long timeHns, long durationHns, int w, int h)
    {
        Check(Mf.MFCreateMemoryBuffer(bufferSize, out IMFMediaBuffer buffer), "MFCreateMemoryBuffer");
        try
        {
            buffer.Lock(out IntPtr dest, out _, out _);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                Mf.MFCopyImage(dest, w * 4, data.Scan0, data.Stride, w * 4, h);
            }
            finally
            {
                bmp.UnlockBits(data);
                buffer.Unlock();
            }
            buffer.SetCurrentLength(bufferSize);
            Check(Mf.MFCreateSample(out IMFSample sample), "MFCreateSample");
            try
            {
                sample.AddBuffer(buffer);
                sample.SetSampleTime(timeHns);
                sample.SetSampleDuration(durationHns);
                lock (_writeLock) writer.WriteSample(index, sample);
            }
            finally { Marshal.ReleaseComObject(sample); }
        }
        finally { Marshal.ReleaseComObject(buffer); }
    }
    private const int AudioRate = AudioCapture.SampleRate;
    private const int AudioChannels = AudioCapture.Channels;
    private const int AudioBits = 16;
    private static int AddAudioStream(IMFSinkWriter writer)
    {
        const int blockAlign = AudioChannels * AudioBits / 8;
        const int avgBytes = AudioRate * blockAlign;
        Check(Mf.MFCreateMediaType(out IMFMediaType outType), "MFCreateMediaType(aac)");
        SetGuid(outType, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Audio);
        SetGuid(outType, Mf.MF_MT_SUBTYPE, Mf.MFAudioFormat_AAC);
        SetU32(outType, Mf.MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
        SetU32(outType, Mf.MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
        SetU32(outType, Mf.MF_MT_AUDIO_BITS_PER_SAMPLE, AudioBits);
        SetU32(outType, Mf.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 20000);
        Check(writer.AddStream(outType, out int audioIndex), "AddStream(audio)");
        Marshal.ReleaseComObject(outType);
        Check(Mf.MFCreateMediaType(out IMFMediaType inType), "MFCreateMediaType(pcm)");
        SetGuid(inType, Mf.MF_MT_MAJOR_TYPE, Mf.MFMediaType_Audio);
        SetGuid(inType, Mf.MF_MT_SUBTYPE, Mf.MFAudioFormat_PCM);
        SetU32(inType, Mf.MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate);
        SetU32(inType, Mf.MF_MT_AUDIO_NUM_CHANNELS, AudioChannels);
        SetU32(inType, Mf.MF_MT_AUDIO_BITS_PER_SAMPLE, AudioBits);
        SetU32(inType, Mf.MF_MT_AUDIO_BLOCK_ALIGNMENT, blockAlign);
        SetU32(inType, Mf.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, avgBytes);
        Check(writer.SetInputMediaType(audioIndex, inType, null), "SetInputMediaType(audio)");
        Marshal.ReleaseComObject(inType);
        return audioIndex;
    }
    private void WriteAudioFrame(IMFSinkWriter writer, int index, byte[] pcm, long timeHns)
    {
        if (pcm.Length == 0) return;
        const int blockAlign = AudioChannels * AudioBits / 8;
        long durationHns = (long)(pcm.Length / blockAlign) * 10_000_000L / AudioRate;
        Check(Mf.MFCreateMemoryBuffer(pcm.Length, out IMFMediaBuffer buffer), "MFCreateMemoryBuffer(audio)");
        try
        {
            buffer.Lock(out IntPtr dest, out _, out _);
            Marshal.Copy(pcm, 0, dest, pcm.Length);
            buffer.Unlock();
            buffer.SetCurrentLength(pcm.Length);
            Check(Mf.MFCreateSample(out IMFSample sample), "MFCreateSample(audio)");
            try
            {
                sample.AddBuffer(buffer);
                sample.SetSampleTime(timeHns);
                sample.SetSampleDuration(durationHns);
                lock (_writeLock) writer.WriteSample(index, sample);
            }
            finally { Marshal.ReleaseComObject(sample); }
        }
        finally { Marshal.ReleaseComObject(buffer); }
    }
    private static void SetGuid(IMFMediaType t, Guid key, Guid val)
    { Guid k = key, v = val; Check(t.SetGUID(ref k, ref v), "SetGUID"); }
    private static void SetU32(IMFMediaType t, Guid key, int val)
    { Guid k = key; Check(t.SetUINT32(ref k, val), "SetUINT32"); }
    private static void SetU64(IMFMediaType t, Guid key, ulong val)
    { Guid k = key; Check(t.SetUINT64(ref k, val), "SetUINT64"); }
    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new InvalidOperationException($"{what} a echoue (HRESULT 0x{hr:X8})");
    }
}