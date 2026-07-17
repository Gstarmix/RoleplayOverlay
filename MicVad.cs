using System;
namespace RoleplayOverlay
{
  public sealed class MicVad : IDisposable
  {
    private VADSettings _cfg;
    public MicVad(VADSettings cfg)
    {
      _cfg = cfg ?? new VADSettings();
    }
    public void UpdateConfig(VADSettings cfg)
    {
      if (cfg == null) return;
      _cfg = cfg;
    }
    public bool IsSpeaking(float linearLevel)
    {
      if (linearLevel <= 0f) return false;
      double db = 20.0 * Math.Log10(Math.Clamp(linearLevel, 1e-6f, 1f));
      return db >= _cfg.ThresholdDb;
    }
    public void Dispose()
    {
    }
  }
}