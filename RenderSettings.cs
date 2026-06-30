namespace RoleplayOverlay
{
  public enum VideoAspect
  {
    Landscape,
    Portrait,
  }
  public static class VideoAspectInfo
  {
    public static (int W, int H) Dimensions(VideoAspect a) => a switch
    {
      VideoAspect.Portrait => (1080, 1920),
      _                    => (1920, 1080),
    };
    public sealed record AvatarPositions(
      double YouX,  double YouY,
      double Bot1X, double Bot1Y,
      double Bot2X, double Bot2Y);
    public static AvatarPositions PortraitDefaults { get; } = new(
      YouX:  485, YouY:  1780,
      Bot1X: 30,  Bot1Y: 30,
      Bot2X: 940, Bot2Y: 30);
    public static int DefaultSubtitleFontSize(VideoAspect a) =>
      a == VideoAspect.Portrait ? 80 : 50;
    public static int DefaultAvatarSize(VideoAspect a) =>
      a == VideoAspect.Portrait ? 110 : 140;
  }
  public sealed class RenderSettings
  {
    public VideoAspect Aspect { get; set; } = VideoAspect.Landscape;
    public int  Crf                 { get; set; } = 18;
    public bool UseNvenc            { get; set; } = false;
    public int  MaxParallelSegments { get; set; } = 0;
    public int  FontSize         { get; set; } = 34;
    public int  SubtitleFontSize { get; set; } = 50;
    public bool BurnSubtitles    { get; set; } = false;
    public bool UseAzureTts { get; set; } = false;
    public bool PreviewIncludeAvatar { get; set; } = false;
    public bool PreviewIncludeShadow { get; set; } = false;
    public bool PreviewIncludeSubs   { get; set; } = false;
    public bool PreviewFullFps       { get; set; } = false;
    public string? FfmpegBinaryPath { get; set; }
    public string? OutputDirectory  { get; set; }
    public bool ShowAvatars { get; set; } = true;
    public int  AvatarSize  { get; set; } = 140;
    public string? YouAvatarPath  { get; set; }
    public string? Bot1AvatarPath { get; set; }
    public string? Bot2AvatarPath { get; set; }
    public double YouX  { get; set; } = 833;
    public double YouY  { get; set; } = 885;
    public double Bot1X { get; set; } = 20;
    public double Bot1Y { get; set; } = 20;
    public double Bot2X { get; set; } = 1760;
    public double Bot2Y { get; set; } = 20;
    public int YouGlowR  { get; set; } = 255;
    public int YouGlowG  { get; set; } = 212;
    public int YouGlowB  { get; set; } = 0;
    public int Bot1GlowR { get; set; } = 255;
    public int Bot1GlowG { get; set; } = 0;
    public int Bot1GlowB { get; set; } = 255;
    public int Bot2GlowR { get; set; } = 0;
    public int Bot2GlowG { get; set; } = 255;
    public int Bot2GlowB { get; set; } = 255;
    public float GlowIntensity { get; set; } = 0.7f;
    public SubtitleColors SubColors { get; set; } = new();
    public string FfmpegExePath =>
      string.IsNullOrWhiteSpace(FfmpegBinaryPath)
        ? "ffmpeg"
        : System.IO.Path.Combine(FfmpegBinaryPath, "ffmpeg.exe");
  }
}