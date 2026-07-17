using System;
using System.Text.RegularExpressions;
namespace RoleplayOverlay
{
  public static class TtsHelper
  {
    private static readonly Regex ReImg         = new(@"\[(?:img|image)\b[^\]]*\]",              RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReHtmlBlock   = new(@"\[html\][\s\S]*?\[/html\]",              RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReHtmlTags    = new(@"</?[^>]+>",                              RegexOptions.Compiled);
    private static readonly Regex ReUrlInline   = new(@"\b((https?:\/\/|www\.)[^\s\)]+)",        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReUrlAngle    = new(@"<https?:\/\/[^>]+>",                     RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReUrlParen    = new(@"\((?:https?:\/\/|www\.)[^\)]+\)",        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReWinPath     = new(@"(?:[A-Za-z]:\\|\\\\)[^\s\]\)""' ']+",    RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReMultiSpace  = new(@"\s{2,}",                                 RegexOptions.Compiled);
    private static readonly Regex ReSpacePunct  = new(@"\s+([,;:\.\?!])",                        RegexOptions.Compiled);
    public static string SanitizeForTts(string text, string? voice = null)
    {
      if (string.IsNullOrWhiteSpace(text)) return string.Empty;
      var s = text;
      s = ReImg.Replace(s, " ");
      s = ReHtmlBlock.Replace(s, " ");
      s = ReHtmlTags.Replace(s, "");
      s = ReUrlInline.Replace(s, "");
      s = ReUrlAngle.Replace(s, "");
      s = ReUrlParen.Replace(s, "");
      s = ReWinPath.Replace(s, "");
      s = ReMultiSpace.Replace(s, " ").Trim();
      s = ReSpacePunct.Replace(s, "$1");
      bool isFrench = voice != null &&
        voice.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
      if (isFrench)
        s = TtsExceptions.Apply(s);
      return s;
    }
  }
}