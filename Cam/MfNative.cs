using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal static class Mf
{
    public const int MF_VERSION = 0x00020070;
    public const int MFSTARTUP_FULL = 0;
    [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType ppMFType);
    [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample ppIMFSample);
    [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);
    [DllImport("mfplat.dll")]
    public static extern int MFCopyImage(IntPtr pDest, int lDestStride, IntPtr pSrc, int lSrcStride,
        int dwWidthInBytes, int dwLines);
    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        IntPtr pByteStream, IMFAttributes? pAttributes, out IntPtr ppSinkWriter);
    [DllImport("mf.dll")]
    public static extern int MFEnumDeviceSources(IMFAttributes pAttributes,
        out IntPtr pppSourceActivate, out int pcSourceActivate);
    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSourceReaderFromMediaSource(
        [MarshalAs(UnmanagedType.IUnknown)] object pMediaSource,
        IMFAttributes? pAttributes, out IMFSourceReader ppSourceReader);
    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSourceReaderFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszURL,
        IMFAttributes? pAttributes, out IMFSourceReader ppSourceReader);
    public const int MF_SOURCE_READERF_ENDOFSTREAM = 0x2;
    public const int MF_SOURCE_READER_ALL_STREAMS = unchecked((int)0xFFFFFFFE);
    public const short VT_I8 = 20;
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
    public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid IID_IMFMediaSource = new("279a808d-aec7-40c8-9c6b-a6b492c78a66");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK =
        new("58d3d7e6-7d0f-4c68-9c46-18bc9fc6a032");
    public const int MF_SOURCE_READER_FIRST_VIDEO_STREAM = unchecked((int)0xFFFFFFFC);
    [DllImport("mf.dll")]
    public static extern int MFCreateDeviceSource(IMFAttributes pAttributes,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppSource);
    public const int MFVideoInterlace_Progressive = 2;
    public static ulong Pack2(uint hi, uint lo) => ((ulong)hi << 32) | lo;
}
[ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int Reserved_GetItem(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_GetItemType(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_CompareItem(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int Reserved_Compare(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int GetUINT32(ref Guid key, out int value);
    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);
    [PreserveSig] int Reserved_GetDouble(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_GetGUID(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_GetStringLength(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_GetString(IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    [PreserveSig] int Reserved_GetAllocatedString(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int Reserved_GetBlobSize(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_GetBlob(IntPtr a, IntPtr b, IntPtr c, IntPtr d);
    [PreserveSig] int Reserved_GetAllocatedBlob(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int Reserved_GetUnknown(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int Reserved_SetItem(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_DeleteItem(IntPtr a);
    [PreserveSig] int Reserved_DeleteAllItems();
    [PreserveSig] int SetUINT32(ref Guid key, int value);
    [PreserveSig] int SetUINT64(ref Guid key, ulong value);
    [PreserveSig] int Reserved_SetDouble(IntPtr a, double b);
    [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
    [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int Reserved_SetBlob(IntPtr a, IntPtr b, int c);
    [PreserveSig] int Reserved_SetUnknown(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_LockStore();
    [PreserveSig] int Reserved_UnlockStore();
    [PreserveSig] int Reserved_GetCount(IntPtr a);
    [PreserveSig] int Reserved_GetItemByIndex(int a, IntPtr b, IntPtr c);
    [PreserveSig] int Reserved_CopyAllItems(IntPtr a);
}
[ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType : IMFAttributes
{
}
[ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    [PreserveSig] int A00(); [PreserveSig] int A01(); [PreserveSig] int A02();
    [PreserveSig] int A03(); [PreserveSig] int A04(); [PreserveSig] int A05();
    [PreserveSig] int A06(); [PreserveSig] int A07(); [PreserveSig] int A08();
    [PreserveSig] int A09(); [PreserveSig] int A10(); [PreserveSig] int A11();
    [PreserveSig] int A12(); [PreserveSig] int A13(); [PreserveSig] int A14();
    [PreserveSig] int A15(); [PreserveSig] int A16(); [PreserveSig] int A17();
    [PreserveSig] int A18(); [PreserveSig] int A19(); [PreserveSig] int A20();
    [PreserveSig] int A21(); [PreserveSig] int A22(); [PreserveSig] int A23();
    [PreserveSig] int A24(); [PreserveSig] int A25(); [PreserveSig] int A26();
    [PreserveSig] int A27(); [PreserveSig] int A28(); [PreserveSig] int A29();
    [PreserveSig] int Reserved_GetSampleFlags(IntPtr a);
    [PreserveSig] int Reserved_SetSampleFlags(int a);
    [PreserveSig] int Reserved_GetSampleTime(IntPtr a);
    [PreserveSig] int SetSampleTime(long hnsSampleTime);
    [PreserveSig] int Reserved_GetSampleDuration(IntPtr a);
    [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
    [PreserveSig] int Reserved_GetBufferCount(IntPtr a);
    [PreserveSig] int GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer);
    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
    [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);
}
[ComImport, Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate
{
    [PreserveSig] int A00(); [PreserveSig] int A01(); [PreserveSig] int A02();
    [PreserveSig] int A03(); [PreserveSig] int A04(); [PreserveSig] int A05();
    [PreserveSig] int A06(); [PreserveSig] int A07(); [PreserveSig] int A08();
    [PreserveSig] int A09();
    [PreserveSig] int GetAllocatedString(ref Guid key,
        [MarshalAs(UnmanagedType.LPWStr)] out string value, out int length);
    [PreserveSig] int A11();
    [PreserveSig] int A12(); [PreserveSig] int A13(); [PreserveSig] int A14();
    [PreserveSig] int A15(); [PreserveSig] int A16(); [PreserveSig] int A17();
    [PreserveSig] int A18(); [PreserveSig] int A19(); [PreserveSig] int A20();
    [PreserveSig] int A21(); [PreserveSig] int A22(); [PreserveSig] int A23();
    [PreserveSig] int A24(); [PreserveSig] int A25(); [PreserveSig] int A26();
    [PreserveSig] int A27(); [PreserveSig] int A28(); [PreserveSig] int A29();
    [PreserveSig] int ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}
[ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int Reserved_GetStreamSelection(int a, IntPtr b);
    [PreserveSig] int SetStreamSelection(int dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
    [PreserveSig] int Reserved_GetNativeMediaType(int a, int b, IntPtr c);
    [PreserveSig] int GetCurrentMediaType(int dwStreamIndex, out IMFMediaType ppMediaType);
    [PreserveSig] int SetCurrentMediaType(int dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
    [PreserveSig] int SetCurrentPosition(ref Guid guidTimeFormat, ref PROPVARIANT varPosition);
    [PreserveSig] int ReadSample(int dwStreamIndex, int dwControlFlags,
        out int pdwActualStreamIndex, out int pdwStreamFlags, out long pllTimestamp, out IMFSample? ppSample);
}
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public short vt;
    public short wReserved1;
    public short wReserved2;
    public short wReserved3;
    public long  data;
}
[ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
    [PreserveSig] int Unlock();
    [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
    [PreserveSig] int SetCurrentLength(int cbCurrentLength);
    [PreserveSig] int GetMaxLength(out int pcbMaxLength);
}
[ComImport, Guid("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMF2DBuffer
{
    [PreserveSig] int Lock2D(out IntPtr pbScanline0, out int plPitch);
    [PreserveSig] int Unlock2D();
    [PreserveSig] int Reserved_GetScanline0AndPitch(IntPtr a, IntPtr b);
    [PreserveSig] int Reserved_IsContiguousFormat(IntPtr a);
    [PreserveSig] int Reserved_GetContiguousLength(IntPtr a);
    [PreserveSig] int Reserved_ContiguousCopyTo(IntPtr a, int b);
    [PreserveSig] int Reserved_ContiguousCopyFrom(IntPtr a, int b);
}
internal static class MfImage
{
    public static volatile bool DebugNextCopy;
    public static bool CopySample(IMFSample sample, Bitmap frame, int width, int height, int mfStride)
    {
        if (sample.GetBufferByIndex(0, out IMFMediaBuffer buf0) >= 0 && buf0 != null)
        {
            try { if (TryCopy2D(buf0, frame, width, height, mfStride, "origin")) return true; }
            finally { Marshal.ReleaseComObject(buf0); }
        }
        if (sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer) >= 0)
        {
            try
            {
                if (TryCopy2D(buffer, frame, width, height, mfStride, "contig")) return true;
                buffer.Lock(out IntPtr pData, out _, out int curLen);
                int srcStride = ResolveStride(width, height, curLen, mfStride);
                if (DebugNextCopy)
                {
                    RoleplayOverlay.Logger.Info($"[MFdbg] contiguous W={width} H={height} curLen={curLen} srcStride={srcStride} (Width*4={width * 4}, mfStride={mfStride})");
                    DebugNextCopy = false;
                }
                var bd = frame.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                Mf.MFCopyImage(bd.Scan0, bd.Stride, pData, srcStride, width * 4, height);
                frame.UnlockBits(bd);
                buffer.Unlock();
                return true;
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }
        return false;
    }
    private static bool TryCopy2D(IMFMediaBuffer buf, Bitmap frame, int width, int height, int mfStride, string tag)
    {
        if (buf is IMF2DBuffer b2d && b2d.Lock2D(out IntPtr scan0, out int pitch) >= 0)
        {
            try
            {
                if (DebugNextCopy)
                {
                    RoleplayOverlay.Logger.Info($"[MFdbg] 2DBuffer({tag}) W={width} H={height} pitch={pitch} (Width*4={width * 4}, mfStride={mfStride})");
                    DebugNextCopy = false;
                }
                var bd = frame.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                Mf.MFCopyImage(bd.Scan0, bd.Stride, scan0, pitch, width * 4, height);
                frame.UnlockBits(bd);
                return true;
            }
            finally { b2d.Unlock2D(); }
        }
        return false;
    }
    private static int ResolveStride(int width, int height, int curLen, int mfStride)
    {
        int min = width * 4;
        if (height > 0 && curLen >= (long)min * height)
        {
            int best = 0;
            for (int h = height; h <= height + 63; h++)
            {
                if (curLen % h != 0) continue;
                int s = curLen / h;
                if (s < min || (s & 3) != 0 || s > min + 256) continue;
                if (best == 0 || s < best) best = s;
            }
            if (best != 0) return best;
        }
        if (mfStride > min) return mfStride;
        return min;
    }
}
[ComImport, Guid("588d72ab-5bc1-496a-8714-b70617141b25"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSinkWriter
{
    [PreserveSig] int AddStream(IMFMediaType pTargetMediaType, out int pdwStreamIndex);
    [PreserveSig] int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes? pEncodingParameters);
    [PreserveSig] int BeginWriting();
    [PreserveSig] int WriteSample(int dwStreamIndex, IMFSample pSample);
    [PreserveSig] int Reserved_SendStreamTick(int a, long b);
    [PreserveSig] int Reserved_PlaceMarker(int a, IntPtr b);
    [PreserveSig] int Reserved_NotifyEndOfSegment(int a);
    [PreserveSig] int Flush(int dwStreamIndex);
    [PreserveSig] int DoFinalize();
}