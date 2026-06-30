using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace RoleplayOverlay.Cam;
internal static class CameraFilter
{
    public enum Preset { Aucun, Naturel, Studio, Portrait, Chaud, Froid, NoirBlanc, Cinema }
    public readonly struct Params
    {
        public readonly float Brightness;
        public readonly float Contrast;
        public readonly float Saturation;
        public readonly float Warmth;
        public readonly int Smoothing;
        public readonly float Sharpness;
        public readonly float Vignette;
        public Params(float brightness, float contrast, float saturation, float warmth, int smoothing,
                      float sharpness = 0f, float vignette = 0f)
        {
            Brightness = brightness; Contrast = contrast; Saturation = saturation;
            Warmth = warmth; Smoothing = smoothing;
            Sharpness = sharpness; Vignette = vignette;
        }
        public bool IsIdentity =>
            Brightness == 0f && Contrast == 1f && Saturation == 1f && Warmth == 0f && Smoothing == 0
            && Sharpness == 0f && Vignette == 0f;
    }
    public static readonly string[] PresetNames =
        { "Aucun", "Naturel", "Studio", "Portrait", "Chaud", "Froid", "Noir & Blanc", "Cinéma" };
    private static readonly Params[] _presets =
    {
        new( 0.00f, 1.00f, 1.00f,  0.00f, 0, 0.0f, 0.00f),
        new( 0.06f, 1.05f, 1.00f,  0.05f, 1, 0.0f, 0.00f),
        new( 0.12f, 1.15f, 0.85f,  0.00f, 2, 0.0f, 0.00f),
        new( 0.12f, 0.90f, 1.10f,  0.10f, 1, 0.7f, 0.20f),
        new( 0.06f, 1.05f, 1.10f,  0.15f, 1, 0.0f, 0.00f),
        new( 0.02f, 1.05f, 0.85f, -0.12f, 0, 0.0f, 0.00f),
        new( 0.05f, 1.10f, 0.00f,  0.00f, 0, 0.0f, 0.00f),
        new(-0.04f, 1.25f, 0.65f,  0.00f, 0, 0.0f, 0.00f),
    };
    public static Params GetPreset(Preset p) => _presets[(int)p];
    public static Params GetPreset(int i)
        => (i >= 0 && i < _presets.Length) ? _presets[i] : _presets[0];
    public static ImageAttributes BuildAttributes(Params p)
    {
        float c = p.Contrast;
        float b = p.Brightness + (1f - c) * 0.5f;
        const float rL = 0.299f, gL = 0.587f, bL = 0.114f;
        float s = p.Saturation;
        float w = p.Warmth;
        float cR = c * (1f + Math.Max(0f, w) * 0.22f);
        float cB = c * (1f + Math.Max(0f, -w) * 0.22f);
        var m = new float[][]
        {
            new[] { (rL*(1-s)+s)*cR,      rL*(1-s)*c,            rL*(1-s)*cB,       0f, 0f },
            new[] { gL*(1-s)*cR,          (gL*(1-s)+s)*c,        gL*(1-s)*cB,       0f, 0f },
            new[] { bL*(1-s)*cR,          bL*(1-s)*c,            (bL*(1-s)+s)*cB,   0f, 0f },
            new[] { 0f,                   0f,                    0f,                1f, 0f },
            new[] { b,                    b,                     b,                 0f, 1f },
        };
        var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix(m), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        return attrs;
    }
    public static unsafe void ApplySmoothing(Bitmap bmp, int radius)
    {
        if (radius <= 0) return;
        int r = Math.Clamp(radius, 1, 5);
        int w = bmp.Width, h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
        try
        {
            int stride = bd.Stride;
            int bytes = stride * h;
            byte[] buf = new byte[bytes];
            Marshal.Copy(bd.Scan0, buf, 0, bytes);
            byte* dst = (byte*)bd.Scan0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sb = 0, sg = 0, sr = 0, n = 0;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, w - 1);
                        int off = y * stride + nx * 4;
                        sb += buf[off]; sg += buf[off + 1]; sr += buf[off + 2]; n++;
                    }
                    int o = y * stride + x * 4;
                    dst[o] = (byte)(sb / n); dst[o + 1] = (byte)(sg / n); dst[o + 2] = (byte)(sr / n);
                }
            }
            Marshal.Copy(bd.Scan0, buf, 0, bytes);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sb = 0, sg = 0, sr = 0, n = 0;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int ny = Math.Clamp(y + dy, 0, h - 1);
                        int off = ny * stride + x * 4;
                        sb += buf[off]; sg += buf[off + 1]; sr += buf[off + 2]; n++;
                    }
                    int o = y * stride + x * 4;
                    dst[o] = (byte)(sb / n); dst[o + 1] = (byte)(sg / n); dst[o + 2] = (byte)(sr / n);
                }
            }
        }
        finally { bmp.UnlockBits(bd); }
    }
    public static unsafe void ApplyUnsharpMask(Bitmap bmp, float amount)
    {
        if (amount <= 0f) return;
        int w = bmp.Width, h = bmp.Height;
        using var blurred = new Bitmap(bmp);
        ApplySmoothing(blurred, 1);
        var bd  = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
        var bd2 = blurred.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly,  PixelFormat.Format32bppRgb);
        try
        {
            byte* src  = (byte*)bd.Scan0;
            byte* blur = (byte*)bd2.Scan0;
            int stride = bd.Stride;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * stride + x * 4;
                for (int c = 0; c < 3; c++)
                    src[o + c] = (byte)Math.Clamp(src[o + c] + (int)(amount * (src[o + c] - blur[o + c])), 0, 255);
            }
        }
        finally { bmp.UnlockBits(bd); blurred.UnlockBits(bd2); }
    }
    public static void DrawVignette(Graphics g, Rectangle rect, float intensity)
    {
        if (intensity <= 0f) return;
        int alpha = Math.Clamp((int)(intensity * 200), 0, 220);
        int sz = (int)(Math.Min(rect.Width, rect.Height) * 0.45f);
        using var dark = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
        void Edge(int x, int y, int w, int h, Color from, Color to)
        {
            using var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(x, y, Math.Max(w, 1), Math.Max(h, 1)), from, to,
                w >= h ? System.Drawing.Drawing2D.LinearGradientMode.Vertical
                       : System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
            g.FillRectangle(br, x, y, w, h);
        }
        var d = Color.FromArgb(alpha, 0, 0, 0);
        var t = Color.Transparent;
        Edge(rect.X, rect.Y, rect.Width, sz, d, t);
        Edge(rect.X, rect.Bottom - sz, rect.Width, sz, t, d);
        Edge(rect.X, rect.Y, sz, rect.Height, d, t);
        Edge(rect.Right - sz, rect.Y, sz, rect.Height, t, d);
    }
}