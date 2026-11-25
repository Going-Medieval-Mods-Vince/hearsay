// Puts a round "Talk" action next to a settler's order buttons (the row at the bottom of the selection panel:
// draft, flee, ...). It's a copy of one of those buttons, kept outside the game's own button list so the game's
// pooling never reuses or hides it, with the game's conversation icon.
using System;
using System.Linq;
using HarmonyLib;
using NSEipix.View.UI;
using NSMedieval.State;
using NSMedieval.UI;
using NSMedieval.UI.Utils;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hearsay
{
    [HarmonyPatch(typeof(SelectionFooterView), nameof(SelectionFooterView.InitializeFooter))]
    internal static class TalkButton
    {
        const string ObjectName = "Hearsay_TalkAction";
        static HumanoidInstance current;
        static Sprite icon;

        static void Postfix(SelectionFooterView __instance, InfoPanelFooter footerData)
        {
            try
            {
                var group = Traverse.Create(__instance).Field("actionButtonGroup").GetValue<LayoutGroupView>();
                if (group == null) return;
                var h = footerData?.Humanoid;
                bool show = h != null && !h.HasDisposed && !h.HasDied && Facts.Safe(() => h.IsWorker())
                            && Facts.Villagers().Contains(h);
                var existing = group.transform.Find(ObjectName);
                if (existing == null)
                {
                    if (!show) return;
                    var made = Create(__instance, group);
                    if (made == null) return;
                    existing = made.transform;
                }
                current = show ? h : null;
                existing.gameObject.SetActive(show);
                if (show) existing.SetAsLastSibling();
            }
            catch (Exception e) { Plugin.Log.LogError("Hearsay: talk button: " + e); }
        }

        static GameObject Create(SelectionFooterView footer, LayoutGroupView group)
        {
            // copy a live order button if there is one (fully set up), otherwise the group's own prefab
            var live = footer.ActionButtons.FirstOrDefault(b => b != null && b.gameObject.activeSelf);
            var prefab = Facts.Safe(() => group.Prefab);
            GameObject source = live != null ? live.gameObject : prefab != null ? prefab.gameObject : null;
            if (source == null) return null;
            var go = Object.Instantiate(source, group.transform, false);
            go.name = ObjectName;
            foreach (var l in go.GetComponentsInChildren<I2.Loc.Localize>(true)) Object.DestroyImmediate(l);
            // the order tooltip describes game orders by id; ours gets a plain one instead
            foreach (var t in go.GetComponentsInChildren<TooltipViewNew>(true)) Object.DestroyImmediate(t);
            var item = go.GetComponent<LayoutGroupItemView>();
            var img = item != null && item.GroupItems.Count > 0 && item.GroupItems[0] != null ? item.GroupItems[0].GetComponent<Image>() : null;
            var sprite = Icon();
            if (img != null && sprite != null)
            {
                img.sprite = sprite;
                img.preserveAspect = true;
                img.transform.localScale = new Vector3(0.72f, 0.72f, 1f);   // the speech icon is drawn larger than the order icons
            }
            if (item != null) Facts.Safe(() => { item.SetText(1, "Talk"); return true; });

            foreach (var b in go.GetComponentsInChildren<SoundButton>(true))
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.interactable = true;
                b.onClick.AddListener(() =>
                {
                    try { if (current != null) ChatWindow.Open(current); }
                    catch (Exception e) { Plugin.Log.LogError("Hearsay: open chat: " + e); }
                });
            }
            go.SetActive(true);
            Plugin.Log.LogInfo($"Hearsay: Talk action added to the order buttons (icon: {(sprite != null ? sprite.name : "none found")})");
            return go;
        }

        /// <summary>The game's own conversation icon (used for chats in the life log), or another speech-like one.</summary>
        internal static Sprite Icon()
        {
            if (icon != null) return icon;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var want in new[] { "life_event_conversation_icon", "conversation", "social", "chat", "speech", "Outgoing" })
            {
                icon = all.FirstOrDefault(s => s != null && s.name.Equals(want, StringComparison.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(s => s != null && s.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if (icon != null) return icon;
            }
            icon = Facts.Safe(() => AssetUtils.GetSprite("life_event_conversation_icon"));
            return icon;
        }
    }
}
