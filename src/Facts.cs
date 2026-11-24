// What a villager knows, read from the live game whenever the player sends a message. Wherever the game has
// its own words for something (effect names, interest levels, activity line, faith level, friend/rival), those
// words are used, so the villager talks about their life the way the game describes it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NSEipix.Base;
using NSEipix.Repository;
using NSMedieval;
using NSMedieval.Controllers;
using NSMedieval.Enums;
using NSMedieval.Manager;
using NSMedieval.Model;
using NSMedieval.Repository;
using NSMedieval.State;
using NSMedieval.State.WorkerJobs;
using NSMedieval.StatsSystem;
using NSMedieval.Types;
using NSMedieval.UI.Utils;
using Social;
using UnityEngine;

namespace Hearsay
{
    internal static class Facts
    {
        /// <summary>A settler's base hunger loss per in-game hour (the Hunger stat's step in the game data); eating adds a
        /// food's nutrition to that stat one-to-one. Used when a villager's own rate can't be read.</summary>
        const float DefaultHungerPerHour = 3.7f;

        internal static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        static LocalizationController L => MonoSingleton<LocalizationController>.Instance;

        internal static string VillageKey => Safe(() => GlobalSaveController.CurrentVillageData?.FolderName);

        internal static List<HumanoidInstance> Villagers() =>
            Safe(() => WorkerManager.LabourersHere.Where(w => w != null && !w.HasDisposed && !w.HasDied).ToList()) ?? new List<HumanoidInstance>();

        internal static string FirstName(HumanoidInstance h) => Safe(() => h.Info.FirstName) ?? FullName(h);
        internal static string FullName(HumanoidInstance h) => Safe(() => h.GetFullName()) ?? "a villager";

        internal static string Now()
        {
            var d = Safe(() => GlobalSaveController.CurrentVillageData.DateAndTime);
            if (d == null) return "";
            // the game's own date wording, as in its logs ("Winter 1, 1353"), plus the hour
            string date = Safe(() => $"{d.GetSeasonDayLocalized(d.DayOfYear)}, {d.Year}");
            return (string.IsNullOrEmpty(date) ? $"day {d.DaysTotal + 1}" : date) + $" at {d.HoursSinceDay:00}:{d.MinutesSinceHour:00}";
        }

        internal static int Today() => Safe(() => (int)GlobalSaveController.CurrentVillageData.DateAndTime.DaysTotal + 1);

        /// <summary>Localized name of anything with LocKeys (perk, background, effector, role...).</summary>
        internal static string Name(LocKeys[] keys, HumanoidInstance h = null)
        {
            string id = Safe(() => LocKeyUtils.GetName(keys));
            if (string.IsNullOrEmpty(id)) return null;
            return Plain(h != null ? Safe(() => L.GetText(id, h)) : Safe(() => L.GetText(id)));
        }

        static string Info(LocKeys[] keys, HumanoidInstance h = null)
        {
            string id = Safe(() => LocKeyUtils.GetInfo(keys));
            if (string.IsNullOrEmpty(id)) return null;
            return Plain(h != null ? Safe(() => L.GetText(id, h)) : Safe(() => L.GetText(id)));
        }

        /// <summary>Strips the game's rich-text tags and sprite markup.</summary>
        internal static string Plain(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Regex.Replace(s, "<[^>]*>", "").Trim();
            return s.Length == 0 ? null : s;
        }

        // ================= the village =================
        internal static string Village()
        {
            var sb = new StringBuilder();
            var data = GlobalSaveController.CurrentVillageData;
            var people = Villagers();
            sb.AppendLine($"The village is called {Safe(() => data.Name)}. It has {people.Count} villagers: {string.Join(", ", people.Select(FullName))}.");
            var d = Safe(() => data.DateAndTime);
            if (d != null)
                sb.AppendLine($"It is {Safe(() => d.Season.Name)?.ToLowerInvariant()}, {d.HoursSinceDay:00}:{d.MinutesSinceHour:00}, " +
                              $"on the {Ordinal(d.DaysTotal + 1)} day since the village was founded. It is {Mathf.RoundToInt(Safe(() => d.TemperatureCelsius))} °C outside.");
            var food = Food();
            double days = FoodDays(people, food);
            if (days < 99)
            {
                string line = days < 1 ? "Food: there is next to nothing to eat in the village"
                    : $"Food: the village has {FoodGuess(days)} of food for everyone";
                if (days >= 1 && food.Stored < food.Usable * 0.5f)
                    line += food.Stored <= 0 ? ", all of it lying about in the open, not yet put away in stores"
                                             : ", much of it lying about, not yet put away in stores";
                if (food.Forbidden > food.Usable * 0.25f && food.Forbidden > 100)
                    line += "; there is more food about that the head of the village has marked off-limits";
                sb.AppendLine(line + ". Everyone can see it, so this is a rough guess by eye, not a count; speak of it that way, never as an exact number of days.");
            }
            return sb.ToString();
        }

