using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
namespace RoleplayOverlay
{
  public sealed class AudioDeviceWatcher : IDisposable
  {
    private static readonly int[] RetryDelaysMs = { 500, 1000, 2000, 5000, 10_000 };
    private readonly Audio             _audio;
    private readonly IEnumerable<string> _micPreferences;
    private readonly MMDeviceEnumerator  _enumerator;
    private readonly NotificationClient  _client;
    private CancellationTokenSource? _retryCts;
    private readonly object _retryLock = new();
    private bool _disposed;
    public AudioDeviceWatcher(Audio audio, IEnumerable<string> micPreferences)
    {
      _audio          = audio ?? throw new ArgumentNullException(nameof(audio));
      _micPreferences = micPreferences ?? Array.Empty<string>();
      _enumerator = new MMDeviceEnumerator();
      _client     = new NotificationClient(this);
      _enumerator.RegisterEndpointNotificationCallback(_client);
    }
    private void OnAudioEndpointChanged(DataFlow flow, DeviceState state)
    {
      if (flow != DataFlow.Capture && flow != DataFlow.All) return;
      CancellationTokenSource newCts;
      lock (_retryLock)
      {
        if (_disposed) return;
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        newCts    = new CancellationTokenSource();
        _retryCts = newCts;
      }
      _ = RetryLoopAsync(newCts.Token);
    }
    private async Task RetryLoopAsync(CancellationToken ct)
    {
      foreach (var delayMs in RetryDelaysMs)
      {
        try { await Task.Delay(delayMs, ct); }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;
        if (_disposed)                  return;
        bool ok = false;
        try { ok = _audio.TryRestartMic(_micPreferences); }
        catch {  }
        if (ok) return;
      }
    }
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      lock (_retryLock)
      {
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        _retryCts = null;
      }
      try { _enumerator.UnregisterEndpointNotificationCallback(_client); } catch { }
      try { _enumerator.Dispose(); }                                       catch { }
    }
    private sealed class NotificationClient : IMMNotificationClient
    {
      private readonly AudioDeviceWatcher _owner;
      public NotificationClient(AudioDeviceWatcher owner) { _owner = owner; }
      public void OnDeviceRemoved(string deviceId)
        => _owner.OnAudioEndpointChanged(DataFlow.Capture, DeviceState.NotPresent);
      public void OnDeviceAdded(string pwstrDeviceId)
        => _owner.OnAudioEndpointChanged(DataFlow.Capture, DeviceState.Active);
      public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        => _owner.OnAudioEndpointChanged(DataFlow.Capture, newState);
      public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        => _owner.OnAudioEndpointChanged(flow, DeviceState.Active);
      public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
  }
}