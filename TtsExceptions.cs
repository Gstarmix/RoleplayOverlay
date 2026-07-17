using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace RoleplayOverlay
{
  public static class TtsExceptions
  {
    private static readonly Func<string, string>[] _rules = BuildRules();
    public static string Apply(string text)
    {
      if (string.IsNullOrWhiteSpace(text)) return text;
      var s = text;
      foreach (var rule in _rules)
        s = rule(s);
      return s;
    }
    private static Func<string, string>[] BuildRules()
    {
      var list = new List<Func<string, string>>();
      void R(string pattern, string replacement,
             RegexOptions opts = RegexOptions.IgnoreCase)
      {
        var re = new Regex(pattern, opts);
        list.Add(s => re.Replace(s, replacement));
      }
      void E(string pattern, MatchEvaluator evaluator,
             RegexOptions opts = RegexOptions.IgnoreCase)
      {
        var re = new Regex(pattern, opts);
        list.Add(s => re.Replace(s, evaluator));
      }
      R(@"\{\{[^}]*render_md_string\([^)]*\)[^}]*\}\}\s*<span\s+class=""item_fake"">([^<]+)<\/span>",
        " $1");
      R(@"\b@?gaylordaboeka\b",               "gaylor aboéka");
      R(@"(^|[^A-Za-z])gaylord(?![A-Za-z])", "$1gaylor");
      R(@"\bG[\s_-]*star",                    "gé star ");
      R(@"\bGameforge\b",                     "guéme forge");
      R(@"\bLisi(?:è|e)re[\s\u00A0\u200B-\u200D]*B\.?[\s\u00A0\u200B-\u200D]*des[\s\u00A0\u200B-\u200D]*e\.?\b",
        "Lisière Bois des Esprits");
      R(@"\bChem\.?\s*de\s*la\s*for(?:ê|e)t\b", "chemin de la forêt");
      R(@"\bferme\W*d\W*olorune\b",              "ferme dolorune");
      R(@"\bNos[-\s]*Fire\b",    "noce faïeur");
      R(@"\bNos[-\s]*Bazar\b",   "nosse bazar");
      R(@"\bNosVersary\b",       "nosse versary");
      R(@"\bNosMall\b",          "nosse mall");
      R(@"\binstagram\b",        "insta gramme");
      R(@"\bbots\b",             "botte");
      R(@"\bmobbing\b",          "mobigne");
      R(@"\bpass\s*baston\b",    "passe basse ton");
      R(@"\bhit\s*and\s*run\b",  "hitte ande reune");
      R(@"\bboosts\b",           "boost");
      R(@"(?<=\b(?:pour|de|d'|à|va|faut|peut|doit|sans|faire|veut|veux)\s+)booster\b", "bousté");
      R(@"(^|[^A-Za-z0-9])monocible(?![A-Za-z])", "$1monossible");
      R(@"Voie\s+c[ée]leste\s+est(?=[\s,.;!?]|$)", "Voie céleste estte");
      R(@"\bTart\s+Hapendam\b",  "tarte apendame");
      R(@"\bAncelloan\b",        "an sé lo an");
      R(@"(^|[^A-Za-z])kertos(?![A-Za-z])",  "$1kèrtoss");
      R(@"(^|[^A-Za-z])hatus(?![A-Za-z])",   "$1a tusse");
      R(@"\blaurena\b",                        "lau ré na");
      R(@"\bmorcos\b",                         "morcosse");
      R(@"\bvalakus\b",                        "valakusse");
      R(@"\bglacerus\b",                       "glacé russe");
      R(@"\bb[ée]r[ií]os\b",                  "bériosse");
      R(@"\bz[ée]nas\b",                       "zénasse");
      R(@"\bpollutus\b",                       "polu tusse");
      R(@"\bfern[oó]n\b",                      "fère non");
      R(@"\bcuby\b",                           "Cul bi");
      R(@"\bGaylord\b",                        "g starlette");
      R(@"\bkirollas\b",                       "kirollasse");
      R(@"\bvolonté\s+ancelloan\b",            "volonté an sé lo an");
      R(@"\basgobas\b",                        "azgobasse");
      R(@"\bibrahim\b",                        "i bra ime");
      R(@"\byerti\b",                          "yèrti");
      R(@"\bfafnir\b",                         "fafnire");
      R(@"\bbelial\b",                         "béli al");
      R(@"\bmukraju\b",                        "mou kra jou");
      R(@"\bfarmer\b",           "farmé");
      R(@"\bfarmers\b",          "farmé");
      R(@"\btanker\b",           "tanké");
      R(@"\bstarter\b",         "starteur");
      R(@"\blooter\b",           "loutté");
      R(@"\bgrinder\b",          "graïndé");
      R(@"\bhealer\b",           "ileur");
      R(@"\bhealers\b",          "ileurs");
      R(@"\bheal\b",             "ile");
      R(@"\bloot\b",             "loutte");
      R(@"\bloots\b",            "loutte");
      R(@"\bstuff\b",            "steuf");
      R(@"\bbuffs\b",            "beufs");
      R(@"\bbuff\b",             "beuf");
      R(@"\bdebuffs?\b",         "dé beuf");
      R(@"(^|[^A-Za-z])nerf(?![A-Za-z])", "$1neurfe");
      R(@"\bnerfs\b",            "neurfes");
      R(@"\brush\b",             "reuche");
      R(@"\bdrops\b",            "dropes");
      R(@"(^|[^A-Za-z])pet(?![A-Za-z])", "$1pètte");
      R(@"\bpets\b",             "pèttes");
      R(@"(^|[^A-Za-z])run(?![A-Za-z])", "$1reune");
      R(@"\bruns\b",             "reunes");
      R(@"\bsetup\b",            "sèt eupe");
      R(@"\bspawn\b",            "spone");
      R(@"\bspawns\b",           "spones");
      R(@"\bcooldown\b",         "coule daune");
      R(@"\bcooldowns\b",        "coule daunes");
      R(@"\bone[\s-]*shot\b",    "wane shotte");
      R(@"\blevel\s*up\b",       "lèvèl eupe");
      R(@"\blevel\b",            "lèvèl");
      R(@"(^|[^A-Za-z])main(?![A-Za-z])", "$1maïne");
      R(@"(^|[^A-Za-z])hit(?![A-Za-z])", "$1hitte");
      R(@"\balts\b",              "altes");
      R(@"\bsafe\b",              "séïfe");
      var spWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        { "1","un"   }, { "2","deux"   }, { "3","trois" }, { "4","quatre" },
        { "5","cinq" }, { "6","six"    }, { "7","sept"  }, { "8","huit"   },
        { "9","neuf" }, {"10","dix"    }, {"11","onze"  }, {"12","douze"  }
      };
      E(
        @"(^|[^A-Za-z0-9])S[\s\u00A0_-]*P[\s\u00A0_-]*(\d{1,2})[\s\u00A0_-]*(AM|E|A|M)?\.?(?=[^A-Za-z]|$)",
        m =>
        {
          var prefix = m.Groups[1].Value;
          var num    = m.Groups[2].Value;
          var suffix = m.Groups[3].Value.ToUpperInvariant();
          var numWord = spWords.TryGetValue(num, out var w) ? w : num;
          var spoken = suffix switch
          {
            "AM" => $"S P {numWord} A M",
            "E"  => $"S P {numWord} E",
            "A"  => $"S P {numWord} A",
            "M"  => $"S P {numWord} M",
            _    => $"S P {numWord}"
          };
          return prefix + spoken;
        }
      );
      R(@"\bLoD\b",              "lodde");
      R(@"\bLoL\b",              "lol");
      R(@"\bNF\s*1\b",           "noce faïeur 1");
      R(@"\bNF\s*2\b",           "noce faïeur 2");
      R(@"\bNF\s*3\b",           "noce faïeur 3");
      R(@"\bAoE\b",              "a o é");
      R(@"\bDPS\b",              "dé pé èss");
      R(@"\bQoL\b",              "cu o èl");
      R(@"(^|[^A-Za-z])IC(?![A-Za-z])", "$1ci");
      R(@"\+(\d+)", "plusse $1");
      var centWords = new Dictionary<string, string>
      {
        { "2", "deux san" },     { "3", "trois san" },
        { "4", "quatre san" },   { "5", "cinq san" },
        { "6", "six san" },      { "7", "sept san" },
        { "8", "huit san" },     { "9", "neuf san" }
      };
      E(
        @"(?<!\d)([2-9])00(?:\s*000)?(?!\d|[kKmMgG])",
        m =>
        {
          var digit = m.Groups[1].Value;
          if (!centWords.TryGetValue(digit, out var w)) return m.Value;
          if (m.Value.Contains("000")) return w + " mille";
          return w;
        }
      );
      E(
        @"(?<!\d)([2-9])(\d{2})(?!\d|[kKmMgG])",
        m =>
        {
          var digit = m.Groups[1].Value;
          var rest  = m.Groups[2].Value;
          if (rest == "00") return m.Value;
          if (!centWords.TryGetValue(digit, out var w)) return m.Value;
          return w + " " + rest;
        }
      );
      return list.ToArray();
    }
  }
}