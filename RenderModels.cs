using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
namespace RoleplayOverlay
{
  public sealed class RenderManifest
  {
    [JsonProperty("version")]
    public int Version { get; set; } = 1;
    [JsonProperty("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;
    [JsonProperty("slidesDirectory")]
    public string SlidesDirectory { get; set; } = string.Empty;
    [JsonProperty("segments")]
    public List<ManifestSegment> Segments { get; set; } = new();
    public static RenderManifest Load(string path)
    {
      var json = System.IO.File.ReadAllText(path);
      return JsonConvert.DeserializeObject<RenderManifest>(json) ?? new RenderManifest();
    }
    public void Save(string path)
    {
      var json = JsonConvert.SerializeObject(this, Formatting.Indented);
      System.IO.File.WriteAllText(path, json);
    }
    private static readonly Regex SlideNumRegex =
      new(@"\bslide\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static RenderManifest ScaffoldFromProject(Project project, string slidesDirectory)
    {
      var manifest = new RenderManifest
      {
        ProjectPath     = string.Empty,
        SlidesDirectory = slidesDirectory,
      };
      var scene = project.CurrentScene;
      if (scene == null) return manifest;
      var groups = new List<(int? slideNum, string label, List<string> ids)>();
      int fallbackIdx = 0;
      foreach (var seq in scene.Sequences)
      {
        if (seq.Id == null) continue;
        int? slideNum = ExtractSlideNum(seq.Note);
        if (slideNum.HasValue)
        {
          var existing = groups.FindIndex(g => g.slideNum == slideNum);
          if (existing >= 0)
          {
            groups[existing].ids.Add(seq.Id);
          }
          else
          {
            var label = ExtractSlideLabel(seq.Note) ?? $"Slide {slideNum:D3}";
            groups.Add((slideNum, label, new List<string> { seq.Id }));
          }
        }
        else
        {
          fallbackIdx++;
          var label = !string.IsNullOrWhiteSpace(seq.Note) ? seq.Note! : $"Segment {fallbackIdx:D3}";
          groups.Add((null, label, new List<string> { seq.Id }));
        }
      }
      int nextFallbackSlide = 1;
      int maxNamed = groups.Where(g => g.slideNum.HasValue)
                           .Select(g => g.slideNum!.Value)
                           .DefaultIfEmpty(0).Max();
      nextFallbackSlide = maxNamed + 1;
      foreach (var (slideNum, label, ids) in groups)
      {
        string slidePath;
        string segLabel;
        if (slideNum.HasValue)
        {
          slidePath = $"slide_{slideNum:D3}.png";
          segLabel  = label;
        }
        else
        {
          slidePath = $"slide_{nextFallbackSlide:D3}.png";
          segLabel  = label;
          nextFallbackSlide++;
        }
        manifest.Segments.Add(new ManifestSegment
        {
          Label       = segLabel,
          SlidePath   = slidePath,
          SequenceIds = ids,
        });
      }
      return manifest;
    }
    private static int? ExtractSlideNum(string? note)
    {
      if (string.IsNullOrWhiteSpace(note)) return null;
      var m = SlideNumRegex.Match(note);
      if (!m.Success) return null;
      return int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }
    private static string? ExtractSlideLabel(string? note)
    {
      if (string.IsNullOrWhiteSpace(note)) return null;
      var clean = Regex.Replace(note, @"\s*[\[(][A-Z]{2}[\])]$", "").Trim();
      return string.IsNullOrWhiteSpace(clean) ? note : clean;
    }
  }
  public sealed class ManifestSegment
  {
    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;
    [JsonProperty("slidePath")]
    public string SlidePath { get; set; } = string.Empty;
    [JsonProperty("sequenceIds")]
    public List<string> SequenceIds { get; set; } = new();
    [JsonProperty("minDurationSec")]
    public double MinDurationSec { get; set; } = 0;
    [JsonProperty("mediaPath", NullValueHandling = NullValueHandling.Ignore)]
    public string? MediaPath { get; set; }
    [JsonProperty("mediaScale", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public float MediaScale { get; set; } = 0.80f;
    [JsonProperty("mediaSpeed", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public float MediaSpeed { get; set; } = 1.0f;
    [JsonProperty("mediaLoop", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool MediaLoop { get; set; } = true;
    [JsonProperty("mediaBorderColor", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MediaBorderColor { get; set; } = "#FFFFFF";
    [JsonProperty("mediaBorderPx", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaBorderPx { get; set; } = 6;
    [JsonProperty("mediaShadowBlur", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaShadowBlur { get; set; } = 18;
    [JsonProperty("mediaShadowAlpha", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public float MediaShadowAlpha { get; set; } = 0.55f;
    [JsonProperty("mediaTrimIn", NullValueHandling = NullValueHandling.Ignore)]
    public float? MediaTrimIn { get; set; }
    [JsonProperty("mediaTrimOut", NullValueHandling = NullValueHandling.Ignore)]
    public float? MediaTrimOut { get; set; }
    [JsonProperty("mediaCropLeft", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropLeft { get; set; }
    [JsonProperty("mediaCropTop", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropTop { get; set; }
    [JsonProperty("mediaCropRight", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropRight { get; set; }
    [JsonProperty("mediaCropBottom", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int MediaCropBottom { get; set; }
    [JsonProperty("mediaCropGravity", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MediaCropGravity { get; set; } = "center";
    [JsonProperty("mediaAnchor", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string MediaAnchor { get; set; } = "center";
  }
  public sealed record RenderSegment(
    int           Index,
    string        Label,
    string?       SlideImagePath,
    string?       AudioPath,
    TimeSpan      Duration,
    string?       BubbleText,
    SpeakerKind   Speaker,
    string?       OutputPath,
    string?       MediaPath        = null,
    float         MediaScale       = 0.80f,
    float         MediaSpeed       = 1.0f,
    bool          MediaLoop        = true,
    string        MediaBorderColor = "#FFFFFF",
    int           MediaBorderPx    = 6,
    int           MediaShadowBlur  = 18,
    float         MediaShadowAlpha = 0.55f,
    float?        MediaTrimIn      = null,
    float?        MediaTrimOut     = null,
    int           MediaCropLeft    = 0,
    int           MediaCropTop     = 0,
    int           MediaCropRight   = 0,
    int           MediaCropBottom  = 0,
    string        MediaCropGravity = "center",
    string        MediaAnchor      = "center",
    List<MediaItemData>? MediaItems = null,
    int           MediaGap         = 6,
    int?          OverrideAvatarX  = null,
    int?          OverrideAvatarY  = null,
    string?       CamClipPath      = null,
    string?       ComposedClipPath = null,
    double        CamX             = 0.80,
    double        CamY             = 0.84,
    double        CamDiam          = 0.34,
    bool          CamRelift        = false
  )
  {
    public string? OutputPath { get; init; } = OutputPath;
  }
  public sealed class RenderOptions
  {
    public string OutputPath { get; set; } = "output.mp4";
    public string TempDirectory { get; set; } =
      System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoleplayOverlay_render");
    public int VideoWidth  { get; set; } = 1920;
    public int VideoHeight { get; set; } = 1080;
    public int Crf         { get; set; } = 18;
    public bool BurnSubtitles   { get; set; } = true;
    public int  SubtitleFontSize { get; set; } = 22;
    public int  FontSize         { get; set; } = 34;
    public string? FontPath      { get; set; } = null;
    public string? FfmpegBinaryPath { get; set; } = null;
    public string? YouAvatarPath  { get; set; }
    public string? Bot1AvatarPath { get; set; }
    public string? Bot2AvatarPath { get; set; }
    public int     AvatarSize     { get; set; } = 140;
    public bool    ShowAvatarInVideo { get; set; } = true;
    public double YouX  { get; set; } = 890;
    public double YouY  { get; set; } = 920;
    public double Bot1X { get; set; } = 20;
    public double Bot1Y { get; set; } = 20;
    public double Bot2X { get; set; } = 1700;
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
    public int  Threads              { get; set; } = 0;
    public bool CleanupTempOnSuccess { get; set; } = true;
    public int MaxParallelSegments { get; set; } = 0;
    public bool UseNvenc { get; set; } = false;
    public bool IsPreview { get; set; } = false;
    public bool PreviewSkipAvatar { get; set; } = true;
    public bool PreviewSkipShadow { get; set; } = true;
    public bool PreviewSkipSubs   { get; set; } = true;
    public bool PreviewLowFps     { get; set; } = true;
    public bool UseAzureTts { get; set; } = false;
    public SubtitleColors SubColors { get; set; } = new();
    public int SubtitleAssSize { get; set; } = 50;
  }
  public sealed record RenderProgress(
    RenderPhase Phase,
    int         CurrentSegment,
    int         TotalSegments,
    string      Message
  )
  {
    public int Percent => TotalSegments == 0 ? 0
      : (int)(100.0 * CurrentSegment / TotalSegments);
  }
  public enum RenderPhase
  {
    Initializing,
    Collecting,
    Rendering,
    Concatenating,
    Cleaning,
    Done,
    Failed
  }
  public sealed record RenderResult(
    bool     Success,
    string?  OutputPath,
    string?  ErrorMessage,
    TimeSpan ElapsedTime,
    int      SegmentCount
  );
}