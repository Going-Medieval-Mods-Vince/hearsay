// What Hearsay remembers per village (save): how the player is addressed, and each conversation.
// Stored as JSON in BepInEx/config/Hearsay/<village folder>.json, next to the save it belongs to by name.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using NSMedieval.State;
using Newtonsoft.Json;

namespace Hearsay
{
    internal sealed class Message
    {
        public bool FromPlayer;
        public string Text;
        public string When;   // in-game time, for the record
        public int Day;       // in-game day number
        public string Line;   // which written line(s) a scripted reply came from, so they aren't repeated soon
        public string Ask;    // the question a villager asked in this message, if any
    }

    /// <summary>A villager waiting to have a word with you.</summary>
    internal sealed class OpenWant
    {
        public string Id;
        public string Note;    // what it's about, filled in when it came up (for the AI)
        public int At;         // in-game hour (since founding) it came up
    }

    /// <summary>A request for a word that went unanswered, remembered until you next talk.</summary>
    internal sealed class Snub
    {
        public string Id;
        public string Note;
        public bool Good;      // it was good news they wanted to share
        public bool Waved;     // you closed the notice, rather than letting it lapse
        public int Day;
    }

    internal sealed class VillageState
    {
        public string Title;                    // how the villagers address the player; null = not asked yet
        public string TitleSetBy;               // who asked first
        public Dictionary<string, List<Message>> Chats = new Dictionary<string, List<Message>>();
        public Dictionary<string, string> Pending = new Dictionary<string, string>();
        public Dictionary<string, int> LastSocialDay = new Dictionary<string, int>();   // settler -> day their Social need was last met by talking

        public Dictionary<string, OpenWant> Wants = new Dictionary<string, OpenWant>();   // settler -> waiting to talk
        public Dictionary<string, int> WantLog = new Dictionary<string, int>();          // "settler|reason" -> day it last came up
        public int LastWantAt = -1000;          // in-game hour of the last notice
        public int LastStanding = -1;           // the settlement's standing last time we looked (to notice it growing)
        public Dictionary<string, Snub> Snubs = new Dictionary<string, Snub>();         // settler -> a request you didn't answer

        [JsonIgnore] public string Key;

        static VillageState current;

        internal static VillageState Current
        {
            get
            {
                string key = Facts.VillageKey;
                if (string.IsNullOrEmpty(key)) return null;
                if (current?.Key == key) return current;
                current = Load(key);
                return current;
            }
        }

        static string PathFor(string key)
        {
            string safe = new string(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            return Path.Combine(Paths.ConfigPath, "Hearsay", safe + ".json");
        }

        static VillageState Load(string key)
        {
            VillageState s = null;
            try
            {
                string p = PathFor(key);
                if (File.Exists(p)) s = JsonConvert.DeserializeObject<VillageState>(File.ReadAllText(p));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hearsay: could not read village state, starting fresh: " + e.Message); }
            s = s ?? Legacy(key);
            s = s ?? new VillageState();
            s.Chats = s.Chats ?? new Dictionary<string, List<Message>>();
            s.Pending = s.Pending ?? new Dictionary<string, string>();
            s.LastSocialDay = s.LastSocialDay ?? new Dictionary<string, int>();
            s.Wants = s.Wants ?? new Dictionary<string, OpenWant>();
            s.WantLog = s.WantLog ?? new Dictionary<string, int>();
            s.Snubs = s.Snubs ?? new Dictionary<string, Snub>();
            s.Key = key;
            return s;
        }

        /// <summary>Older versions named the file after the village folder alone, so a new game with the same village name
        /// picked up the old one. Such a file is taken over only when it plainly belongs to this game: its villagers are
        /// the ones living here, and nothing in it happened later than today.</summary>
        static VillageState Legacy(string key)
        {
            string folder = Facts.VillageFolder;
            if (string.IsNullOrEmpty(folder) || folder == key) return null;
            string p = PathFor(folder);
            if (!File.Exists(p)) return null;
            try
            {
                var old = JsonConvert.DeserializeObject<VillageState>(File.ReadAllText(p));
                if (old?.Chats == null || old.Chats.Count == 0) return null;
                var here = new HashSet<string>(Facts.Villagers().Select(SettlerKey));
                int today = Facts.Today();
                int lastDay = old.Chats.Values.SelectMany(c => c).Select(m => m.Day).DefaultIfEmpty(0).Max();
                int known = old.Chats.Keys.Count(here.Contains);
                bool ours = lastDay <= today + 1 && known * 2 >= old.Chats.Count && known > 0
                            && old.Chats.Values.SelectMany(c => c).All(m => m.Day >= 1);
                if (!ours)
                {
                    Plugin.Log.LogInfo($"Hearsay: {Path.GetFileName(p)} is from another game with the same village name; starting fresh");
                    return null;
                }
                Plugin.Log.LogInfo($"Hearsay: carrying over conversations from {Path.GetFileName(p)}");
                File.Move(p, p + ".moved");
                return old;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Hearsay: could not check old village file: " + e.Message); return null; }
        }

        /// <summary>Most messages kept per conversation; older ones are dropped when saving.</summary>
        internal const int KeepMessages = 300;

        internal void Save()
        {
            try
            {
                foreach (var list in Chats.Values)
                    if (list.Count > KeepMessages) list.RemoveRange(0, list.Count - KeepMessages);
                string p = PathFor(Key);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch (Exception e) { Plugin.Log.LogError("Hearsay: could not save village state: " + e); }
        }

        /// <summary>Talking with a villager counts as a conversation for their Social need, the same game effect two
        /// villagers get when they chat (InteractionAffectSocial), at most once per in-game day per villager.</summary>
        internal void Talked(HumanoidInstance h)
        {
            if (h == null) return;
            string k = SettlerKey(h);
            if (Snubs.Remove(k)) Save();   // they've had their chance to bring it up
            if (!Plugin.SocialBoost.Value || h.HasDisposed || h.HasDied) return;
            int today = Facts.Today();
            if (LastSocialDay.TryGetValue(k, out var day) && day >= today) return;
            bool ok = Facts.Safe(() => h.Stats.StartEffector("InteractionAffectSocial"));
            if (!ok) return;
            LastSocialDay[k] = today;
            Save();
            Plugin.Log.LogInfo($"Hearsay: {Facts.FirstName(h)} enjoyed the talk (Social need met for today)");
        }

        internal static string SettlerKey(HumanoidInstance h) => h.UniqueId + "_" + Facts.FullName(h);

        internal List<Message> ChatWith(HumanoidInstance h)
        {
            string k = SettlerKey(h);
            if (!Chats.TryGetValue(k, out var list)) Chats[k] = list = new List<Message>();
            return list;
        }
    }
}
