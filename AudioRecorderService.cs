
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace RoleplayOverlay
{
  public enum RecorderState { Idle, Recording, Converting, Playing }
  public sealed record RecordingResult(bool Success, string? Mp3Path, string? Error);
  public sealed class AudioRecorderService : IDisposable
  {
    private RecorderState _state = RecorderState.Idle;
    private readonly object _stateLock = new();
    private WasapiCapture? _capture;
    private WaveInEvent?   _waveIn;
    private WaveFileWriter? _writer;
    private string?        _currentWavPath;
    private WaveOutEvent?    _playOutput;
    private AudioFileReader? _playReader;
    private readonly object  _playLock = new();
    private readonly string _ffmpegPath;
    public event Action<RecorderState>? StateChanged;
    public event Action<float>? LevelChanged;
    public AudioRecorderService(string? ffmpegPath = null)
    {
      _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath)
        ? @"C:\ffmpeg\bin\ffmpeg.exe"
        : ffmpegPath;
    }
    public RecorderState State
    {
      get { lock (_stateLock) return _state; }
    }
    public bool StartRecording(string? preferredDeviceName = null)
    {
      lock (_stateLock)
      {
        if (_state == RecorderState.Recording)
          StopCaptureLocked();
        StopPlaybackLocked();
        var tempDir = Path.Combine(Path.GetTempPath(), "ro_recorder");
        Directory.CreateDirectory(tempDir);
        var guidPart = Guid.NewGuid().ToString("N").Substring(0, 6);
        _currentWavPath = Path.Combine(tempDir, "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + guidPart + ".wav");
        if (TryStartWasapi(preferredDeviceName) || TryStartWaveIn())
        {
          SetState(RecorderState.Recording);
          Logger.Info($"[Recorder] Started → {_currentWavPath}");
          return true;
        }
        _currentWavPath = null;
        Logger.Warn("[Recorder] Failed to open capture device");
        return false;
      }
    }
    public async Task<RecordingResult> StopAndConvertAsync(string mp3DestPath)
    {
      string? wavPath;
      lock (_stateLock)
      {
        if (_state != RecorderState.Recording)
          return new RecordingResult(false, null, "Pas d'enregistrement en cours");
        StopCaptureLocked();
        wavPath = _currentWavPath;
        _currentWavPath = null;
        SetState(RecorderState.Converting);
      }
      if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
      {
        SetState(RecorderState.Idle);
        return new RecordingResult(false, null, "Fichier WAV temporaire introuvable");
      }
      try
      {
        Logger.Info($"[Recorder] Converting {wavPath} → {mp3DestPath}");
        var err = await ConvertWavToMp3Async(wavPath, mp3DestPath);
        if (err != null)
        {
          Logger.Warn($"[Recorder] Conversion failed: {err}");
          SetState(RecorderState.Idle);
          return new RecordingResult(false, null, err);
        }
        Logger.Info($"[Recorder] Conversion OK → {mp3DestPath}");
        SetState(RecorderState.Idle);
        return new RecordingResult(true, mp3DestPath, null);
      }
      catch (Exception ex)
      {
        Logger.Error("[Recorder] StopAndConvertAsync", ex);
        SetState(RecorderState.Idle);
        return new RecordingResult(false, null, ex.Message);
      }
      finally
      {
        try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
      }
    }
    public void CancelRecording()
    {
      lock (_stateLock)
      {
        if (_state != RecorderState.Recording) return;
        StopCaptureLocked();
        var wav = _currentWavPath;
        _currentWavPath = null;
        SetState(RecorderState.Idle);
        if (wav != null) try { File.Delete(wav); } catch { }
      }
    }
    public void PlayPreview(string filePath)
    {
      if (!File.Exists(filePath))
      {
        Logger.Warn($"[Recorder] PlayPreview: file not found {filePath}");
        return;
      }
      lock (_playLock)
      {
        StopPlaybackLocked();
        try
        {
          _playReader = new AudioFileReader(filePath);
          int notifySamples = Math.Max(1, _playReader.WaveFormat.SampleRate / 20);
          var metering = new MeteringSampleProvider(_playReader, notifySamples);
          _meteringTickCounter = 0;
          metering.StreamVolume += (_, ev) =>
          {
            float maxPeak = 0f;
            for (int i = 0; i < ev.MaxSampleValues.Length; i++)
              if (ev.MaxSampleValues[i] > maxPeak) maxPeak = ev.MaxSampleValues[i];
            float visual = Math.Min(1.0f, maxPeak * 6.0f);
            _meteringTickCounter++;
            if (_meteringTickCounter <= 3 || _meteringTickCounter % 50 == 0)
              Logger.Info($"[Recorder] Metering tick #{_meteringTickCounter} peak={maxPeak:F4} visual={visual:F4} channels={ev.MaxSampleValues.Length}");
            LevelChanged?.Invoke(visual);
          };
          Logger.Info($"[Recorder] Metering wired: notifySamples={notifySamples} fmt={_playReader.WaveFormat.Encoding} {_playReader.WaveFormat.SampleRate}Hz ch={_playReader.WaveFormat.Channels}");
          _playOutput = new WaveOutEvent { DesiredLatency = 120 };
          _playOutput.PlaybackStopped += (_, _) =>
          {
            lock (_playLock) { StopPlaybackLocked(); }
            lock (_stateLock)
            {
              if (_state == RecorderState.Playing)
                SetState(RecorderState.Idle);
            }
          };
          _playOutput.Init(metering.ToWaveProvider());
          _playOutput.Play();
          SetState(RecorderState.Playing);
          Logger.Info($"[Recorder] PlayPreview → {filePath}");
        }
        catch (Exception ex)
        {
          Logger.Error("[Recorder] PlayPreview", ex);
          StopPlaybackLocked();
        }
      }
    }
    public void StopPlayback()
    {
      lock (_playLock)
      {
        StopPlaybackLocked();
      }
      lock (_stateLock)
      {
        if (_state == RecorderState.Playing)
          SetState(RecorderState.Idle);
      }
    }
    private int _wasapiTickCounter;
    private int _meteringTickCounter;
    private bool TryStartWasapi(string? preferredDeviceName)
    {
      try
      {
        var en = new MMDeviceEnumerator();
        MMDevice? chosen = null;
        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
          foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
          {
            if (d.FriendlyName.IndexOf(preferredDeviceName, StringComparison.OrdinalIgnoreCase) >= 0)
            { chosen = d; break; }
          }
        }
        chosen ??= en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        if (chosen == null) return false;
        var capture = new WasapiCapture(chosen) { ShareMode = AudioClientShareMode.Shared };
        var devFmt = capture.WaveFormat;
        _writer = new WaveFileWriter(_currentWavPath!, devFmt);
        Logger.Info($"[Recorder] WASAPI device='{chosen.FriendlyName}' format={devFmt.Encoding} {devFmt.SampleRate}Hz ch={devFmt.Channels} bits={devFmt.BitsPerSample}");
        _wasapiTickCounter = 0;
        capture.DataAvailable += (_, a) =>
        {
          try
          {
            if (a.BytesRecorded <= 0) return;
            lock (_stateLock)
            {
              if (_writer == null) return;
              _writer.Write(a.Buffer, 0, a.BytesRecorded);
              float level = ComputeLevelAuto(a.Buffer, a.BytesRecorded, devFmt, out float peak);
              float visual = Math.Min(1.0f, level * 6.0f);
              _wasapiTickCounter++;
              if (_wasapiTickCounter <= 3 || _wasapiTickCounter % 50 == 0)
                Logger.Info($"[Recorder] WASAPI tick #{_wasapiTickCounter} bytes={a.BytesRecorded} level={level:F4} peak={peak:F4} visual={visual:F4}");
              LevelChanged?.Invoke(visual);
            }
          }
          catch (Exception ex)
          {
            Logger.Error("[Recorder] DataAvailable WASAPI", ex);
          }
        };
        capture.RecordingStopped += (_, ev) =>
        {
          if (ev.Exception != null)
            Logger.Warn($"[Recorder] WASAPI RecordingStopped exception: {ev.Exception.Message}");
          lock (_stateLock) { FlushWriterLocked(); }
        };
        capture.StartRecording();
        _capture = capture;
        return true;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[Recorder] WASAPI init failed: {ex.Message}");
        _writer?.Dispose(); _writer = null;
        return false;
      }
    }
    private bool TryStartWaveIn()
    {
      try
      {
        var wf = new WaveFormat(44100, 16, 1);
        _writer = new WaveFileWriter(_currentWavPath!, wf);
        var waveIn = new WaveInEvent { WaveFormat = wf };
        waveIn.DataAvailable += (_, a) =>
        {
          if (a.BytesRecorded <= 0) return;
          lock (_stateLock)
          {
            _writer?.Write(a.Buffer, 0, a.BytesRecorded);
            LevelChanged?.Invoke(ComputeRmsInt16(a.Buffer, a.BytesRecorded));
          }
        };
        waveIn.RecordingStopped += (_, _) =>
        {
          lock (_stateLock) { FlushWriterLocked(); }
        };
        waveIn.StartRecording();
        _waveIn = waveIn;
        return true;
      }
      catch (Exception ex)
      {
        Logger.Warn($"[Recorder] WaveIn init failed: {ex.Message}");
        _writer?.Dispose(); _writer = null;
        return false;
      }
    }
    private void StopCaptureLocked()
    {
      if (_capture != null)
      {
        try { _capture.StopRecording(); } catch { }
        try { _capture.Dispose();       } catch { }
        _capture = null;
      }
      if (_waveIn != null)
      {
        try { _waveIn.StopRecording(); } catch { }
        try { _waveIn.Dispose();       } catch { }
        _waveIn = null;
      }
      FlushWriterLocked();
    }
    private void FlushWriterLocked()
    {
      if (_writer == null) return;
      try { _writer.Flush(); _writer.Dispose(); } catch { }
      _writer = null;
    }
    private void StopPlaybackLocked()
    {
      if (_playOutput != null)
      {
        try { _playOutput.Stop();    } catch { }
        try { _playOutput.Dispose(); } catch { }
        _playOutput = null;
      }
      if (_playReader != null)
      {
        try { _playReader.Dispose(); } catch { }
        _playReader = null;
      }
    }
    private async Task<string?> ConvertWavToMp3Async(string wavPath, string mp3Path)
    {
      var dir = Path.GetDirectoryName(mp3Path);
      if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);
      try { if (File.Exists(mp3Path)) File.Delete(mp3Path); } catch { }
      var ffmpeg = File.Exists(_ffmpegPath) ? _ffmpegPath : "ffmpeg";
      var args = $"-y -i \"{wavPath}\" -codec:a libmp3lame -qscale:a 2 \"{mp3Path}\"";
      Logger.Info($"[Recorder] FFmpeg: {ffmpeg} {args}");
      var psi = new ProcessStartInfo
      {
        FileName               = ffmpeg,
        Arguments              = args,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
        CreateNoWindow         = true,
      };
      try
      {
        using var proc = new Process { StartInfo = psi };
        var stderrBuilder = new System.Text.StringBuilder();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };
        proc.OutputDataReceived += (_, _) => { };
        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        await proc.WaitForExitAsync(cts.Token);
        var stderr = stderrBuilder.ToString();
        Logger.FfmpegResult("recorder_convert", ffmpeg, args, proc.ExitCode, stderr, mp3Path);
        if (proc.ExitCode != 0 || !File.Exists(mp3Path))
          return $"FFmpeg exit {proc.ExitCode}: {stderr[..Math.Min(200, stderr.Length)]}";
        return null;
      }
      catch (OperationCanceledException)
      {
        return "Timeout: conversion FFmpeg > 60s";
      }
      catch (Exception ex)
      {
        return ex.Message;
      }
    }
    private static float ComputeRmsAuto(byte[] buffer, int byteCount, WaveFormat fmt)
      => ComputeLevelAuto(buffer, byteCount, fmt, out _);
    private static float ComputeLevelAuto(byte[] buffer, int byteCount, WaveFormat fmt, out float peak)
    {
      peak = 0f;
      if (byteCount <= 0) return 0f;
      bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat;
      int bps = fmt.BitsPerSample / 8;
      if (bps <= 0) return 0f;
      int totalSamples = byteCount / bps;
      if (totalSamples <= 0) return 0f;
      double sum = 0;
      float peakLocal = 0f;
      for (int i = 0; i < totalSamples; i++)
      {
        int offset = i * bps;
        float sample;
        if (isFloat && bps == 4)
          sample = BitConverter.ToSingle(buffer, offset);
        else if (!isFloat && bps == 2)
          sample = BitConverter.ToInt16(buffer, offset) / 32768f;
        else if (!isFloat && bps == 4)
          sample = BitConverter.ToInt32(buffer, offset) / 2147483648f;
        else if (!isFloat && bps == 3)
        {
          int s24 = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
          if ((s24 & 0x800000) != 0) s24 |= unchecked((int)0xFF000000);
          sample = s24 / 8388608f;
        }
        else sample = 0f;
        float abs = sample < 0 ? -sample : sample;
        if (abs > peakLocal) peakLocal = abs;
        sum += sample * sample;
      }
      float rms = (float)Math.Sqrt(sum / totalSamples);
      peak = peakLocal;
      float level = Math.Max(rms * 3.0f, peakLocal);
      return Math.Min(1.0f, level);
    }
    private static float ComputeRmsInt16(byte[] buffer, int byteCount)
    {
      int samples = byteCount / 2;
      if (samples <= 0) return 0f;
      double sum = 0;
      for (int i = 0; i < samples; i++)
      {
        short s = BitConverter.ToInt16(buffer, i * 2);
        float f = s / 32768f;
        sum += f * f;
      }
      return (float)Math.Sqrt(sum / samples);
    }
    private void SetState(RecorderState newState)
    {
      _state = newState;
      try { StateChanged?.Invoke(newState); } catch { }
    }
    public void Dispose()
    {
      lock (_stateLock) { StopCaptureLocked(); }
      lock (_playLock)  { StopPlaybackLocked(); }
    }
  }
}