        /// <summary>The village's food, counted from every food pile on the map (the game's own pile list): what can be
        /// eaten, how much of that sits in a stockpile rather than lying about, and what is marked off-limits.</summary>
        internal struct Larder { public float Usable, Stored, Forbidden; public bool Known; }

        internal static Larder Food()
        {
            var l = new Larder();
            if (!MonoSingleton<ResourcePileManager>.IsInstantiated()) return l;
            var piles = Safe(() => MonoSingleton<ResourcePileManager>.Instance.SpawnedPileInstances);
            if (piles == null) return l;
            l.Known = true;
            for (int i = 0; i < piles.Count; i++)
            {
                var p = piles[i];
                if (p == null || p.HasDisposed || p.Blueprint == null || !p.Blueprint.Category.HasFlag(ResourceCategory.CtgEdible)) continue;
                float n = Safe(() => (float)p.GetNutrition());
                if (n <= 0) continue;
                if (p.IsForbidden) { l.Forbidden += n; continue; }
                l.Usable += n;
                if (p.InstanceStockpile != null) l.Stored += n;
            }
            return l;
        }

        /// <summary>Whole days the usable food would feed everyone, by each villager's own hunger rate (perks included);
        /// 99 when unknown.</summary>
        internal static double FoodDays(List<HumanoidInstance> people) => FoodDays(people, Food());

        internal static double FoodDays(List<HumanoidInstance> people, Larder food)
        {
            if (people.Count == 0 || !food.Known) return 99;
            float perHour = people.Sum(h =>
            {
                float step = Safe(() => h.Stats.GetStat(StatType.Hunger)?.Step) ?? 0f;
                return step < -0.01f ? -step : DefaultHungerPerHour;
            });
            return Math.Min(98, Math.Floor(food.Usable / (perHour * 24f)));
        }

        /// <summary>How a villager would judge the stores by looking at them.</summary>
        internal static string FoodGuess(double days) =>
            days < 1 ? "next to nothing" : days < 3 ? "a day or two" : days < 6 ? "a few days" : days < 10 ? "about a week"
            : days < 20 ? "a couple of weeks" : days < 40 ? "about a month" : "plenty";

