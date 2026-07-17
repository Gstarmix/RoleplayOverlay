using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace RoleplayOverlay
{
  public static class PairingService
  {
    private static readonly Regex SuffixEnRegex =
      new(@"^(.+)_en$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SuffixFrRegex =
      new(@"^(.+)_fr$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static int MigrateFromLegacy(IList<Sequence> sequences)
    {
      if (sequences == null || sequences.Count < 2) return 0;
      int paired = 0;
      var byId = sequences.Where(s => s.Id != null)
                           .ToDictionary(s => s.Id!, StringComparer.OrdinalIgnoreCase);
      foreach (var seq in sequences)
      {
        if (!string.IsNullOrWhiteSpace(seq.PairId)) continue;
        if (string.IsNullOrWhiteSpace(seq.Id)) continue;
        var mEn = SuffixEnRegex.Match(seq.Id);
        if (!mEn.Success) continue;
        var baseName = mEn.Groups[1].Value;
        Sequence? sibling = null;
        var frId = baseName + "_fr";
        if (byId.TryGetValue(frId, out var frSeq) && string.IsNullOrWhiteSpace(frSeq.PairId))
          sibling = frSeq;
        else if (byId.TryGetValue(baseName, out var bareSeq) && string.IsNullOrWhiteSpace(bareSeq.PairId))
          sibling = bareSeq;
        if (sibling == null) continue;
        seq.PairId     = baseName;
        sibling.PairId = baseName;
        paired += 2;
      }
      return paired;
    }
    public static Sequence? FindSibling(Sequence seq, IEnumerable<Sequence> all)
    {
      if (seq == null || string.IsNullOrWhiteSpace(seq.PairId)) return null;
      return all.FirstOrDefault(s =>
        s != seq
        && string.Equals(s.PairId, seq.PairId, StringComparison.OrdinalIgnoreCase));
    }
    private static readonly string[] MediaPropertyNames =
    {
      nameof(Sequence.MediaPath),
      nameof(Sequence.MediaScale),
      nameof(Sequence.MediaSpeed),
      nameof(Sequence.MediaLoop),
      nameof(Sequence.MediaBorderColor),
      nameof(Sequence.MediaBorderPx),
      nameof(Sequence.MediaShadowBlur),
      nameof(Sequence.MediaShadowAlpha),
      nameof(Sequence.MediaTrimIn),
      nameof(Sequence.MediaTrimOut),
      nameof(Sequence.MediaCropLeft),
      nameof(Sequence.MediaCropTop),
      nameof(Sequence.MediaCropRight),
      nameof(Sequence.MediaCropBottom),
      nameof(Sequence.MediaCropGravity),
      nameof(Sequence.MediaAnchor),
      nameof(Sequence.MediaGap),
      nameof(Sequence.MediaItems),
    };
    public static bool IsMediaProperty(string propertyName)
      => MediaPropertyNames.Contains(propertyName);
    public static void PropagateMediaIfNeeded(Sequence source, IEnumerable<Sequence> all)
    {
      if (source == null) return;
      if (string.IsNullOrWhiteSpace(source.PairId)) return;
      if (source.MediaUnlinked) return;
      var sibling = FindSibling(source, all);
      if (sibling == null || sibling.MediaUnlinked) return;
      CopyMediaFields(source, sibling);
    }
    public static void CopyMediaFields(Sequence source, Sequence target)
    {
      target.MediaPath        = source.MediaPath;
      target.MediaScale       = source.MediaScale;
      target.MediaSpeed       = source.MediaSpeed;
      target.MediaLoop        = source.MediaLoop;
      target.MediaBorderColor = source.MediaBorderColor;
      target.MediaBorderPx    = source.MediaBorderPx;
      target.MediaShadowBlur  = source.MediaShadowBlur;
      target.MediaShadowAlpha = source.MediaShadowAlpha;
      target.MediaTrimIn      = source.MediaTrimIn;
      target.MediaTrimOut     = source.MediaTrimOut;
      target.MediaCropLeft    = source.MediaCropLeft;
      target.MediaCropTop     = source.MediaCropTop;
      target.MediaCropRight   = source.MediaCropRight;
      target.MediaCropBottom  = source.MediaCropBottom;
      target.MediaCropGravity = source.MediaCropGravity;
      target.MediaAnchor      = source.MediaAnchor;
      target.MediaGap         = source.MediaGap;
      target.MediaItems       = source.MediaItems?.Select(m => m.Clone()).ToList();
    }
    public static void LinkPair(Sequence a, Sequence b)
    {
      string pairId;
      var mA = SuffixEnRegex.Match(a.Id ?? "");
      var mB = SuffixEnRegex.Match(b.Id ?? "");
      if (mA.Success)
        pairId = mA.Groups[1].Value;
      else if (mB.Success)
        pairId = mB.Groups[1].Value;
      else
        pairId = $"pair_{Guid.NewGuid().ToString("N")[..8]}";
      a.PairId = pairId;
      b.PairId = pairId;
      a.MediaUnlinked = false;
      b.MediaUnlinked = false;
    }
    public static void Unlink(Sequence seq)
    {
      seq.PairId = null;
      seq.MediaUnlinked = false;
    }
    public static void UnlinkBoth(Sequence seq, IEnumerable<Sequence> all)
    {
      var sibling = FindSibling(seq, all);
      seq.PairId = null;
      seq.MediaUnlinked = false;
      if (sibling != null)
      {
        sibling.PairId = null;
        sibling.MediaUnlinked = false;
      }
    }
    public static List<Sequence> SortByPairs(IEnumerable<Sequence> sequences, bool frenchFirst = false)
    {
      var input  = sequences.ToList();
      var result = new List<Sequence>(input.Count);
      var placed = new HashSet<Sequence>();
      foreach (var seq in input)
      {
        if (placed.Contains(seq)) continue;
        if (string.IsNullOrWhiteSpace(seq.PairId))
        {
          result.Add(seq);
          placed.Add(seq);
          continue;
        }
        var pairGroup = input
          .Where(s => !placed.Contains(s)
                      && string.Equals(s.PairId, seq.PairId, StringComparison.OrdinalIgnoreCase))
          .ToList();
        List<Sequence> sorted = frenchFirst
          ? pairGroup.OrderBy(s => IsFrench(s) ? 0 : 1).ToList()
          : pairGroup.OrderBy(s => IsEnglish(s) ? 0 : 1).ToList();
        foreach (var s in sorted)
        {
          result.Add(s);
          placed.Add(s);
        }
      }
      return result;
    }
    public static void AutoPairNewSequence(Sequence newSeq, IList<Sequence> existing)
    {
      if (newSeq == null || string.IsNullOrWhiteSpace(newSeq.Id)) return;
      if (!string.IsNullOrWhiteSpace(newSeq.PairId)) return;
      var mEn = SuffixEnRegex.Match(newSeq.Id);
      var mFr = SuffixFrRegex.Match(newSeq.Id);
      if (mEn.Success)
      {
        var baseName = mEn.Groups[1].Value;
        var sibling = existing.FirstOrDefault(s =>
          s != newSeq
          && string.IsNullOrWhiteSpace(s.PairId)
          && s.Id != null
          && (s.Id.Equals(baseName + "_fr", StringComparison.OrdinalIgnoreCase)
              || s.Id.Equals(baseName, StringComparison.OrdinalIgnoreCase)));
        if (sibling != null)
          LinkPair(newSeq, sibling);
      }
      else if (mFr.Success)
      {
        var baseName = mFr.Groups[1].Value;
        var sibling = existing.FirstOrDefault(s =>
          s != newSeq
          && string.IsNullOrWhiteSpace(s.PairId)
          && s.Id != null
          && s.Id.Equals(baseName + "_en", StringComparison.OrdinalIgnoreCase));
        if (sibling != null)
          LinkPair(newSeq, sibling);
      }
    }
    public static bool IsEnglish(Sequence s)
    {
      if (s.Voice != null && s.Voice.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        return true;
      if (string.Equals(s.Speaker, "bot1", StringComparison.OrdinalIgnoreCase))
        return true;
      if (s.Id != null && s.Id.EndsWith("_en", StringComparison.OrdinalIgnoreCase))
        return true;
      return false;
    }
    public static bool IsFrench(Sequence s)
    {
      if (s.Voice != null && s.Voice.StartsWith("fr", StringComparison.OrdinalIgnoreCase))
        return true;
      if (string.Equals(s.Speaker, "bot2", StringComparison.OrdinalIgnoreCase))
        return true;
      if (s.Id != null && s.Id.EndsWith("_fr", StringComparison.OrdinalIgnoreCase))
        return true;
      return false;
    }
    public static string GetPairingStats(IEnumerable<Sequence> sequences)
    {
      var list = sequences.ToList();
      var paired   = list.Count(s => !string.IsNullOrWhiteSpace(s.PairId));
      var orphans  = list.Count - paired;
      var pairIds  = list.Where(s => !string.IsNullOrWhiteSpace(s.PairId))
                         .Select(s => s.PairId!)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Count();
      return $"{pairIds} paires ({paired} liées) · {orphans} orphelines";
    }
  }
}