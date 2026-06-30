using System;
using System.Collections.Generic;
using System.Linq;
namespace RoleplayOverlay
{
  public sealed class SequencePlayer
  {
    private readonly IAudioEngine     _audio;
    private readonly IOverlayRenderer _renderer;
    private          Project          _project;
    private int _index = -1;
    private readonly Dictionary<SpeakerKind, int> _lastIdxBySpeaker = new()
    {
      { SpeakerKind.You,  -1 },
      { SpeakerKind.Bot1, -1 },
      { SpeakerKind.Bot2, -1 }
    };
    public SequencePlayer(IAudioEngine audio, IOverlayRenderer renderer)
    {
      _audio    = audio    ?? throw new ArgumentNullException(nameof(audio));
      _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
      _project  = Project.CreateDefault();
    }
    public SequencePlayer(Audio audio, OverlayWindow overlay)
      : this(new LiveAudioEngine(audio), new LiveOverlayRenderer(overlay))
    { }
    public void Preload(Project project)
    {
      _project = project ?? Project.CreateDefault();
      _index   = -1;
      _lastIdxBySpeaker[SpeakerKind.You]  = -1;
      _lastIdxBySpeaker[SpeakerKind.Bot1] = -1;
      _lastIdxBySpeaker[SpeakerKind.Bot2] = -1;
    }
    public void PlayAt(int i, bool showBubbleText = true)
      => PlayAtCore(i, affectGlobal: true, showBubbleText: showBubbleText);
    public void PlayNext()                => Navigate(+1, affectGlobal: true);
    public void PlayPrev()                => Navigate(-1, affectGlobal: true);
    public void PlayNextFor(SpeakerKind who) => NavigateFor(who, forward: true);
    public void PlayPrevFor(SpeakerKind who) => NavigateFor(who, forward: false);
    public void Stop()
    {
      _audio.StopAll();
      _renderer.HideAllTexts();
      _renderer.ResetAllLevels();
    }
    public int CurrentIndex => _index;
    private void Navigate(int delta, bool affectGlobal)
    {
      var list = CurrentList();
      if (list.Count == 0) return;
      int next = _index < 0
        ? (delta > 0 ? 0 : 0)
        : Math.Clamp(_index + delta, 0, list.Count - 1);
      PlayAtCore(next, affectGlobal);
    }
    private void NavigateFor(SpeakerKind who, bool forward)
    {
      var list = CurrentList();
      if (list.Count == 0) return;
      int start = _lastIdxBySpeaker.TryGetValue(who, out var si) ? si : -1;
      int found = FindNextIndexFor(start, who, forward);
      if (found >= 0) PlayAtCore(found, affectGlobal: false);
    }
    private void PlayAtCore(int i, bool affectGlobal, bool showBubbleText = true)
    {
      var list = CurrentList();
      if (list.Count == 0) return;
      i = Math.Clamp(i, 0, list.Count - 1);
      if (affectGlobal) _index = i;
      var s   = list[i];
      var who = SpeakerHelper.Parse(s.Speaker);
      _audio.StopAll();
      try { _audio.SetLevelSink(who); } catch { }
      _renderer.HideAllTexts();
      _renderer.SetActiveSpeaker(who);
      _lastIdxBySpeaker[who] = i;
      TryHighlightEditor(i, who);
      if (showBubbleText && s.ShowText && !string.IsNullOrWhiteSpace(s.Text))
        _renderer.ShowSpeakerText(who, s.Text!);
      var voice = SpeakerHelper.ResolveVoice(s, _project.Global);
      if (string.Equals(s.Mode, "mp3", StringComparison.OrdinalIgnoreCase)
          && !string.IsNullOrWhiteSpace(s.Mp3))
      {
        _audio.PlayMp3(s.Mp3!);
      }
      else
      {
        _audio.Speak(TtsHelper.SanitizeForTts(s.Text ?? string.Empty, voice), voice);
      }
    }
    private List<Sequence> CurrentList()
      => _project.CurrentScene?.Sequences ?? new List<Sequence>();
    private int FindNextIndexFor(int startIndex, SpeakerKind who, bool forward)
    {
      var list = CurrentList();
      if (list.Count == 0) return -1;
      int idx = startIndex < 0 ? (forward ? -1 : 0) : startIndex;
      for (int step = 0; step < list.Count; step++)
      {
        idx = forward
          ? (idx + 1 + list.Count) % list.Count
          : (idx - 1 + list.Count) % list.Count;
        if (SpeakerHelper.Parse(list[idx].Speaker) == who) return idx;
      }
      return -1;
    }
    private static void TryHighlightEditor(int index, SpeakerKind who)
    {
      try
      {
        var editor = System.Windows.Application.Current?.Windows
          ?.OfType<EditorWindow>()
          .FirstOrDefault();
        editor?.HighlightRow(index, who);
      }
      catch { }
    }
  }
}