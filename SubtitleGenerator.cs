
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace RoleplayOverlay
{
  public static class SubtitleGenerator
  {
    private const int    MAX_WORDS_PER_LINE   = 6;
    private const int    SOFT_BREAK_MIN_WORDS = 3;
    private const int    ORPHAN_MAX_WORDS     = 2;
    private const double CHUNK_GAP_SEC        = 0.05;
    private const double MIN_CHUNK_SEC        = 0.4;
    private static readonly HashSet<char> HardBreak = new() { '.', '!', '?' };
    private static readonly HashSet<char> SoftBreak = new() { ',', ';', ':' };
    private static readonly char[] AllPunct = { '.', '!', '?', ',', ';', ':' };
    public static string HexToAss(string hex)
    {
      var h = hex.TrimStart('#');
      if (h.Length != 6) return "&H00FFFFFF";
      var r = h[0..2]; var g = h[2..4]; var b = h[4..6];
      return $"&H00{b}{g}{r}";
    }
    public static string GenerateAss(
      string      text,
      TimeSpan    totalDuration,
      SpeakerKind speaker,
      string      outputPath,
      SubtitleColors colors,
      int         fontSize  = 28,
      int         playResX  = 1920,
      int         playResY  = 1080)
    {
      var tagged = ChunkText(text);
      MergeOrphanChunks(tagged);
      var chunks   = tagged.ConvertAll(t => t.text);
      var events   = AssignTimings(chunks, totalDuration);
      var assColor = GetAssColors(speaker, colors);
      var content  = BuildAssFile(events, assColor.primary, assColor.outline, fontSize, playResX, playResY);
      File.WriteAllText(outputPath, content, new UTF8Encoding(false));
      return outputPath;
    }
    private static List<(string text, bool newSentence)> ChunkText(string text)
    {
      var chunks = new List<(string text, bool newSentence)>();
      if (string.IsNullOrWhiteSpace(text)) return chunks;
      text = Regex.Replace(text, @"\[img\s+src=""[^""]*""\]", "", RegexOptions.IgnoreCase);
      text = Regex.Replace(text, @"[\r\n]+", ". ");
      text = Regex.Replace(text, @"\.{2,}", ".");
      text = Regex.Replace(text, @"\s{2,}", " ").Trim();
      var words        = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var currentChunk = new List<string>();
      bool nextIsNewSentence = true;
      void FlushChunk(bool hardBreakCaused)
      {
        if (currentChunk.Count == 0) return;
        var joined = string.Join(" ", currentChunk).Trim();
        if (!string.IsNullOrWhiteSpace(joined))
        {
          chunks.Add((joined, nextIsNewSentence));
        }
        currentChunk.Clear();
        nextIsNewSentence = hardBreakCaused;
      }
      foreach (var raw in words)
      {
        var display = raw;
        bool isHard = raw.Length > 0 && HardBreak.Contains(raw[^1]);
        bool isSoft = !isHard && raw.Length > 0 && SoftBreak.Contains(raw[^1]);
        if (!string.IsNullOrWhiteSpace(display))
          currentChunk.Add(display);
        if (currentChunk.Count >= MAX_WORDS_PER_LINE)
        {
          FlushChunk(hardBreakCaused: false);
        }
        else if (isHard)
        {
          FlushChunk(hardBreakCaused: true);
        }
        else if (isSoft && currentChunk.Count >= SOFT_BREAK_MIN_WORDS)
        {
          FlushChunk(hardBreakCaused: false);
        }
      }
      FlushChunk(hardBreakCaused: false);
      return chunks;
    }
    private static void MergeOrphanChunks(List<(string text, bool newSentence)> chunks)
    {
      if (chunks.Count <= 1) return;
      for (int i = chunks.Count - 1; i >= 1; i--)
      {
        var (chunkText, isNewSentence) = chunks[i];
        int wordCount = CountWords(chunkText);
        if (wordCount > ORPHAN_MAX_WORDS) continue;
        if (isNewSentence) continue;
        if (StartsWithUpperCase(chunkText)) continue;
        int prevWords = CountWords(chunks[i - 1].text);
        if (prevWords + wordCount <= MAX_WORDS_PER_LINE + 2 || wordCount <= 1)
        {
          chunks[i - 1] = (chunks[i - 1].text + " " + chunkText, chunks[i - 1].newSentence);
          chunks.RemoveAt(i);
        }
      }
    }
    private static bool StartsWithUpperCase(string text)
    {
      if (string.IsNullOrEmpty(text)) return false;
      foreach (var c in text)
      {
        if (char.IsLetter(c))
          return char.IsUpper(c);
      }
      return false;
    }
    private static List<(string text, TimeSpan start, TimeSpan end)> AssignTimings(
      List<string> chunks, TimeSpan total)
    {
      var result = new List<(string, TimeSpan, TimeSpan)>();
      if (chunks.Count == 0) return result;
      var wordCounts = new int[chunks.Count];
      int totalWords = 0;
      for (int i = 0; i < chunks.Count; i++)
      {
        wordCounts[i] = CountWords(chunks[i]);
        totalWords   += wordCounts[i];
      }
      if (totalWords == 0) totalWords = 1;
      double gapTotal  = CHUNK_GAP_SEC * (chunks.Count - 1);
      double available = Math.Max(0.1, total.TotalSeconds - gapTotal);
      double cursor = 0;
      for (int i = 0; i < chunks.Count; i++)
      {
        double weight   = (double)wordCounts[i] / totalWords;
        double duration = Math.Max(MIN_CHUNK_SEC, available * weight);
        double endSec   = cursor + duration;
        result.Add((chunks[i],
          TimeSpan.FromSeconds(cursor),
          TimeSpan.FromSeconds(endSec)));
        cursor = endSec + CHUNK_GAP_SEC;
      }
      return result;
    }
    private static int CountWords(string text)
      => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    private static string BuildAssFile(
      List<(string text, TimeSpan start, TimeSpan end)> events,
      string primaryAss, string outlineAss, int fontSize,
      int playResX, int playResY)
    {
      var sb = new StringBuilder();
      sb.AppendLine("[Script Info]");
      sb.AppendLine("ScriptType: v4.00+");
      sb.AppendLine($"PlayResX: {playResX}");
      sb.AppendLine($"PlayResY: {playResY}");
      sb.AppendLine("WrapStyle: 0");
      sb.AppendLine();
      sb.AppendLine("[V4+ Styles]");
      sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, " +
                    "OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, " +
                    "ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, " +
                    "Alignment, MarginL, MarginR, MarginV, Encoding");
      int marginV = Math.Max(40, (int)(playResY * 0.075));
      sb.AppendLine($"Style: Default,Arial,{fontSize},{primaryAss},&H000000FF," +
                    $"&H00000000,&H50000000," +
                    $"-1,0,0,0,100,100,0,0,1,3,2,2,20,20,{marginV},1");
      sb.AppendLine();
      sb.AppendLine("[Events]");
      sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
      foreach (var (text, start, end) in events)
      {
        var inline = $"{{\\c{primaryAss}\\3c{outlineAss}\\4c&H00000000\\an2}}";
        sb.AppendLine($"Dialogue: 0,{FormatAssTime(start)},{FormatAssTime(end)}," +
                      $"Default,,0,0,0,,{inline}{text}");
      }
      return sb.ToString();
    }
    private static (string primary, string outline) GetAssColors(
      SpeakerKind speaker, SubtitleColors colors)
    {
      return speaker switch
      {
        SpeakerKind.Bot1 => (HexToAss(colors.Bot1Primary), HexToAss(colors.Bot1Outline)),
        SpeakerKind.Bot2 => (HexToAss(colors.Bot2Primary), HexToAss(colors.Bot2Outline)),
        SpeakerKind.You  => (HexToAss(colors.YouPrimary),  HexToAss(colors.YouOutline)),
        _                => (HexToAss(colors.Bot1Primary), HexToAss(colors.Bot1Outline)),
      };
    }
    private static string FormatAssTime(TimeSpan t)
    {
      int h  = (int)t.TotalHours;
      int m  = t.Minutes;
      int s  = t.Seconds;
      int cs = t.Milliseconds / 10;
      return $"{h}:{m:D2}:{s:D2}.{cs:D2}";
    }
  }
  public sealed class SubtitleColors
  {
    public string Bot1Primary { get; set; } = "#FF00FF";
    public string Bot1Outline { get; set; } = "#FFFFFF";
    public string Bot2Primary { get; set; } = "#00FFFF";
    public string Bot2Outline { get; set; } = "#FFFFFF";
    public string YouPrimary  { get; set; } = "#FFD400";
    public string YouOutline  { get; set; } = "#FFFFFF";
  }
}