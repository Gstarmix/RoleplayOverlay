
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace RoleplayOverlay
{
  public partial class SplitSequenceDialog : Window
  {
    private const string SEP = "\n───────────────────────\n";
    private static readonly Regex SepRegex = new(
      @"\n?───────────────────────\n?",
      RegexOptions.Compiled);
    private readonly Sequence  _srcEn;
    private readonly Sequence  _srcFr;
    private WpfTextBox? _focusedBox;
    public List<(Sequence En, Sequence Fr)> Result { get; private set; } = new();
    public SplitSequenceDialog(Sequence srcEn, Sequence srcFr)
    {
      _srcEn = srcEn ?? throw new ArgumentNullException(nameof(srcEn));
      _srcFr = srcFr ?? throw new ArgumentNullException(nameof(srcFr));
      InitializeComponent();
      var pairId = _srcEn.PairId ?? _srcEn.Id ?? "?";
      PairIdLabel.Text     = $"— pairId : {pairId}";
      EnSpeakerLabel.Text  = $"{_srcEn.Speaker ?? "bot1"} · {_srcEn.Voice ?? "en-US"}";
      FrSpeakerLabel.Text  = $"{_srcFr.Speaker ?? "bot2"} · {_srcFr.Voice ?? "fr-FR"}";
      EnBox.Text = _srcEn.Text ?? "";
      FrBox.Text = _srcFr.Text ?? "";
      _focusedBox = EnBox;
      UpdateSummary();
    }
    private void OnEnBoxGotFocus(object sender, RoutedEventArgs e)
      => _focusedBox = EnBox;
    private void OnFrBoxGotFocus(object sender, RoutedEventArgs e)
      => _focusedBox = FrBox;
    private void OnCutHereEn(object sender, RoutedEventArgs e)
      => InsertSeparator(EnBox);
    private void OnCutHereFr(object sender, RoutedEventArgs e)
      => InsertSeparator(FrBox);
    private void InsertSeparator(WpfTextBox box)
    {
      int caretPos = box.CaretIndex;
      var text = box.Text;
      int insertAt = caretPos;
      box.Text = text.Substring(0, insertAt) + SEP + text.Substring(insertAt);
      box.CaretIndex = insertAt + SEP.Length;
      box.Focus();
    }
    private void OnTextChanged(object sender, TextChangedEventArgs e)
      => UpdateSummary();
    private void UpdateSummary()
    {
      var enFrags = GetFragments(EnBox?.Text ?? "");
      var frFrags = GetFragments(FrBox?.Text ?? "");
      int nEn = enFrags.Count;
      int nFr = frFrags.Count;
      int nPairs = Math.Max(nEn, nFr);
      if (SummaryLabel != null)
        SummaryLabel.Text = nPairs == 1
          ? "1 paire sera créée (aucune coupure)"
          : $"→ {nPairs} paires seront créées";
      if (EnFragCountLabel != null)
        EnFragCountLabel.Text = nEn > 1 ? $"{nEn} fragments" : "";
      if (FrFragCountLabel != null)
        FrFragCountLabel.Text = nFr > 1 ? $"{nFr} fragments" : "";
      bool warn = nEn != nFr && (nEn > 1 || nFr > 1);
      if (WarningLabel != null)
      {
        WarningLabel.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
        if (warn)
        {
          int missing = Math.Abs(nEn - nFr);
          string side = nEn < nFr ? "EN" : "FR";
          WarningLabel.Text =
            $"⚠ Déséquilibre : EN={nEn} / FR={nFr} — " +
            $"le dernier fragment {side} sera répété pour compléter les {nPairs} paires.";
        }
      }
      if (BtnValidate != null)
        BtnValidate.IsEnabled = nPairs >= 1;
    }
    private void OnValidate(object sender, RoutedEventArgs e)
    {
      var enFrags = GetFragments(EnBox.Text);
      var frFrags = GetFragments(FrBox.Text);
      int nPairs = Math.Max(enFrags.Count, frFrags.Count);
      var basePairId = _srcEn.PairId ?? _srcEn.Id ?? Guid.NewGuid().ToString("N")[..12];
      var suffixes   = GenerateSuffixes(nPairs);
      Result = new List<(Sequence En, Sequence Fr)>();
      for (int i = 0; i < nPairs; i++)
      {
        var newPairId = basePairId + suffixes[i];
        var enText = enFrags.Count > 0
          ? enFrags[Math.Min(i, enFrags.Count - 1)]
          : "";
        var frText = frFrags.Count > 0
          ? frFrags[Math.Min(i, frFrags.Count - 1)]
          : "";
        var newEn = CloneWithNewContent(_srcEn, newPairId, newPairId + "_en", enText);
        var newFr = CloneWithNewContent(_srcFr, newPairId, newPairId + "_fr", frText);
        Result.Add((newEn, newFr));
      }
      DialogResult = true;
      Close();
    }
    private void OnCancel(object sender, RoutedEventArgs e)
    {
      DialogResult = false;
      Close();
    }
    private static List<string> GetFragments(string text)
    {
      var parts = SepRegex.Split(text ?? "");
      return parts
        .Select(p => p.Trim())
        .Where(p => p.Length > 0)
        .ToList();
    }
    private static Sequence CloneWithNewContent(
      Sequence src, string newPairId, string newId, string newText)
    {
      var clone = src.Clone();
      clone.Id     = newId;
      clone.PairId = newPairId;
      clone.Text   = newText;
      clone.EstimatedDuration = TtsTimingService.EstimateFromWordCount(newText, clone.Voice);
      clone.DurationIsExact   = false;
      return clone;
    }
    private static List<string> GenerateSuffixes(int count)
    {
      var result = new List<string> { "" };
      for (int i = 1; i < count; i++)
      {
        if (i <= 25)
          result.Add("_" + (char)('a' + i));
        else
          result.Add("_z" + i);
      }
      return result;
    }
  }
}