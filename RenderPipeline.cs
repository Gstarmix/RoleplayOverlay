using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
namespace RoleplayOverlay
{
  internal enum MediaKind { Image, Gif, Video }
  public sealed class RenderPipeline
  {
    private static bool? _nvencAvailable;
    public static async Task<RenderResult> BuildAsync(
      RenderManifest              manifest,
      Project                     project,
      RenderOptions               options,
      IProgress<RenderProgress>?  progress = null,
      CancellationToken           ct       = default)
    {
      Logger.Info("[BuildAsync] ENTERED");
      var sw = Stopwatch.StartNew();
      try
      {
        Logger.Info("[BuildAsync] Phase: Initializing");
        Report(progress, RenderPhase.Initializing, 0, 0, "Initialisation du pipeline...");
        Directory.CreateDirectory(options.TempDirectory);
        ConfigureFfmpeg(options);
        Logger.Info($"[BuildAsync] FFmpeg configured: {options.FfmpegBinaryPath}");
        ValidateManifest(manifest, project);
        Logger.Info("[BuildAsync] Manifest validated OK");
        if (options.UseNvenc && _nvencAvailable == null)
        {
          Logger.Info("[BuildAsync] Probing NVENC...");
          Report(progress, RenderPhase.Initializing, 0, 0, "Test NVENC...");
          _nvencAvailable = await ProbeNvencAsync(options);
          Logger.Info($"[BuildAsync] NVENC probe result: {_nvencAvailable}");
          if (_nvencAvailable == false)
          {
            Report(progress, RenderPhase.Initializing, 0, 0,
              "NVENC indisponible — fallback libx264 CPU");
          }
        }
        Report(progress, RenderPhase.Collecting, 0, 0, "Generation audio + calcul des timings...");
        Logger.Info("[BuildAsync] Starting CollectSegmentsAsync...");
        var segments = await CollectSegmentsAsync(manifest, project, options, ct);
        Logger.Info($"[BuildAsync] CollectSegments done: {segments.Count} segments");
        if (segments.Count == 0) return Fail("Aucun segment trouve dans le manifest.", sw);
        bool actualNvenc = options.UseNvenc && _nvencAvailable == true;
        int maxPar;
        if (options.MaxParallelSegments > 0)
        {
          maxPar = options.MaxParallelSegments;
        }
        else if (actualNvenc)
        {
          maxPar = Math.Clamp(Environment.ProcessorCount / 3, 2, 8);
        }
        else
        {
          maxPar = Math.Clamp(Environment.ProcessorCount / 4, 2, 6);
        }
        var rendered = new RenderSegment[segments.Count];
        int completedCount = 0;
        if (maxPar >= 2 && segments.Count >= 2)
        {
          using var sem = new SemaphoreSlim(maxPar, maxPar);
          var tasks = new Task[segments.Count];
          var enc = actualNvenc ? "NVENC GPU" : "libx264 CPU";
          Report(progress, RenderPhase.Rendering, 0, segments.Count,
            $"Rendu de {segments.Count} segments (x{maxPar} parallele, {enc})...");
          for (int i = 0; i < segments.Count; i++)
          {
            ct.ThrowIfCancellationRequested();
            var idx = i;
            var seg = segments[i];
            tasks[i] = Task.Run(async () =>
            {
              await sem.WaitAsync(ct);
              try
              {
                var r = await RenderSegmentAsync(seg, options, ct);
                rendered[idx] = r;
                var done = Interlocked.Increment(ref completedCount);
                Report(progress, RenderPhase.Rendering, done, segments.Count,
                  $"Segment {done}/{segments.Count} terminé — {seg.Label}");
              }
              finally
              {
                sem.Release();
              }
            }, ct);
          }
          await Task.WhenAll(tasks);
        }
        else
        {
          for (int i = 0; i < segments.Count; i++)
          {
            ct.ThrowIfCancellationRequested();
            var seg = segments[i];
            Logger.Info($"[BuildAsync] Rendering segment {i + 1}/{segments.Count}: {seg.Label}");
            Report(progress, RenderPhase.Rendering, i + 1, segments.Count,
              $"Rendu segment {i + 1}/{segments.Count} - {seg.Label}");
            rendered[i] = await RenderSegmentAsync(seg, options, ct);
            Logger.Info($"[BuildAsync] Segment {i + 1} done: {rendered[i].OutputPath}");
          }
        }
        var renderedList = rendered.ToList();
        Logger.Info("[BuildAsync] Starting concat...");
        Report(progress, RenderPhase.Concatenating, segments.Count, segments.Count,
          "Assemblage du MP4 final...");
        await ConcatSegmentsAsync(renderedList, options, ct);
        Logger.Info("[BuildAsync] Concat done");
        try
        {
          var chaptersPath = Path.Combine(
            Path.GetDirectoryName(options.OutputPath) ?? ".",
            Path.GetFileNameWithoutExtension(options.OutputPath) + "_chapters.txt");
          var sb = new System.Text.StringBuilder();
          double cumulSec = 0.0;
          foreach (var seg in renderedList)
          {
            if (string.IsNullOrWhiteSpace(seg.Label)) { cumulSec += seg.Duration.TotalSeconds; continue; }
            var ts = TimeSpan.FromSeconds(Math.Floor(cumulSec));
            string timecode = ts.TotalHours >= 1
              ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
              : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            sb.AppendLine($"{timecode} {seg.Label}");
            cumulSec += seg.Duration.TotalSeconds;
          }
          File.WriteAllText(chaptersPath, sb.ToString(), new System.Text.UTF8Encoding(false));
          Logger.Info($"[BuildAsync] Chapters file written: {chaptersPath}");
        }
        catch (Exception ex)
        {
          Logger.Warn($"[BuildAsync] Chapters file generation failed (non-bloquant): {ex.Message}");
        }
        if (options.CleanupTempOnSuccess)
        {
          Report(progress, RenderPhase.Cleaning, segments.Count, segments.Count, "Nettoyage...");
          CleanTemp(renderedList, options.TempDirectory);
        }
        Report(progress, RenderPhase.Done, segments.Count, segments.Count,
          $"Rendu termine -> {options.OutputPath}");
        sw.Stop();
        return new RenderResult(true, options.OutputPath, null, sw.Elapsed, renderedList.Count);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        Logger.Error("[BuildAsync] UNHANDLED EXCEPTION", ex);
        Report(progress, RenderPhase.Failed, 0, 0, $"Erreur : {ex.Message}");
        return Fail(ex.Message, sw);
      }
      catch (OperationCanceledException)
      {
        Report(progress, RenderPhase.Failed, 0, 0, "Rendu annule.");
        return Fail("Rendu annule par l'utilisateur.", sw);
      }
      catch (Exception ex)
      {
        Report(progress, RenderPhase.Failed, 0, 0, $"Erreur : {ex.Message}");
        return Fail(ex.Message, sw);
      }
    }
    private static async Task<List<RenderSegment>> CollectSegmentsAsync(
      RenderManifest manifest,
      Project        project,
      RenderOptions  options,
      CancellationToken ct)
    {
      var scene = project.CurrentScene;
      if (scene == null) return new List<RenderSegment>();
      var seqById = scene.Sequences
        .Where(s => !string.IsNullOrWhiteSpace(s.Id))
        .ToDictionary(s => s.Id!, StringComparer.OrdinalIgnoreCase);
      var (azKey, azRegion) = AzureConfig.Load();
      using var audioEngine  = new OfflineAudioEngine(
          options.TempDirectory, options.UseAzureTts, azKey, azRegion);
      var renderer          = new HeadlessRenderer();
      renderer.ApplyVisibilityFrom(project.Global);
      var player = new SequencePlayer(audioEngine, renderer);
      player.Preload(project);
      var result      = new List<RenderSegment>(manifest.Segments.Count);
      int globalIndex = 0;
      foreach (var manifestSeg in manifest.Segments)
      {
        ct.ThrowIfCancellationRequested();
        string? slidePath = ResolveSlidePath(manifest.SlidesDirectory, manifestSeg.SlidePath);
        if (manifestSeg.SequenceIds.Count == 0)
        {
          var silentDur = TimeSpan.FromSeconds(Math.Max(1.0, manifestSeg.MinDurationSec));
          result.Add(new RenderSegment(globalIndex++, manifestSeg.Label,
            slidePath, null, silentDur, null, SpeakerKind.Bot1, null));
          continue;
        }
        foreach (var seqId in manifestSeg.SequenceIds)
        {
          ct.ThrowIfCancellationRequested();
          if (!seqById.TryGetValue(seqId, out var seq)) continue;
          var speaker = SpeakerHelper.Parse(seq.Speaker);
          renderer.HideAllTexts();
          renderer.SetActiveSpeaker(speaker);
          if (seq.ShowText && !string.IsNullOrWhiteSpace(seq.Text))
            renderer.ShowSpeakerText(speaker, seq.Text!);
          var voice = SpeakerHelper.ResolveVoice(seq, project.Global);
          string? camClip = ResolveMediaPath(seq.CamClipPath);
          if (camClip == null && !string.IsNullOrWhiteSpace(seq.CamClipPath) && File.Exists(seq.CamClipPath))
            camClip = seq.CamClipPath;
          bool hasCamClip = camClip != null && File.Exists(camClip);
          string? composedClip = ResolveMediaPath(seq.ComposedClipPath);
          if (composedClip == null && !string.IsNullOrWhiteSpace(seq.ComposedClipPath) && File.Exists(seq.ComposedClipPath))
            composedClip = seq.ComposedClipPath;
          bool hasComposedClip = composedClip != null && File.Exists(composedClip);
          string? voClip    = hasComposedClip ? composedClip : (hasCamClip ? camClip : null);
          bool    hasVoClip = voClip != null;
          if (hasVoClip)
            audioEngine.StopAll();
          else if (string.Equals(seq.Mode, "mp3", StringComparison.OrdinalIgnoreCase)
              && !string.IsNullOrWhiteSpace(seq.Mp3))
            audioEngine.PlayMp3(seq.Mp3!);
          else
          {
            var ttsText = TtsHelper.SanitizeForTts(seq.Text ?? string.Empty, voice);
            if (!string.IsNullOrWhiteSpace(ttsText)) audioEngine.Speak(ttsText, voice);
            else audioEngine.StopAll();
          }
          var frame     = renderer.CaptureFrame();
          var audioPath = audioEngine.LastAudioPath;
          var duration  = audioEngine.LastDuration ?? TimeSpan.FromSeconds(manifestSeg.MinDurationSec);
          if (hasVoClip)
          {
            var (voWav, voDur) = ExtractCamAudio(voClip!, options, globalIndex);
            if (voWav != null) audioPath = voWav;
            if (voDur > TimeSpan.Zero) duration = voDur;
          }
          if (duration < TimeSpan.FromSeconds(manifestSeg.MinDurationSec))
            duration = TimeSpan.FromSeconds(manifestSeg.MinDurationSec);
          if (duration < TimeSpan.FromSeconds(0.5))
            duration = TimeSpan.FromSeconds(0.5);
          if (audioPath != null
              && audioPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
              && File.Exists(audioPath))
          {
              var wavNorm = Path.Combine(options.TempDirectory,
                  $"norm_{globalIndex:D4}_{Path.GetFileNameWithoutExtension(audioPath)}.wav");
              try
              {
                  var exe = string.IsNullOrWhiteSpace(options.FfmpegBinaryPath)
                      ? @"C:\ffmpeg\bin\ffmpeg.exe"
                      : Path.Combine(options.FfmpegBinaryPath, "ffmpeg.exe");
                  var normArgs = $"-y -i \"{audioPath}\" -acodec pcm_s16le -ar 44100 -ac 2 \"{wavNorm}\"";
                  var psi = new System.Diagnostics.ProcessStartInfo
                  {
                      FileName = exe,
                      Arguments = normArgs,
                      UseShellExecute = false,
                      CreateNoWindow = true,
                      RedirectStandardError = true
                  };
                  using var proc = System.Diagnostics.Process.Start(psi);
                  if (proc != null)
                  {
                      var stderr = proc.StandardError.ReadToEnd();
                      proc.WaitForExit(30_000);
                      if (proc.ExitCode == 0 && File.Exists(wavNorm))
                      {
                          var wavInfo = new FileInfo(wavNorm);
                          Logger.Info($"[CollectSeg] Normalized MP3→WAV: '{audioPath}' → '{wavNorm}' (size={wavInfo.Length}B)");
                          try
                          {
                              using var reader = new NAudio.Wave.WaveFileReader(wavNorm);
                              var buffer = new byte[8192];
                              int read = reader.Read(buffer, 0, buffer.Length);
                              int maxSample = 0;
                              for (int i = 0; i + 1 < read; i += 2)
                              {
                                  int s = (short)(buffer[i] | (buffer[i+1] << 8));
                                  if (s < 0) s = -s;
                                  if (s > maxSample) maxSample = s;
                              }
                              Logger.Info($"[CollectSeg] WAV silence check: maxSample={maxSample:F6} (0=silent) dur={reader.TotalTime.TotalSeconds:F2}s");
                          }
                          catch (Exception ex) { Logger.Warn($"[CollectSeg] WAV check failed: {ex.Message}"); }
                          audioPath = wavNorm;
                      }
                      else
                      {
                          Logger.Warn($"[CollectSeg] MP3→WAV failed (exit={proc.ExitCode}): {stderr}");
                      }
                  }
              }
              catch (Exception ex)
              {
                  Logger.Warn($"[CollectSeg] MP3→WAV exception: {ex.Message}");
              }
          }
          string? bubbleText = seq.ShowText ? GetActiveText(frame, speaker) : null;
          string? mediaPath       = ResolveMediaPath(seq.MediaPath) ?? ResolveMediaPath(manifestSeg.MediaPath);
          float   mediaScale      = seq.MediaPath != null ? seq.MediaScale      : manifestSeg.MediaScale;
          float   mediaSpeed      = seq.MediaPath != null ? seq.MediaSpeed      : manifestSeg.MediaSpeed;
          bool    mediaLoop       = seq.MediaPath != null ? seq.MediaLoop       : manifestSeg.MediaLoop;
          string  mediaBorderColor= seq.MediaPath != null ? seq.MediaBorderColor: manifestSeg.MediaBorderColor;
          int     mediaBorderPx   = seq.MediaPath != null ? seq.MediaBorderPx   : manifestSeg.MediaBorderPx;
          int     mediaShadowBlur = seq.MediaPath != null ? seq.MediaShadowBlur : manifestSeg.MediaShadowBlur;
          float   mediaShadowAlpha= seq.MediaPath != null ? seq.MediaShadowAlpha: manifestSeg.MediaShadowAlpha;
          float?  mediaTrimIn     = seq.MediaPath != null ? seq.MediaTrimIn     : manifestSeg.MediaTrimIn;
          float?  mediaTrimOut    = seq.MediaPath != null ? seq.MediaTrimOut    : manifestSeg.MediaTrimOut;
          int     mediaCropLeft   = seq.MediaPath != null ? seq.MediaCropLeft   : manifestSeg.MediaCropLeft;
          int     mediaCropTop    = seq.MediaPath != null ? seq.MediaCropTop    : manifestSeg.MediaCropTop;
          int     mediaCropRight  = seq.MediaPath != null ? seq.MediaCropRight  : manifestSeg.MediaCropRight;
          int     mediaCropBottom = seq.MediaPath != null ? seq.MediaCropBottom : manifestSeg.MediaCropBottom;
          string  mediaCropGravity = seq.MediaPath != null ? seq.MediaCropGravity : manifestSeg.MediaCropGravity;
          string  mediaAnchor      = seq.MediaPath != null ? seq.MediaAnchor      : manifestSeg.MediaAnchor;
          List<MediaItemData>? mediaItemDatas = null;
          int mediaGap = 6;
          if (seq.MediaItems != null && seq.MediaItems.Count > 0)
          {
            mediaItemDatas = seq.MediaItems
              .Where(m => !string.IsNullOrWhiteSpace(m.Path))
              .Select(m => MediaItemData.FromMediaItem(m))
              .ToList();
            if (mediaItemDatas.Count == 0) mediaItemDatas = null;
            mediaGap = seq.MediaGap;
          }
          int? overrideAvatarX = null;
          int? overrideAvatarY = null;
          if (!string.IsNullOrWhiteSpace(seq.PairId))
          {
              var sibling = scene.Sequences.FirstOrDefault(s =>
                  s != seq
                  && string.Equals(s.PairId, seq.PairId, StringComparison.OrdinalIgnoreCase));
              if (sibling != null)
              {
                  var siblingVoice = sibling.Voice ?? project.Global.Voice ?? "";
                  var seqVoice     = seq.Voice     ?? project.Global.Voice ?? "";
                  bool seqIsFr     = seqVoice.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
                  bool siblingIsEn = siblingVoice.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                  if (seqIsFr && siblingIsEn)
                  {
                      var sibSpeaker = SpeakerHelper.Parse(sibling.Speaker);
                      switch (sibSpeaker)
                      {
                          case SpeakerKind.You:
                              overrideAvatarX = (int)options.YouX;
                              overrideAvatarY = (int)options.YouY;
                              break;
                          case SpeakerKind.Bot1:
                              overrideAvatarX = (int)options.Bot1X;
                              overrideAvatarY = (int)options.Bot1Y;
                              break;
                          case SpeakerKind.Bot2:
                              overrideAvatarX = (int)options.Bot2X;
                              overrideAvatarY = (int)options.Bot2Y;
                              break;
                      }
                  }
              }
          }
          result.Add(new RenderSegment(
            globalIndex++,
            $"{manifestSeg.Label} - {seqId}",
            slidePath,
            audioPath,
            duration,
            bubbleText,
            speaker,
            null,
            MediaPath:        mediaPath,
            MediaScale:       mediaScale,
            MediaSpeed:       mediaSpeed,
            MediaLoop:        mediaLoop,
            MediaBorderColor: mediaBorderColor,
            MediaBorderPx:    mediaBorderPx,
            MediaShadowBlur:  mediaShadowBlur,
            MediaShadowAlpha: mediaShadowAlpha,
            MediaTrimIn:      mediaTrimIn,
            MediaTrimOut:     mediaTrimOut,
            MediaCropLeft:    mediaCropLeft,
            MediaCropTop:     mediaCropTop,
            MediaCropRight:   mediaCropRight,
            MediaCropBottom:  mediaCropBottom,
            MediaCropGravity: mediaCropGravity,
            MediaAnchor:      mediaAnchor,
            MediaItems:       mediaItemDatas,
            MediaGap:         mediaGap,
            OverrideAvatarX:  overrideAvatarX,
            OverrideAvatarY:  overrideAvatarY,
            CamClipPath:      hasCamClip ? camClip : null,
            ComposedClipPath: hasComposedClip ? composedClip : null,
            CamX:             seq.CamX,
            CamY:             seq.CamY,
            CamDiam:          seq.CamDiam,
            CamRelift:        seq.CamRelift
          ));
        }
      }
      await Task.Yield();
      return result;
    }
    private static async Task<RenderSegment> RenderSegmentAsync(
      RenderSegment     seg,
      RenderOptions     options,
      CancellationToken ct)
    {
      var tempDir = options.TempDirectory;
      var segName = $"segment_{seg.Index:D4}.mp4";
      var segPath = Path.Combine(tempDir, segName);
      bool hasAudio = seg.AudioPath != null && File.Exists(seg.AudioPath);
      Logger.Info($"[RenderSeg#{seg.Index}] Audio: hasAudio={hasAudio}, path='{seg.AudioPath}'");
      if (seg.AudioPath != null)
      {
        try
        {
          var fi = new FileInfo(seg.AudioPath);
          Logger.Info($"[RenderSeg#{seg.Index}] Audio file check: exists={fi.Exists}, size={(fi.Exists ? fi.Length : 0)}B");
        }
        catch (Exception ex)
        {
          Logger.Warn($"[RenderSeg#{seg.Index}] Audio file check exception: {ex.Message}");
        }
      }
      bool hasSlide = seg.SlideImagePath != null && File.Exists(seg.SlideImagePath);
      string imageInput = hasSlide
        ? seg.SlideImagePath!
        : GenerateBlackFramePath(tempDir, options.VideoWidth, options.VideoHeight);
      var dur = seg.Duration.TotalSeconds
        .ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
      int w = options.VideoWidth;
      int h = options.VideoHeight;
      bool hasComposed = !string.IsNullOrWhiteSpace(seg.ComposedClipPath) && File.Exists(seg.ComposedClipPath);
      if (hasComposed)
      {
        string? composedAssPath = null;
        bool composedBurnSubs = options.BurnSubtitles && !(options.IsPreview && options.PreviewSkipSubs);
        if (composedBurnSubs && !string.IsNullOrWhiteSpace(seg.BubbleText))
        {
          composedAssPath = Path.Combine(tempDir, $"sub_{seg.Index:D4}.ass");
          SubtitleGenerator.GenerateAss(
            seg.BubbleText!, seg.Duration, seg.Speaker, composedAssPath,
            options.SubColors, options.SubtitleAssSize, options.VideoWidth, options.VideoHeight);
        }
        bool composedHasAudio = seg.AudioPath != null && File.Exists(seg.AudioPath);
        bool composedHasMosaic = seg.MediaItems != null && seg.MediaItems.Count > 0
          && seg.MediaItems.Any(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path));
        bool composedHasLegacy = !composedHasMosaic
          && !string.IsNullOrWhiteSpace(seg.MediaPath) && File.Exists(seg.MediaPath);
        if (composedHasMosaic || composedHasLegacy)
        {
          int cFps = (options.IsPreview && options.PreviewLowFps) ? 15 : 25;
          string CFfPath(string p) => "'" + p.Replace("\\", "/").Replace(":", "\\:") + "'";
          var mcsb = new StringBuilder();
          mcsb.Append("-y ");
          mcsb.Append($"-i \"{seg.ComposedClipPath}\" ");
          if (composedHasAudio) mcsb.Append($"-i \"{seg.AudioPath}\" ");
          int cNextIdx = composedHasAudio ? 2 : 1;
          var cMediaIdx = new List<int>();
          List<MediaItemData>? cValidItems = null;
          if (composedHasMosaic)
          {
            cValidItems = seg.MediaItems!
              .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
              .ToList();
            mcsb.Append(BuildTimelineInputArgs(cValidItems, dur, cFps));
            for (int mi = 0; mi < cValidItems.Count; mi++) cMediaIdx.Add(cNextIdx++);
          }
          else
          {
            cMediaIdx.Add(cNextIdx++);
            mcsb.Append(BuildMediaInputArgs(seg, dur, cFps));
          }
          bool camRelift = seg.CamRelift;
          int rcDiam = ((int)(w * Math.Clamp(seg.CamDiam, 0.10, 1.0))) / 2 * 2;
          int rcRad  = rcDiam / 2;
          int rcX    = Math.Clamp((int)(w * Math.Clamp(seg.CamX, 0, 1)) - rcRad, 0, Math.Max(0, w - rcDiam));
          int rcY    = Math.Clamp((int)(h * Math.Clamp(seg.CamY, 0, 1)) - rcRad, 0, Math.Max(0, h - rcDiam));
          var cfc = new StringBuilder();
          cfc.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}");
          cfc.Append(camRelift ? ",split=2[bg][camsrc];" : "[bg];");
          if (composedHasMosaic)
            cfc.Append(BuildTimelineOverlays(
              cValidItems!, cMediaIdx, w, h,
              seg.MediaBorderColor, seg.MediaBorderPx, "bg",
              double.Parse(dur, System.Globalization.CultureInfo.InvariantCulture), cFps));
          else
            cfc.Append(BuildMediaFilterComplex(
              seg, cMediaIdx[0], w, h, "bg", options.IsPreview && options.PreviewSkipShadow));
          string preOut = "bg_with_media";
          if (camRelift)
          {
            cfc.Append($"[camsrc]crop={rcDiam}:{rcDiam}:{rcX}:{rcY},format=rgba,"
                     + $"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':"
                     + $"a='255*max(0,min(1,({rcRad}-hypot(X-{rcRad},Y-{rcRad}))/1.5))'[relcam];");
            cfc.Append($"[bg_with_media][relcam]overlay={rcX}:{rcY}[camlifted];");
            preOut = "camlifted";
          }
          if (composedAssPath != null)
            cfc.Append($"[{preOut}]subtitles={CFfPath(composedAssPath)}[out];");
          else
            cfc.Append($"[{preOut}]copy[out];");
          var cmFilterName = $"filter_{seg.Index:D4}.filter";
          var cmFilterPath = Path.Combine(tempDir, cmFilterName);
          File.WriteAllText(cmFilterPath, cfc.ToString(), new System.Text.UTF8Encoding(false));
          mcsb.Append($"-filter_complex_script \"{cmFilterName}\" ");
          mcsb.Append(composedHasAudio ? "-map [out] -map 1:a " : "-map [out] ");
          mcsb.Append(VideoEncoderArgs(options));
          mcsb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
          mcsb.Append(composedHasAudio ? "-shortest " : $"-t {dur} ");
          mcsb.Append("-movflags +faststart ");
          if (options.Threads > 0) mcsb.Append($"-threads {options.Threads} ");
          mcsb.Append($"\"{segPath}\"");
          Logger.Info($"[RenderSeg#{seg.Index}] FFmpeg command (composed clip + media): {mcsb}");
          await RunFfmpegWithNvencFallbackAsync(mcsb.ToString(), options.FfmpegBinaryPath, tempDir, options, ct);
          try { File.Delete(cmFilterPath); } catch { }
          if (composedAssPath != null) try { File.Delete(composedAssPath); } catch { }
          return seg with { OutputPath = segPath };
        }
        var csb = new StringBuilder();
        csb.Append("-y ");
        csb.Append($"-i \"{seg.ComposedClipPath}\" ");
        if (composedHasAudio) csb.Append($"-i \"{seg.AudioPath}\" ");
        var composedFilterName = $"filter_{seg.Index:D4}.txt";
        var composedFilterPath = Path.Combine(tempDir, composedFilterName);
        string composedFilter = composedAssPath != null
          ? $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}," +
            $"subtitles='" + composedAssPath.Replace("\\", "/").Replace(":", "\\:") + "'"
          : $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}";
        File.WriteAllText(composedFilterPath, composedFilter, new System.Text.UTF8Encoding(false));
        csb.Append(composedHasAudio ? "-map 0:v -map 1:a " : "-map 0:v ");
        csb.Append(VideoEncoderArgs(options));
        csb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
        csb.Append($"-filter_script:v \"{composedFilterName}\" ");
        csb.Append(composedHasAudio ? "-shortest " : $"-t {dur} ");
        csb.Append("-movflags +faststart ");
        if (options.Threads > 0) csb.Append($"-threads {options.Threads} ");
        csb.Append($"\"{segPath}\"");
        Logger.Info($"[RenderSeg#{seg.Index}] FFmpeg command (composed clip): {csb}");
        await RunFfmpegWithNvencFallbackAsync(csb.ToString(), options.FfmpegBinaryPath, tempDir, options, ct);
        try { File.Delete(composedFilterPath); } catch { }
        if (composedAssPath != null) try { File.Delete(composedAssPath); } catch { }
        return seg with { OutputPath = segPath };
      }
      bool hasMedia = !string.IsNullOrWhiteSpace(seg.MediaPath) && File.Exists(seg.MediaPath);
      MediaKind mediaKind = MediaKind.Image;
      if (hasMedia)
        mediaKind = DetectMediaKind(seg.MediaPath!);
      string? avatarPath = null;
      int avatarX = 20, avatarY = 20;
      int avatarSize = options.AvatarSize;
      int glowR = 255, glowG = 212, glowB = 0;
      if (options.ShowAvatarInVideo)
      {
        switch (seg.Speaker)
        {
          case SpeakerKind.You:
            avatarPath = options.YouAvatarPath;
            avatarX    = (int)options.YouX;
            avatarY    = (int)options.YouY;
            glowR = options.YouGlowR; glowG = options.YouGlowG; glowB = options.YouGlowB;
            break;
          case SpeakerKind.Bot1:
            avatarPath = options.Bot1AvatarPath;
            avatarX    = (int)options.Bot1X;
            avatarY    = (int)options.Bot1Y;
            glowR = options.Bot1GlowR; glowG = options.Bot1GlowG; glowB = options.Bot1GlowB;
            break;
          case SpeakerKind.Bot2:
            avatarPath = options.Bot2AvatarPath;
            avatarX    = (int)options.Bot2X;
            avatarY    = (int)options.Bot2Y;
            glowR = options.Bot2GlowR; glowG = options.Bot2GlowG; glowB = options.Bot2GlowB;
            break;
        }
        if (string.IsNullOrWhiteSpace(avatarPath) || !File.Exists(avatarPath))
          avatarPath = null;
        if (seg.OverrideAvatarX.HasValue) avatarX = seg.OverrideAvatarX.Value;
        if (seg.OverrideAvatarY.HasValue) avatarY = seg.OverrideAvatarY.Value;
      }
      if (options.IsPreview && options.PreviewSkipAvatar)
        avatarPath = null;
      bool hasAvatar = avatarPath != null;
      int half = avatarSize / 2;
      string? assName    = null;
      string? assAbsPath = null;
      bool burnSubs = options.BurnSubtitles && !(options.IsPreview && options.PreviewSkipSubs);
      if (burnSubs && !string.IsNullOrWhiteSpace(seg.BubbleText))
      {
        assName    = $"sub_{seg.Index:D4}.ass";
        assAbsPath = Path.Combine(tempDir, assName);
        SubtitleGenerator.GenerateAss(
          seg.BubbleText!,
          seg.Duration,
          seg.Speaker,
          assAbsPath,
          options.SubColors,
          options.SubtitleAssSize,
          options.VideoWidth,
          options.VideoHeight);
      }
      var sb = new StringBuilder();
      int Fps = (options.IsPreview && options.PreviewLowFps) ? 15 : 25;
      int totalVFrames = Math.Max(1,(int)Math.Ceiling(seg.Duration.TotalSeconds*Fps));
      sb.Append("-y ");
      sb.Append($"-framerate {Fps} -loop 1 -t {dur} -i \"{imageInput}\" ");
      if (hasAudio)
        sb.Append($"-i \"{seg.AudioPath}\" ");
      else
        sb.Append("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 ");
      int audioIdx = 1;
      int nextInputIdx = 2;
      int avatarInputIdx = -1;
      if (hasAvatar)
      {
        avatarInputIdx = nextInputIdx++;
        sb.Append($"-framerate {Fps} -loop 1 -t {dur} -i \"{avatarPath}\" ");
      }
      bool hasMosaicMedia = seg.MediaItems != null && seg.MediaItems.Count > 0
        && seg.MediaItems.Any(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path));
      bool hasLegacyMedia = !hasMosaicMedia && hasMedia;
      var mediaInputIndices = new List<int>();
      List<MediaItemData>? validMosaicItems = null;
      if (hasMosaicMedia)
      {
        validMosaicItems = seg.MediaItems!
          .Where(m => !string.IsNullOrWhiteSpace(m.Path) && File.Exists(m.Path))
          .ToList();
        if (hasAvatar)
        {
          var (mosaicArgs, mosaicCount) = MosaicRenderer.BuildAllInputArgs(
            validMosaicItems, dur, Fps, seg.MediaLoop);
          for (int mi = 0; mi < mosaicCount; mi++)
            mediaInputIndices.Add(nextInputIdx++);
          sb.Append(mosaicArgs);
        }
        else
        {
          sb.Append(BuildTimelineInputArgs(validMosaicItems, dur, Fps));
          for (int mi = 0; mi < validMosaicItems.Count; mi++)
            mediaInputIndices.Add(nextInputIdx++);
        }
      }
      else if (hasLegacyMedia)
      {
        mediaInputIndices.Add(nextInputIdx++);
        sb.Append(BuildMediaInputArgs(seg, dur, Fps));
      }
      int mediaInputIdx = mediaInputIndices.Count > 0 ? mediaInputIndices[0] : -1;
      bool hasCam = !string.IsNullOrWhiteSpace(seg.CamClipPath) && File.Exists(seg.CamClipPath);
      int camInputIdx = -1;
      if (hasCam)
      {
        camInputIdx = nextInputIdx++;
        sb.Append($"-t {dur} -i \"{seg.CamClipPath}\" ");
      }
      int camDiam   = ((int)(w * Math.Clamp(seg.CamDiam, 0.10, 1.0))) / 2 * 2;
      int camRad    = camDiam / 2;
      int camPosX   = Math.Clamp((int)(w * Math.Clamp(seg.CamX, 0, 1)) - camRad, 0, Math.Max(0, w - camDiam));
      int camPosY   = Math.Clamp((int)(h * Math.Clamp(seg.CamY, 0, 1)) - camRad, 0, Math.Max(0, h - camDiam));
      string CamMaskFilter() =>
        $"[{camInputIdx}:v]scale={camDiam}:{camDiam}:force_original_aspect_ratio=increase," +
        $"crop={camDiam}:{camDiam},format=rgba," +
        $"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':" +
        $"a='255*max(0,min(1,({camRad}-hypot(X-{camRad},Y-{camRad}))/1.5))'[campip];";
      string InjectCam(string graph)
      {
        if (!hasCam) return graph;
        int i = graph.LastIndexOf("[out]", StringComparison.Ordinal);
        if (i < 0) return graph;
        string head = graph.Substring(0, i);
        string tail = graph.Substring(i + "[out]".Length);
        return head + "[precam]" + tail + CamMaskFilter()
             + $"[precam][campip]overlay={camPosX}:{camPosY}[out];";
      }
      if (hasAvatar)
      {
        float[]? _ampFrames  = null;
        string?  _ampRawPath = null;
        int      ampInputIdx = -1;
        bool skipAmplitude = options.IsPreview && options.PreviewSkipAvatar;
        if (hasAudio && !skipAmplitude)
        {
          try
          {
            _ampFrames  = await ExtractFrameAmplitudeAsync(
              seg.AudioPath!, totalVFrames, Fps,
              options.FfmpegBinaryPath, tempDir, ct);
            var _ampBytes = _ampFrames.Select(v => (byte)(Math.Clamp(v,0f,1f)*255f)).ToArray();
            _ampRawPath = Path.Combine(tempDir, $"amp_{seg.Index:D4}.raw");
            File.WriteAllBytes(_ampRawPath, _ampBytes);
            ampInputIdx = nextInputIdx++;
            sb.Append($"-f rawvideo -pixel_format gray -video_size 1x1 " +
                      $"-framerate {Fps} -t {dur} -i \"{_ampRawPath}\" ");
          }
          catch { }
        }
        bool hasAmpInput = _ampRawPath != null;
        var filterName = $"filter_{seg.Index:D4}.filter";
        var filterPath = Path.Combine(tempDir, filterName);
        string FfmpegPath(string p) => "'" + p.Replace("\\", "/").Replace(":", "\\:") + "'";
        string subPart = assAbsPath != null
          ? $"[video_out]subtitles={FfmpegPath(assAbsPath)}[out];"
          : "[video_out]copy[out];";
        var fc = new StringBuilder();
        var inv   = System.Globalization.CultureInfo.InvariantCulture;
        int grow  = Math.Max(6, avatarSize / 8);
        int avMax = avatarSize + grow;
        int cvs   = avMax + 16;
        int cvH   = cvs / 2;
        int cx    = avatarX + half;
        int cy    = avatarY + half;
        int cLeft = cx - cvH;
        int cTop  = cy - cvH;
        string sinT = "(0.5+0.5*sin(2*3.14159265*0.9*T))";
        string ampFactor = hasAmpInput ? $"*r({cvs},0)/255" : "";
        string Rav  = $"({avatarSize}/2+{grow}/2*{sinT}{ampFactor})";
        string rInn = $"({Rav}+2)";
        string rOut = $"({Rav}+6)";
        bool doGlow = hasAudio && options.GlowIntensity > 0.001f;
        if (options.IsPreview && options.PreviewSkipAvatar)
          doGlow = false;
        int ampCopies = hasAmpInput ? (doGlow ? 3 : 2) : 0;
        if (doGlow)
          fc.Append($"[0:v]scale={w}:{h},split=2[bg][glow_src];");
        else
          fc.Append($"[0:v]scale={w}:{h}[bg];");
        string bgLabel = "bg";
        if (hasMosaicMedia && validMosaicItems != null)
        {
          var mediaFilters = MosaicRenderer.BuildFilterComplex(
            validMosaicItems, mediaInputIndices, w, h,
            seg.MediaScale, seg.MediaGap, seg.MediaLoop,
            seg.MediaBorderColor, seg.MediaBorderPx,
            seg.MediaShadowBlur, seg.MediaShadowAlpha,
            bgLabel, options.IsPreview && options.PreviewSkipShadow);
          fc.Append(mediaFilters);
          bgLabel = "bg_with_media";
        }
        else if (hasLegacyMedia)
        {
          var mediaFilters = BuildMediaFilterComplex(seg, mediaInputIdx, w, h, bgLabel, options.IsPreview && options.PreviewSkipShadow);
          fc.Append(mediaFilters);
          bgLabel = "bg_with_media";
        }
        if (hasAmpInput && ampCopies > 0)
        {
          string ampOuts = string.Concat(Enumerable.Range(1, ampCopies).Select(i => $"[amp{i}]"));
          fc.Append($"[{ampInputIdx}:v]split={ampCopies}{ampOuts};");
        }
        if (hasAmpInput && hasAudio)
        {
          fc.Append($"[{avatarInputIdx}:v]scale={cvs}:{cvs},format=rgba[av_base];");
          fc.Append($"[amp1]scale=1:{cvs}:sws_flags=neighbor,format=rgba[amp_av];");
          fc.Append($"[av_base][amp_av]hstack=inputs=2[av_hs];");
          fc.Append($"[av_hs]geq=" +
                    $"r='if(gte(X,{cvs}),0,r(X,Y))'" +
                    $":g='if(gte(X,{cvs}),0,g(X,Y))'" +
                    $":b='if(gte(X,{cvs}),0,b(X,Y))'" +
                    $":a='if(gte(X,{cvs}),0,255*max(0,min(1,({Rav}-hypot(X-{cvH},Y-{cvH}))/1.5)))'" +
                    $"[av_hs_out];");
          fc.Append($"[av_hs_out]crop={cvs}:{cvs}:0:0[av_circ];");
        }
        else if (hasAudio)
        {
          fc.Append($"[{avatarInputIdx}:v]scale={cvs}:{cvs},format=rgba,");
          fc.Append($"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':");
          fc.Append($"a='255*max(0,min(1,({Rav}-hypot(X-{cvH},Y-{cvH}))/1.5))'[av_circ];");
        }
        else
        {
          fc.Append($"[{avatarInputIdx}:v]scale={cvs}:{cvs},format=rgba,");
          fc.Append($"geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':");
          fc.Append($"a='255*max(0,min(1,({avatarSize/2-2}-hypot(X-{cvH},Y-{cvH}))/1.5))'[av_circ];");
        }
        if (hasAmpInput && hasAudio)
        {
          fc.Append($"[{avatarInputIdx}:v]scale={cvs}:{cvs},format=rgba[ring_base];");
          fc.Append($"[amp2]scale=1:{cvs}:sws_flags=neighbor,format=rgba[amp_ring];");
          fc.Append($"[ring_base][amp_ring]hstack=inputs=2[ring_hs];");
          fc.Append($"[ring_hs]geq=" +
                    $"r='if(gte(X,{cvs}),0,255)'" +
                    $":g='if(gte(X,{cvs}),0,255)'" +
                    $":b='if(gte(X,{cvs}),0,255)'" +
                    $":a='if(gte(X,{cvs}),0," +
                    $"255*max(0,min(1,(hypot(X-{cvH},Y-{cvH})-{rInn})/1.5))" +
                    $"*max(0,min(1,({rOut}-hypot(X-{cvH},Y-{cvH}))/1.5)))'" +
                    $"[ring_hs_out];");
          fc.Append($"[ring_hs_out]crop={cvs}:{cvs}:0:0[ring];");
        }
        else
        {
          int rFix = (hasAudio ? avMax : avatarSize) / 2 + 2;
          int rFout = rFix + 4;
          fc.Append($"[{avatarInputIdx}:v]scale={cvs}:{cvs},format=rgba,");
          fc.Append($"geq=r='255':g='255':b='255':");
          fc.Append($"a='255");
          fc.Append($"*max(0,min(1,(hypot(X-{cvH},Y-{cvH})-{rFix})/1.5))");
          fc.Append($"*max(0,min(1,({rFout}-hypot(X-{cvH},Y-{cvH}))/1.5))'[ring];");
        }
        if (doGlow)
        {
          int    peakR    = avMax / 2 + 6 + 3;
          string intensS  = options.GlowIntensity.ToString("F3", inv);
          bool   isBot1   = seg.Speaker == SpeakerKind.Bot1;
          string dist = $"hypot(X-{cx},Y-{cy})";
          string gaussInner, glowAlpha;
          if (isBot1)
          {
            gaussInner = $"(0.7*exp(-pow(({dist}-{peakR})/5,2))+0.4*exp(-pow(({dist}-{peakR+10})/9,2)))";
          }
          else
          {
            gaussInner = $"exp(-pow(({dist}-{peakR})/6,2))";
          }
          if (hasAmpInput)
          {
            fc.Append($"[amp{(ampCopies >= 3 ? 3 : 1)}]scale=1:{h}:sws_flags=neighbor,format=rgba[amp_glow];");
            fc.Append($"[glow_src]format=rgba[glow_base];");
            fc.Append($"[glow_base][amp_glow]hstack=inputs=2[glow_hs];");
            glowAlpha = $"if(gte(X,{w}),0," +
                        $"255*{intensS}*{sinT}*r({w},0)/255" +
                        $"*{gaussInner})";
            fc.Append($"[glow_hs]geq=" +
                      $"r='if(gte(X,{w}),0,{glowR})'" +
                      $":g='if(gte(X,{w}),0,{glowG})'" +
                      $":b='if(gte(X,{w}),0,{glowB})'" +
                      $":a='{glowAlpha}'[glow_wide];");
            fc.Append($"[glow_wide]crop={w}:{h}:0:0[glow];");
          }
          else
          {
            glowAlpha = $"255*{intensS}*{sinT}*{gaussInner}";
            fc.Append($"[glow_src]format=rgba,");
            fc.Append($"geq=r='{glowR}':g='{glowG}':b='{glowB}':a='{glowAlpha}'[glow];");
          }
        }
        if (doGlow)
        {
          fc.Append($"[{bgLabel}][glow]overlay=format=auto[with_glow];");
          fc.Append($"[with_glow][ring]overlay={cLeft}:{cTop}[with_ring];");
        }
        else
          fc.Append($"[{bgLabel}][ring]overlay={cLeft}:{cTop}[with_ring];");
        fc.Append($"[with_ring][av_circ]overlay={cLeft}:{cTop}[video_out];");
        fc.Append(subPart);
        File.WriteAllText(filterPath, InjectCam(fc.ToString()),
          new System.Text.UTF8Encoding(false));
        sb.Append($"-filter_complex_script \"{filterName}\" ");
        sb.Append("-map [out] ");
        sb.Append($"-map {audioIdx}:a ");
        sb.Append(VideoEncoderArgs(options));
        sb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
        if (hasAudio) sb.Append("-shortest ");
        else          sb.Append($"-t {dur} ");
        sb.Append("-movflags +faststart ");
        if (options.Threads > 0) sb.Append($"-threads {options.Threads} ");
        sb.Append($"\"{segPath}\"");
        Logger.Info($"[RenderSeg#{seg.Index}] FFmpeg command (avatar): {sb}");
        await RunFfmpegWithNvencFallbackAsync(sb.ToString(), options.FfmpegBinaryPath, tempDir, options, ct);
        try { File.Delete(filterPath); } catch { }
        try { if (_ampRawPath != null) File.Delete(_ampRawPath); } catch { }
      }
      else
      {
        var filterName = $"filter_{seg.Index:D4}.filter";
        var filterPath = Path.Combine(tempDir, filterName);
        string FfmpegPath(string p) => "'" + p.Replace("\\", "/").Replace(":", "\\:") + "'";
        if (hasMosaicMedia && validMosaicItems != null)
        {
          var fc = new StringBuilder();
          fc.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black[bg];");
          var mediaFilters = BuildTimelineOverlays(
            validMosaicItems, mediaInputIndices, w, h,
            seg.MediaBorderColor, seg.MediaBorderPx, "bg",
            double.Parse(dur, System.Globalization.CultureInfo.InvariantCulture), Fps);
          fc.Append(mediaFilters);
          if (assAbsPath != null)
            fc.Append($"[bg_with_media]subtitles={FfmpegPath(assAbsPath)}[out];");
          else
            fc.Append("[bg_with_media]copy[out];");
          File.WriteAllText(filterPath, InjectCam(fc.ToString()),
            new System.Text.UTF8Encoding(false));
          sb.Append($"-filter_complex_script \"{filterName}\" ");
          sb.Append("-map [out] ");
          sb.Append($"-map {audioIdx}:a ");
          sb.Append(VideoEncoderArgs(options));
          sb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
          if (hasAudio) sb.Append("-shortest ");
          else          sb.Append($"-t {dur} ");
          sb.Append("-movflags +faststart ");
          if (options.Threads > 0) sb.Append($"-threads {options.Threads} ");
          sb.Append($"\"{segPath}\"");
        }
        else if (hasLegacyMedia)
        {
          var fc = new StringBuilder();
          fc.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black[bg];");
          var mediaFilters = BuildMediaFilterComplex(seg, mediaInputIdx, w, h, "bg", options.IsPreview && options.PreviewSkipShadow);
          fc.Append(mediaFilters);
          if (assAbsPath != null)
            fc.Append($"[bg_with_media]subtitles={FfmpegPath(assAbsPath)}[out];");
          else
            fc.Append("[bg_with_media]copy[out];");
          File.WriteAllText(filterPath, InjectCam(fc.ToString()),
            new System.Text.UTF8Encoding(false));
          sb.Append($"-filter_complex_script \"{filterName}\" ");
          sb.Append("-map [out] ");
          sb.Append($"-map {audioIdx}:a ");
          sb.Append(VideoEncoderArgs(options));
          sb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
          if (hasAudio) sb.Append("-shortest ");
          else          sb.Append($"-t {dur} ");
          sb.Append("-movflags +faststart ");
          if (options.Threads > 0) sb.Append($"-threads {options.Threads} ");
          sb.Append($"\"{segPath}\"");
        }
        else if (hasCam)
        {
          var fc = new StringBuilder();
          fc.Append($"[0:v]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black[bg];");
          if (assAbsPath != null)
            fc.Append($"[bg]subtitles={FfmpegPath(assAbsPath)}[out];");
          else
            fc.Append("[bg]copy[out];");
          File.WriteAllText(filterPath, InjectCam(fc.ToString()),
            new System.Text.UTF8Encoding(false));
          sb.Append($"-filter_complex_script \"{filterName}\" ");
          sb.Append("-map [out] ");
          sb.Append($"-map {audioIdx}:a ");
          sb.Append(VideoEncoderArgs(options));
          sb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
          if (hasAudio) sb.Append("-shortest ");
          else          sb.Append($"-t {dur} ");
          sb.Append("-movflags +faststart ");
          if (options.Threads > 0) sb.Append($"-threads {options.Threads} ");
          sb.Append($"\"{segPath}\"");
        }
        else
        {
          string filterContent;
          if (assAbsPath != null)
          {
            var assPathEscaped = "'" + assAbsPath.Replace("\\", "/").Replace(":", "\\:") + "'";
            filterContent =
              $"scale={w}:{h}:force_original_aspect_ratio=decrease," +
              $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black," +
              $"subtitles={assPathEscaped}";
          }
          else
          {
            filterContent =
              $"scale={w}:{h}:force_original_aspect_ratio=decrease," +
              $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black";
          }
          File.WriteAllText(filterPath, filterContent,
            new System.Text.UTF8Encoding(false));
          sb.Append($"-map 0:v -map {audioIdx}:a ");
          sb.Append(VideoEncoderArgs(options));
          sb.Append("-c:a aac -b:a 192k -ar 44100 -ac 2 ");
          sb.Append($"-filter_script:v \"{filterName}\" ");
          if (hasAudio) sb.Append("-shortest ");
          else          sb.Append($"-t {dur} ");
          sb.Append("-movflags +faststart ");
          if (options.Threads > 0) sb.Append($"-threads {options.Threads} ");
          sb.Append($"\"{segPath}\"");
        }
        Logger.Info($"[RenderSeg#{seg.Index}] FFmpeg command (no-avatar): {sb}");
        await RunFfmpegWithNvencFallbackAsync(sb.ToString(), options.FfmpegBinaryPath, tempDir, options, ct);
        try { File.Delete(filterPath); } catch { }
      }
      if (assAbsPath != null)
        try { File.Delete(assAbsPath); } catch { }
      return seg with { OutputPath = segPath };
    }
    private static MediaKind DetectMediaKind(string path)
    {
      var ext = Path.GetExtension(path).ToLowerInvariant();
      return ext switch
      {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" => MediaKind.Image,
        ".gif" => MediaKind.Gif,
        ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv"  => MediaKind.Video,
        _ => MediaKind.Image
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
    private static (string x, string y) OverlayPosition(string? anchor, int margin)
    {
      string a = (anchor ?? "center").ToLowerInvariant();
      string x = a switch
      {
        "left" or "topleft" or "bottomleft"    => $"{margin}",
        "right" or "topright" or "bottomright" => $"main_w-overlay_w-{margin}",
        _                                       => "(main_w-overlay_w)/2",
      };
      string y = a switch
      {
        "top" or "topleft" or "topright"          => $"{margin}",
        "bottom" or "bottomleft" or "bottomright" => $"main_h-overlay_h-{margin}",
        _                                          => "(main_h-overlay_h)/2",
      };
      return (x, y);
    }
    private static (string? Wav, TimeSpan Dur) ExtractCamAudio(string camClip, RenderOptions options, int index)
    {
      try
      {
        var exe = string.IsNullOrWhiteSpace(options.FfmpegBinaryPath)
            ? @"C:\ffmpeg\bin\ffmpeg.exe"
            : Path.Combine(options.FfmpegBinaryPath, "ffmpeg.exe");
        var wav = Path.Combine(options.TempDirectory, $"camaudio_{index:D4}.wav");
        var args = $"-y -i \"{camClip}\" -vn -acodec pcm_s16le -ar 44100 -ac 2 \"{wav}\"";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
          FileName = exe, Arguments = args,
          UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) return (null, TimeSpan.Zero);
        proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        if (proc.ExitCode != 0 || !File.Exists(wav))
        {
          Logger.Warn($"[CamAudio] extraction echouee (exit={proc.ExitCode}) pour '{camClip}'");
          return (null, TimeSpan.Zero);
        }
        TimeSpan dur = TimeSpan.Zero;
        try { using var r = new NAudio.Wave.WaveFileReader(wav); dur = r.TotalTime; } catch { }
        return (wav, dur);
      }
      catch (Exception ex) { Logger.Warn($"[CamAudio] exception : {ex.Message}"); return (null, TimeSpan.Zero); }
    }
    private static string BuildMediaInputArgs(RenderSegment seg, string dur, int fps)
    {
      var kind = DetectMediaKind(seg.MediaPath!);
      var sb   = new StringBuilder();
      var inv  = System.Globalization.CultureInfo.InvariantCulture;
      float speed    = Math.Clamp(seg.MediaSpeed, 0.1f, 4.0f);
      double durSec  = double.Parse(dur, inv);
      double sourceNeeded = durSec * speed;
      float ssVal   = seg.MediaTrimIn ?? 0f;
      float? outVal = seg.MediaTrimOut;
      double? trimSegmentDur = null;
      if (outVal.HasValue && outVal.Value > ssVal)
        trimSegmentDur = outVal.Value - ssVal;
      double effectiveSourceDur;
      if (trimSegmentDur.HasValue)
      {
        effectiveSourceDur = Math.Min(trimSegmentDur.Value, sourceNeeded);
      }
      else
      {
        effectiveSourceDur = sourceNeeded;
      }
      bool needsLoop = false;
      if (trimSegmentDur.HasValue && trimSegmentDur.Value < sourceNeeded)
        needsLoop = true;
      if (!trimSegmentDur.HasValue && speed > 1.0f)
        needsLoop = true;
      if (seg.MediaLoop)
        needsLoop = true;
      string ssTrim  = ssVal > 0.01f ? ssVal.ToString("F2", inv) : null;
      string durTrim = effectiveSourceDur.ToString("F2", inv);
      switch (kind)
      {
        case MediaKind.Image:
          sb.Append($"-framerate {fps} -loop 1 -t {dur} -i \"{seg.MediaPath}\" ");
          break;
        case MediaKind.Gif:
          if (ssTrim != null) sb.Append($"-ss {ssTrim} ");
          sb.Append($"-stream_loop -1 -t {durTrim} -i \"{seg.MediaPath}\" ");
          break;
        case MediaKind.Video:
          if (ssTrim != null) sb.Append($"-ss {ssTrim} ");
          if (needsLoop) sb.Append("-stream_loop -1 ");
          sb.Append($"-t {durTrim} -i \"{seg.MediaPath}\" ");
          break;
      }
      return sb.ToString();
    }
    private static string BuildMediaFilterComplex(
      RenderSegment seg, int mediaInputIdx, int w, int h, string bgLabel, bool isPreviewSkipShadow = false)
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      var fc  = new StringBuilder();
      float speed = Math.Clamp(seg.MediaSpeed, 0.1f, 4.0f);
      string speedPts = speed != 1.0f
        ? $"setpts=PTS/{speed.ToString("F2", inv)},"
        : "";
      int mediaW = (int)(w * Math.Clamp(seg.MediaScale, 0.1f, 1.0f));
      int mediaH = (int)(h * Math.Clamp(seg.MediaScale, 0.1f, 1.0f));
      int borderPx    = Math.Max(0, seg.MediaBorderPx);
      int shadowBlur  = Math.Max(1, seg.MediaShadowBlur);
      float shadowAlpha = Math.Clamp(seg.MediaShadowAlpha, 0f, 1f);
      string alphaS   = shadowAlpha.ToString("F2", inv);
      string borderColor = "white";
      if (!string.IsNullOrWhiteSpace(seg.MediaBorderColor))
      {
        var hex = seg.MediaBorderColor.TrimStart('#');
        if (hex.Length == 6)
          borderColor = $"0x{hex}";
      }
      fc.Append($"[{mediaInputIdx}:v]{speedPts}");
      bool hasCrop = seg.MediaCropLeft > 0 || seg.MediaCropTop > 0
                  || seg.MediaCropRight > 0 || seg.MediaCropBottom > 0;
      if (hasCrop)
      {
        int cL = Math.Max(0, seg.MediaCropLeft);
        int cT = Math.Max(0, seg.MediaCropTop);
        int cR = Math.Max(0, seg.MediaCropRight);
        int cB = Math.Max(0, seg.MediaCropBottom);
        fc.Append($"crop=iw-{cL}-{cR}:ih-{cT}-{cB}:{cL}:{cT},");
      }
      fc.Append($"scale={mediaW}:{mediaH}:force_original_aspect_ratio=increase,");
      var (gcx, gcy) = GravityOffset(seg.MediaCropGravity ?? "center");
      fc.Append($"crop={mediaW}:{mediaH}:{gcx}:{gcy}," +
                $"format=rgba[media_raw];");
      if (borderPx > 0)
      {
        fc.Append($"[media_raw]pad=iw+{2 * borderPx}:ih+{2 * borderPx}:" +
                  $"{borderPx}:{borderPx}:{borderColor}[media_framed];");
      }
      else
      {
        fc.Append("[media_raw]copy[media_framed];");
      }
      var (ovx, ovy) = OverlayPosition(seg.MediaAnchor, Math.Max(0, (int)(w * 0.03f)));
      if (shadowBlur > 0 && shadowAlpha > 0.01f && !isPreviewSkipShadow)
      {
        fc.Append($"[media_framed]split=2[media_for_shadow][media_for_overlay];");
        fc.Append($"[media_for_shadow]format=rgba," +
                  $"colorchannelmixer=rr=0:gg=0:bb=0:aa=1," +
                  $"boxblur={shadowBlur}:{shadowBlur}," +
                  $"colorchannelmixer=aa={alphaS}" +
                  $"[media_shadow_blur];");
        fc.Append($"[{bgLabel}][media_shadow_blur]overlay=" +
                  $"x={ovx}+8:y={ovy}+8:format=auto" +
                  $"[bg_with_shadow];");
        fc.Append($"[bg_with_shadow][media_for_overlay]overlay=" +
                  $"x={ovx}:y={ovy}:format=auto" +
                  $"[bg_with_media];");
      }
      else
      {
        fc.Append($"[{bgLabel}][media_framed]overlay=" +
                  $"x={ovx}:y={ovy}:format=auto" +
                  $"[bg_with_media];");
      }
      return fc.ToString();
    }
    private static string BuildTimelineInputArgs(List<MediaItemData> items, string segDur, int fps)
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      var sb = new StringBuilder();
      double segSec = double.Parse(segDur, inv);
      foreach (var it in items)
      {
        if (string.IsNullOrWhiteSpace(it.Path) || !File.Exists(it.Path)) continue;
        var kind = DetectMediaKind(it.Path!);
        if (kind == MediaKind.Image)
        {
          sb.Append($"-framerate {fps} -loop 1 -t {segDur} -i \"{it.Path}\" ");
          continue;
        }
        float speed = Math.Clamp(it.Speed, 0.1f, 4.0f);
        float ss    = it.TrimIn ?? 0f;
        float? outv = it.TrimOut;
        double? trimSpan = (outv.HasValue && outv.Value > ss) ? outv.Value - ss : (double?)null;
        double sourceNeeded = segSec * speed;
        double effDur = trimSpan.HasValue ? Math.Min(trimSpan.Value, sourceNeeded) : sourceNeeded;
        string loop = it.Loop ? "-stream_loop -1 " : "";
        if (ss > 0.01f) sb.Append($"-ss {ss.ToString("F2", inv)} ");
        sb.Append($"{loop}-t {effDur.ToString("F2", inv)} -i \"{it.Path}\" ");
      }
      return sb.ToString();
    }
    private static string BuildTimelineOverlays(
      List<MediaItemData> items, List<int> inputIndices, int w, int h,
      string borderColorHex, int borderPx, string bgLabel, double segDurSec, int fps)
    {
      var inv = System.Globalization.CultureInfo.InvariantCulture;
      var fc  = new StringBuilder();
      string borderColor = "white";
      if (!string.IsNullOrWhiteSpace(borderColorHex))
      {
        var hex = borderColorHex.TrimStart('#');
        if (hex.Length == 6) borderColor = $"0x{hex}";
      }
      int b = Math.Max(0, borderPx);
      int n = Math.Min(items.Count, inputIndices.Count);
      if (n == 0) { fc.Append($"[{bgLabel}]copy[bg_with_media];"); return fc.ToString(); }
      for (int i = 0; i < n; i++)
      {
        var it  = items[i];
        int idx = inputIndices[i];
        double scale = Math.Clamp(it.ItemScale, 0.05, 1.0);
        int sw = (int)Math.Round(w * scale); if (sw % 2 != 0) sw++;
        int sh = (int)Math.Round(h * scale); if (sh % 2 != 0) sh++;
        string anim = (it.Anim ?? "none").ToLowerInvariant();
        float itSpeed = Math.Clamp(it.Speed, 0.1f, 4.0f);
        string speedPre = "";
        if (DetectMediaKind(it.Path!) != MediaKind.Image)
        {
          double aAppear = Math.Max(0, it.AppearAt);
          speedPre = $"setpts=(PTS-STARTPTS)/{itSpeed.ToString("F4", inv)}+{aAppear.ToString("F3", inv)}/TB,";
        }
        string cropPre = "";
        if (it.HasContentCrop)
        {
          double cL = Math.Clamp(it.CropFL, 0, 0.9), cT = Math.Clamp(it.CropFT, 0, 0.9);
          double cR = Math.Clamp(it.CropFR, 0, 0.9), cB = Math.Clamp(it.CropFB, 0, 0.9);
          double kw = Math.Max(0.05, 1 - cL - cR), kh = Math.Max(0.05, 1 - cT - cB);
          cropPre = $"crop=iw*{kw.ToString("F4", inv)}:ih*{kh.ToString("F4", inv)}:"
                  + $"iw*{cL.ToString("F4", inv)}:ih*{cT.ToString("F4", inv)},";
        }
        string panX = it.PanX.ToString("F4", inv), panY = it.PanY.ToString("F4", inv);
        string prep;
        if (it.Contain)
          prep = $"{speedPre}{cropPre}scale={sw}:{sh}:force_original_aspect_ratio=decrease";
        else
        {
          string px = panX, py = panY;
          int tsw = sw, tsh = sh;
          double aA = Math.Max(0, it.AppearAt);
          double wdA = it.AppearDur > 0.01 ? it.AppearDur : Math.Max(0.1, segDurSec - aA);
          string aS3 = aA.ToString("F3", inv), wdS3 = wdA.ToString("F3", inv);
          string tnorm = $"min(max((t-{aS3})/{wdS3},0),1)";
          switch (anim)
          {
            case "slide":
              tsw = (int)Math.Round(sw * 1.12); if (tsw % 2 != 0) tsw++;
              tsh = (int)Math.Round(sh * 1.12); if (tsh % 2 != 0) tsh++;
              px = $"({tnorm}*2-1)*0.8"; py = "0"; break;
            case "float":
              tsw = (int)Math.Round(sw * 1.12); if (tsw % 2 != 0) tsw++;
              tsh = (int)Math.Round(sh * 1.12); if (tsh % 2 != 0) tsh++;
              px = "0"; py = $"sin((t-{aS3})*1.8)*0.5"; break;
            case "zoom":
              px = "0"; py = "0"; break;
            case "scrolldown":
              px = "0"; py = $"({tnorm}*2-1)"; break;
            case "scrollup":
              px = "0"; py = $"(1-{tnorm}*2)"; break;
            default:
              if (it.PanAnim)
              {
                double pt1 = Math.Max(0, it.PanT1);
                double t1 = aA + pt1;
                double d  = it.PanDur > 0.01 ? it.PanDur : Math.Max(0.05, wdA - pt1);
                string f = $"min(max((t-{t1.ToString("F3", inv)})/{d.ToString("F3", inv)},0),1)";
                px = $"{panX}+({(it.PanX2 - it.PanX).ToString("F4", inv)})*{f}";
                py = $"{panY}+({(it.PanY2 - it.PanY).ToString("F4", inv)})*{f}";
              }
              break;
          }
          prep = $"{speedPre}{cropPre}scale={tsw}:{tsh}:force_original_aspect_ratio=increase,"
               + $"crop={sw}:{sh}:x='(in_w-{sw})/2*(1+({px}))':y='(in_h-{sh})/2*(1+({py}))'";
          if (anim == "zoom")
            prep += $",zoompan=z='min(pzoom+0.0015,1.18)':d=1:x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s={sw}x{sh}:fps={fps}";
        }
        fc.Append($"[{idx}:v]{prep},format=rgba");
        if (b > 0 && it.Border) fc.Append($",pad=iw+{2 * b}:ih+{2 * b}:{b}:{b}:{borderColor}");
        fc.Append($"[m{i}];");
      }
      string running = bgLabel;
      for (int k = n - 1; k >= 0; k--)
      {
        var it = items[k];
        double a  = Math.Max(0, it.AppearAt);
        string aS = a.ToString("F2", inv);
        string enable = it.AppearDur > 0.01
          ? $"between(t,{aS},{(a + it.AppearDur).ToString("F2", inv)})"
          : $"gte(t,{aS})";
        string px = it.PosX.ToString("F4", inv);
        string py = it.PosY.ToString("F4", inv);
        string bx = $"(main_w*{px})-overlay_w/2";
        string by = $"(main_h*{py})-overlay_h/2";
        string ox = bx, oy = by;
        string outLbl = (k == 0) ? "bg_with_media" : $"bgt{k}";
        fc.Append($"[{running}][m{k}]overlay=x='{ox}':y='{oy}':enable='{enable}':format=auto[{outLbl}];");
        running = outLbl;
      }
      return fc.ToString();
    }
    private static string? ResolveMediaPath(string? path)
    {
      if (string.IsNullOrWhiteSpace(path)) return null;
      return File.Exists(path) ? path : null;
    }
    private static async Task<float[]> ExtractFrameAmplitudeAsync(
      string            audioPath,
      int               totalFrames,
      int               fps,
      string?           ffmpegBin,
      string            tempDir,
      CancellationToken ct)
    {
      var rnd       = Path.GetRandomFileName().Replace(".", "");
      var statsPath = Path.Combine(tempDir, $"ampstats_{rnd}.txt");
      var statsEsc  = "'" + statsPath.Replace("\\", "/").Replace(":", "\\:") + "'";
      int chunkHz   = fps * 100;
      int chunkN    = 100;
      var args =
        $"-y -i \"{audioPath}\" " +
        $"-af \"aresample={chunkHz}," +
        $"asetnsamples=n={chunkN}:p=0," +
        $"astats=metadata=1:reset=1," +
        $"ametadata=print:key=lavfi.astats.Overall.RMS_level:file={statsEsc}\" " +
        $"-f null -";
      try { await RunFfmpegAsync(args, ffmpegBin, tempDir, ct); }
      catch { }
      if (!File.Exists(statsPath))
        return Enumerable.Repeat(1f, totalFrames).ToArray();
      var rms = new List<float>();
      foreach (var line in File.ReadLines(statsPath))
      {
        var ki = line.IndexOf("RMS_level=", StringComparison.Ordinal);
        if (ki < 0) continue;
        var vs = line.Substring(ki + 10).Trim();
        if (string.IsNullOrEmpty(vs) || vs.Contains("inf") || vs == "nan")
          { rms.Add(0f); continue; }
        if (float.TryParse(vs, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var db))
          rms.Add(db < -80f ? 0f : (float)Math.Min(1.0, Math.Pow(10.0, db / 20.0)));
      }
      try { File.Delete(statsPath); } catch { }
      if (rms.Count == 0) return Enumerable.Repeat(1f, totalFrames).ToArray();
      float peak = rms.Max();
      if (peak > 0.001f) for (int i = 0; i < rms.Count; i++) rms[i] /= peak;
      var result = new float[totalFrames];
      for (int f = 0; f < totalFrames; f++)
      {
        if (rms.Count == 1) { result[f] = rms[0]; continue; }
        double t  = (double)f / (totalFrames - 1) * (rms.Count - 1);
        int    lo = (int)Math.Min(t, rms.Count - 2);
        result[f] = rms[lo] + (float)((t - lo) * (rms[lo+1] - rms[lo]));
      }
      return result;
    }
    private static async Task RunFfmpegAsync(
      string            args,
      string?           ffmpegBinDir,
      string?           workingDirectory,
      CancellationToken ct)
    {
      var exe = string.IsNullOrWhiteSpace(ffmpegBinDir)
        ? "ffmpeg"
        : Path.Combine(ffmpegBinDir, "ffmpeg.exe");
      var psi = new ProcessStartInfo
      {
        FileName               = exe,
        Arguments              = args,
        WorkingDirectory       = workingDirectory ?? "",
        UseShellExecute        = false,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        CreateNoWindow         = true
      };
      using var proc = new Process { StartInfo = psi };
      var stderr = new StringBuilder();
      proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
      proc.OutputDataReceived += (_, _) => { };
      proc.Start();
      proc.BeginErrorReadLine();
      proc.BeginOutputReadLine();
      await proc.WaitForExitAsync(ct);
      var stderrText = stderr.ToString();
      var tail = stderrText.Length > 2000 ? stderrText.Substring(stderrText.Length - 2000) : stderrText;
      Logger.Info($"[FFmpeg exit={proc.ExitCode}] args: {args}\n[FFmpeg stderr tail]\n{tail}");
      if (proc.ExitCode != 0)
        throw new InvalidOperationException(
          $"ffmpeg a echoue (exit {proc.ExitCode}):\n{stderrText}");
    }
    private static async Task RunFfmpegWithNvencFallbackAsync(
      string            args,
      string?           ffmpegBinDir,
      string?           workingDirectory,
      RenderOptions     options,
      CancellationToken ct)
    {
      try
      {
        await RunFfmpegAsync(args, ffmpegBinDir, workingDirectory, ct);
      }
      catch (InvalidOperationException ex)
        when (args.Contains("h264_nvenc") && ex.Message.Contains("ffmpeg a echoue"))
      {
        _nvencAvailable = false;
        var fallbackArgs = args
          .Replace("-c:v h264_nvenc", "-c:v libx264")
          .Replace("-preset p4", "")
          .Replace("-preset p1", "")
          .Replace("-b:v 0", "");
        fallbackArgs = Regex.Replace(
          fallbackArgs,
          @"-cq\s+(\d+)",
          m => $"-crf {Math.Max(0, int.Parse(m.Groups[1].Value) - 2)}");
        await RunFfmpegAsync(fallbackArgs, ffmpegBinDir, workingDirectory, ct);
      }
    }
    private static async Task ConcatSegmentsAsync(
      List<RenderSegment> segments,
      RenderOptions       options,
      CancellationToken   ct)
    {
      var validPaths = segments
        .Where(s => !string.IsNullOrWhiteSpace(s.OutputPath) && File.Exists(s.OutputPath))
        .Select(s => s.OutputPath!)
        .ToList();
      if (validPaths.Count == 0)
        throw new InvalidOperationException("Aucun segment valide a concatener.");
      if (validPaths.Count == 1)
      {
        var outDir2 = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(outDir2)) Directory.CreateDirectory(outDir2);
        try
        {
          if (File.Exists(options.OutputPath)) File.Delete(options.OutputPath);
          File.Move(validPaths[0], options.OutputPath);
        }
        catch
        {
          File.Copy(validPaths[0], options.OutputPath, overwrite: true);
        }
        return;
      }
      var listPath = Path.Combine(options.TempDirectory, "concat_list.txt");
      if (File.Exists(listPath)) File.Delete(listPath);
      using (var sw = new System.IO.StreamWriter(listPath, false, System.Text.Encoding.ASCII))
      {
        foreach (var p in validPaths)
          sw.WriteLine("file " + p.Replace("\\", "/"));
      }
      var outDir = Path.GetDirectoryName(options.OutputPath);
      if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);
      var concatArgs = new StringBuilder();
      concatArgs.Append("-y ");
      concatArgs.Append("-f concat -safe 0 ");
      concatArgs.Append("-i \"" + listPath + "\" ");
      concatArgs.Append("-c copy ");
      concatArgs.Append("-movflags +faststart ");
      concatArgs.Append("\"" + options.OutputPath + "\"");
      await RunFfmpegAsync(concatArgs.ToString(), options.FfmpegBinaryPath, null, ct);
    }
    private static void ConfigureFfmpeg(RenderOptions options)
    {
      if (string.IsNullOrWhiteSpace(options.FfmpegBinaryPath))
      {
        var knownPath = @"C:\ffmpeg\bin\ffmpeg.exe";
        if (File.Exists(knownPath))
          options.FfmpegBinaryPath = @"C:\ffmpeg\bin";
      }
      GlobalFFOptions.Configure(new FFOptions
      {
        BinaryFolder         = options.FfmpegBinaryPath ?? "",
        TemporaryFilesFolder = options.TempDirectory
      });
    }
    private static string VideoEncoderArgs(RenderOptions options)
    {
      if (options.UseNvenc && _nvencAvailable == true)
      {
        int cq = Math.Clamp(options.Crf + 2, 0, 51);
        string preset = options.IsPreview ? "p1" : "p4";
        return $"-c:v h264_nvenc -preset {preset} -cq {cq} -b:v 0 -pix_fmt yuv420p ";
      }
      string x264Preset = options.IsPreview ? "-preset ultrafast " : "";
      return $"-c:v libx264 {x264Preset}-crf {options.Crf} -pix_fmt yuv420p ";
    }
    private static async Task<bool> ProbeNvencAsync(RenderOptions options)
    {
      return await Task.Run(() =>
      {
        try
        {
          var exe = string.IsNullOrWhiteSpace(options.FfmpegBinaryPath)
            ? "ffmpeg"
            : Path.Combine(options.FfmpegBinaryPath, "ffmpeg.exe");
          if (exe != "ffmpeg" && !File.Exists(exe))
          {
            if (File.Exists(@"C:\ffmpeg\bin\ffmpeg.exe"))
              exe = @"C:\ffmpeg\bin\ffmpeg.exe";
            else
              exe = "ffmpeg";
          }
          var psi = new ProcessStartInfo
          {
            FileName               = exe,
            Arguments              = "-encoders",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
          };
          string encoderList;
          using (var proc = Process.Start(psi))
          {
            if (proc == null) return false;
            encoderList = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
          }
          if (!encoderList.Contains("h264_nvenc"))
            return false;
          var smiPsi = new ProcessStartInfo
          {
            FileName               = "nvidia-smi",
            Arguments              = "--query-gpu=driver_version --format=csv,noheader",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
          };
          string driverStr;
          using (var smi = Process.Start(smiPsi))
          {
            if (smi == null) return false;
            driverStr = smi.StandardOutput.ReadToEnd().Trim();
            smi.StandardError.ReadToEnd();
            smi.WaitForExit(3000);
          }
          var dotIdx = driverStr.IndexOf('.');
          var majorStr = dotIdx > 0 ? driverStr[..dotIdx] : driverStr;
          if (!int.TryParse(majorStr, out var driverMajor) || driverMajor < 570)
            return false;
          var testPsi = new ProcessStartInfo
          {
            FileName               = exe,
            Arguments              = "-y -f lavfi -i color=black:size=256x256:rate=1 -frames:v 1 -c:v h264_nvenc -f null -",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
          };
          using (var testProc = Process.Start(testPsi))
          {
            if (testProc == null) return false;
            testProc.StandardOutput.ReadToEnd();
            testProc.StandardError.ReadToEnd();
            testProc.WaitForExit(10000);
            if (testProc.ExitCode != 0)
              return false;
          }
          return true;
        }
        catch
        {
          return false;
        }
      });
    }
    private static string GenerateBlackFramePath(string tempDir, int w, int h)
    {
      var path = Path.Combine(tempDir, $"black_{w}x{h}.png");
      if (File.Exists(path)) return path;
      try
      {
        var psi = new ProcessStartInfo
        {
          FileName  = "ffmpeg",
          Arguments = $"-y -f lavfi -i color=black:size={w}x{h}:rate=1 -vframes 1 \"{path}\"",
          UseShellExecute        = false,
          RedirectStandardOutput = true,
          RedirectStandardError  = true,
          CreateNoWindow         = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
      }
      catch
      {
        if (!File.Exists(path))
          File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAADklEQVQI12NgYGD4DwABBAEAwTVGMgAAAABJRU5ErkJggg=="));
      }
      return path;
    }
    private static string? ResolveSlidePath(string slidesDir, string slidePath)
    {
      if (string.IsNullOrWhiteSpace(slidePath)) return null;
      if (Path.IsPathRooted(slidePath)) return File.Exists(slidePath) ? slidePath : null;
      var full = Path.Combine(slidesDir, slidePath);
      return File.Exists(full) ? full : null;
    }
    private static string? GetActiveText(OverlayFrame frame, SpeakerKind who) => who switch
    {
      SpeakerKind.You  => frame.YouText,
      SpeakerKind.Bot1 => frame.Bot1Text,
      SpeakerKind.Bot2 => frame.Bot2Text,
      _                => null
    };
    private static void ValidateManifest(RenderManifest manifest, Project project)
    {
      if (manifest.Segments.Count == 0)
        throw new ArgumentException("Le manifest ne contient aucun segment.");
      if (project.CurrentScene == null)
        throw new ArgumentException("Le projet ne contient aucune scene active.");
    }
    private static void CleanTemp(List<RenderSegment> segments, string tempDir)
    {
      foreach (var seg in segments)
        if (!string.IsNullOrWhiteSpace(seg.OutputPath))
          TryDelete(seg.OutputPath);
      TryDelete(Path.Combine(tempDir, "concat_list.txt"));
    }
    private static void TryDelete(string path)
    {
      try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
    private static void Report(
      IProgress<RenderProgress>? progress,
      RenderPhase phase, int current, int total, string message)
    {
      progress?.Report(new RenderProgress(phase, current, total, message));
    }
    private static RenderResult Fail(string message, Stopwatch sw)
    {
      sw.Stop();
      return new RenderResult(false, null, message, sw.Elapsed, 0);
    }
  }
}