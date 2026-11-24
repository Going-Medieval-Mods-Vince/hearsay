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

    internal sealed class VillageState
    {
        public string Title;                    // how the villagers address the player; null = not asked yet
        public string TitleSetBy;               // who asked first
        public Dictionary<string, List<Message>> Chats = new Dictionary<string, List<Message>>();
        public Dictionary<string, string> Pending = new Dictionary<string, string>();
        public Dictionary<string, int> LastSocialDay = new Dictionary<string, int>();   // settler -> day their Social need was last met by talking   // settler -> question awaiting your answer

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
            s = s ?? new VillageState();
            s.Chats = s.Chats ?? new Dictionary<string, List<Message>>();
            s.Pending = s.Pending ?? new Dictionary<string, string>();
            s.LastSocialDay = s.LastSocialDay ?? new Dictionary<string, int>();
            s.Key = key;
            return s;
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
            if (!Plugin.SocialBoost.Value || h == null || h.HasDisposed || h.HasDied) return;
            string k = SettlerKey(h);
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
