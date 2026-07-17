using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal static class Ds
{
    static readonly Guid CLSID_SystemDeviceEnum        = new("62be5d10-60eb-11d0-bd3b-00a0c911ce86");
    static readonly Guid CLSID_VideoInputDeviceCategory = new("860bb310-5d01-11d0-bd3b-00a0c911ce86");
    static readonly Guid IID_ICreateDevEnum            = new("29840822-5b84-11d0-bd3b-00a0c911ce86");
    static readonly Guid IID_IPropertyBag              = new("55272a00-42cb-11ce-8135-00aa004bb851");
    const uint CLSCTX_INPROC_SERVER = 1;
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
                                       ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    const uint COINIT_APARTMENTTHREADED = 0x2;
    const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
    public readonly record struct VideoDevice(string Name, string DevicePath, string MonikerName);
    public static VideoDevice[] ListVideoDevices()
    {
        var devices = new List<VideoDevice>();
        int hr = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        bool ownCom = hr >= 0;
        try
        {
            var clsid = CLSID_SystemDeviceEnum;
            var riid  = IID_ICreateDevEnum;
            if (CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref riid, out var enumObj) < 0)
                return Array.Empty<VideoDevice>();
            var devEnum = (ICreateDevEnum)enumObj;
            var cat = CLSID_VideoInputDeviceCategory;
            if (devEnum.CreateClassEnumerator(ref cat, out var enumMon, 0) != 0 || enumMon == null)
            {
                Marshal.ReleaseComObject(devEnum);
                return Array.Empty<VideoDevice>();
            }
            var buf = new IMoniker[1];
            while (enumMon.Next(1, buf, IntPtr.Zero) == 0 && buf[0] != null)
            {
                try
                {
                    var iid = IID_IPropertyBag;
                    buf[0].BindToStorage(IntPtr.Zero, null, ref iid, out var bagObj);
                    if (bagObj is IPropertyBag bag)
                    {
                        string name = ReadProp(bag, "FriendlyName");
                        string path = ReadProp(bag, "DevicePath");
                        string mk = "";
                        try { buf[0].GetDisplayName(IntPtr.Zero, null, out mk); } catch { }
                        if (!string.IsNullOrWhiteSpace(name))
                            devices.Add(new VideoDevice(name, path, mk ?? ""));
                        Marshal.ReleaseComObject(bag);
                    }
                }
                catch { }
                finally { Marshal.ReleaseComObject(buf[0]); }
            }
            Marshal.ReleaseComObject(enumMon);
            Marshal.ReleaseComObject(devEnum);
        }
        catch { }
        finally { if (ownCom) CoUninitialize(); }
        return devices.ToArray();
    }
    static readonly Guid IID_IBaseFilter = new("56a86895-0ad4-11ce-b03a-0020af0ba770");
    [DllImport("ole32.dll")]
    static extern int CreateBindCtx(int reserved, out System.Runtime.InteropServices.ComTypes.IBindCtx ppbc);
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    static extern int MkParseDisplayName(System.Runtime.InteropServices.ComTypes.IBindCtx pbc,
        string szUserName, out int pchEaten, out IMoniker ppmk);
    public static object? BindFilter(string monikerName)
    {
        if (string.IsNullOrWhiteSpace(monikerName)) return null;
        try
        {
            if (CreateBindCtx(0, out var bind) < 0) return null;
            if (MkParseDisplayName(bind, monikerName, out _, out var mk) < 0 || mk == null) return null;
            var iid = IID_IBaseFilter;
            mk.BindToObject(bind, null, ref iid, out var filter);
            Marshal.ReleaseComObject(mk);
            Marshal.ReleaseComObject(bind);
            return filter;
        }
        catch { return null; }
    }
    static string ReadProp(IPropertyBag bag, string propName)
    {
        try
        {
            object val = "";
            return bag.Read(propName, ref val, IntPtr.Zero) >= 0 && val is string s ? s : "";
        }
        catch { return ""; }
    }
}
[ComImport, Guid("29840822-5b84-11d0-bd3b-00a0c911ce86"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICreateDevEnum
{
    [PreserveSig]
    int CreateClassEnumerator(ref Guid clsidDeviceClass, out IEnumMoniker ppEnum, int dwFlags);
}
[ComImport, Guid("00000102-0000-0000-c000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEnumMoniker
{
    [PreserveSig]
    int Next(int celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IMoniker[] rgelt,
             IntPtr pceltFetched);
    [PreserveSig] int Skip(int celt);
    [PreserveSig] int Reset();
    void Clone(out IEnumMoniker ppenum);
}
[ComImport, Guid("0000000f-0000-0000-c000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMoniker
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load(IntPtr pStm);
    void Save(IntPtr pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
    void GetSizeMax(out long pcbSize);
    void BindToObject(System.Runtime.InteropServices.ComTypes.IBindCtx? pbc, IMoniker? pmkToLeft,
                      ref Guid riidResult, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResult);
    void BindToStorage(IntPtr pbc, IMoniker? pmkToLeft, ref Guid riid,
                       [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
    void Reduce(IntPtr pbc, int dwReduceHowFar, ref IMoniker? ppmkToLeft, out IMoniker ppmkReduced);
    void ComposeWith(IMoniker pmkRight, [MarshalAs(UnmanagedType.Bool)] bool fOnlyIfNotGeneric,
                     out IMoniker ppmkComposite);
    void Enum([MarshalAs(UnmanagedType.Bool)] bool fForward, out IEnumMoniker ppenumMoniker);
    [PreserveSig] int IsEqual(IMoniker pmkOtherMoniker);
    void Hash(out int pdwHash);
    [PreserveSig] int IsRunning(IntPtr pbc, IMoniker? pmkToLeft, IMoniker? pmkNewlyRunning);
    void GetTimeOfLastChange(IntPtr pbc, IMoniker? pmkToLeft, out long pFileTime);
    void Inverse(out IMoniker ppmk);
    void CommonPrefixWith(IMoniker pmkOther, out IMoniker ppmkPrefix);
    void RelativePathTo(IMoniker pmkOther, out IMoniker ppmkRelPath);
    void GetDisplayName(IntPtr pbc, IMoniker? pmkToLeft,
                        [MarshalAs(UnmanagedType.LPWStr)] out string ppszDisplayName);
    void ParseDisplayName(IntPtr pbc, IMoniker pmkToLeft,
                          [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
                          out int pchEaten, out IMoniker ppmkOut);
    [PreserveSig] int IsSystemMoniker(out int pdwMksys);
}
[ComImport, Guid("55272a00-42cb-11ce-8135-00aa004bb851"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyBag
{
    [PreserveSig]
    int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar, IntPtr pErrorLog);
    [PreserveSig]
    int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
}