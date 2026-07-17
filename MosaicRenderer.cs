using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace RoleplayOverlay
{
  public sealed record MediaItemData(
    string? Path,
    string? SourcePath,
    float?  TrimIn,
    float?  TrimOut,
    int     CropLeft,
    int     CropTop,
    int     CropRight,
    int     CropBottom,
    string  CropGravity,
    float   Speed,
    double  AppearAt  = 0,
    double  AppearDur = 0,
    double  PosX      = 0.5,
    double  PosY      = 0.5,
    double  ItemScale = 0.5,
    string  Anim      = "none",
    double  PanX      = 0,
    double  PanY      = 0,
    bool    Loop      = true,
    double  CropFL    = 0,
    double  CropFT    = 0,
    double  CropFR    = 0,
    double  CropFB    = 0,
    bool    Border    = true,
    bool    Contain   = false,
    bool    PanAnim   = false,
    double  PanX2     = 0,
    double  PanY2     = 0,
    double  PanT1     = 0,
    double  PanDur    = 0
  )
  {
    public bool HasCrop => CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0;
    public bool HasContentCrop => CropFL > 0 || CropFT > 0 || CropFR > 0 || CropFB > 0;
    public static MediaItemData FromMediaItem(MediaItem mi) => new(
      mi.Path, mi.SourcePath,
      mi.TrimIn, mi.TrimOut,
      mi.CropLeft, mi.CropTop, mi.CropRight, mi.CropBottom,
      mi.CropGravity ?? "center", mi.Speed,
      mi.AppearAt, mi.AppearDur, mi.PosX, mi.PosY, mi.Scale, mi.Anim ?? "none",
      mi.PanX, mi.PanY, mi.Loop,
      mi.CropFL, mi.CropFT, mi.CropFR, mi.CropFB,
      mi.Border, mi.Contain,
      mi.PanAnim, mi.PanX2, mi.PanY2, mi.PanT1, mi.PanDur
    );
  }
  public static class MosaicRenderer
  {
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static (string args, int inputCount) BuildAllInputArgs(
      List<MediaItemData> items, string dur, int fps, bool loop)
    {
      var sb = new StringBuilder();
      int count = 0;
      foreach (var item in items)
      {
        if (string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
          continue;
        sb.Append(BuildItemInputArgs(item, dur, fps, loop));
        count++;
      }
      return (sb.ToString(), count);
    }
    private static string BuildItemInputArgs(
      MediaItemData item, string dur, int fps, bool loop)
    {
      var kind = DetectKind(item.Path!);
      var sb   = new StringBuilder();
      float speed    = Math.Clamp(item.Speed, 0.1f, 4.0f);
      double durSec  = double.Parse(dur, Inv);
      double sourceNeeded = durSec * speed;
      float ssVal   = item.TrimIn ?? 0f;
      float? outVal = item.TrimOut;
      double? trimSegmentDur = null;
      if (outVal.HasValue && outVal.Value > ssVal)
        trimSegmentDur = outVal.Value - ssVal;
      double effectiveSourceDur;
      if (trimSegmentDur.HasValue)
        effectiveSourceDur = Math.Min(trimSegmentDur.Value, sourceNeeded);
      else
        effectiveSourceDur = sourceNeeded;
      bool needsLoop = false;
      if (trimSegmentDur.HasValue && trimSegmentDur.Value < sourceNeeded)
        needsLoop = true;
      if (!trimSegmentDur.HasValue && speed > 1.0f)
        needsLoop = true;
      if (loop)
        needsLoop = true;
      string? ssTrim = ssVal > 0.01f ? ssVal.ToString("F2", Inv) : null;
      string  durTrim = effectiveSourceDur.ToString("F2", Inv);
      switch (kind)
      {
        case MosaicMediaKind.Image:
          sb.Append($"-framerate {fps} -loop 1 -t {dur} -i \"{item.Path}\" ");
          break;
        case MosaicMediaKind.Gif:
          if (ssTrim != null) sb.Append($"-ss {ssTrim} ");
          sb.Append($"-stream_loop -1 -t {durTrim} -i \"{item.Path}\" ");
          break;
        case MosaicMediaKind.Video:
          if (ssTrim != null) sb.Append($"-ss {ssTrim} ");
          if (needsLoop) sb.Append("-stream_loop -1 ");
          sb.Append($"-t {durTrim} -i \"{item.Path}\" ");
          break;
      }
      return sb.ToString();
    }
    public static string BuildFilterComplex(
      List<MediaItemData> items,
      List<int>           inputIndices,
      int w, int h,
      float scale, int gap, bool loop,
      string borderColor, int borderPx,
      int shadowBlur, float shadowAlpha,
      string bgLabel,
      bool skipShadow = false)
    {
      if (items.Count == 0 || inputIndices.Count == 0)
        return $"[{bgLabel}]copy[bg_with_media];";
      var fc = new StringBuilder();
      int mosaicW = (int)(w * Math.Clamp(scale, 0.1f, 1.0f));
      int mosaicH = (int)(h * Math.Clamp(scale, 0.1f, 1.0f));
      int validCount = Math.Min(items.Count, inputIndices.Count);
      var cells = MosaicLayout.GetCells(validCount, mosaicW, mosaicH, gap);
      for (int i = 0; i < validCount; i++)
      {
        var item = items[i];
        var cell = cells[i];
        int idx  = inputIndices[i];
        float speed = Math.Clamp(item.Speed, 0.1f, 4.0f);
        string speedPts = speed != 1.0f
          ? $"setpts=PTS/{speed.ToString("F2", Inv)},"
          : "";
        fc.Append($"[{idx}:v]{speedPts}");
        if (item.HasCrop)
        {
          int cL = Math.Max(0, item.CropLeft);
          int cT = Math.Max(0, item.CropTop);
          int cR = Math.Max(0, item.CropRight);
          int cB = Math.Max(0, item.CropBottom);
          fc.Append($"crop=iw-{cL}-{cR}:ih-{cT}-{cB}:{cL}:{cT},");
        }
        fc.Append($"scale={cell.Width}:{cell.Height}:force_original_aspect_ratio=increase,");
        var (gcx, gcy) = GravityOffset(item.CropGravity ?? "center");
        fc.Append($"crop={cell.Width}:{cell.Height}:{gcx}:{gcy},");
        fc.Append($"format=yuv420p[m{i}];");
      }
      string mosaicLabel;
      if (validCount == 1)
      {
        mosaicLabel = "m0";
      }
      else
      {
        for (int i = 0; i < validCount; i++)
          fc.Append($"[m{i}]");
        var layoutStr = MosaicLayout.ToXStackLayoutString(cells);
        fc.Append($"xstack=inputs={validCount}:layout={layoutStr}:fill=black[mosaic];");
        mosaicLabel = "mosaic";
      }
      int bpx = Math.Max(0, borderPx);
      string ffBorderColor = ParseBorderColor(borderColor);
      string borderedLabel;
      if (bpx > 0)
      {
        fc.Append($"[{mosaicLabel}]pad=iw+{2 * bpx}:ih+{2 * bpx}:{bpx}:{bpx}:{ffBorderColor}[media_bordered];");
        borderedLabel = "media_bordered";
      }
      else
      {
        borderedLabel = mosaicLabel;
      }
      int sBlur = Math.Max(1, shadowBlur);
      float sAlpha = Math.Clamp(shadowAlpha, 0f, 1f);
      if (!skipShadow && sAlpha > 0.01f)
      {
        string alphaStr = sAlpha.ToString("F2", Inv);
        fc.Append($"[{borderedLabel}]split[ms][msh];");
        fc.Append($"[msh]colorchannelmixer=aa={alphaStr},boxblur={sBlur}[shadow];");
        fc.Append($"[{bgLabel}][shadow]overlay=(W-w)/2:(H-h)/2[bg_shadow];");
        fc.Append($"[bg_shadow][ms]overlay=(W-w)/2:(H-h)/2[bg_with_media];");
      }
      else
      {
        fc.Append($"[{bgLabel}][{borderedLabel}]overlay=(W-w)/2:(H-h)/2[bg_with_media];");
      }
      return fc.ToString();
    }
    private enum MosaicMediaKind { Image, Gif, Video }
    private static MosaicMediaKind DetectKind(string path)
    {
      var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
      return ext switch
      {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" => MosaicMediaKind.Image,
        ".gif" => MosaicMediaKind.Gif,
        ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv"  => MosaicMediaKind.Video,
        _ => MosaicMediaKind.Image,
      };
    }
    private static (string cx, string cy) GravityOffset(string gravity) =>
      gravity.ToLowerInvariant() switch
      {
        "top"         => ("(iw-ow)/2", "0"),
        "bottom"      => ("(iw-ow)/2", "ih-oh"),
        "left"        => ("0",         "(ih-oh)/2"),
        "right"       => ("iw-ow",     "(ih-oh)/2"),
        "topleft"     => ("0",         "0"),
        "topright"    => ("iw-ow",     "0"),
        "bottomleft"  => ("0",         "ih-oh"),
        "bottomright" => ("iw-ow",     "ih-oh"),
        _             => ("(iw-ow)/2", "(ih-oh)/2"),
      };
    private static string ParseBorderColor(string? hex)
    {
      if (string.IsNullOrWhiteSpace(hex)) return "white";
      var clean = hex.TrimStart('#');
      return clean.Length == 6 ? $"0x{clean}" : "white";
    }
  }
}