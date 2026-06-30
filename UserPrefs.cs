
using System;
using System.IO;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public static class UserPrefs
  {
    private static readonly string PrefsPath =
      Path.Combine(@"C:\RoleplayOverlay", "prefs.json");
    public static bool TrimGuideVisible { get; set; } = true;
    public static string? LastProjectPath { get; set; }
    public static double RowHeight { get; set; } = 28;
    public static string? PreferredMicDevice { get; set; }
    public static string? ImageEditorColor { get; set; }
    public static double ImageEditorStrokeWidth { get; set; } = 3;
    public static string? ImageEditorFontFamily { get; set; }
    public static double ImageEditorFontSize { get; set; } = 32;
    public static bool ImageEditorTextOutline { get; set; }
    public static bool ImageEditorFillEnabled { get; set; }
    public static string? ImageEditorFillColor { get; set; }
    public static string? ImageEditorOutlineColor { get; set; }
    public static string SortMode { get; set; } = "Free";
    public static string VideoAspect { get; set; } = "Landscape";
    public static void Load()
    {
      try
      {
        if (!File.Exists(PrefsPath)) return;
        var dto = JsonConvert.DeserializeObject<PrefsDto>(File.ReadAllText(PrefsPath));
        if (dto == null) return;
        TrimGuideVisible   = dto.TrimGuideVisible;
        LastProjectPath    = dto.LastProjectPath;
        RowHeight          = dto.RowHeight > 0 ? dto.RowHeight : 30;
        PreferredMicDevice = dto.PreferredMicDevice;
        SortMode           = string.IsNullOrWhiteSpace(dto.SortMode) ? "Free" : dto.SortMode!;
        VideoAspect        = string.IsNullOrWhiteSpace(dto.VideoAspect) ? "Landscape" : dto.VideoAspect!;
        ImageEditorColor       = dto.ImageEditorColor;
        ImageEditorStrokeWidth = dto.ImageEditorStrokeWidth > 0 ? dto.ImageEditorStrokeWidth : 3;
        ImageEditorFontFamily  = dto.ImageEditorFontFamily;
        ImageEditorFontSize    = dto.ImageEditorFontSize > 0 ? dto.ImageEditorFontSize : 32;
        ImageEditorTextOutline = dto.ImageEditorTextOutline;
        ImageEditorFillEnabled = dto.ImageEditorFillEnabled;
        ImageEditorFillColor   = dto.ImageEditorFillColor;
        ImageEditorOutlineColor = dto.ImageEditorOutlineColor;
      }
      catch (Exception ex)
      {
        Logger.Warn($"UserPrefs.Load failed: {ex.Message}");
      }
    }
    public static void Save()
    {
      try
      {
        var dto = new PrefsDto { TrimGuideVisible = TrimGuideVisible, LastProjectPath = LastProjectPath, RowHeight = RowHeight, PreferredMicDevice = PreferredMicDevice, SortMode = SortMode, VideoAspect = VideoAspect, ImageEditorColor = ImageEditorColor, ImageEditorStrokeWidth = ImageEditorStrokeWidth, ImageEditorFontFamily = ImageEditorFontFamily, ImageEditorFontSize = ImageEditorFontSize, ImageEditorTextOutline = ImageEditorTextOutline, ImageEditorFillEnabled = ImageEditorFillEnabled, ImageEditorFillColor = ImageEditorFillColor, ImageEditorOutlineColor = ImageEditorOutlineColor };
        File.WriteAllText(PrefsPath,
          JsonConvert.SerializeObject(dto, Formatting.Indented));
      }
      catch (Exception ex)
      {
        Logger.Warn($"UserPrefs.Save failed: {ex.Message}");
      }
    }
    private sealed class PrefsDto
    {
      public bool    TrimGuideVisible   { get; set; } = true;
      public string? PreferredMicDevice { get; set; }
      public string? LastProjectPath  { get; set; }
      public double  RowHeight        { get; set; } = 28;
      public string? SortMode         { get; set; } = "Free";
      public string? VideoAspect      { get; set; } = "Landscape";
      public string? ImageEditorColor       { get; set; }
      public double  ImageEditorStrokeWidth { get; set; } = 3;
      public string? ImageEditorFontFamily  { get; set; }
      public double  ImageEditorFontSize    { get; set; } = 32;
      public bool    ImageEditorTextOutline { get; set; }
      public bool    ImageEditorFillEnabled { get; set; }
      public string? ImageEditorFillColor   { get; set; }
      public string? ImageEditorOutlineColor { get; set; }
    }
  }
}