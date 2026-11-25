// Hearsay's own mood effects, shown in the villager's mood list like the game's: being heard quickly when they asked
// for a word, or having a worry ignored. They're added to the game's effect list the moment the game first looks
// anything up there (and again if it ever reloads that list), built the same way the game reads its own from JSON.
using System;
using HarmonyLib;
using NSEipix.Repository;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using UnityEngine;

namespace Hearsay
{
    internal static class Moods
    {
        internal const string Heard = "Hearsay_Heard", Ignored = "Hearsay_Ignored";
        const string Category = "hearsay_heard";   // one replaces the other

        static readonly (string id, string name, string info, int mood, int hours)[] All =
        {
            (Heard, "Heard out", "Asked the head of the village for a word and they came quickly.", 3, 12),
            (Ignored, "Brushed off", "Asked the head of the village for a word about something that worried them, and they never came.", -4, 24),
        };

        static bool adding;

        static void Ensure(EffectorRepository repo)
        {
            if (adding || repo == null) return;
            adding = true;
            try
            {
                var t = Traverse.Create(repo);
                foreach (var m in All)
                {
                    if (t.Method("ContainsKey", m.id).GetValue<bool>()) continue;
                    var e = Make(m);
                    if (e != null) t.Method("Add", e).GetValue();
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Hearsay: could not add mood effects: " + e); }
            finally { adding = false; }
        }

        static StatEffector Make((string id, string name, string info, int mood, int hours) m)
        {
            string json = "{\"id\":\"" + m.id + "\"," +
                          "\"locKeys\":[{\"name\":\"" + m.name + "\",\"info\":\"" + m.info + "\"}]," +
                          "\"category\":\"" + Category + "\",\"uiGroup\":" + (int)NSMedieval.UI.EffectorUiGroup.Mood + "," +
                          "\"duration\":" + m.hours + ",\"maxStack\":1,\"stackMultiplier\":0," +
                          "\"effects\":[{\"type\":" + (int)EffectorType.MoodModify + ",\"parameters\":[{\"key\":\"BaseValue\",\"value\":\"" + m.mood + "\"}]}]}";
            return JsonUtility.FromJson<StatEffector>(json);
        }

        /// <summary>Starts one of our moods on a villager; false if the game refused.</summary>
        internal static bool Give(HumanoidInstance h, string id)
        {
            if (h == null || h.HasDisposed || h.HasDied || !Plugin.RequestMoods.Value) return false;
            return Facts.Safe(() => h.Stats.StartEffector(id));
        }

        [HarmonyPatch(typeof(EffectorRepository), nameof(EffectorRepository.TryGetValue))]
        static class OnTryGet
        {
            static void Prefix(EffectorRepository __instance, string id)
            {
                if (id != null && id.StartsWith("Hearsay_")) Ensure(__instance);
            }
        }

        [HarmonyPatch(typeof(EffectorRepository), nameof(EffectorRepository.GetByID))]
        static class OnGet
        {
            static void Prefix(EffectorRepository __instance, string id)
            {
                if (id != null && id.StartsWith("Hearsay_")) Ensure(__instance);
            }
        }
    }
}
