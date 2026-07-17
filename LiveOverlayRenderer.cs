using System;
namespace RoleplayOverlay
{
  public sealed class LiveOverlayRenderer : IOverlayRenderer
  {
    private readonly OverlayWindow _overlay;
    public LiveOverlayRenderer(OverlayWindow overlay)
    {
      _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }
    public void SetActiveSpeaker(SpeakerKind who)
      => Dispatch(() => _overlay.SetActiveSpeaker(who));
    public void ShowSpeakerText(SpeakerKind who, string text)
      => Dispatch(() => _overlay.ShowSpeakerText(who, text));
    public void HideAllTexts()
      => Dispatch(() => _overlay.HideAllTexts());
    public void ResetAllLevels()
    {
      Dispatch(() =>
      {
        _overlay.SetYouLevel(0f);
        _overlay.SetBot1Level(0f);
        _overlay.SetBot2Level(0f);
      });
    }
    public void SetLevel(SpeakerKind who, float level)
    {
      Dispatch(() =>
      {
        switch (who)
        {
          case SpeakerKind.You:  _overlay.SetYouLevel(level);  break;
          case SpeakerKind.Bot1: _overlay.SetBot1Level(level); break;
          case SpeakerKind.Bot2: _overlay.SetBot2Level(level); break;
        }
      });
    }
    public OverlayFrame CaptureFrame()
      => new OverlayFrame(
           ActiveSpeaker: SpeakerKind.Bot1,
           YouText:       null,
           Bot1Text:      null,
           Bot2Text:      null,
           YouVisible:    true,
           Bot1Visible:   true,
           Bot2Visible:   true);
    private void Dispatch(Action action)
    {
      var dispatcher = _overlay.Dispatcher;
      if (dispatcher.CheckAccess())
        action();
      else
        dispatcher.BeginInvoke(action);
    }
  }
}