        static string Ordinal(long n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        // ================= one villager =================
        internal static string Villager(HumanoidInstance h)
        {
            var sb = new StringBuilder();
            var info = h.Info;
            bool woman = Safe(() => info.BodyType.ToString()) == "Female";
            sb.AppendLine($"{FullName(h)}, {(woman ? "a woman" : "a man")} of {Safe(() => info.Age)}, born in {Safe(() => info.OriginTown) ?? "a town far from here"}.");

            Fact(sb, "Earlier life", Join("; ", Described(Safe(() => info.Background?.LocKeys), h), Described(Safe(() => info.BackStory?.LocKeys), h)));
            var pseudonym = Safe(() => Repository<PseudonymRepository, Pseudonym>.Instance.GetByID(info.PseudonymId));
            Fact(sb, "Known around the village as", Described(Safe(() => pseudonym?.LocKeys), h));
            Fact(sb, "Character", string.Join("; ", (Safe(() => h.Perks) ?? new List<Perk>()).Select(p => Described(p.LocKeys, h)).Where(x => x != null)));
            Fact(sb, "Faith", Faith(h));
            Fact(sb, "Duty in the village", Name(Safe(() => h.WorkerBehaviour.HumanoidRoleOwner.RoleInstance?.Blueprint?.LocKeys), h));
            Fact(sb, "Work", Work(h));
            Fact(sb, "Doing right now", Plain(Safe(() => CreatureBaseUtils.GetLocalizedCurrentActionInfo(h))));
            Fact(sb, "Wounds", Wounds(h));
            float mood = Safe(() => h.Stats.GetStat(StatType.Mood)?.Current) ?? -1f;
            Fact(sb, "Mood", mood < 0 ? null : $"{Mathf.RoundToInt(mood)} out of 100");
            Fact(sb, "What is affecting them", Effects(h));
            Fact(sb, "The other villagers", Others(h));
            var life = Safe(() => h.LifeEventLogs.Where(e => e.Type != LifeEventType.Conversation).Take(10)
                .Select(e => Plain(e.GetLocalizedLogWithoutSprites())).Where(x => x != null).ToList());
            if (life?.Count > 0) Fact(sb, "Lately, most recent first", string.Join(" | ", life));
            return sb.ToString();
        }

        static string Described(LocKeys[] keys, HumanoidInstance h)
        {
            string n = Name(keys, h);
            if (n == null) return null;
            string i = Info(keys, h);
            return i == null || i == n ? n : $"{n} ({i})";
        }

        /// <summary>Where they stand on the game's faith scale, in the game's own words for that level.</summary>
        internal static string Faith(HumanoidInstance h)
        {
            float v = Safe(() => h.Stats.GetStat(StatType.ReligiousAlignment).Current);
            string key = Safe(() => h.HumanoidBelief.GetThresholdName(v));
            return string.IsNullOrEmpty(key) ? null : Plain(Safe(() => L.GetText(key, h)));
        }

        /// <summary>Skills that matter, each with the game's interest word (Passionate, Eager, ...) when it isn't neutral.</summary>
        static string Work(HumanoidInstance h)
        {
            var skills = Safe(() => h.SkillsOrdered.Where(s => s.Id != SkillType.None).ToList());
            if (skills == null) return null;
            var parts = new List<string>();
            foreach (var s in skills.OrderByDescending(s => s.Level))
            {
                int pref = s.GetGoalPreferenceLevel();
                if (s.Level < 3 && (pref == 3 || pref == 0)) continue;   // untrained and indifferent: nothing to say
                string name = Safe(() => L.GetText("skill_name_" + s.Id));
                if (string.IsNullOrEmpty(name) || name.StartsWith("skill_name_")) name = s.Id.ToString();
                string interest = pref == 3 || pref == 0 ? null
                    : Name(Safe(() => Repository<GoalPreferenceLevelRepository, GoalPreferenceLevelData>.Instance.GetDataByPreferenceLevel((GoalPreferenceLevel)pref)?.LocKeys));
                int max = Math.Max(1, Safe(() => s.GetMaxLevel()));
                parts.Add($"{name} {s.Level}/{max}" + (interest == null ? "" : $" ({interest.ToLowerInvariant()})"));
            }
            return string.Join(", ", parts);
        }

        static string Wounds(HumanoidInstance h)
        {
            var list = Safe(() => h.Stats.GetActiveEffectors().Where(e => e.WoundInfo != null).ToList());
            if (list == null || list.Count == 0) return null;
            return string.Join(", ", list.Select(e => (Name(Safe(() => e.Blueprint.LocKeys), h) ?? "a wound")
                + (Safe(() => WoundUtils.IsTended(e.WoundInfo)) ? ", tended" : ", untended")));
        }

        /// <summary>Every active effect (hunger, sleep, weather, rooms, food, events...) by its game name,
        /// with its mood value from the game's data, strongest first.</summary>
        static string Effects(HumanoidInstance h)
        {
            var active = Safe(() => h.Stats.GetActiveEffectors().Where(e => e.WoundInfo == null).ToList());
            if (active == null) return null;
            var totals = new Dictionary<string, float>();
            foreach (var e in active)
            {
                var bp = Safe(() => e.Blueprint);
                string name = Name(Safe(() => bp.LocKeys), h);
                if (name == null) continue;
                float mood = 0;
                foreach (var fx in Safe(() => bp.Effects) ?? new EffectDetailsHolder[0])
                    if (fx.Type == EffectorType.MoodModify && fx.Parameters != null && fx.Parameters.TryGetValue("BaseValue", out var raw)
                        && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
                        mood += val * Math.Max(1, e.StackCount);
                totals[name] = (totals.TryGetValue(name, out var t) ? t : 0) + mood;
            }
            return string.Join(", ", totals.OrderByDescending(p => Math.Abs(p.Value)).Take(14)
                .Select(p => p.Value == 0 ? p.Key : $"{p.Key} (mood {(p.Value > 0 ? "+" : "")}{p.Value:0.#})"));
        }

        /// <summary>Friend / rival / neutral by the game's own thresholds, both directions, and what passed between them lately.</summary>
        static string Others(HumanoidInstance h)
        {
            var settings = Safe(() => Repository<SocialCompatibilitySettingsRepository, SocialCompatibilitySettings>.Instance.Settings());
            var log = Safe(() => h.ISocialBehaviour.Social.AffectionEffectorsLog.ToList()) ?? new List<EffectorLogStruct>();
            var parts = new List<string>();
            foreach (var o in Villagers().Where(o => o != h))
            {
                float mine = Safe(() => h.ISocialBehaviour.Social.GetAffectionTowards(o));
                float theirs = Safe(() => o.ISocialBehaviour.Social.GetAffectionTowards(h));
                string Level(float a) => settings == null ? "" : settings.GetAffectionLevel(a).ToString().ToLowerInvariant();
                string s = $"{FullName(o)}: {FirstName(h)} counts them as {Level(mine)} ({mine:0}); {FirstName(o)} counts {FirstName(h)} as {Level(theirs)} ({theirs:0})";
                var recent = log.Where(l => l.UniqueId == o.UniqueId).Take(3)
                    .Select(l => Name(Safe(() => Repository<EffectorRepository, StatEffector>.Instance.GetByID(l.EffectorId)?.LocKeys), h))
                    .Where(x => x != null).Distinct().ToList();
                if (recent.Count > 0) s += "; lately: " + string.Join(", ", recent);
                parts.Add(s);
            }
            return string.Join(" | ", parts);
        }

        static string Join(string sep, params string[] xs) => string.Join(sep, xs.Where(x => !string.IsNullOrEmpty(x)));

        static void Fact(StringBuilder sb, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"- {label}: {value}");
        }
    }
}
