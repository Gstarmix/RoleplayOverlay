using System.Collections.Generic;
namespace RoleplayOverlay
{
  public sealed class HeadlessRenderer : IOverlayRenderer
  {
    private SpeakerKind _activeSpeaker = SpeakerKind.Bot1;
    private string?     _youText;
    private string?     _bot1Text;
    private string?     _bot2Text;
    private bool        _youVisible  = true;
    private bool        _bot1Visible = true;
    private bool        _bot2Visible = true;
    public List<OverlayFrame> FrameHistory { get; } = new();
    public void SetActiveSpeaker(SpeakerKind who)
      => _activeSpeaker = who;
    public void ShowSpeakerText(SpeakerKind who, string text)
    {
      switch (who)
      {
        case SpeakerKind.You:  _youText  = text; break;
        case SpeakerKind.Bot1: _bot1Text = text; break;
        case SpeakerKind.Bot2: _bot2Text = text; break;
      }
    }
    public void HideAllTexts()
    {
      _youText  = null;
      _bot1Text = null;
      _bot2Text = null;
    }
    public void ResetAllLevels()
    {
    }
    public void SetLevel(SpeakerKind who, float level)
    {
    }
    public OverlayFrame CaptureFrame()
    {
      var frame = new OverlayFrame(
        ActiveSpeaker: _activeSpeaker,
        YouText:       _youText,
        Bot1Text:      _bot1Text,
        Bot2Text:      _bot2Text,
        YouVisible:    _youVisible,
        Bot1Visible:   _bot1Visible,
        Bot2Visible:   _bot2Visible
      );
      FrameHistory.Add(frame);
      return frame;
    }
    public void SetYouVisibility(bool visible)  => _youVisible  = visible;
    public void SetBot1Visibility(bool visible) => _bot1Visible = visible;
    public void SetBot2Visibility(bool visible) => _bot2Visible = visible;
    public void ApplyVisibilityFrom(GlobalSettings g)
    {
      _youVisible  = g.ShowYou;
      _bot1Visible = g.ShowBot1;
      _bot2Visible = g.ShowBot2;
    }
  }
}