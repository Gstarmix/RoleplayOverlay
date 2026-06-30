namespace RoleplayOverlay
{
  public interface IOverlayRenderer
  {
    void SetActiveSpeaker(SpeakerKind who);
    void ShowSpeakerText(SpeakerKind who, string text);
    void HideAllTexts();
    void ResetAllLevels();
    void SetLevel(SpeakerKind who, float level);
    OverlayFrame CaptureFrame();
  }
  public sealed record OverlayFrame(
    SpeakerKind  ActiveSpeaker,
    string?      YouText,
    string?      Bot1Text,
    string?      Bot2Text,
    bool         YouVisible,
    bool         Bot1Visible,
    bool         Bot2Visible
  );
}