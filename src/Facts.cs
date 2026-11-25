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
        internal static T Safe<T>(Func<T> f, T fallback) { try { return f(); } catch { return fallback; } }

        static LocalizationController L => MonoSingleton<LocalizationController>.Instance;

        /// <summary>One game: the save folder (named after the village) plus the map seed, since a new game can reuse a
        /// village name and its folder.</summary>
        internal static string VillageKey => Safe(() =>
        {
            var d = GlobalSaveController.CurrentVillageData;
            if (string.IsNullOrEmpty(d?.FolderName)) return null;
            string seed = d.MapSeed;
            return string.IsNullOrEmpty(seed) ? d.FolderName : d.FolderName + "_" + seed;
        });

        internal static string VillageFolder => Safe(() => GlobalSaveController.CurrentVillageData?.FolderName);

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
            if (keys == null) return null;   // a villager without a role, backstory or nickname: the game logs an error on null
            string id = Safe(() => LocKeyUtils.GetName(keys));
            if (string.IsNullOrEmpty(id)) return null;
            return Plain(h != null ? Safe(() => L.GetText(id, h)) : Safe(() => L.GetText(id)));
        }

        static string Info(LocKeys[] keys, HumanoidInstance h = null)
        {
            if (keys == null) return null;   // a villager without a role, backstory or nickname: the game logs an error on null
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
            int standing = Standing(people.Count, out float wealth);
            sb.AppendLine($"What it has become: {StandingNames[standing]}. {StandingRespect[standing]} Let this shape how you address them, within your own character.");
            Plugin.Log.LogInfo($"Hearsay: settlement standing {standing} ({StandingNames[standing]}): {people.Count} villagers, wealth {wealth:0} (animals {AnimalWealth():0})");
            var animals = Animals();
            if (animals != null) sb.Append(animals);
            var food = Food();
            double days = FoodDays(people, food);
            if (days < 99)
            {
                double meatDays = CarcassDays(people, food);
                string line = days < 1 ? "Food: there is next to nothing ready to eat in the village"
                    : $"Food: the village has {FoodGuess(days)} of food ready for everyone";
                if (days >= 1 && food.Stored < food.Usable * 0.5f)
                    line += food.Stored <= 0 ? ", all of it lying about in the open, nothing put away yet"
                                             : ", much of it lying about, not yet put away";
                if (food.Forbidden > food.Usable * 0.25f && food.Forbidden > 100)
                    line += "; there is more food about, but the person you are talking to (the head of the village) told everyone to leave it alone, so say \"you told us to leave it\"";
                if (food.Carcasses > 0)
                    line += $". There {(food.Carcasses == 1 ? "is a carcass" : $"are {food.Carcasses} carcasses")} waiting to be butchered: " +
                            $"{(meatDays < 1 ? "a little" : FoodGuess(meatDays))} of meat once someone cuts {(food.Carcasses == 1 ? "it" : "them")} up, " +
                            "so the worry is getting it butchered and cooked, not having nothing";
                sb.AppendLine(line + ". Everyone can see it, so this is a rough guess by eye, not a count; speak of it that way, never as an exact number of days.");
            }
            return sb.ToString();
        }

        /// <summary>The village's food, counted from every food pile on the map (the game's own pile list): what can be
        /// eaten, how much of that sits in a stockpile rather than lying about, and what is marked off-limits.</summary>
        internal struct Larder
        {
            public float Usable, Stored, Forbidden, Carcass;   // Carcass: the food the unbutchered carcasses would give
            public int Carcasses;
            public bool Known;
        }

        static Dictionary<string, float> carcassFood;

        /// <summary>Nutrition one carcass gives once butchered, from the game's own butchering recipes (Production.json)
        /// and food values (Resources.json): a deer is about 106 meat at 15 each.</summary>
        static float CarcassFood(string id)
        {
            if (carcassFood == null)
            {
                carcassFood = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string dir = Application.streamingAssetsPath + "/Resources/";
                    var nutrition = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(dir + "Resources.json"))["repository"] ?? new Newtonsoft.Json.Linq.JArray())
                        if ((string)r["id"] != null && (float?)r["nutrition"] > 0) nutrition[(string)r["id"]] = (float)r["nutrition"];
                    foreach (var c in Newtonsoft.Json.Linq.JToken.Parse(System.IO.File.ReadAllText(dir + "Production.json")).SelectTokens("$..customProducts[*]"))
                    {
                        string input = (string)c["input"];
                        if (input == null || !input.EndsWith("_carcass")) continue;
                        float food = 0;
                        foreach (var o in c["output"] ?? new Newtonsoft.Json.Linq.JArray())
                            if (nutrition.TryGetValue((string)o["blueprintID"] ?? "", out var n)) food += n * ((float?)o["amount"] ?? 0);
                        if (food > 0 && (!carcassFood.TryGetValue(input, out var had) || food > had)) carcassFood[input] = food;
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Hearsay: could not read butchering recipes: " + e.Message); }
            }
            return carcassFood.TryGetValue(id ?? "", out var f) ? f : 0f;
        }

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
                if (p == null || p.HasDisposed || p.Blueprint == null) continue;
                var cat = p.Blueprint.Category;
                if (cat.HasFlag(ResourceCategory.CtgCarcass) && !cat.HasFlag(ResourceCategory.CtgHumanCarcass) && !p.IsForbidden)
                {
                    float meat = CarcassFood(Safe(() => p.Blueprint.GetID())) * (Safe(() => p.GetStoredResource()?.Amount) ?? 1);
                    if (meat > 0) { l.Carcass += meat; l.Carcasses += Safe(() => p.GetStoredResource()?.Amount) ?? 1; }
                    continue;
                }
                if (!cat.HasFlag(ResourceCategory.CtgEdible)) continue;
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

        /// <summary>Days the unbutchered carcasses would feed everyone once cut up.</summary>
        internal static double CarcassDays(List<HumanoidInstance> people, Larder food)
        {
            if (people.Count == 0 || food.Carcass <= 0) return 0;
            float perHour = people.Sum(h => { float st = Safe(() => h.Stats.GetStat(StatType.Hunger)?.Step) ?? 0f; return st < -0.01f ? -st : DefaultHungerPerHour; });
            return Math.Min(98, Math.Floor(food.Carcass / (perHour * 24f)));
        }

        // ================= how grand the settlement is =================
        static readonly string[] StandingNames = { "a rough camp", "a hamlet", "a village", "a market town", "a great stronghold" };
        static readonly string[] StandingRespect =
        {
            "It is barely more than a camp. The head of the village lives as roughly as everyone else, and people talk to them as one of their own: plainly, even cheekily.",
            "It is a small hamlet. People know the head of the village made this happen; they still speak freely, with a little more regard.",
            "It is a proper village now. The head of the village is clearly in charge; even the jokers show some respect, and cheek comes with a smile.",
            "It is a market town. The head of the village is a person of standing; people choose their words, and even the boldest keep their teasing polite.",
            "It is a great stronghold. The head of the village rules it; everyone shows real deference, and the irreverent ones keep their jokes for safer company.",
        };

        /// <summary>The village's own livestock and pets, at the game's value for each animal (scaled by its health).
        /// The game leaves animals out of its wealth figure; a medieval village would not.</summary>
        internal static float AnimalWealth() => Safe(() =>
        {
            if (!MonoSingleton<NSMedieval.Manager.AnimalManager>.IsInstantiated()) return 0f;
            var m = MonoSingleton<NSMedieval.Manager.AnimalManager>.Instance;
            float sum = 0f;
            foreach (var type in new[] { NSMedieval.Types.AnimalType.Domestic, NSMedieval.Types.AnimalType.Pet })
                foreach (var a in m.GetAnimals(type))
                    if (a != null && !a.HasDied && !a.HasDisposed) sum += a.WealthPoints;
            return sum;
        });

        // ================= animals =================
        static readonly Regex AnimalWords = new Regex(@"\b(animals?|beasts?|livestock|pets?|herd|hounds?|dogs?|cats?|horses?|cattle|shepherd|husbandry|hunt\w*)\b", RegexOptions.IgnoreCase);

        static IEnumerable<AnimalInstance> OurAnimals()
        {
            if (!MonoSingleton<AnimalManager>.IsInstantiated()) return Enumerable.Empty<AnimalInstance>();
            var m = MonoSingleton<AnimalManager>.Instance;
            return Safe(() => m.GetAnimals(AnimalType.Pet).Concat(m.GetAnimals(AnimalType.Domestic))
                .Where(a => a != null && !a.HasDied && !a.HasDisposed && !a.IsLeavingMap).ToList()) ?? new List<AnimalInstance>();
        }

        /// <summary>The village's animals: a short roll call, then only the ones worth a word (hurt, hungry, being
        /// trained, expecting). Healthy, fed, ordinary animals cost nothing beyond the roll call.</summary>
        internal static string Animals()
        {
            var all = OurAnimals().ToList();
            if (all.Count == 0) return "Animals: the village keeps none.\n";
            var sb = new StringBuilder("Animals: ");
            sb.Append(string.Join("; ", all.GroupBy(a => Kind(a)).OrderByDescending(g => g.Count()).Select(g =>
            {
                var names = g.Where(a => !a.IsUnnamed).Select(a => Safe(() => a.GetFullName())).Where(n => !string.IsNullOrEmpty(n)).Take(6).ToList();
                return $"{g.Key} x{g.Count()}" + (names.Count > 0 ? $" ({string.Join(", ", names)}{(g.Count() > names.Count ? ", ..." : "")})" : "");
            })));
            sb.AppendLine(".");
            var notes = new List<string>();
            foreach (var a in all)
            {
                var bits = new List<string>();
                float hp = Safe(() => a.Stats.GetStat(StatType.Health).GetNormalizedPercentage(), 1f);
                var wounds = Safe(() => a.Stats.GetActiveEffectors().Where(e => e.WoundInfo != null).ToList()) ?? new List<ActiveEffectorInfo>();
                int untended = wounds.Count(e => !Safe(() => WoundUtils.IsTended(e.WoundInfo)));
                if (hp < 0.35f) bits.Add("badly hurt");
                else if (hp < 0.75f || wounds.Count > 0) bits.Add("hurt");
                if (untended > 0) bits.Add("wounds untended");
                // the game calls an animal hungry once its hunger falls below zero (range about -100 to its full belly)
                float food = Safe(() => a.Stats.GetStat(StatType.Hunger).Current, 1f);
                if (food < -50f) bits.Add("starving");
                else if (food < 0f) bits.Add("hungry");
                if (a.Pregnant) bits.Add("expecting young");
                if (a.AnimalType == AnimalType.Domestic && Safe(() => a.Blueprint.CanBeTrained))
                {
                    int t = Safe(() => a.GetTrainedPercentage());
                    if (t > 0) bits.Add(t >= 85 ? "nearly trained" : t >= 50 ? "half trained" : "training has begun");
                }
                if (a.AnimalType == AnimalType.Pet)
                {
                    string owner = Safe(() => a.PetOwner is HumanoidInstance o ? FirstName(o) : null);
                    bits.Add("fully trained" + (owner != null ? ", follows " + owner : ""));
                }
                if (bits.Count > 0) notes.Add($"{AnimalLabel(a)}: {string.Join(", ", bits)}");
            }
            foreach (var n in notes.Take(10)) sb.AppendLine("  - " + n);
            if (notes.Count > 10) sb.AppendLine($"  - and {notes.Count - 10} more worth a look");
            sb.AppendLine(notes.Any(n => n.Contains("hurt") || n.Contains("hungry") || n.Contains("starving") || n.Contains("untended"))
                ? "  Every animal not listed as hurt or hungry is fed and well."
                : "  All of them are fed and well; don't invent trouble with them.");
            return sb.ToString();
        }

        /// <summary>The worst-off of the village's animals, as "Rex the dog, starving", or null if all are well.</summary>
        internal static string AnimalTrouble()
        {
            string worst = null; int worstScore = 0;
            foreach (var a in OurAnimals())
            {
                float hp = Safe(() => a.Stats.GetStat(StatType.Health).GetNormalizedPercentage(), 1f);
                float food = Safe(() => a.Stats.GetStat(StatType.Hunger).Current, 1f);
                bool untended = Safe(() => a.Stats.GetActiveEffectors().Any(e => e.WoundInfo != null && !WoundUtils.IsTended(e.WoundInfo)));
                int score = 0; var what = new List<string>();
                if (food < -50f) { score += 3; what.Add("starving"); }
                else if (food < 0f) { score += 1; what.Add("hungry"); }
                if (hp < 0.35f) { score += 3; what.Add("badly hurt"); }
                if (untended) { score += 2; what.Add("its wounds untended"); }
                if (score > worstScore) { worstScore = score; worst = $"{AnimalLabel(a)}, {string.Join(" and ", what)}"; }
            }
            return worst;
        }

        /// <summary>The name of the pet that follows this villager, if any.</summary>
        internal static string PetOf(HumanoidInstance h)
        {
            var pet = OurAnimals().FirstOrDefault(a => a.AnimalType == AnimalType.Pet && Safe(() => a.PetOwner == h));
            return pet == null ? null : AnimalLabel(pet);
        }

        internal static string StandingName(int standing) => StandingNames[Math.Max(0, Math.Min(StandingNames.Length - 1, standing))];

        static string Kind(AnimalInstance a) => Safe(() => AnimalUtils.GetLocalizedName(a.Blueprint)) ?? Safe(() => a.Blueprint.GetID()) ?? "animal";
        static string AnimalLabel(AnimalInstance a)
        {
            string n = a.IsUnnamed ? null : Safe(() => a.GetFullName());
            return string.IsNullOrEmpty(n) ? "a " + Kind(a).ToLowerInvariant() : $"{n} the {Kind(a).ToLowerInvariant()}";
        }

        /// <summary>What this villager especially cares about, from their own interests, perks and past: so that they,
        /// and not everyone, are the ones to speak up when it suffers.</summary>
        internal static string Cares(HumanoidInstance h)
        {
            var info = h.Info;
            var skill = Safe(() => h.SkillsOrdered.FirstOrDefault(s => s.Id == SkillType.AnimalHandling));
            int pref = skill == null ? 3 : Safe(() => skill.GetGoalPreferenceLevel(), 3);
            string text = string.Join(" ", new[]
            {
                Described(Safe(() => info.Background?.LocKeys), h), Described(Safe(() => info.BackStory?.LocKeys), h),
                Described(Safe(() => Repository<PseudonymRepository, Pseudonym>.Instance.GetByID(info.PseudonymId)?.LocKeys), h),
                string.Join(" ", (Safe(() => h.Perks) ?? new List<Perk>()).Select(p => Described(p.LocKeys, h))),
            }.Where(x => x != null));
            bool animals = pref == 5 || pref == 4 || AnimalWords.IsMatch(text) || (skill != null && skill.Level >= 8);
            return animals ? "animals: they notice how the beasts are kept and it bothers them when one suffers" : null;
        }

        /// <summary>0 (camp) to 4 (stronghold), from the number of villagers and the settlement's wealth (the game's own
        /// BaseWealth: buildings, furniture and goods, the figure it scales raids by).</summary>
        internal static int Standing(int people, out float wealth)
        {
            wealth = Safe(() => MonoSingleton<NSMedieval.GameEventSystem.BaseWealth>.IsInstantiated() ? MonoSingleton<NSMedieval.GameEventSystem.BaseWealth>.Instance.GetTotalWealth() : 0f);
            wealth += AnimalWealth();
            int byPeople = people <= 3 ? 0 : people <= 7 ? 1 : people <= 14 ? 2 : people <= 24 ? 3 : 4;
            int byWealth = wealth < 4000 ? 0 : wealth < 15000 ? 1 : wealth < 50000 ? 2 : wealth < 150000 ? 3 : 4;
            return (byPeople + byWealth + 1) / 2;   // both matter; a rich handful or a poor crowd lands in between
        }

        /// <summary>How a villager would judge the stores by looking at them.</summary>
        internal static string FoodGuess(double days) =>
            days < 1 ? "next to nothing" : days < 3 ? "a day or two" : days < 6 ? "a few days" : days < 10 ? "about a week"
            : days < 20 ? "a couple of weeks" : days < 40 ? "about a month" : "plenty";

        static string Ordinal(long n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        // ================= one villager =================
        /// <summary>Who the villager is: what rarely changes, so it can sit in the cached part of the prompt.</summary>
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
            Fact(sb, "Work", Work(h));
            Fact(sb, "Close to their heart", Cares(h));
            return sb.ToString();
        }

        /// <summary>How the villager is right now: everything here can change from one message to the next.</summary>
        internal static string VillagerNow(HumanoidInstance h)
        {
            var sb = new StringBuilder();
            Fact(sb, "Duty in the village", Name(Safe(() => h.WorkerBehaviour.HumanoidRoleOwner.RoleInstance?.Blueprint?.LocKeys), h));
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
