// A snapshot of one villager's situation, read from the live game, for the scripted (no-AI) conversations.
// Conditions in the dialogue files ("mood<30", "fx:Hungry|VeryHungry", "perk:Gourmet"...) are checked against it,
// and placeholders ("{name}", "{worst}", "{loved}"...) are filled from it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NSEipix.Repository;
using NSMedieval;
using NSMedieval.Controllers;
using NSMedieval.Model;
using NSMedieval.Repository;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using NSMedieval.Types;
using Social;
using UnityEngine;

namespace Hearsay
{
    internal sealed class Scene
    {
        internal HumanoidInstance H;
        internal float Mood = 50f, Age, Temp;
        internal int Pop, Day, Hour, Talks, Standing;
        internal float Wealth, MinMood = 50f, AvgMood = 50f;
        internal bool CaresAnimals, StandingUp, Snubbed, SnubbedGood;
        internal string AnimalTrouble, Pet;
        internal double FoodDays = 99, LarderDays = 99;   // ready to eat; ready plus carcasses still to butcher
        internal bool Woman, Wounded, Untended;
        internal string Season = "", Faith = "", FaithLevel, Title, Village, Town, Role, Background, BackgroundId, Wound;
        internal string Worst, Best;   // strongest bad / good effect, by the game's name
        internal readonly HashSet<string> Fx = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Perks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal string BestSkill, Loved, Hated, Passion;   // skill ids
        internal readonly List<HumanoidInstance> Friends = new List<HumanoidInstance>(), Rivals = new List<HumanoidInstance>();
        internal readonly Dictionary<HumanoidInstance, AffectionLevel> MyView = new Dictionary<HumanoidInstance, AffectionLevel>();
        internal readonly Dictionary<HumanoidInstance, AffectionLevel> TheirView = new Dictionary<HumanoidInstance, AffectionLevel>();

        /// <summary>The villager being talked about ("What do you think of Anna?"), if any.</summary>
        internal HumanoidInstance Other;
        internal Scene OtherScene;

        internal Book Book;

        static T Safe<T>(Func<T> f) => Facts.Safe(f);

