// The written dialogue for conversations without an AI: what you can ask, what villagers ask you, and every
// line they can answer with. The default book ships inside the DLL (dialogue.json); extra files dropped into
// BepInEx/config/Hearsay/dialogue/*.json add lines, topics and questions on top of it (or override them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json;

#pragma warning disable CS0649   // fields are filled from the JSON
namespace Hearsay
{
    /// <summary>One written answer: it applies when all of <see cref="When"/> hold; one of <see cref="Say"/> is picked.
    /// The most specific line wins (more conditions, plus <see cref="P"/>), so general lines are the fallback.</summary>
    internal sealed class Line
    {
        public List<string> When = new List<string>();
        public List<string> Say = new List<string>();
        public int P;
    }

    /// <summary>Something you can say. The reply is built from <see cref="Parts"/>: one line from each part, joined.</summary>
    internal sealed class Topic
    {
        public string Id, Label;
        public List<string> When = new List<string>();
        public int Order = 50;
        public List<List<Line>> Parts = new List<List<Line>>();
        public string Ask;      // a question the villager asks right after replying
        public bool About;      // opens the list of villagers; the reply is about the one you pick
        public bool Close;      // ends the conversation
        public bool NoFollowUp; // never followed by a villager's own question
    }

    /// <summary>A question a villager puts to you, with the answers you can give.</summary>
    internal sealed class Ask
    {
        public string Id;
        public List<string> When = new List<string>();
        public int Chance = 25;        // % chance to come up after a reply, when its conditions hold
        public int Cooldown = 3;       // in-game days before the same villager asks it again
        public bool Manual;            // only asked when something asks for it (the title question)
        public List<Line> Say = new List<Line>();
        public List<Choice> Options = new List<Choice>();
    }

    internal sealed class Choice
    {
        public string Label;
        public List<string> When = new List<string>();
        public List<Line> Reply = new List<Line>();
        public string Title;    // sets how the whole village addresses you
        public bool Name;       // lets you type how you want to be addressed
        public string Ask;      // a follow-up question
    }

    internal sealed class Book
    {
        public Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Skills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<Line> Greet = new List<Line>();
        public List<Topic> Topics = new List<Topic>();
        public List<Ask> Asks = new List<Ask>();

        static Book loaded;
        static readonly Random rng = new Random();

        internal static Book Get()
        {
            if (loaded != null) return loaded;
            var b = new Book();
            try
            {
                using (var s = typeof(Book).Assembly.GetManifestResourceStream("Hearsay.dialogue.json"))
                using (var r = new StreamReader(s))
                    b.Merge(JsonConvert.DeserializeObject<Book>(r.ReadToEnd()));
            }
            catch (Exception e) { Plugin.Log.LogError("Hearsay: the built-in dialogue could not be read: " + e.Message); }
            string dir = Path.Combine(Paths.ConfigPath, "Hearsay", "dialogue");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try { b.Merge(JsonConvert.DeserializeObject<Book>(File.ReadAllText(f))); Plugin.Log.LogInfo("Hearsay: added dialogue from " + Path.GetFileName(f)); }
                    catch (Exception e) { Plugin.Log.LogError($"Hearsay: skipped {Path.GetFileName(f)}: {e.Message}"); }
                }
            int lines = b.Greet.Sum(l => l.Say.Count) + b.Topics.Sum(t => t.Parts.Sum(p => p.Sum(l => l.Say.Count)))
                        + b.Asks.Sum(a => a.Say.Sum(l => l.Say.Count) + a.Options.Sum(o => o.Reply.Sum(l => l.Say.Count)));
            Plugin.Log.LogInfo($"Hearsay: dialogue ready, {b.Topics.Count} topics, {b.Asks.Count} questions, {lines} lines");
            return loaded = b;
        }

        /// <summary>Reload after editing a dialogue file (next conversation picks it up).</summary>
        internal static void Reset() => loaded = null;

        void Merge(Book o)
        {
            if (o == null) return;
            foreach (var g in o.Groups ?? new Dictionary<string, string>()) Groups[g.Key] = g.Value;
            foreach (var g in o.Skills ?? new Dictionary<string, string>()) Skills[g.Key] = g.Value;
            Greet.AddRange(Clean(o.Greet));
            foreach (var t in o.Topics ?? new List<Topic>())
            {
                if (string.IsNullOrEmpty(t.Id)) continue;
                t.Parts = (t.Parts ?? new List<List<Line>>()).Select(Clean).ToList();
                t.When = t.When ?? new List<string>();
                var mine = Topics.FirstOrDefault(x => x.Id == t.Id);
                if (mine == null) { Topics.Add(t); continue; }
                // same topic: its lines are added to ours, part by part
                if (!string.IsNullOrEmpty(t.Label)) mine.Label = t.Label;
                for (int i = 0; i < t.Parts.Count; i++)
                {
                    if (i < mine.Parts.Count) mine.Parts[i].AddRange(t.Parts[i]);
                    else mine.Parts.Add(t.Parts[i]);
                }
            }
            foreach (var a in o.Asks ?? new List<Ask>())
            {
                if (string.IsNullOrEmpty(a.Id)) continue;
                a.Say = Clean(a.Say); a.When = a.When ?? new List<string>();
                a.Options = (a.Options ?? new List<Choice>()).Where(c => c != null).ToList();
                foreach (var c in a.Options) { c.Reply = Clean(c.Reply); c.When = c.When ?? new List<string>(); }
                Asks.RemoveAll(x => x.Id == a.Id);
                Asks.Add(a);
            }
        }

        static List<Line> Clean(List<Line> ls)
        {
            ls = (ls ?? new List<Line>()).Where(l => l != null).ToList();
            foreach (var l in ls) { l.When = l.When ?? new List<string>(); l.Say = l.Say ?? new List<string>(); }
            return ls;
        }

        internal Topic Topic(string id) => Topics.FirstOrDefault(t => t.Id == id);
        internal Ask AskById(string id) => Asks.FirstOrDefault(a => a.Id == id);

        /// <summary>Picks one line: the most specific that applies, avoiding what was said recently.</summary>
        internal static (string text, string key) Pick(List<Line> lines, Scene s, ICollection<string> recent)
        {
            var fits = new List<(int score, string text, string key)>();
            foreach (var l in lines ?? new List<Line>())
            {
                if (l.Say.Count == 0 || !s.All(l.When)) continue;
                int score = l.When.Count(w => !w.StartsWith("chance:")) + l.P;
                foreach (var say in l.Say)
                {
                    string text = s.Fill(say);
                    if (text != null) fits.Add((score, text, Key(say)));
                }
            }
            if (fits.Count == 0) return (null, null);
            int top = fits.Max(f => f.score);
            var best = fits.Where(f => f.score == top).ToList();
            var fresh = best.Where(f => recent == null || !recent.Contains(f.key)).ToList();
            var pool = fresh.Count > 0 ? fresh : best;
            var pick = pool[rng.Next(pool.Count)];
            return (pick.text, pick.key);
        }

        /// <summary>A reply made of several parts (each may come out empty).</summary>
        internal static (string text, string key) Compose(List<List<Line>> parts, Scene s, ICollection<string> recent)
        {
            var texts = new List<string>(); var keys = new List<string>();
            foreach (var part in parts)
            {
                var (t, k) = Pick(part, s, recent);
                if (string.IsNullOrWhiteSpace(t)) continue;
                texts.Add(t.Trim()); keys.Add(k);
            }
            return texts.Count == 0 ? (null, null) : (string.Join(" ", texts), string.Join(",", keys));
        }

        static string Key(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
