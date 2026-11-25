// Villagers asking for a word. Once every in-game hour (daytime only) Hearsay looks for a villager with something
// on their mind, good or bad, from the reasons in the dialogue data ("wants"). At most one comes up per check, a few
// per day, never the same reason from the same villager twice in a week. It shows as a notice in the game's own
// message stack; clicking it opens the chat with them speaking first. Nothing is sent to an AI until you click.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NSEipix.Base;
using NSMedieval;
using NSMedieval.Model;
using NSMedieval.State;
using NSMedieval.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Hearsay
{
    internal static class Wants
    {
        const string IdPrefix = "hearsay_want_", IconName = "hearsay_talk";
        const int ExpireHours = 24, GapHours = 3, FirstHour = 7, LastHour = 21, QuickHours = 3, RememberDays = 3;

        static readonly Dictionary<string, WarningMessageData> notices = new Dictionary<string, WarningMessageData>();   // village|settler -> notice
        static int lastHour = -1;
        static string lastVillage;

        internal static void Start() => Scheduler.Start(Loop(), e => Plugin.Log.LogError("Hearsay: villager requests stopped: " + e));

        static IEnumerator Loop()
        {
            while (true)
            {
                yield return 2f;
                try { Check(); }
                catch (Exception e) { Plugin.Log.LogError("Hearsay: villager requests: " + e); }
            }
        }

        static int HourNow()
        {
            var d = Facts.Safe(() => GlobalSaveController.CurrentVillageData.DateAndTime);
            return d == null ? -1 : (int)d.DaysTotal * 24 + d.HoursSinceDay;
        }

        static void Check()
        {
            var v = VillageState.Current;
            if (v == null || !MonoSingleton<WarningMessageController>.IsInstantiated()) { lastVillage = null; return; }
            if (lastVillage != v.Key) { HideAll(); lastVillage = v.Key; lastHour = -1; }
            int now = HourNow();
            if (now < 0) return;
            if (now != lastHour)
            {
                lastHour = now;
                Hourly(v, now);
            }
            Sync(v);
        }

        static void Hourly(VillageState v, int now)
        {
            bool changed = false;
            // old requests lapse: unanswered, and remembered
            foreach (var kv in v.Wants.Where(w => now - w.Value.At > ExpireHours || now < w.Value.At).ToList())
            {
                v.Wants.Remove(kv.Key); changed = true;
                if (now >= kv.Value.At) Unanswered(v, kv.Key, kv.Value, waved: false);
            }
            int today = now / 24 + 1;
            foreach (var k in v.Snubs.Where(x => today - x.Value.Day > RememberDays || x.Value.Day > today).Select(x => x.Key).ToList())
            { v.Snubs.Remove(k); changed = true; }

            var people = Facts.Villagers();
            int standing = Facts.Standing(people.Count, out _);
            if (v.LastStanding < 0 || standing < v.LastStanding) { v.LastStanding = standing; changed = true; }

            if (Plugin.AskToTalk.Value && Ready(v, now)) changed |= Raise(v, people, now, standing);
            if (changed) v.Save();
        }

        static bool Ready(VillageState v, int now)
        {
            int hour = now % 24, today = now / 24 + 1;
            if (hour < FirstHour || hour > LastHour) return false;
            if (string.IsNullOrEmpty(v.Title)) return false;   // first things first: they haven't even asked what to call you
            if (now - v.LastWantAt < GapHours) return false;
            if (v.Wants.Count >= 2) return false;
            return v.WantLog.Values.Count(d => d == today) < Math.Max(1, Plugin.AsksPerDay.Value);
        }

        static bool Raise(VillageState v, List<HumanoidInstance> people, int now, int standing)
        {
            int today = now / 24 + 1;
            var book = Book.Get();
            if (book.Wants.Count == 0) return false;
            foreach (var h in people.OrderBy(_ => UnityEngine.Random.value))
            {
                string key = VillageState.SettlerKey(h);
                if (v.Wants.ContainsKey(key) || ChatWindow.Showing == h) continue;
                var chat = v.ChatWith(h);
                if (chat.Count > 0 && chat[chat.Count - 1].Day >= today) continue;   // you've already spoken today
                if (v.Pending.ContainsKey(key)) continue;                          // they're waiting on an answer already
                var s = Scene.Of(h, v, book);
                foreach (var w in book.Wants.OrderByDescending(w => w.P).ThenByDescending(w => w.When.Count).ThenBy(_ => UnityEngine.Random.value))
                {
                    if (v.WantLog.TryGetValue(key + "|" + w.Id, out var last) && today - last < Math.Max(1, w.Cooldown)) continue;
                    if (!s.All(w.When)) continue;
                    if (UnityEngine.Random.value * 100f >= w.Chance) continue;
                    string note = w.Note == null ? null : s.Fill(w.Note);
                    v.Wants[key] = new OpenWant { Id = w.Id, Note = note, At = now };
                    v.WantLog[key + "|" + w.Id] = today;
                    v.LastWantAt = now;
                    if (w.When.Contains("standingup")) v.LastStanding = standing;   // the growth has been remarked on
                    Plugin.Log.LogInfo($"Hearsay: {Facts.FirstName(h)} would like a word ({w.Id}{(w.Good ? ", good news" : "")})");
                    return true;
                }
            }
            return false;
        }

        // ================= developer console (DevCommands.cs) =================
        /// <summary>Raises a request now, skipping the time, cooldown and chance checks; for testing.</summary>
        internal static string DebugRaise(string id, string who)
        {
            var v = VillageState.Current;
            if (v == null) return "No village loaded.";
            var book = Book.Get();
            var w = book.WantById(id);
            if (w == null) return $"No reason '{id}'. hearsay_reasons lists them.";
            var people = Facts.Villagers();
            var h = string.IsNullOrEmpty(who) ? people.FirstOrDefault()
                : people.FirstOrDefault(p => string.Equals(Facts.FirstName(p), who, StringComparison.OrdinalIgnoreCase) || string.Equals(Facts.FullName(p), who, StringComparison.OrdinalIgnoreCase));
            if (h == null) return string.IsNullOrEmpty(who) ? "No villagers." : $"No villager called '{who}'.";
            int now = HourNow();
            string key = VillageState.SettlerKey(h);
            var s = Scene.Of(h, v, book);
            v.Wants[key] = new OpenWant { Id = w.Id, Note = w.Note == null ? null : s.Fill(w.Note), At = now };
            v.WantLog[key + "|" + w.Id] = now / 24 + 1;
            v.LastWantAt = now;
            v.Save();
            Sync(v);
            Plugin.Log.LogInfo($"Hearsay: {Facts.FirstName(h)} would like a word ({w.Id}{(w.Good ? ", good news" : "")}) [console]");
            return $"{Facts.FirstName(h)} would like a word ({w.Id}{(w.Good ? ", good news" : ", a worry")}); conditions {(s.All(w.When) ? "hold" : "do NOT hold")} right now.";
        }

        /// <summary>Every open request goes unanswered now, as if it had lapsed; for testing moods and snubs.</summary>
        internal static string DebugLapse()
        {
            var v = VillageState.Current;
            if (v == null) return "No village loaded.";
            if (v.Wants.Count == 0) return "No open requests.";
            var lapsed = v.Wants.ToList();
            foreach (var kv in lapsed) { v.Wants.Remove(kv.Key); Unanswered(v, kv.Key, kv.Value, waved: false); }
            v.Save();
            Sync(v);
            return $"{lapsed.Count} request(s) lapsed: " + string.Join(", ", lapsed.Select(kv => kv.Value.Id));
        }

        internal static string DebugState()
        {
            var v = VillageState.Current;
            if (v == null) return "No village loaded.";
            return $"village {v.Key}, title {(v.Title ?? "(not asked yet)")}, hour {HourNow()}, open: "
                + (v.Wants.Count == 0 ? "none" : string.Join("; ", v.Wants.Select(kv => $"{kv.Key} {kv.Value.Id} at {kv.Value.At}")))
                + ", snubs: " + (v.Snubs.Count == 0 ? "none" : string.Join("; ", v.Snubs.Select(kv => $"{kv.Key} {kv.Value.Id} day {kv.Value.Day}")));
        }

        /// <summary>Called when you open a chat: hands over (and clears) what they wanted to talk about.</summary>
        internal static (Want want, OpenWant open) Take(HumanoidInstance h)
        {
            var v = VillageState.Current;
            if (v == null || h == null) return (null, null);
            string key = VillageState.SettlerKey(h);
            if (!v.Wants.TryGetValue(key, out var open)) return (null, null);
            v.Wants.Remove(key);
            v.Snubs.Remove(key);   // you came this time
            v.Save();
            Hide(v.Key + "|" + key);
            int now = HourNow();
            if (now >= open.At && now - open.At <= QuickHours && Moods.Give(h, Moods.Heard))
                Plugin.Log.LogInfo($"Hearsay: {Facts.FirstName(h)} feels heard (you came within {now - open.At} h)");
            var w = Book.Get().WantById(open.Id);
            return w == null ? (null, null) : (w, open);
        }

        static void Dismiss(string village, string settler)
        {
            var v = VillageState.Current;
            if (v != null && v.Key == village && v.Wants.TryGetValue(settler, out var open))
            {
                v.Wants.Remove(settler);
                Unanswered(v, settler, open, waved: true);
                v.Save();
            }
            Hide(village + "|" + settler);
        }

        /// <summary>A request nobody answered: they remember it, and if it was a worry it stings.</summary>
        static void Unanswered(VillageState v, string settler, OpenWant open, bool waved)
        {
            var w = Book.Get().WantById(open.Id);
            bool good = w?.Good ?? false;
            v.Snubs[settler] = new Snub { Id = open.Id, Note = open.Note, Good = good, Waved = waved, Day = Facts.Today() };
            var h = Facts.Villagers().FirstOrDefault(p => VillageState.SettlerKey(p) == settler);
            string name = h != null ? Facts.FirstName(h) : settler;
            bool hurt = !good && Moods.Give(h, Moods.Ignored);
            Plugin.Log.LogInfo($"Hearsay: {name}'s request went unanswered ({open.Id}, {(waved ? "closed" : "lapsed")}{(hurt ? ", mood -4" : "")})");
        }

        // ================= notices =================
        static void Sync(VillageState v)
        {
            var people = Facts.Villagers();
            // show every open request (again, after a load or after the game rebuilt its interface)
            foreach (var kv in v.Wants.ToList())
            {
                var h = people.FirstOrDefault(p => VillageState.SettlerKey(p) == kv.Key);
                if (h == null) { v.Wants.Remove(kv.Key); continue; }
                string id = v.Key + "|" + kv.Key;
                if (notices.TryGetValue(id, out var shown) && Showing(shown)) continue;
                var w = Book.Get().WantById(kv.Value.Id);
                notices[id] = Show(v.Key, kv.Key, h, w?.Good ?? false);
            }
            foreach (var id in notices.Keys.ToList())
                if (!v.Wants.ContainsKey(id.Substring(id.IndexOf('|') + 1)) || !id.StartsWith(v.Key + "|")) Hide(id);
        }

        static bool Showing(WarningMessageData m) =>
            Facts.Safe(() => MonoSingleton<WarningMessageManager>.IsInstantiated() && MonoSingleton<WarningMessageManager>.Instance.IsMessageShowing(m));

        static WarningMessageData Show(string village, string settler, HumanoidInstance h, bool good)
        {
            string name = Facts.FirstName(h);
            string text = good ? $"{name} has something to tell you" : $"{name} would like a word";
            string tip = (good ? "Something good happened and they want to share it." : "Something is on their mind.") + " Click to talk, or close to let it pass."
                         + (Plugin.RequestMoods.Value ? (good ? " Come soon and it lifts their mood." : " Come soon and it lifts their mood; ignore it and it stings.") : "");
            var m = new WarningMessageData(WarningMessageCategory.News, text, tip, IconName,
                _ => Ui.Guard(() =>
                {
                    var who = Facts.Villagers().FirstOrDefault(p => VillageState.SettlerKey(p) == settler);
                    if (who != null) ChatWindow.Open(who); else Dismiss(village, settler);
                }),
                null,
                _ => Ui.Guard(() => Dismiss(village, settler)),
                showInPlayerVillageOnly: true, id: IdPrefix + settler);
            MonoSingleton<WarningMessageController>.Instance.ShowMessage(m);
            return m;
        }

        static void Hide(string id)
        {
            if (!notices.TryGetValue(id, out var m)) return;
            notices.Remove(id);
            if (Showing(m)) Facts.Safe(() => { MonoSingleton<WarningMessageController>.Instance.HideMessage(m); return true; });
        }

        static void HideAll() { foreach (var id in notices.Keys.ToList()) Hide(id); }

        /// <summary>Our notices get the same speech icon as the Talk button.</summary>
        [HarmonyPatch(typeof(WarningMessageLayoutItemView), nameof(WarningMessageLayoutItemView.SetData))]
        static class NoticeIcon
        {
            static void Postfix(WarningMessageLayoutItemView __instance, WarningMessageData message)
            {
                if (message?.ID == null || !message.ID.StartsWith(IdPrefix)) return;
                var img = Traverse.Create(__instance).Field("image").GetValue<Image>();
                var icon = TalkButton.Icon();
                if (img != null && icon != null) { img.sprite = icon; img.preserveAspect = true; }
            }
        }
    }
}
