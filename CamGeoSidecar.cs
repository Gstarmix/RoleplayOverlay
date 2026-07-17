using System;
using System.IO;
using Newtonsoft.Json.Linq;
namespace RoleplayOverlay;
internal static class CamGeoSidecar
{
    public static string PathFor(string composedClipPath) => composedClipPath + ".camgeo.json";
    public static bool ApplyTo(Sequence seq, string? composedClipPath)
    {
        if (seq == null || string.IsNullOrWhiteSpace(composedClipPath)) return false;
        string sidecar = PathFor(composedClipPath);
        if (!File.Exists(sidecar)) return false;
        try
        {
            var jo = JObject.Parse(File.ReadAllText(sidecar));
            var x = (double?)jo["camX"];
            var y = (double?)jo["camY"];
            var d = (double?)jo["camDiam"];
            if (x is null || y is null || d is null) return false;
            seq.CamX = Math.Clamp(x.Value, 0, 1);
            seq.CamY = Math.Clamp(y.Value, 0, 1);
            seq.CamDiam = Math.Clamp(d.Value, 0.10, 1.0);
            seq.CamRelift = true;
            return true;
        }
        catch (Exception ex) { Logger.Warn("Sidecar camgeo illisible : " + ex.Message); return false; }
    }
}