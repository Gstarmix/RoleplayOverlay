using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace RoleplayOverlay
{
  public sealed class GlobalHotkeys : IDisposable
  {
    private readonly OverlayWindow _overlay;
    private readonly SequencePlayer _player;
    private HwndSource? _source;
    private IntPtr _hwnd = IntPtr.Zero;
    private int _nextId = 1;
    private readonly List<int> _ids = new();
    private readonly Dictionary<int, Action> _actions = new();
    private readonly Dictionary<int, long> _lastTriggerTicks = new();
    private static readonly long DebounceTicks = TimeSpan.FromMilliseconds(120).Ticks;
    public GlobalHotkeys(OverlayWindow overlay, SequencePlayer player)
    {
      _overlay = overlay;
      _player  = player;
    }
    public void RegisterFromProject(Project p)
    {
      EnsureHwnd();
      TryRegister(p.Global.NextHotkey, () => _player.PlayNext());
      TryRegister(p.Global.PrevHotkey, () => _player.PlayPrev());
      TryRegister(p.Global.StopHotkey, () => _player.Stop());
      TryRegister(p.Global.ToggleHotkey, () => _overlay.ToggleVisibility());
      TryRegister(p.Global.LayoutModeHotkey, () => _overlay.ToggleLayoutEditMode());
      if (!string.IsNullOrWhiteSpace(p.Global.NextHotkeyYou))
        TryRegister(p.Global.NextHotkeyYou!, () => _player.PlayNextFor(SpeakerKind.You));
      if (!string.IsNullOrWhiteSpace(p.Global.PrevHotkeyYou))
        TryRegister(p.Global.PrevHotkeyYou!, () => _player.PlayPrevFor(SpeakerKind.You));
      if (!string.IsNullOrWhiteSpace(p.Global.NextHotkeyBot1))
        TryRegister(p.Global.NextHotkeyBot1!, () => _player.PlayNextFor(SpeakerKind.Bot1));
      if (!string.IsNullOrWhiteSpace(p.Global.PrevHotkeyBot1))
        TryRegister(p.Global.PrevHotkeyBot1!, () => _player.PlayPrevFor(SpeakerKind.Bot1));
      if (!string.IsNullOrWhiteSpace(p.Global.NextHotkeyBot2))
        TryRegister(p.Global.NextHotkeyBot2!, () => _player.PlayNextFor(SpeakerKind.Bot2));
      if (!string.IsNullOrWhiteSpace(p.Global.PrevHotkeyBot2))
        TryRegister(p.Global.PrevHotkeyBot2!, () => _player.PlayPrevFor(SpeakerKind.Bot2));
    }
    private void EnsureHwnd()
    {
      if (_source != null) return;
      var helper = new WindowInteropHelper(_overlay);
      _hwnd = helper.Handle;
      _source = HwndSource.FromHwnd(_hwnd);
      _source.AddHook(WndProc);
    }
    private void TryRegister(string? hotkey, Action action)
    {
      if (string.IsNullOrWhiteSpace(hotkey)) return;
      if (!ParseHotkey(hotkey, out uint mods, out uint vk)) return;
      mods |= MOD_NOREPEAT;
      int id = _nextId++;
      if (RegisterHotKey(_hwnd, id, mods, vk))
      {
        _ids.Add(id);
        _actions[id] = action;
      }
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
      if (msg == WM_HOTKEY)
      {
        int id = wParam.ToInt32();
        long now = DateTime.UtcNow.Ticks;
        if (_lastTriggerTicks.TryGetValue(id, out var last) && now - last < DebounceTicks)
        {
          handled = true;
          return IntPtr.Zero;
        }
        _lastTriggerTicks[id] = now;
        if (_actions.TryGetValue(id, out var act))
        {
          try { act(); } catch { }
          handled = true;
        }
      }
      return IntPtr.Zero;
    }
    public void Dispose()
    {
      foreach (var id in _ids)
      {
        try { UnregisterHotKey(_hwnd, id); } catch { }
      }
      _ids.Clear();
      _actions.Clear();
      _lastTriggerTicks.Clear();
      if (_source != null)
      {
        try { _source.RemoveHook(WndProc); } catch { }
        _source = null;
      }
    }
    private static bool ParseHotkey(string s, out uint modifiers, out uint vk)
    {
      modifiers = 0; vk = 0;
      var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length == 0) return false;
      for (int i = 0; i < parts.Length - 1; i++)
      {
        switch (parts[i].ToLowerInvariant())
        {
          case "ctrl":
          case "control": modifiers |= MOD_CONTROL; break;
          case "alt":     modifiers |= MOD_ALT;     break;
          case "shift":
          case "maj":     modifiers |= MOD_SHIFT;   break;
          case "win":     modifiers |= MOD_WIN;     break;
          case "altgr":   modifiers |= (MOD_CONTROL | MOD_ALT); break;
        }
      }
      string key = parts[^1];
      if (Enum.TryParse<VirtualKey>(key, true, out var v))
      {
        vk = (uint)v; return true;
      }
      if (key.Length == 1)
      {
        char c = char.ToUpperInvariant(key[0]);
        if (c >= 'A' && c <= 'Z') { vk = (uint)c; return true; }
        if (c >= '0' && c <= '9') { vk = (uint)c; return true; }
      }
      return false;
    }
    private const int WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private enum VirtualKey : uint
    {
      Space = 0x20, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28
    }
  }
}