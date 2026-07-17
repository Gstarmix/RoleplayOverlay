
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
    public static string? ImageEditorTextAlign { get; set; }
    public static string SortMode { get; set; } = "Free";
    public static string VideoAspect { get; set; } = "Landscape";
    public static string? CamDeviceName  { get; set; }
    public static int     CamPresetIndex { get; set; } = 1;
    public static int     CamShapeIndex  { get; set; } = 2;
    public static bool    CamMirror      { get; set; }
    public static double  CamWidthFrac   { get; set; } = 0.6;
    public static double  CamWidthFracLandscape { get; set; } = 0.2;
    public static double  CamCenterXFrac { get; set; } = 0.5;
    public static double  CamCenterYFrac { get; set; } = 0.77;
    public static string? CamLastScript  { get; set; }
    public static string? CamLastSlide   { get; set; }
    public static string? CamMediaLayersJson { get; set; }
    public static bool CamCountdownEnabled { get; set; } = true;
    public static bool CamShowCursor { get; set; } = true;
    public static bool CamHideWinWatermark { get; set; } = true;
    public static int CamPanelWidth  { get; set; }
    public static int CamPanelHeight { get; set; }
    public static double TelePromptSpeed   { get; set; } = 45;
    public static double TelePromptFont    { get; set; } = 30;
    public static double TelePromptAnchor  { get; set; } = 0.16;
    public static double TelePromptOpacity { get; set; } = 0.93;
    public static int TelePromptX { get; set; }
    public static int TelePromptY { get; set; }
    public static int TelePromptW { get; set; }
    public static int TelePromptH { get; set; }
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
        ImageEditorTextAlign   = dto.ImageEditorTextAlign;
        CamDeviceName   = dto.CamDeviceName;
        CamPresetIndex  = dto.CamPresetIndex;
        CamShapeIndex   = dto.CamShapeIndex;
        CamMirror       = dto.CamMirror;
        CamWidthFrac    = dto.CamWidthFrac > 0 ? dto.CamWidthFrac : 0.6;
        CamWidthFracLandscape = dto.CamWidthFracLandscape > 0 ? dto.CamWidthFracLandscape : 0.2;
        CamCenterXFrac  = dto.CamCenterXFrac > 0 ? dto.CamCenterXFrac : 0.5;
        CamCenterYFrac  = dto.CamCenterYFrac > 0 ? dto.CamCenterYFrac : 0.77;
        CamLastScript   = dto.CamLastScript;
        CamLastSlide    = dto.CamLastSlide;
        CamMediaLayersJson = dto.CamMediaLayersJson;
        CamCountdownEnabled = dto.CamCountdownEnabled;
        CamShowCursor = dto.CamShowCursor;
        CamHideWinWatermark = dto.CamHideWinWatermark;
        CamPanelWidth = dto.CamPanelWidth;
        CamPanelHeight = dto.CamPanelHeight;
        TelePromptSpeed   = dto.TelePromptSpeed   > 0 ? dto.TelePromptSpeed   : 45;
        TelePromptFont    = dto.TelePromptFont    > 0 ? dto.TelePromptFont    : 30;
        TelePromptAnchor  = dto.TelePromptAnchor  > 0 ? dto.TelePromptAnchor  : 0.16;
        TelePromptOpacity = dto.TelePromptOpacity > 0 ? dto.TelePromptOpacity : 0.93;
        TelePromptX = dto.TelePromptX; TelePromptY = dto.TelePromptY;
        TelePromptW = dto.TelePromptW; TelePromptH = dto.TelePromptH;
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
        var dto = new PrefsDto { TrimGuideVisible = TrimGuideVisible, LastProjectPath = LastProjectPath, RowHeight = RowHeight, PreferredMicDevice = PreferredMicDevice, SortMode = SortMode, VideoAspect = VideoAspect, ImageEditorColor = ImageEditorColor, ImageEditorStrokeWidth = ImageEditorStrokeWidth, ImageEditorFontFamily = ImageEditorFontFamily, ImageEditorFontSize = ImageEditorFontSize, ImageEditorTextOutline = ImageEditorTextOutline, ImageEditorFillEnabled = ImageEditorFillEnabled, ImageEditorFillColor = ImageEditorFillColor, ImageEditorOutlineColor = ImageEditorOutlineColor, ImageEditorTextAlign = ImageEditorTextAlign, CamDeviceName = CamDeviceName, CamPresetIndex = CamPresetIndex, CamShapeIndex = CamShapeIndex, CamMirror = CamMirror, CamWidthFrac = CamWidthFrac, CamWidthFracLandscape = CamWidthFracLandscape, CamCenterXFrac = CamCenterXFrac, CamCenterYFrac = CamCenterYFrac, CamLastScript = CamLastScript, CamLastSlide = CamLastSlide, CamMediaLayersJson = CamMediaLayersJson, CamCountdownEnabled = CamCountdownEnabled, CamShowCursor = CamShowCursor, CamHideWinWatermark = CamHideWinWatermark, CamPanelWidth = CamPanelWidth, CamPanelHeight = CamPanelHeight, TelePromptSpeed = TelePromptSpeed, TelePromptFont = TelePromptFont, TelePromptAnchor = TelePromptAnchor, TelePromptOpacity = TelePromptOpacity, TelePromptX = TelePromptX, TelePromptY = TelePromptY, TelePromptW = TelePromptW, TelePromptH = TelePromptH };
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
      public string? ImageEditorTextAlign   { get; set; }
      public string? CamDeviceName  { get; set; }
      public int     CamPresetIndex { get; set; } = 1;
      public int     CamShapeIndex  { get; set; } = 2;
      public bool    CamMirror      { get; set; }
      public double  CamWidthFrac   { get; set; } = 0.6;
      public double  CamWidthFracLandscape { get; set; } = 0.2;
      public double  CamCenterXFrac { get; set; } = 0.5;
      public double  CamCenterYFrac { get; set; } = 0.77;
      public string? CamLastScript  { get; set; }
      public string? CamLastSlide   { get; set; }
      public string? CamMediaLayersJson { get; set; }
      public bool    CamCountdownEnabled { get; set; } = true;
      public bool    CamShowCursor  { get; set; } = true;
      public bool    CamHideWinWatermark { get; set; } = true;
      public int     CamPanelWidth  { get; set; }
      public int     CamPanelHeight { get; set; }
      public double  TelePromptSpeed   { get; set; } = 45;
      public double  TelePromptFont    { get; set; } = 30;
      public double  TelePromptAnchor  { get; set; } = 0.16;
      public double  TelePromptOpacity { get; set; } = 0.93;
      public int     TelePromptX { get; set; }
      public int     TelePromptY { get; set; }
      public int     TelePromptW { get; set; }
      public int     TelePromptH { get; set; }
    }
  }
}