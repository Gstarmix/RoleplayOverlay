using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
namespace RoleplayOverlay
{
  public sealed class TtsTimingService : IDisposable
  {
    private readonly ConcurrentDictionary<string, TimeSpan> _cache = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly int _debounceMs;
    private          bool _disposed;
    private static readonly SemaphoreSlim _sapiLock = new(1, 1);
    private CancellationTokenSource _globalCts = new();
    public event Action<string, TimeSpan>? DurationComputed;
    public TtsTimingService(int debounceMs = 800)
    {
      _debounceMs = Math.Max(200, debounceMs);
    }
    public static TimeSpan EstimateFromWordCount(string? text, string? voice)
    {
      if (string.IsNullOrWhiteSpace(text)) return TimeSpan.Zero;
      var clean = TtsHelper.SanitizeForTts(text, voice);
      if (string.IsNullOrWhiteSpace(clean)) return TimeSpan.Zero;
      var words = clean.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
      bool isFrench = voice != null &&
        voice.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
      double wps = isFrench ? 2.33 : 2.5;
      double seconds = (words / wps) + 0.5;
      return TimeSpan.FromSeconds(Math.Max(0.5, seconds));
    }
    public void RequestExactDuration(Sequence seq)
    {
      if (_disposed) return;
      if (seq == null || string.IsNullOrWhiteSpace(seq.Id)) return;
      if (string.IsNullOrWhiteSpace(seq.Text)) return;
      var key = CacheKey(seq.Text, seq.Voice);
      if (_cache.TryGetValue(key, out var cached))
      {
        DurationComputed?.Invoke(seq.Id!, cached);
        return;
      }
      if (_pending.TryRemove(seq.Id!, out var oldCts))
      {
        try { oldCts.Cancel(); oldCts.Dispose(); } catch { }
      }
      var cts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token);
      _pending[seq.Id!] = cts;
      _ = Task.Run(async () =>
      {
        try
        {
          await Task.Delay(_debounceMs, cts.Token);
          await _sapiLock.WaitAsync(cts.Token);
          try
          {
            if (cts.Token.IsCancellationRequested) return;
            var duration = ComputeViaSapi(seq.Text!, seq.Voice);
            if (duration.HasValue && !cts.Token.IsCancellationRequested)
            {
              _cache[key] = duration.Value;
              DurationComputed?.Invoke(seq.Id!, duration.Value);
            }
          }
          finally
          {
            _sapiLock.Release();
          }
        }
        catch (OperationCanceledException) {  }
        catch (Exception ex)
        {
          Logger.Warn($"[TtsTiming] SAPI failed for seq={seq.Id}: {ex.Message}");
        }
        finally
        {
          _pending.TryRemove(seq.Id!, out _);
          cts.Dispose();
        }
      });
    }
    public void ComputeAllAsync(IEnumerable<Sequence> sequences)
    {
      CancelAll();
      foreach (var seq in sequences)
      {
        if (string.IsNullOrWhiteSpace(seq.Text)) continue;
        seq.EstimatedDuration = EstimateFromWordCount(seq.Text, seq.Voice);
        seq.DurationIsExact   = false;
        RequestExactDuration(seq);
      }
      Logger.Info($"[TtsTiming] ComputeAllAsync: {sequences.Count()} sequences queued (serialized SAPI)");
    }
    public void CancelAll()
    {
      try { _globalCts.Cancel(); } catch { }
      try { _globalCts.Dispose(); } catch { }
      _globalCts = new CancellationTokenSource();
      foreach (var kvp in _pending)
      {
        try { kvp.Value.Cancel(); kvp.Value.Dispose(); } catch { }
      }
      _pending.Clear();
    }
    private static TimeSpan? ComputeViaSapi(string text, string? voice)
    {
      try
      {
        var clean = TtsHelper.SanitizeForTts(text, voice);
        if (string.IsNullOrWhiteSpace(clean)) return TimeSpan.Zero;
        using var synth = new SpeechSynthesizer();
        synth.Rate   = 0;
        synth.Volume = 100;
        if (!string.IsNullOrWhiteSpace(voice))
        {
          try
          {
            var ci = new System.Globalization.CultureInfo(voice);
            var v  = synth.GetInstalledVoices()
                          .FirstOrDefault(x => x.Enabled && x.VoiceInfo.Culture.Equals(ci));
            if (v != null) synth.SelectVoice(v.VoiceInfo.Name);
          }
          catch { try { synth.SelectVoice(voice); } catch { } }
        }
        using var ms = new MemoryStream();
        synth.SetOutputToWaveStream(ms);
        synth.Speak(clean);
        synth.SetOutputToNull();
        ms.Position = 0;
        using var reader = new NAudio.Wave.WaveFileReader(ms);
        return reader.TotalTime;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[TtsTiming] ComputeViaSapi exception: {ex.Message}");
        return null;
      }
    }
    public void Invalidate(string? text, string? voice)
    {
      if (string.IsNullOrWhiteSpace(text)) return;
      var key = CacheKey(text, voice);
      _cache.TryRemove(key, out _);
    }
    public void ClearCache() => _cache.Clear();
    private static string CacheKey(string? text, string? voice)
      => $"{voice ?? "default"}|{text?.GetHashCode() ?? 0}";
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      CancelAll();
      try { _globalCts.Dispose(); } catch { }
    }
  }
}