        internal static Scene Of(HumanoidInstance h, VillageState v, Book book, bool deep = true)
        {
            var s = new Scene { H = h, Book = book, Title = v?.Title };
            var info = h.Info;
            s.Woman = Safe(() => info.BodyType.ToString()) == "Female";
            s.Age = Safe(() => (float)info.Age);
            s.Town = Safe(() => info.OriginTown);
            s.BackgroundId = Safe(() => info.Background?.GetID());
            s.Background = Facts.Name(Safe(() => info.Background?.LocKeys), h);
            s.Mood = Safe(() => h.Stats.GetStat(StatType.Mood)?.Current) ?? 50f;
            s.Role = Facts.Name(Safe(() => h.WorkerBehaviour.HumanoidRoleOwner.RoleInstance?.Blueprint?.LocKeys), h);
            float faith = Safe(() => h.Stats.GetStat(StatType.ReligiousAlignment).Current);
            s.Faith = Safe(() => Repository<ReligionRepository, ReligionConfig>.Instance.GetConfigForFaith(faith)?.GetID()) ?? "";
            s.FaithLevel = Facts.Faith(h);
            foreach (var p in Safe(() => h.Perks) ?? new List<Perk>()) { string id = Safe(() => p.GetID()); if (id != null) s.Perks.Add(id); }

            // effects, with the game's own mood value
            float worst = 0, best = 0;
            foreach (var e in Safe(() => h.Stats.GetActiveEffectors()) ?? new List<ActiveEffectorInfo>())
            {
                var bp = Safe(() => e.Blueprint);
                string id = Safe(() => bp.GetID());
                if (id == null) continue;
                if (e.WoundInfo != null)
                {
                    s.Wounded = true;
                    s.Wound = s.Wound ?? Facts.Name(Safe(() => bp.LocKeys), h)?.ToLowerInvariant();
                    if (!Safe(() => WoundUtils.IsTended(e.WoundInfo))) s.Untended = true;
                    continue;
                }
                s.Fx.Add(id);
                float mood = 0;
                foreach (var fx in Safe(() => bp.Effects) ?? new EffectDetailsHolder[0])
                    if (fx.Type == EffectorType.MoodModify && fx.Parameters != null && fx.Parameters.TryGetValue("BaseValue", out var raw)
                        && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                        mood += val * Math.Max(1, e.StackCount);
                if (mood < worst) { worst = mood; s.Worst = Facts.Name(Safe(() => bp.LocKeys), h); }
                if (mood > best) { best = mood; s.Best = Facts.Name(Safe(() => bp.LocKeys), h); }
            }

            // work: best skill, and what they love or hate doing (the game's interest levels)
            var skills = Safe(() => h.SkillsOrdered.Where(k => k.Id != SkillType.None).ToList()) ?? new List<WorkerSkill>();
            s.BestSkill = skills.OrderByDescending(k => k.Level).FirstOrDefault()?.Id.ToString();
            s.Passion = skills.Where(k => k.GetGoalPreferenceLevel() == 5).OrderByDescending(k => k.Level).FirstOrDefault()?.Id.ToString();
            s.Loved = s.Passion ?? skills.Where(k => k.GetGoalPreferenceLevel() == 4).OrderByDescending(k => k.Level).FirstOrDefault()?.Id.ToString();
            s.Hated = skills.Where(k => k.GetGoalPreferenceLevel() == 1).Select(k => k.Id.ToString()).FirstOrDefault()
                      ?? skills.Where(k => k.GetGoalPreferenceLevel() == 2).Select(k => k.Id.ToString()).FirstOrDefault();

            var data = Safe(() => GlobalSaveController.CurrentVillageData);
            s.Village = Safe(() => data.Name);
            var d = Safe(() => data.DateAndTime);
            if (d != null)
            {
                s.Season = Safe(() => d.Season.Name) ?? "";
                s.Day = (int)d.DaysTotal + 1;
                s.Hour = d.HoursSinceDay;
                s.Temp = Safe(() => d.TemperatureCelsius);
            }
            var people = Facts.Villagers();
            s.Pop = people.Count;
            var larder = Facts.Food();
            s.FoodDays = Facts.FoodDays(people, larder);
            s.LarderDays = s.FoodDays >= 99 ? 99 : s.FoodDays + Facts.CarcassDays(people, larder);
            s.Standing = Facts.Standing(people.Count, out s.Wealth);
            s.Talks = v?.ChatWith(h).Count(m => m.FromPlayer) ?? 0;
            s.StandingUp = v != null && v.LastStanding >= 0 && s.Standing > v.LastStanding;
            if (v != null && h != null && v.Snubs.TryGetValue(VillageState.SettlerKey(h), out var snub)) { s.Snubbed = !snub.Good; s.SnubbedGood = snub.Good; }

            if (deep)
            {
                var moods = people.Select(o => Safe(() => o.Stats.GetStat(StatType.Mood)?.Current) ?? 50f).ToList();
                if (moods.Count > 0) { s.MinMood = moods.Min(); s.AvgMood = moods.Average(); }
                s.CaresAnimals = Facts.Cares(h) != null;
                s.AnimalTrouble = Facts.AnimalTrouble();
                s.Pet = Facts.PetOf(h);
                var settings = Safe(() => Repository<SocialCompatibilitySettingsRepository, SocialCompatibilitySettings>.Instance.Settings());
                foreach (var o in people.Where(o => o != h))
                {
                    if (settings == null) break;
                    var mine = settings.GetAffectionLevel(Safe(() => h.ISocialBehaviour.Social.GetAffectionTowards(o)));
                    var theirs = settings.GetAffectionLevel(Safe(() => o.ISocialBehaviour.Social.GetAffectionTowards(h)));
                    s.MyView[o] = mine; s.TheirView[o] = theirs;
                    if (mine == AffectionLevel.Friend) s.Friends.Add(o);
                    if (mine == AffectionLevel.Rival) s.Rivals.Add(o);
                }
            }
            return s;
        }

        internal Scene About(HumanoidInstance other, VillageState v)
        {
            Other = other;
            OtherScene = other == null ? null : Of(other, v, Book, false);
            return this;
        }

        // ================= conditions =================
        static readonly Regex Cmp = new Regex(@"^([a-z.]+)\s*(<=|>=|<|>|=)\s*(-?[0-9.]+)$");

        internal bool All(IEnumerable<string> when) => when == null || when.All(Test);

        internal bool Test(string token)
        {
            token = token?.Trim();
            if (string.IsNullOrEmpty(token)) return true;
            if (token[0] == '!') return !Test(token.Substring(1));
            if (token[0] == '@')   // a named group from the book
                return Book != null && Book.Groups.TryGetValue(token.Substring(1), out var g) && Test(g);

            var m = Cmp.Match(token);
            if (m.Success)
            {
                double x = Number(m.Groups[1].Value), y = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                if (double.IsNaN(x)) return false;
                switch (m.Groups[2].Value)
                {
                    case "<": return x < y;
                    case ">": return x > y;
                    case "<=": return x <= y;
                    case ">=": return x >= y;
                    default: return Math.Abs(x - y) < 0.001;
                }
            }

            int colon = token.IndexOf(':');
            string key = colon < 0 ? token : token.Substring(0, colon);
            var args = colon < 0 ? new string[0] : token.Substring(colon + 1).Split('|').Select(a => a.Trim()).Where(a => a.Length > 0).ToArray();
            if (key.StartsWith("o.")) return OtherScene != null && OtherTest(key.Substring(2), args);
            switch (key)
            {
                case "fx": return args.Any(a => Fx.Any(f => Glob(a, f)));
                case "perk": return args.Any(a => Perks.Contains(a));
                case "bg": return args.Any(a => string.Equals(a, BackgroundId, StringComparison.OrdinalIgnoreCase));
                case "season": return args.Any(a => Season.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
                case "faith": return args.Any(a => string.Equals(a, Faith, StringComparison.OrdinalIgnoreCase));
                case "best": return args.Any(a => string.Equals(a, BestSkill, StringComparison.OrdinalIgnoreCase));
                case "loves": return args.Length == 0 ? Loved != null : args.Any(a => string.Equals(a, Loved, StringComparison.OrdinalIgnoreCase));
                case "hates": return args.Length == 0 ? Hated != null : args.Any(a => string.Equals(a, Hated, StringComparison.OrdinalIgnoreCase));
                case "passion": return Passion != null;
                case "wounded": return Wounded;
                case "untended": return Untended;
                case "friend": return Friends.Count > 0;
                case "rival": return Rivals.Count > 0;
                case "role": return Role != null;
                case "title": return !string.IsNullOrEmpty(Title);
                case "titleis": return args.Any(a => string.Equals(a, Title, StringComparison.OrdinalIgnoreCase));
                case "alone": return Pop <= 1;
                case "woman": return Woman;
                case "man": return !Woman;
                case "night": return Hour < 6 || Hour >= 21;
                case "caresanimals": return CaresAnimals;
                case "animaltrouble": return AnimalTrouble != null;
                case "haspet": return Pet != null;
                case "standingup": return StandingUp;
                case "snubbed": return Snubbed;          // a worry they raised went unanswered
                case "snubbedgood": return SnubbedGood;  // good news they wanted to share went unheard
                case "chance": return args.Length > 0 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && UnityEngine.Random.value * 100 < p;
                default:
                    Plugin.Log.LogWarning("Hearsay: unknown condition '" + token + "' in a dialogue file");
                    return false;
            }
        }

        bool OtherTest(string key, string[] args)
        {
            var o = OtherScene;
            MyView.TryGetValue(Other, out var mine);
            TheirView.TryGetValue(Other, out var theirs);
            switch (key)
            {
                case "friend": return MyView.ContainsKey(Other) && mine == AffectionLevel.Friend;
                case "rival": return MyView.ContainsKey(Other) && mine == AffectionLevel.Rival;
                case "neutral": return !MyView.ContainsKey(Other) || mine == AffectionLevel.Neutral;
                case "likesme": return TheirView.ContainsKey(Other) && theirs == AffectionLevel.Friend;
                case "hatesme": return TheirView.ContainsKey(Other) && theirs == AffectionLevel.Rival;
                case "woman": return o.Woman;
                case "man": return !o.Woman;
                case "wounded": return o.Wounded;
                case "untended": return o.Untended;
                case "fx": return args.Any(a => o.Fx.Any(f => Glob(a, f)));
                case "perk": return args.Any(a => o.Perks.Contains(a));
                case "best": return args.Any(a => string.Equals(a, o.BestSkill, StringComparison.OrdinalIgnoreCase));
                case "samefaith": return o.Faith == Faith;
                case "role": return o.Role != null;
                default:
                    Plugin.Log.LogWarning("Hearsay: unknown condition 'o." + key + "' in a dialogue file");
                    return false;
            }
        }

        double Number(string name)
        {
            switch (name)
            {
                case "mood": return Mood;
                case "food": return FoodDays;
                case "larder": return LarderDays;
                case "pop": return Pop;
                case "day": return Day;
                case "hour": return Hour;
                case "temp": return Temp;
                case "age": return Age;
                case "talks": return Talks;
                case "standing": return Standing;
                case "wealth": return Wealth;
                case "minmood": return MinMood;
                case "avgmood": return AvgMood;
                case "friends": return Friends.Count;
                case "rivals": return Rivals.Count;
                case "o.mood": return OtherScene?.Mood ?? double.NaN;
                case "o.age": return OtherScene?.Age ?? double.NaN;
                default:
                    Plugin.Log.LogWarning("Hearsay: unknown number '" + name + "' in a dialogue file");
                    return double.NaN;
            }
        }

        static bool Glob(string pattern, string id)
        {
            if (pattern.StartsWith("*")) return id.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
            if (pattern.EndsWith("*")) return id.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(pattern, id, StringComparison.OrdinalIgnoreCase);
        }

        // ================= placeholders =================
        static readonly Regex Slot = new Regex(@"\{([A-Za-z.]+)\}");

        /// <summary>Fills {placeholders}; null when one of them has no value here (the line is then not used).</summary>
        internal string Fill(string template)
        {
            bool ok = true;
            string r = Slot.Replace(template, m =>
            {
                string name = m.Groups[1].Value;
                string v = Value(name.ToLowerInvariant());
                if (string.IsNullOrEmpty(v)) { ok = false; return ""; }
                return char.IsUpper(name[0]) ? char.ToUpperInvariant(v[0]) + v.Substring(1) : v;
            });
            return ok ? r : null;
        }

        string Skill(string id) => id == null ? null : Book != null && Book.Skills.TryGetValue(id, out var w) ? w : id.ToLowerInvariant();

        string Value(string name)
        {
            switch (name)
            {
                case "name": return Facts.FirstName(H);
                case "full": return Facts.FullName(H);
                case "title": return Title;
                case "village": return Village;
                case "town": return Town;
                case "season": return Season.ToLowerInvariant();
                case "food": return FoodDays >= 99 ? null : Facts.FoodGuess(FoodDays);
                case "pop": return Pop.ToString(CultureInfo.InvariantCulture);
                case "day": return Day.ToString(CultureInfo.InvariantCulture);
                case "worst": return Worst?.ToLowerInvariant();
                case "best": return Best?.ToLowerInvariant();
                case "wound": return Wound;
                case "animal": return AnimalTrouble;
                case "pet": return Pet;
                case "standing": return Facts.StandingName(Standing);
                case "role": return Role?.ToLowerInvariant();
                case "background": return Background?.ToLowerInvariant();
                case "faithlevel": return FaithLevel?.ToLowerInvariant();
                case "bestskill": return Skill(BestSkill);
                case "loved": return Skill(Loved);
                case "hated": return Skill(Hated);
                case "friend": return Friends.Count == 0 ? null : Facts.FirstName(Friends[0]);
                case "rival": return Rivals.Count == 0 ? null : Facts.FirstName(Rivals[0]);
                case "other": return Other == null ? null : Facts.FirstName(Other);
                case "o.full": return Other == null ? null : Facts.FullName(Other);
                case "o.bestskill": return Skill(OtherScene?.BestSkill);
                case "o.he": return OtherScene == null ? null : OtherScene.Woman ? "she" : "he";
                case "o.him": return OtherScene == null ? null : OtherScene.Woman ? "her" : "him";
                case "o.his": return OtherScene == null ? null : OtherScene.Woman ? "her" : "his";
                case "god": return Faith == "christian" ? "God" : Faith == "pagan" ? "the old gods" : null;
                default: return null;
            }
        }
    }
}
