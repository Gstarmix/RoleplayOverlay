using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace RoleplayOverlay
{
  public sealed class RenderService
  {
    private static readonly Regex SlideNumRegex =
      new(@"\bslide\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ProjectService _projectService;
    public RenderService(ProjectService projectService)
    {
      _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
    }
    public RenderOptions BuildOptions(
      RenderSettings settings,
      string         outputPath,
      string         tempDirectory,
      int?           crfOverride = null,
      bool           isPreview   = false)
    {
      var (vw, vh) = VideoAspectInfo.Dimensions(settings.Aspect);
      double youX  = settings.YouX,  youY  = settings.YouY;
      double bot1X = settings.Bot1X, bot1Y = settings.Bot1Y;
      double bot2X = settings.Bot2X, bot2Y = settings.Bot2Y;
      if (settings.Aspect == VideoAspect.Portrait)
      {
        var p = VideoAspectInfo.PortraitDefaults;
        youX  = p.YouX;  youY  = p.YouY;
        bot1X = p.Bot1X; bot1Y = p.Bot1Y;
        bot2X = p.Bot2X; bot2Y = p.Bot2Y;
      }
      return new RenderOptions
      {
        OutputPath           = outputPath,
        TempDirectory        = tempDirectory,
        VideoWidth           = vw,
        VideoHeight          = vh,
        Crf                  = crfOverride ?? settings.Crf,
        BurnSubtitles        = settings.BurnSubtitles,
        FontSize             = settings.FontSize,
        SubtitleFontSize     = settings.SubtitleFontSize,
        SubtitleAssSize      = settings.SubtitleFontSize,
        FontPath             = GetDefaultFontPath(),
        FfmpegBinaryPath     = settings.FfmpegBinaryPath,
        CleanupTempOnSuccess = true,
        UseAzureTts          = settings.UseAzureTts,
        SubColors            = settings.SubColors ?? new SubtitleColors(),
        ShowAvatarInVideo    = settings.ShowAvatars,
        YouAvatarPath        = settings.YouAvatarPath,
        Bot1AvatarPath       = settings.Bot1AvatarPath,
        Bot2AvatarPath       = settings.Bot2AvatarPath,
        AvatarSize           = settings.AvatarSize,
        YouX  = youX,  YouY  = youY,
        Bot1X = bot1X, Bot1Y = bot1Y,
        Bot2X = bot2X, Bot2Y = bot2Y,
        YouGlowR  = settings.YouGlowR,  YouGlowG  = settings.YouGlowG,  YouGlowB  = settings.YouGlowB,
        Bot1GlowR = settings.Bot1GlowR, Bot1GlowG = settings.Bot1GlowG, Bot1GlowB = settings.Bot1GlowB,
        Bot2GlowR = settings.Bot2GlowR, Bot2GlowG = settings.Bot2GlowG, Bot2GlowB = settings.Bot2GlowB,
        GlowIntensity       = settings.GlowIntensity,
        MaxParallelSegments  = settings.MaxParallelSegments,
        UseNvenc             = settings.UseNvenc,
        IsPreview            = isPreview,
      };
    }
    public string ResolveSlidesDirectory()
      => ResolveSlidesDirectory(_projectService.Project?.CurrentScene);
    public string ResolveSlidesDirectory(Scene? scene)
    {
      var projectPath = _projectService.FilePath;
      if (string.IsNullOrWhiteSpace(projectPath)) return "";
      if (scene != null
          && !string.IsNullOrWhiteSpace(scene.SlidesDirectory)
          && Directory.Exists(scene.SlidesDirectory))
        return scene.SlidesDirectory;
      if (scene != null)
      {
        var inferred = InferSlidesDirForScene(scene, projectPath);
        if (!string.IsNullOrWhiteSpace(inferred) && Directory.Exists(inferred))
        {
          scene.SlidesDirectory = inferred;
          return inferred;
        }
      }
      var renderPath = Path.ChangeExtension(projectPath, ".render.json");
      if (File.Exists(renderPath))
      {
        try
        {
          var rm = RenderManifest.Load(renderPath);
          if (!string.IsNullOrWhiteSpace(rm.SlidesDirectory) && Directory.Exists(rm.SlidesDirectory))
            return rm.SlidesDirectory;
        }
        catch { }
      }
      var jsonDir  = Path.GetDirectoryName(projectPath)!;
      var parent   = Path.GetDirectoryName(jsonDir) ?? jsonDir;
      var baseName = Path.GetFileNameWithoutExtension(projectPath);
      var candidates = new[]
      {
        Path.Combine(parent, "slides", baseName),
        Path.Combine(jsonDir, "slides", baseName),
        Path.Combine(parent, "slides"),
        Path.Combine(jsonDir, "slides"),
        jsonDir,
      };
      foreach (var c in candidates)
        if (Directory.Exists(c)) return c;
      return "";
    }
    private static readonly Regex ScenePrefixRegex = new(
      @"^\s*project_([A-Z0-9]+)_([^\s—]+)",
      RegexOptions.Compiled);
    private static readonly Dictionary<string, string> _sceneSlidesDirCache =
      new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] _knownMatieres =
      { "AN1", "EN1", "PRG2", "PSI", "ISE" };
    private string? InferSlidesDirForScene(Scene scene, string projectPath)
    {
      if (string.IsNullOrWhiteSpace(scene?.Name)) return null;
      var m = ScenePrefixRegex.Match(scene.Name);
      if (!m.Success) return null;
      var mat = m.Groups[1].Value;
      var id  = m.Groups[2].Value;
      var expectedJsonName = $"project_{mat}_{id}.json";
      var cacheKey = projectPath + "|" + expectedJsonName;
      if (_sceneSlidesDirCache.TryGetValue(cacheKey, out var cached)
          && Directory.Exists(cached))
        return cached;
      var coursRoot = FindCoursRoot(projectPath);
      if (coursRoot == null) return null;
      var matDir = Path.Combine(coursRoot, mat);
      string? found = null;
      try
      {
        if (Directory.Exists(matDir))
        {
          var hit = Directory
            .EnumerateFiles(matDir, expectedJsonName, SearchOption.AllDirectories)
            .FirstOrDefault();
          if (hit != null) found = Path.GetDirectoryName(hit);
        }
        if (found == null)
        {
          var hit = Directory
            .EnumerateFiles(coursRoot, expectedJsonName, SearchOption.AllDirectories)
            .FirstOrDefault();
          if (hit != null) found = Path.GetDirectoryName(hit);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"[RenderService] InferSlidesDir scan failed: {ex.Message}");
        return null;
      }
      if (found != null && Directory.Exists(found))
      {
        _sceneSlidesDirCache[cacheKey] = found;
        return found;
      }
      return null;
    }
    private static string? FindCoursRoot(string anyPath)
    {
      var dir = Path.GetDirectoryName(anyPath);
      for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
      {
        if (string.Equals(Path.GetFileName(dir), "COURS", StringComparison.OrdinalIgnoreCase))
          return dir;
        int hits = 0;
        foreach (var mat in _knownMatieres)
          if (Directory.Exists(Path.Combine(dir, mat))) hits++;
        if (hits >= 2) return dir;
        dir = Path.GetDirectoryName(dir);
      }
      return null;
    }
    public string? ResolveSlidePathFromNote(string? note, string slidesDir)
    {
      if (string.IsNullOrWhiteSpace(note)) return null;
      var m = SlideNumRegex.Match(note);
      if (!m.Success || !int.TryParse(m.Groups[1].Value, out var slideNum))
        return null;
      var candidate = Path.Combine(slidesDir, $"slide_{slideNum:D3}.png");
      return File.Exists(candidate) ? candidate : null;
    }
    public string ResolveSlideRelativeFromNote(string? note)
    {
      if (string.IsNullOrWhiteSpace(note)) return "slide_001.png";
      var m = SlideNumRegex.Match(note);
      if (m.Success && int.TryParse(m.Groups[1].Value, out var sn))
        return $"slide_{sn:D3}.png";
      return "slide_001.png";
    }
    public async Task<string?> GeneratePreviewFrameAsync(
      Sequence       seq,
      RenderSettings settings)
    {
      if (seq == null) return null;
      Logger.Section("GeneratePreviewFrameAsync");
      Logger.Info($"Sequence: id={seq.Id}, note={seq.Note}, mediaPath={seq.MediaPath}, trimIn={seq.MediaTrimIn}, trimOut={seq.MediaTrimOut}");
      var slidesDir = ResolveSlidesDirectory();
      var slidePath = ResolveSlidePathFromNote(seq.Note, slidesDir);
      Logger.Info($"SlidesDir: {slidesDir}");
      Logger.Info($"SlidePath: {slidePath ?? "(null)"}");
      if (slidePath == null)
        Logger.Warn($"Slide introuvable pour note='{seq.Note}' dans {slidesDir} — fond noir utilisé");
      var tempDir = Path.Combine(Path.GetTempPath(), "ro_preview");
      Directory.CreateDirectory(tempDir);
      var outPng = Path.Combine(tempDir, $"preview_{Guid.NewGuid():N}.png");
      var exe = settings.FfmpegExePath;
      var inv = CultureInfo.InvariantCulture;
      var (vw, vh) = VideoAspectInfo.Dimensions(settings.Aspect);
      Logger.Info($"FFmpeg exe: {exe}");
      Logger.Info($"Aspect: {settings.Aspect} ({vw}x{vh})");
      var cmd = new StringBuilder();
      cmd.Append("-y ");
      if (slidePath != null)
        cmd.Append($"-loop 1 -i \"{slidePath}\" ");
      else
        cmd.Append($"-f lavfi -i color=black:size={vw}x{vh}:rate=1 ");
      int nextIdx = 1;
      bool hasMedia = !string.IsNullOrWhiteSpace(seq.MediaPath) && File.Exists(seq.MediaPath);
      bool hasMosaicMedia = seq.MediaItems != null && seq.MediaItems.Count > 0
        && seq.MediaItems.Any(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path));
      bool hasLegacyMedia = !hasMosaicMedia && hasMedia;
      Logger.Info($"HasMedia: {hasMedia}, HasMosaicMedia: {hasMosaicMedia}");
      var mediaInputIndices = new List<int>();
      List<MediaItemData>? validMosaicItems = null;
      if (hasMosaicMedia)
      {
        validMosaicItems = seq.MediaItems!
          .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
          .Select(m => MediaItemData.FromMediaItem(m))
          .ToList();
        foreach (var mi in validMosaicItems)
        {
          mediaInputIndices.Add(nextIdx++);
          var mExt = Path.GetExtension(mi.Path).ToLowerInvariant();
          var seekSec = (mi.TrimIn ?? 0f).ToString("F2", inv);
          if (mExt is ".gif" || mExt is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi")
            cmd.Append($"-ss {seekSec} -t 0.1 -i \"{mi.Path}\" ");
          else
            cmd.Append($"-loop 1 -i \"{mi.Path}\" ");
        }
      }
      else if (hasLegacyMedia)
      {
        mediaInputIndices.Add(nextIdx++);
        var mExt = Path.GetExtension(seq.MediaPath).ToLowerInvariant();
        var seekSec = (seq.MediaTrimIn ?? 0f).ToString("F2", inv);
        Logger.Info($"Media ext: {mExt}, seekSec: {seekSec}");
        if (mExt is ".gif")
        {
          cmd.Append($"-ss {seekSec} -t 0.1 -i \"{seq.MediaPath}\" ");
        }
        else if (mExt is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi")
        {
          cmd.Append($"-ss {seekSec} -t 0.1 -i \"{seq.MediaPath}\" ");
        }
        else
        {
          cmd.Append($"-loop 1 -i \"{seq.MediaPath}\" ");
        }
      }
      var fc = new StringBuilder();
      fc.Append($"[0:v]scale={vw}:{vh}:force_original_aspect_ratio=decrease,pad={vw}:{vh}:(ow-iw)/2:(oh-ih)/2:black[bg];");
      if (hasMosaicMedia && validMosaicItems != null)
      {
        var mosaicFilter = MosaicRenderer.BuildFilterComplex(
          validMosaicItems, mediaInputIndices, vw, vh,
          seq.MediaScale, seq.MediaGap, seq.MediaLoop,
          seq.MediaBorderColor, seq.MediaBorderPx,
          seq.MediaShadowBlur, seq.MediaShadowAlpha,
          "bg", false);
        fc.Append(mosaicFilter);
        fc.Append("[bg_with_media]copy[out]");
      }
      else if (hasLegacyMedia)
      {
        BuildMediaFilterComplex(fc, seq, mediaInputIndices[0], vw, vh);
      }
      else
        fc.Append("[bg]copy[out]");
      var filterPath = Path.Combine(tempDir, "preview_filter.txt");
      File.WriteAllText(filterPath, fc.ToString(), new UTF8Encoding(false));
      cmd.Append($"-filter_complex_script \"{filterPath}\" -map [out] -vframes 1 \"{outPng}\"");
      var fullArgs = cmd.ToString();
      Logger.Info($"Filter complex:\n{fc}");
      Logger.Info($"Full FFmpeg args:\n{fullArgs}");
      var result = await Task.Run(() =>
      {
        try
        {
          var psi = new ProcessStartInfo
          {
            FileName         = exe,
            Arguments        = fullArgs,
            WorkingDirectory = tempDir,
            UseShellExecute  = false,
            CreateNoWindow   = true,
            RedirectStandardError = true,
          };
          using var proc = Process.Start(psi);
          if (proc == null)
          {
            Logger.Error("Process.Start returned null — ffmpeg not found?");
            return null;
          }
          var stderrTask = proc.StandardError.ReadToEndAsync();
          var exited = proc.WaitForExit(15000);
          if (!exited)
          {
            Logger.Warn("FFmpeg timeout (15s) — killing process");
            try { proc.Kill(); } catch { }
            return null;
          }
          stderrTask.Wait(2000);
          var stderr = stderrTask.IsCompleted ? stderrTask.Result : "(read timeout)";
          var exitCode = proc.ExitCode;
          Logger.FfmpegResult("GeneratePreviewFrameAsync", exe, fullArgs, exitCode, stderr, outPng);
          return File.Exists(outPng) ? outPng : null;
        }
        catch (Exception ex)
        {
          Logger.Error("FFmpeg execution failed", ex);
          return null;
        }
      });
      try { File.Delete(filterPath); } catch { }
      if (result != null)
        Logger.Info($"Preview generated OK: {result}");
      else
        Logger.Warn("Preview FAILED — no output PNG");
      return result;
    }
    public RenderManifest BuildMiniManifest(Sequence seq)
    {
      var projectPath = _projectService.FilePath ?? "";
      var slidesDir   = ResolveSlidesDirectory();
      var slidePath   = ResolveSlideRelativeFromNote(seq.Note);
      return new RenderManifest
      {
        ProjectPath     = projectPath,
        SlidesDirectory = slidesDir,
        Segments = new List<ManifestSegment>
        {
          new ManifestSegment
          {
            Label            = $"Preview — {seq.Id}",
            SlidePath        = slidePath,
            SequenceIds      = new List<string> { seq.Id ?? "" },
            MediaPath        = seq.MediaPath,
            MediaScale       = seq.MediaScale,
            MediaSpeed       = seq.MediaSpeed,
            MediaLoop        = seq.MediaLoop,
            MediaBorderColor = seq.MediaBorderColor,
            MediaBorderPx    = seq.MediaBorderPx,
            MediaShadowBlur  = seq.MediaShadowBlur,
            MediaShadowAlpha = seq.MediaShadowAlpha,
            MediaTrimIn      = seq.MediaTrimIn,
            MediaTrimOut     = seq.MediaTrimOut,
          }
        }
      };
    }
    public async Task<RenderResult> RenderPreviewSegmentAsync(
      Sequence       seq,
      RenderSettings settings)
    {
      Logger.Section("RenderPreviewSegmentAsync");
      Logger.Info($"Sequence: id={seq.Id}");
      try
      {
        var miniManifest = BuildMiniManifest(seq);
        Logger.Info($"MiniManifest: {miniManifest.Segments.Count} segments, slidesDir={miniManifest.SlidesDirectory}");
        var previewDir = Path.Combine(Path.GetTempPath(), "ro_preview");
        Directory.CreateDirectory(previewDir);
        var tmpDir = Path.Combine(previewDir, "tmp");
        Directory.CreateDirectory(tmpDir);
        var outFile = Path.Combine(previewDir, $"preview_{DateTime.Now:HHmmss}.mp4");
        Logger.Info($"Output: {outFile}, Temp: {tmpDir}");
        var options = BuildOptions(
          settings, outFile, tmpDir,
          crfOverride: 30, isPreview: true);
        options.PreviewSkipAvatar = !settings.PreviewIncludeAvatar;
        options.PreviewSkipShadow = !settings.PreviewIncludeShadow;
        options.PreviewSkipSubs   = !settings.PreviewIncludeSubs;
        options.PreviewLowFps     = !settings.PreviewFullFps;
        Logger.Info($"Options: CRF={options.Crf}, NVENC={options.UseNvenc}, IsPreview={options.IsPreview}, Azure={options.UseAzureTts}, FFmpeg={options.FfmpegBinaryPath}");
        Logger.Info("Calling RenderPipeline.BuildAsync on thread pool...");
        var project = _projectService.Project;
        var result = await Task.Run(async () =>
        {
          try
          {
            return await RenderPipeline.BuildAsync(
              miniManifest, project, options, null, default);
          }
          catch (Exception innerEx)
          {
            Logger.Error("BuildAsync INNER exception", innerEx);
            return new RenderResult(false, null, innerEx.Message, TimeSpan.Zero, 0);
          }
        });
        Logger.Info($"RenderPreviewSegment result: success={result.Success}, path={result.OutputPath}, elapsed={result.ElapsedTime.TotalSeconds:F1}s, error={result.ErrorMessage}");
        return result;
      }
      catch (Exception ex)
      {
        Logger.Error("RenderPreviewSegmentAsync EXCEPTION", ex);
        return new RenderResult(false, null, ex.Message, TimeSpan.Zero, 0);
      }
    }
    public (RenderManifest manifest, RenderOptions options) PrepareFullExport(
      RenderSettings settings)
    {
      var projectPath = _projectService.FilePath!;
      var project     = _projectService.Project;
      var scene       = project.CurrentScene;
      var slidesDir = ResolveSlidesDirectory(scene);
      var manifest = RenderLauncher.LoadOrScaffoldManifest(projectPath, project, slidesDir);
      if (!string.IsNullOrWhiteSpace(slidesDir) && Directory.Exists(slidesDir))
        manifest.SlidesDirectory = slidesDir;
      var outDir = settings.OutputDirectory;
      if (string.IsNullOrWhiteSpace(outDir))
      {
        if (!string.IsNullOrWhiteSpace(slidesDir) && Directory.Exists(slidesDir)
            && scene?.SlidesDirectory != null)
        {
          var sceneDirParent = Path.GetDirectoryName(slidesDir) ?? slidesDir;
          outDir = Path.Combine(sceneDirParent, "exports");
        }
        else
        {
          var jsonDir  = Path.GetDirectoryName(projectPath) ?? ".";
          var repoRoot = Path.GetDirectoryName(jsonDir) ?? jsonDir;
          outDir = Path.Combine(repoRoot, "exports");
        }
      }
      Directory.CreateDirectory(outDir);
      var baseName = Path.GetFileNameWithoutExtension(projectPath);
      if (project.Scenes.Count > 1 && scene != null)
        baseName += "_" + SafeFileFragment(scene.Id);
      var outFile = Path.Combine(outDir,
        baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
      var options = BuildOptions(settings, outFile, Path.Combine(Path.GetTempPath(), "ro_render"));
      Logger.Info($"PrepareFullExport: scene='{scene?.Name ?? "(none)"}', slidesDir={manifest.SlidesDirectory}, output={outFile}, segments={manifest.Segments.Count}");
      return (manifest, options);
    }
    private static string SafeFileFragment(string? raw)
    {
      if (string.IsNullOrWhiteSpace(raw)) return "default";
      var sb = new StringBuilder(raw.Length);
      foreach (var c in raw)
        sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
      return sb.ToString();
    }
    private static void BuildMediaFilterComplex(StringBuilder fc, Sequence seq, int mediaIdx, int canvasW, int canvasH)
    {
      var inv = CultureInfo.InvariantCulture;
      int mW    = (int)(canvasW * Math.Clamp(seq.MediaScale, 0.1f, 1f));
      int mH    = (int)(canvasH * Math.Clamp(seq.MediaScale, 0.1f, 1f));
      int bpx   = Math.Max(0, seq.MediaBorderPx);
      int blur  = Math.Max(1, seq.MediaShadowBlur);
      var alpha  = Math.Clamp(seq.MediaShadowAlpha, 0f, 1f).ToString("F2", inv);
      bool hasCrop = seq.HasCrop;
      string cropFilter = "";
      if (hasCrop)
      {
        int cL = Math.Max(0, seq.MediaCropLeft);
        int cT = Math.Max(0, seq.MediaCropTop);
        int cR = Math.Max(0, seq.MediaCropRight);
        int cB = Math.Max(0, seq.MediaCropBottom);
        cropFilter = $"crop=iw-{cL}-{cR}:ih-{cT}-{cB}:{cL}:{cT},";
      }
      fc.Append($"[{mediaIdx}:v]{cropFilter}scale={mW}:{mH}:force_original_aspect_ratio=increase,");
      var (gcx, gcy) = GravityOffset(seq.MediaCropGravity ?? "center");
      fc.Append($"crop={mW}:{mH}:{gcx}:{gcy},format=rgba[media_raw];");
      if (bpx > 0)
        fc.Append($"[media_raw]pad=iw+{2 * bpx}:ih+{2 * bpx}:{bpx}:{bpx}:white[media_framed];");
      else
        fc.Append("[media_raw]copy[media_framed];");
      if (blur > 0 && seq.MediaShadowAlpha > 0.01f)
      {
        fc.Append("[media_framed]split=2[mfs][mfo];");
        fc.Append($"[mfs]format=rgba,colorchannelmixer=rr=0:gg=0:bb=0:aa=1,boxblur={blur}:{blur},");
        fc.Append($"colorchannelmixer=aa={alpha}[msh];");
        fc.Append("[bg][msh]overlay=(main_w-overlay_w)/2+8:(main_h-overlay_h)/2+8:format=auto[bgs];");
        fc.Append("[bgs][mfo]overlay=(main_w-overlay_w)/2:(main_h-overlay_h)/2:format=auto[out]");
      }
      else
      {
        fc.Append("[bg][media_framed]overlay=(main_w-overlay_w)/2:(main_h-overlay_h)/2:format=auto[out]");
      }
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
    public static string? GetDefaultFontPath()
    {
      var candidates = new[]
      {
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
      };
      foreach (var c in candidates)
        if (File.Exists(c)) return c;
      return null;
    }
  }
}