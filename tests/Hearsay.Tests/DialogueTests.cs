// Checks the built-in dialogue.json: it loads, every condition and placeholder is one Hearsay understands, and references resolve.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Hearsay;

public static class DialogueTests
{
    static Book Load() { Book.Reset(); return Book.Get(); }

    /// <summary>Every condition list in the book, with where it came from.</summary>
    static IEnumerable<(string where, List<string> when)> Conditions(Book b)
    {
        foreach (var l in b.Greet) yield return ("greet", l.When);
        foreach (var t in b.Topics)
        {
            yield return ("topic " + t.Id, t.When);
            foreach (var p in t.Parts) foreach (var l in p) yield return ("topic " + t.Id, l.When);
        }
        foreach (var a in b.Asks)
        {
            yield return ("ask " + a.Id, a.When);
            foreach (var l in a.Say) yield return ("ask " + a.Id, l.When);
            foreach (var c in a.Options)
            {
                yield return ("ask " + a.Id + " / " + c.Label, c.When);
                foreach (var l in c.Reply) yield return ("ask " + a.Id + " / " + c.Label, l.When);
            }
        }
        foreach (var w in b.Wants)
        {
            yield return ("want " + w.Id, w.When);
            foreach (var l in w.Say) yield return ("want " + w.Id, l.When);
        }
        foreach (var g in b.Groups) yield return ("group @" + g.Key, new List<string> { g.Value });
    }

    static IEnumerable<(string where, Line line)> Lines(Book b)
    {
        foreach (var l in b.Greet) yield return ("greet", l);
        foreach (var t in b.Topics) foreach (var p in t.Parts) foreach (var l in p) yield return ("topic " + t.Id, l);
        foreach (var a in b.Asks)
        {
            foreach (var l in a.Say) yield return ("ask " + a.Id, l);
            foreach (var c in a.Options) foreach (var l in c.Reply) yield return ("ask " + a.Id + " / " + c.Label, l);
        }
        foreach (var w in b.Wants) foreach (var l in w.Say) yield return ("want " + w.Id, l);
    }

    public static void TestLoads()
    {
        var b = Load();
        Assert.True(b.Topics.Count >= 10 && b.Asks.Count >= 10 && b.Wants.Count >= 10, $"book looks empty: {b.Topics.Count} topics, {b.Asks.Count} asks, {b.Wants.Count} wants");
        Assert.True(b.Greet.Count > 0, "no greetings");
    }

    public static void TestEveryConditionIsKnown()
    {
        var b = Load();
        var s = new Scene { Book = b };
        var bad = new List<string>();
        foreach (var (where, when) in Conditions(b))
            foreach (var token in when)
                {
                    string t = token.Trim().TrimStart('!');
                    if (t.StartsWith("chance:"))   // uses UnityEngine.Random, so check its shape instead of running it
                    {
                        if (!double.TryParse(t.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) || p <= 0 || p > 100) bad.Add($"{where}: bad '{token}'");
                        continue;
                    }
                    if (t.StartsWith("@") && !b.Groups.ContainsKey(t.Substring(1))) { bad.Add($"{where}: unknown group '{token}'"); continue; }
                    Env.Warnings.Clear();
                    s.Test(t);
                    if (Env.Warnings.Count > 0) bad.Add($"{where}: {Env.Warnings[0]}");
                }
        Assert.True(bad.Count == 0, bad.Count + " problem(s):\n" + string.Join("\n", bad.Distinct().Take(15)));
    }

    // Placeholders that need a real villager (another HumanoidInstance) and so are only checked by name here.
    static readonly HashSet<string> NeedVillager = new HashSet<string> { "friend", "rival", "other", "o.full", "o.bestskill", "o.he", "o.him", "o.his" };

    public static void TestEveryPlaceholderIsKnown()
    {
        var b = Load();
        var s = new Scene
        {
            Book = b, Title = "Chief", Village = "Testford", Town = "Oldbury", Season = "Summer", FoodDays = 4, Worst = "Hungry", Best = "Well fed",
            Wound = "a cut", AnimalTrouble = "the goat", Pet = "Rex", Role = "Farmer", Background = "Farmer", FaithLevel = "Devout",
            BestSkill = "Farming", Loved = "Cooking", Hated = "Mining", Faith = "christian", Standing = 2, Pop = 3, Day = 5,
        };
        var bad = new List<string>();
        foreach (var (where, line) in Lines(b))
            foreach (var say in line.Say)
                foreach (Match m in Regex.Matches(say, @"\{([A-Za-z.]+)\}"))
                {
                    string name = m.Groups[1].Value.ToLowerInvariant();
                    if (NeedVillager.Contains(name)) continue;
                    if (s.Fill("{" + m.Groups[1].Value + "}") == null) bad.Add($"{where}: unknown placeholder {m.Value}");
                }
        Assert.True(bad.Count == 0, bad.Count + " problem(s):\n" + string.Join("\n", bad.Distinct().Take(15)));
    }

    public static void TestIdsUniqueAndReferencesResolve()
    {
        var b = Load();
        var bad = new List<string>();
        foreach (var dup in b.Topics.GroupBy(t => t.Id).Where(g => g.Count() > 1)) bad.Add("duplicate topic id " + dup.Key);
        foreach (var dup in b.Wants.GroupBy(w => w.Id).Where(g => g.Count() > 1)) bad.Add("duplicate want id " + dup.Key);
        var asks = new HashSet<string>(b.Asks.Select(a => a.Id));
        foreach (var t in b.Topics) if (!string.IsNullOrEmpty(t.Ask) && !asks.Contains(t.Ask)) bad.Add($"topic {t.Id} asks missing '{t.Ask}'");
        foreach (var w in b.Wants) if (!string.IsNullOrEmpty(w.Ask) && !asks.Contains(w.Ask)) bad.Add($"want {w.Id} asks missing '{w.Ask}'");
        foreach (var a in b.Asks) foreach (var c in a.Options)
            if (!string.IsNullOrEmpty(c.Ask) && !asks.Contains(c.Ask)) bad.Add($"ask {a.Id} / {c.Label} follows up with missing '{c.Ask}'");
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    public static void TestNumbersAreSane()
    {
        var b = Load();
        var bad = new List<string>();
        foreach (var a in b.Asks)
        {
            if (a.Chance < 0 || a.Chance > 100) bad.Add($"ask {a.Id} chance {a.Chance}");
            if (a.Cooldown < 0) bad.Add($"ask {a.Id} cooldown {a.Cooldown}");
            if (a.Options.Count == 0) bad.Add($"ask {a.Id} has no answers to choose from");
        }
        foreach (var w in b.Wants)
        {
            if (w.Chance <= 0 || w.Chance > 100) bad.Add($"want {w.Id} chance {w.Chance}");
            if (w.Cooldown < 0) bad.Add($"want {w.Id} cooldown {w.Cooldown}");
            if (string.IsNullOrWhiteSpace(w.Note)) bad.Add($"want {w.Id} has no note for the AI");
            if (w.Say.Sum(l => l.Say.Count) == 0) bad.Add($"want {w.Id} has no written opening line");
        }
        foreach (var (where, line) in Lines(b))
        {
            if (line.Say.Count == 0) bad.Add($"{where}: a line with nothing to say");
            // a blank line is allowed only inside a topic's parts: Compose skips it, so that part can say nothing
            if (!where.StartsWith("topic ") && line.Say.Any(string.IsNullOrWhiteSpace)) bad.Add($"{where}: an empty line");
        }
        Assert.True(bad.Count == 0, bad.Count + " problem(s):\n" + string.Join("\n", bad.Distinct().Take(15)));
    }
}
