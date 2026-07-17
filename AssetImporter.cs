using System;
using System.IO;
namespace RoleplayOverlay
{
  public static class AssetImporter
  {
    public static string MediaDir(string? slidesDir)
    {
      var dir = string.IsNullOrWhiteSpace(slidesDir)
        ? Path.Combine(AppContext.BaseDirectory, "FaceCam", "Gallery")
        : Path.Combine(slidesDir, "media");
      Directory.CreateDirectory(dir);
      return dir;
    }
    public static string CopyIntoProjectMedia(string? slidesDir, string sourcePath, string? nameHint = null)
    {
      try
      {
        var destDir  = MediaDir(slidesDir);
        var ext      = Path.GetExtension(sourcePath);
        var origBase = Path.GetFileNameWithoutExtension(sourcePath);
        var hint     = string.IsNullOrWhiteSpace(nameHint) ? ShortHash(sourcePath) : nameHint;
        var destPath = Path.Combine(destDir, $"{hint}_{origBase}{ext}");
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath),
            StringComparison.OrdinalIgnoreCase))
          return sourcePath;
        File.Copy(sourcePath, destPath, overwrite: true);
        Logger.Info($"[AssetImporter] Copié → {destPath}");
        return destPath;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[AssetImporter] Copie échouée, chemin original conservé : {ex.Message}");
        return sourcePath;
      }
    }
    private static string ShortHash(string s)
    {
      using var md5 = System.Security.Cryptography.MD5.Create();
      var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
      return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
  }
}