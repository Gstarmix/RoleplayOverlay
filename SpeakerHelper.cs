using System;
namespace RoleplayOverlay
{
  public static class SpeakerHelper
  {
    public static SpeakerKind Parse(string? speaker)
    {
      if (string.IsNullOrWhiteSpace(speaker)) return SpeakerKind.Bot1;
      return speaker.Trim().ToLowerInvariant() switch
      {
        "you"  => SpeakerKind.You,
        "bot2" => SpeakerKind.Bot2,
        _      => SpeakerKind.Bot1
      };
    }
    public static string ResolveVoice(Sequence seq, GlobalSettings global)
    {
      return string.IsNullOrWhiteSpace(seq.Voice) ? global.Voice : seq.Voice!;
    }
  }
}