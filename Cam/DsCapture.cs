using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal sealed class DsCapture : IDisposable
{
    static readonly Guid CLSID_FilterGraph          = new("e436ebb3-524f-11ce-9f53-0020af0ba770");
    static readonly Guid CLSID_CaptureGraphBuilder2  = new("bf87b6e1-8c27-11d0-b3f0-00aa003761c5");
    static readonly Guid CLSID_SampleGrabber         = new("c1f400a0-3f08-11d3-9f0b-006008039e37");
    static readonly Guid CLSID_NullRenderer          = new("c1f400a4-3f08-11d3-9f0b-006008039e37");
    static readonly Guid MEDIATYPE_Video    = new("73646976-0000-0010-8000-00aa00389b71");
    static readonly Guid MEDIASUBTYPE_RGB32 = new("e436eb7e-524f-11ce-9f53-0020af0ba770");
    static readonly Guid PIN_CATEGORY_CAPTURE = new("fb6c4281-0353-11d1-905f-0000c0cc16ba");
    const uint CLSCTX_INPROC_SERVER = 1;
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
                                       ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");
    static object? Create(Guid clsid)
    {
        var c = clsid; var iid = IID_IUnknown;
        return CoCreateInstance(ref c, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out var o) >= 0 ? o : null;
    }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public event Action<IntPtr, int>? FrameArrived;
    object? _graph, _builder, _source, _grabberF, _nullF;
    ISampleGrabber? _grabber;
    IMediaControl? _control;
    GrabberCb? _cb;
    public bool Start(string monikerName)
    {
        try
        {
            _source = Ds.BindFilter(monikerName);
            if (_source == null) return false;
            _graph = Create(CLSID_FilterGraph);
            _builder = Create(CLSID_CaptureGraphBuilder2);
            _grabberF = Create(CLSID_SampleGrabber);
            _nullF = Create(CLSID_NullRenderer);
            if (_graph is not IGraphBuilder graph || _builder is not ICaptureGraphBuilder2 builder
                || _grabberF is not ISampleGrabber grabber || _grabberF is not IBaseFilter grabberBf
                || _nullF is not IBaseFilter nullBf || _source is not IBaseFilter sourceBf)
                return false;
            _grabber = grabber;
            builder.SetFiltergraph(graph);
            var mt = new AM_MEDIA_TYPE { majortype = MEDIATYPE_Video, subtype = MEDIASUBTYPE_RGB32 };
            grabber.SetMediaType(ref mt);
            grabber.SetOneShot(false);
            grabber.SetBufferSamples(false);
            _cb = new GrabberCb(this);
            grabber.SetCallback(_cb, 1);
            if (graph.AddFilter(sourceBf, "Source") < 0) return false;
            if (graph.AddFilter(grabberBf, "Grabber") < 0) return false;
            if (graph.AddFilter(nullBf, "Null") < 0) return false;
            int hr = builder.RenderStream(PIN_CATEGORY_CAPTURE, MEDIATYPE_Video, sourceBf, grabberBf, nullBf);
            if (hr < 0)
                hr = builder.RenderStream(Guid.Empty, Guid.Empty, sourceBf, grabberBf, nullBf);
            if (hr < 0) return false;
            var connected = new AM_MEDIA_TYPE();
            if (grabber.GetConnectedMediaType(ref connected) >= 0 && connected.pbFormat != IntPtr.Zero)
            {
                var vih = Marshal.PtrToStructure<VIDEOINFOHEADER>(connected.pbFormat);
                Width = vih.bmiHeader.biWidth;
                Height = Math.Abs(vih.bmiHeader.biHeight);
                FreeMediaType(ref connected);
            }
            if (Width <= 0 || Height <= 0) return false;
            _control = graph as IMediaControl;
            if (_control == null || _control.Run() < 0) return false;
            return true;
        }
        catch { return false; }
    }
    void OnBuffer(IntPtr p, int len) => FrameArrived?.Invoke(p, len);
    public void Dispose()
    {
        try { _control?.Stop(); } catch { }
        try { if (_grabber != null) _grabber.SetCallback(null, 0); } catch { }
        _cb = null;
        Release(ref _control);
        _grabber = null;
        Release(ref _nullF);
        Release(ref _grabberF);
        Release(ref _source);
        Release(ref _builder);
        Release(ref _graph);
    }
    static void Release<T>(ref T? o) where T : class
    {
        if (o != null) { try { Marshal.ReleaseComObject(o); } catch { } o = null; }
    }
    static void FreeMediaType(ref AM_MEDIA_TYPE mt)
    {
        if (mt.cbFormat != 0 && mt.pbFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mt.pbFormat);
        mt.pbFormat = IntPtr.Zero; mt.cbFormat = 0;
        if (mt.pUnk != IntPtr.Zero) { Marshal.Release(mt.pUnk); mt.pUnk = IntPtr.Zero; }
    }
    sealed class GrabberCb : ISampleGrabberCB
    {
        readonly DsCapture _owner;
        public GrabberCb(DsCapture o) => _owner = o;
        public int SampleCB(double t, IntPtr pSample) => 0;
        public int BufferCB(double t, IntPtr pBuffer, int len)
        {
            if (pBuffer != IntPtr.Zero && len > 0) _owner.OnBuffer(pBuffer, len);
            return 0;
        }
    }
}
[StructLayout(LayoutKind.Sequential)]
internal struct AM_MEDIA_TYPE
{
    public Guid majortype;
    public Guid subtype;
    [MarshalAs(UnmanagedType.Bool)] public bool bFixedSizeSamples;
    [MarshalAs(UnmanagedType.Bool)] public bool bTemporalCompression;
    public int lSampleSize;
    public Guid formattype;
    public IntPtr pUnk;
    public int cbFormat;
    public IntPtr pbFormat;
}
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public int biSize, biWidth, biHeight;
    public short biPlanes, biBitCount;
    public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
}
[StructLayout(LayoutKind.Sequential)]
internal struct VIDEOINFOHEADER
{
    public int rcSrcL, rcSrcT, rcSrcR, rcSrcB;
    public int rcTgtL, rcTgtT, rcTgtR, rcTgtB;
    public int dwBitRate, dwBitErrorRate;
    public long AvgTimePerFrame;
    public BITMAPINFOHEADER bmiHeader;
}
[ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphBuilder
{
    [PreserveSig] int AddFilter(IBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);
}
[ComImport, Guid("93e5a4e0-2d50-11d2-abfa-00a0c9c6e38d"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICaptureGraphBuilder2
{
    [PreserveSig] int SetFiltergraph(IGraphBuilder pfg);
    [PreserveSig] int GetFiltergraph(out IGraphBuilder ppfg);
    [PreserveSig] int SetOutputFileName([In, MarshalAs(UnmanagedType.LPStruct)] Guid pType,
                                        [In, MarshalAs(UnmanagedType.LPWStr)] string lpstrFile,
                                        out IBaseFilter ppf, out IntPtr ppSink);
    [PreserveSig] int FindInterface([In, MarshalAs(UnmanagedType.LPStruct)] Guid pCategory,
                                    [In, MarshalAs(UnmanagedType.LPStruct)] Guid pType, IBaseFilter pf,
                                    [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                                    [MarshalAs(UnmanagedType.IUnknown)] out object ppint);
    [PreserveSig] int RenderStream([In, MarshalAs(UnmanagedType.LPStruct)] Guid pCategory,
                                   [In, MarshalAs(UnmanagedType.LPStruct)] Guid pType,
                                   [MarshalAs(UnmanagedType.IUnknown)] object pSource,
                                   IBaseFilter? pfCompressor, IBaseFilter? pfRenderer);
}
[ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IBaseFilter
{
}
[ComImport, Guid("6b652fff-11fe-4fce-92ad-0266b5d7c78f"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISampleGrabber
{
    [PreserveSig] int SetOneShot([MarshalAs(UnmanagedType.Bool)] bool oneShot);
    [PreserveSig] int SetMediaType(ref AM_MEDIA_TYPE pmt);
    [PreserveSig] int GetConnectedMediaType(ref AM_MEDIA_TYPE pmt);
    [PreserveSig] int SetBufferSamples([MarshalAs(UnmanagedType.Bool)] bool bufferThem);
    [PreserveSig] int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);
    [PreserveSig] int GetCurrentSample(out IntPtr ppSample);
    [PreserveSig] int SetCallback(ISampleGrabberCB? pCallback, int whichMethodToCallback);
}
[ComImport, Guid("0579154a-2b53-4994-b0d0-e773148eff85"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISampleGrabberCB
{
    [PreserveSig] int SampleCB(double sampleTime, IntPtr pSample);
    [PreserveSig] int BufferCB(double sampleTime, IntPtr pBuffer, int bufferLen);
}
[ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMediaControl
{
    [PreserveSig] int _GetTypeInfoCount();
    [PreserveSig] int _GetTypeInfo();
    [PreserveSig] int _GetIDsOfNames();
    [PreserveSig] int _Invoke();
    [PreserveSig] int Run();
    [PreserveSig] int Pause();
    [PreserveSig] int Stop();
}