using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal struct AudioOptions
{
    public bool System;
    public bool Mic;
    public float SystemGain;
    public float MicGain;
    public bool MicGate;
    public float GateThreshold;
    public static AudioOptions Default => new()
    {
        System = true, Mic = true, SystemGain = 1f, MicGain = 1f, MicGate = false, GateThreshold = 0.02f,
    };
}
internal sealed class AudioCapture
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    private readonly object _sync = new();
    private SourceCapture? _system;
    private SourceCapture? _mic;
    private Thread? _mixer;
    private volatile bool _running;
    private AudioOptions _opt = AudioOptions.Default;
    private float _gateEnv;
    private float _gateGain;
    public bool HasAudio { get; private set; }
    public void Start(System.Diagnostics.Stopwatch clock, Action<byte[], long> writeAudio, AudioOptions opt)
    {
        if (_running) return;
        _opt = opt;
        _gateEnv = 0f;
        _gateGain = 1f;
        _system = opt.System ? SourceCapture.TryCreate(loopback: true) : null;
        _mic = opt.Mic ? SourceCapture.TryCreate(loopback: false) : null;
        HasAudio = _system != null || _mic != null;
        if (!HasAudio) return;
        _running = true;
        _mixer = new Thread(() => MixLoop(clock, writeAudio))
        {
            IsBackground = true,
            Name = "VertiMask.AudioMix",
        };
        _mixer.SetApartmentState(ApartmentState.MTA);
        _mixer.Start();
    }
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _mixer?.Join(3000);
        _mixer = null;
        lock (_sync)
        {
            _system?.Dispose(); _system = null;
            _mic?.Dispose(); _mic = null;
        }
    }
    private void MixLoop(System.Diagnostics.Stopwatch clock, Action<byte[], long> writeAudio)
    {
        Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
        try
        {
            _system?.StartCapture();
            _mic?.StartCapture();
            long produced = 0;
            var sysBuf = new List<float>(SampleRate);
            var micBuf = new List<float>(SampleRate);
            while (_running)
            {
                Thread.Sleep(10);
                _system?.Drain(sysBuf);
                _mic?.Drain(micBuf);
                long target = clock.ElapsedTicks * SampleRate / System.Diagnostics.Stopwatch.Frequency;
                long need = target - produced;
                if (need <= 0) continue;
                int sysFrames = sysBuf.Count / 2;
                int micFrames = micBuf.Count / 2;
                int take = (int)need;
                float sysG = _opt.SystemGain, micG = _opt.MicGain;
                bool gate = _opt.MicGate;
                float thr = _opt.GateThreshold;
                var pcm = new byte[take * 2 * 2];
                int o = 0;
                for (int i = 0; i < take; i++)
                {
                    float l = 0, r = 0;
                    if (i < sysFrames) { l += sysBuf[i * 2] * sysG; r += sysBuf[i * 2 + 1] * sysG; }
                    if (i < micFrames)
                    {
                        float ml = micBuf[i * 2], mr = micBuf[i * 2 + 1];
                        if (gate) ApplyGate(ref ml, ref mr, thr);
                        l += ml * micG; r += mr * micG;
                    }
                    WriteSample16(pcm, ref o, l);
                    WriteSample16(pcm, ref o, r);
                }
                RemoveFront(sysBuf, Math.Min(sysFrames, take) * 2);
                RemoveFront(micBuf, Math.Min(micFrames, take) * 2);
                long timeHns = produced * 10_000_000L / SampleRate;
                writeAudio(pcm, timeHns);
                produced += take;
            }
            _system?.StopCapture();
            _mic?.StopCapture();
        }
        catch {  }
        finally { Wasapi.CoUninitialize(); }
    }
    private void ApplyGate(ref float l, ref float r, float threshold)
    {
        float level = Math.Max(Math.Abs(l), Math.Abs(r));
        float envCoeff = level > _gateEnv ? 0.02f : 0.0008f;
        _gateEnv += (level - _gateEnv) * envCoeff;
        float open = Math.Max(0.001f, threshold);
        float close = open * 0.6f;
        float target = _gateEnv > open ? 1f : (_gateEnv < close ? 0f : _gateGain);
        float gCoeff = target > _gateGain ? 0.02f : 0.0005f;
        _gateGain += (target - _gateGain) * gCoeff;
        l *= _gateGain;
        r *= _gateGain;
    }
    private static void WriteSample16(byte[] buf, ref int o, float v)
    {
        v = Math.Clamp(v, -1f, 1f);
        short s = (short)(v * 32767f);
        buf[o++] = (byte)(s & 0xFF);
        buf[o++] = (byte)((s >> 8) & 0xFF);
    }
    private static void RemoveFront(List<float> list, int count)
    {
        if (count <= 0) return;
        if (count >= list.Count) { list.Clear(); return; }
        list.RemoveRange(0, count);
    }
}
internal sealed class SourceCapture : IDisposable
{
    private readonly Wasapi.IAudioClient _client;
    private readonly Wasapi.IAudioCaptureClient _capture;
    private readonly int _srcRate;
    private readonly int _srcChannels;
    private readonly bool _srcFloat;
    private readonly bool _loopback;
    private readonly object _lock = new();
    private readonly List<float> _pending = new();
    private double _resamplePos;
    private float _lastL, _lastR;
    private Thread? _pump;
    private volatile bool _running;
    private SourceCapture(Wasapi.IAudioClient client, Wasapi.IAudioCaptureClient capture,
        int rate, int channels, bool isFloat, bool loopback)
    {
        _client = client; _capture = capture;
        _srcRate = rate; _srcChannels = channels; _srcFloat = isFloat; _loopback = loopback;
    }
    public static SourceCapture? TryCreate(bool loopback)
    {
        try
        {
            var enumType = Type.GetTypeFromCLSID(Wasapi.CLSID_MMDeviceEnumerator)!;
            var enumerator = (Wasapi.IMMDeviceEnumerator)Activator.CreateInstance(enumType)!;
            int dataFlow = loopback ? Wasapi.eRender : Wasapi.eCapture;
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, Wasapi.eConsole, out Wasapi.IMMDevice dev) < 0 || dev == null)
                return null;
            Guid iidAudioClient = Wasapi.IID_IAudioClient;
            if (dev.Activate(ref iidAudioClient, Wasapi.CLSCTX_ALL, IntPtr.Zero, out object obj) < 0)
                return null;
            var client = (Wasapi.IAudioClient)obj;
            if (client.GetMixFormat(out IntPtr pFmt) < 0) return null;
            var wf = Marshal.PtrToStructure<Wasapi.WAVEFORMATEX>(pFmt);
            int rate = (int)wf.nSamplesPerSec;
            int channels = wf.nChannels;
            bool isFloat = wf.wBitsPerSample == 32;
            if (wf.wBitsPerSample != 32 && wf.wBitsPerSample != 16) { Marshal.FreeCoTaskMem(pFmt); return null; }
            int flags = loopback ? Wasapi.AUDCLNT_STREAMFLAGS_LOOPBACK : 0;
            const long bufDuration = 2_000_000;
            int hr = client.Initialize(Wasapi.AUDCLNT_SHAREMODE_SHARED, flags, bufDuration, 0, pFmt, IntPtr.Zero);
            Marshal.FreeCoTaskMem(pFmt);
            if (hr < 0) return null;
            Guid iidCap = Wasapi.IID_IAudioCaptureClient;
            if (client.GetService(ref iidCap, out object capObj) < 0) return null;
            var capture = (Wasapi.IAudioCaptureClient)capObj;
            return new SourceCapture(client, capture, rate, channels, isFloat, loopback);
        }
        catch { return null; }
    }
    public void StartCapture()
    {
        _client.Start();
        _running = true;
        _pump = new Thread(Pump) { IsBackground = true, Name = "VertiMask.Audio" };
        _pump.SetApartmentState(ApartmentState.MTA);
        _pump.Start();
    }
    public void StopCapture()
    {
        _running = false;
        _pump?.Join(2000);
        _pump = null;
        try { _client.Stop(); } catch { }
    }
    public void Drain(List<float> dst)
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            dst.AddRange(_pending);
            _pending.Clear();
        }
    }
    private void Pump()
    {
        while (_running)
        {
            Thread.Sleep(5);
            try
            {
                while (_capture.GetNextPacketSize(out int packetFrames) >= 0 && packetFrames > 0)
                {
                    if (_capture.GetBuffer(out IntPtr pData, out int frames, out int flags, out _, out _) < 0)
                        break;
                    bool silent = (flags & Wasapi.AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                    ConvertAndAppend(pData, frames, silent);
                    _capture.ReleaseBuffer(frames);
                }
            }
            catch {  }
        }
    }
    private unsafe void ConvertAndAppend(IntPtr pData, int frames, bool silent)
    {
        if (frames <= 0) return;
        var srcL = new float[frames];
        var srcR = new float[frames];
        if (silent)
        {
        }
        else if (_srcFloat)
        {
            float* p = (float*)pData;
            for (int i = 0; i < frames; i++)
            {
                int b = i * _srcChannels;
                srcL[i] = p[b];
                srcR[i] = _srcChannels >= 2 ? p[b + 1] : p[b];
            }
        }
        else
        {
            short* p = (short*)pData;
            for (int i = 0; i < frames; i++)
            {
                int b = i * _srcChannels;
                srcL[i] = p[b] / 32768f;
                srcR[i] = _srcChannels >= 2 ? p[b + 1] / 32768f : p[b] / 32768f;
            }
        }
        lock (_lock)
        {
            if (_srcRate == AudioCapture.SampleRate)
            {
                for (int i = 0; i < frames; i++) { _pending.Add(srcL[i]); _pending.Add(srcR[i]); }
                if (frames > 0) { _lastL = srcL[frames - 1]; _lastR = srcR[frames - 1]; }
            }
            else
            {
                double step = (double)_srcRate / AudioCapture.SampleRate;
                double pos = _resamplePos;
                while (pos < frames)
                {
                    int i0 = (int)pos;
                    double frac = pos - i0;
                    float aL = i0 == 0 ? _lastL : srcL[i0 - 1];
                    float aR = i0 == 0 ? _lastR : srcR[i0 - 1];
                    float bL = srcL[i0];
                    float bR = srcR[i0];
                    _pending.Add((float)(aL + (bL - aL) * frac));
                    _pending.Add((float)(aR + (bR - aR) * frac));
                    pos += step;
                }
                _resamplePos = pos - frames;
                _lastL = srcL[frames - 1];
                _lastR = srcR[frames - 1];
            }
        }
    }
    public void Dispose()
    {
        StopCapture();
        try { if (_capture != null) Marshal.ReleaseComObject(_capture); } catch { }
        try { if (_client != null) Marshal.ReleaseComObject(_client); } catch { }
    }
}
internal static class Wasapi
{
    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    public const int eRender = 0;
    public const int eCapture = 1;
    public const int eConsole = 0;
    public const int CLSCTX_ALL = 23;
    public const int AUDCLNT_SHAREMODE_SHARED = 0;
    public const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    public const int COINIT_MULTITHREADED = 0x0;
    [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr p, int coInit);
    [DllImport("ole32.dll")] public static extern void CoUninitialize();
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long hnsBufferDuration,
            long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out int numBufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out int padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr ppData, out int numFramesToRead, out int dwFlags,
            out long devicePosition, out long qpcPosition);
        [PreserveSig] int ReleaseBuffer(int numFramesRead);
        [PreserveSig] int GetNextPacketSize(out int numFramesInNextPacket);
    }
}