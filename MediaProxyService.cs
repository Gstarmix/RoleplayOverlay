using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace RoleplayOverlay
{
  public sealed class MediaProxyService : IDisposable
  {
    private readonly ConcurrentDictionary<string, string> _thumbCache = new();
    private readonly ConcurrentDictionary<string, TimeSpan> _durationCache = new();
    private CancellationTokenSource? _pendingCts;
    private readonly object          _debounceLock = new();
    private readonly string _tempDir;
    private readonly int    _debounceMs;
    private          bool   _disposed;
    public event Action<string>? SnapshotReady;
    public event Action<string, TimeSpan>? DurationResolved;
    public MediaProxyService(int debounceMs = 200)
    {
      _debounceMs = Math.Max(50, debounceMs);
      _tempDir    = Path.Combine(Path.GetTempPath(), "ro_proxy");
      Directory.CreateDirectory(_tempDir);
    }
    public async Task<TimeSpan> ProbeDurationAsync(string mediaPath, string? ffmpegBinDir = null)
    {
      if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
        return TimeSpan.Zero;
      if (_durationCache.TryGetValue(mediaPath, out var cached))
        return cached;
      var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
      if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
      {
        _durationCache[mediaPath] = TimeSpan.Zero;
        return TimeSpan.Zero;
      }
      var duration = await Task.Run(() => ProbeWithFfprobe(mediaPath, ffmpegBinDir));
      _durationCache[mediaPath] = duration;
      DurationResolved?.Invoke(mediaPath, duration);
      return duration;
    }
    private static TimeSpan ProbeWithFfprobe(string mediaPath, string? ffmpegBinDir)
    {
      try
      {
        var probe = string.IsNullOrWhiteSpace(ffmpegBinDir)
          ? "ffprobe"
          : Path.Combine(ffmpegBinDir, "ffprobe.exe");
        var psi = new ProcessStartInfo
        {
          FileName  = probe,
          Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"",
          UseShellExecute        = false,
          CreateNoWindow         = true,
          RedirectStandardOutput = true,
          RedirectStandardError  = true,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return TimeSpan.Zero;
        var output = proc.StandardOutput.ReadToEnd().Trim();
        var _      = proc.StandardError.ReadToEnd();
        proc.WaitForExit(10000);
        if (double.TryParse(output, System.Globalization.NumberStyles.Float,
              System.Globalization.CultureInfo.InvariantCulture, out var seconds))
          return TimeSpan.FromSeconds(seconds);
      }
      catch { }
      return TimeSpan.Zero;
    }
    public void RequestSnapshot(string mediaPath, double timestampSec, string? ffmpegBinDir = null)
    {
      if (_disposed) return;
      if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath)) return;
      var quantized = Math.Round(timestampSec, 1);
      var cacheKey  = CacheKey(mediaPath, quantized);
      if (_thumbCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
      {
        SnapshotReady?.Invoke(cachedPath);
        return;
      }
      lock (_debounceLock)
      {
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
        _pendingCts = new CancellationTokenSource();
      }
      var cts = _pendingCts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(_debounceMs, cts.Token);
          var pngPath = ExtractFrame(mediaPath, quantized, ffmpegBinDir);
          if (pngPath != null && !cts.Token.IsCancellationRequested)
          {
            _thumbCache[cacheKey] = pngPath;
            SnapshotReady?.Invoke(pngPath);
          }
        }
        catch (OperationCanceledException) { }
        catch { }
      });
    }
    private string? ExtractFrame(string mediaPath, double timestampSec, string? ffmpegBinDir)
    {
      try
      {
        var exe = string.IsNullOrWhiteSpace(ffmpegBinDir)
          ? "ffmpeg"
          : Path.Combine(ffmpegBinDir, "ffmpeg.exe");
        var outPng = Path.Combine(_tempDir, $"snap_{Path.GetFileNameWithoutExtension(mediaPath)}_{timestampSec:F1}.png");
        if (File.Exists(outPng)) return outPng;
        var ts = TimeSpan.FromSeconds(timestampSec).ToString(@"hh\:mm\:ss\.ff");
        var psi = new ProcessStartInfo
        {
          FileName  = exe,
          Arguments = $"-y -ss {ts} -i \"{mediaPath}\" -vframes 1 -q:v 2 -vf scale=320:-1 \"{outPng}\"",
          UseShellExecute        = false,
          CreateNoWindow         = true,
          RedirectStandardError  = true,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return null;
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(5000))
        {
          try { proc.Kill(); } catch { }
          return null;
        }
        stderrTask.Wait(1000);
        return File.Exists(outPng) ? outPng : null;
      }
      catch { return null; }
    }
    public void InvalidateCache(string mediaPath)
    {
      var prefix = mediaPath + "|";
      foreach (var key in _thumbCache.Keys)
      {
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
          if (_thumbCache.TryRemove(key, out var path))
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
      }
      _durationCache.TryRemove(mediaPath, out _);
    }
    public void ClearCache()
    {
      foreach (var kvp in _thumbCache)
        try { if (File.Exists(kvp.Value)) File.Delete(kvp.Value); } catch { }
      _thumbCache.Clear();
      _durationCache.Clear();
    }
    public int CacheCount => _thumbCache.Count;
    private static string CacheKey(string path, double timestamp)
      => $"{path}|{timestamp:F1}";
    public static bool IsScrubable(string? mediaPath)
    {
      if (string.IsNullOrWhiteSpace(mediaPath)) return false;
      var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
      return ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv" or ".gif";
    }
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      lock (_debounceLock)
      {
        _pendingCts?.Cancel();
        _pendingCts?.Dispose();
      }
    }
  }
}