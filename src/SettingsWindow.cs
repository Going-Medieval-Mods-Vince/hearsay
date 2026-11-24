// Hearsay's settings, inside the game: a "Hearsay" tab in the top bar (after Objectives) opens this window.
// Every change is written straight to BepInEx/config/hearsay.cfg, the same file you can still edit by hand.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NSEipix.View.UI;
using NSMedieval;
using NSMedieval.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static Hearsay.Ui;

namespace Hearsay
{
    internal static class SettingsWindow
    {
        const float Width = 780f, Height = 700f, Pad = 22f, RowHeight = 42f, LabelWidth = 210f;
        static readonly Color Picked = new Color(1f, 0.82f, 0.45f), Normal = Color.white;
        static readonly Color Good = new Color(0.62f, 0.83f, 0.6f), Bad = new Color(0.89f, 0.51f, 0.44f), Dim = new Color(0.75f, 0.73f, 0.68f);

        static Popup popup;
        static TMP_Text style, status, history, length, titleText;
        static TMP_InputField key, model;
        static SoundButton tpl;
        static readonly Dictionary<string, GameObject> modeButtons = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, GameObject> thinkButtons = new Dictionary<string, GameObject>();
        static bool testing;
        static GameObject socialOn, socialOff;

        internal static void Open(Transform over = null)
        {
            if (!Ensure()) { Plugin.Log.LogWarning("Hearsay: no popup frame found to build the settings window from"); return; }
            if (popup.Title != null) popup.Title.text = "HEARSAY";
            popup.Show();
            key.text = Plugin.ApiKey.Value ?? "";
            model.text = Plugin.Model.Value ?? "";
            SetStatus(ModeHelp(), Dim);
            Refresh();
        }

        // ================= building =================
        static bool Ensure()
        {
            if (popup != null && popup.Panel != null) return true;
            var p = Popup.Build("HearsaySettings", Width, Height, SaveFields);
            if (p == null) return false;
            style = p.Title;
            tpl = TextButtonTemplate();

            var list = Rect("HearsaySettingsRows", p.Frame);
            Stretch(list, Pad, Pad, Pad, p.Top + 6f);
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10f; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var row = Row(list, "Replies");
            foreach (var m in new[] { "Auto", "Written", "AI" })
            {
                string mode = m;
                modeButtons[m] = Small(row, m == "Written" ? "Written" : m, 120f, () => { Plugin.Mode.Value = mode; SetStatus(ModeHelp(), Dim); Refresh(); });
            }

            row = Row(list, "Gemini API key");
            key = Field(row, "Paste your key from aistudio.google.com/apikey", 200);
            key.contentType = TMP_InputField.ContentType.Password;
            key.onEndEdit.AddListener(_ => Guard(SaveFields));
            Small(row, "Test", 90f, TestKey);

            row = Row(list, "Model");
            model = Field(row, "gemini-3.8-flash", 80);
            model.onEndEdit.AddListener(_ => Guard(SaveFields));

            row = Row(list, "Thinking");
            foreach (var t in new[] { "off", "low", "medium", "high" })
            {
                string level = t;
                thinkButtons[t] = Small(row, char.ToUpperInvariant(t[0]) + t.Substring(1), 100f, () => { Plugin.Thinking.Value = level; Refresh(); });
            }

            row = Row(list, "Memory (messages)");
            Small(row, "-", 48f, () => { Plugin.HistoryTurns.Value = Math.Max(2, Plugin.HistoryTurns.Value - 2); Refresh(); });
            history = Value(row);
            Small(row, "+", 48f, () => { Plugin.HistoryTurns.Value = Math.Min(60, Plugin.HistoryTurns.Value + 2); Refresh(); });

            row = Row(list, "Longest reply (tokens)");
            Small(row, "-", 48f, () => { Plugin.MaxReplyTokens.Value = Math.Max(200, Plugin.MaxReplyTokens.Value - 100); Refresh(); });
            length = Value(row);
            Small(row, "+", 48f, () => { Plugin.MaxReplyTokens.Value = Math.Min(4000, Plugin.MaxReplyTokens.Value + 100); Refresh(); });

            row = Row(list, "Talking meets Social");
            socialOn = Small(row, "On", 90f, () => { Plugin.SocialBoost.Value = true; Refresh(); });
            socialOff = Small(row, "Off", 90f, () => { Plugin.SocialBoost.Value = false; Refresh(); });
            var hint = Value(row, 260f); hint.text = "once a day per villager"; hint.color = Dim; hint.fontSize = 17f;

            row = Row(list, "This village");
            titleText = Value(row, 300f);
            Small(row, "Ask again", 140f, ForgetTitle);

            row = Row(list, "Written dialogue");
            Small(row, "Reload files", 160f, () =>
            {
                Book.Reset();
                var b = Book.Get();
                SetStatus($"Dialogue reloaded: {b.Topics.Count} topics, {b.Asks.Count} questions. Extra files go in BepInEx/config/Hearsay/dialogue/.", Good);
            });

            status = Text(list, style, 18f);
            status.gameObject.AddComponent<LayoutElement>().minHeight = 70f;
            status.alignment = TextAlignmentOptions.TopLeft;

            p.Finish();
            popup = p;
            return true;
        }

        static RectTransform Row(Transform parent, string label)
        {
            var row = Rect("Row " + label, parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            row.gameObject.AddComponent<LayoutElement>().minHeight = RowHeight;
            var t = Text(row, style, 20f);
            t.text = label; t.alignment = TextAlignmentOptions.MidlineLeft; t.color = Dim;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = LabelWidth;
            return row;
        }

        static GameObject Small(Transform row, string label, float width, Action onClick)
        {
            var b = Button(row, tpl, style, label, onClick);
            var le = b.GetComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true)) { t.enableAutoSizing = true; t.fontSizeMin = 12f; t.fontSizeMax = 19f; }
            return b;
        }

        static TMP_InputField Field(Transform row, string hint, int limit)
        {
            var f = Input(row, style, hint, limit, out _);
            var le = f.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minWidth = 200f;
            return f;
        }

        static TMP_Text Value(Transform row, float width = 80f)
        {
            var t = Text(row, style, 20f);
            t.alignment = TextAlignmentOptions.Center;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            return t;
        }

        // ================= behaviour =================
        static void Refresh()
        {
            foreach (var kv in modeButtons) Mark(kv.Value, string.Equals(kv.Key, Plugin.Mode.Value, StringComparison.OrdinalIgnoreCase));
            foreach (var kv in thinkButtons) Mark(kv.Value, string.Equals(kv.Key, Plugin.Thinking.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
            Mark(socialOn, Plugin.SocialBoost.Value); Mark(socialOff, !Plugin.SocialBoost.Value);
            history.text = Plugin.HistoryTurns.Value.ToString();
            length.text = Plugin.MaxReplyTokens.Value.ToString();
            var v = VillageState.Current;
            titleText.text = v == null ? "No village loaded"
                : string.IsNullOrEmpty(v.Title) ? "Nobody has asked yet" : $"They call you \"{v.Title}\"";
        }

        static void Mark(GameObject b, bool on)
        {
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true)) { t.color = on ? Picked : Normal; t.fontStyle = on ? FontStyles.Bold : FontStyles.Normal; }
            var img = b.GetComponent<Image>();
            if (img != null) img.color = on ? new Color(1f, 0.9f, 0.7f) : Color.white;
        }

        static string ModeHelp()
        {
            bool hasKey = !string.IsNullOrWhiteSpace(Plugin.ApiKey.Value);
            switch (Plugin.Mode.Value)
            {
                case "Written": return "Written: pick what to say from a list; villagers answer with written lines from their live situation. No key needed.";
                case "AI": return hasKey ? "AI: type freely, Gemini answers. Topics show as quick prompts." : "AI: needs a Gemini key (free at aistudio.google.com/apikey).";
                default: return hasKey ? "Auto: a key is set, so Gemini answers. Remove the key to use written dialogue." : "Auto: no key, so villagers use written dialogue. Add a key for free-typed AI chat.";
            }
        }

        static void SaveFields()
        {
            if (key == null) return;
            string k = key.text?.Trim() ?? "", m = model.text?.Trim() ?? "";
            if (k != (Plugin.ApiKey.Value ?? "")) Plugin.ApiKey.Value = k;
            if (m.Length > 0 && m != Plugin.Model.Value) Plugin.Model.Value = m;
            if (popup != null && popup.IsOpen) { SetStatus(ModeHelp(), Dim); Refresh(); }
        }

        static void TestKey()
        {
            SaveFields();
            if (testing) return;
            if (string.IsNullOrWhiteSpace(Plugin.ApiKey.Value)) { SetStatus("Paste a key first.", Bad); return; }
            testing = true;
            SetStatus($"Asking {Plugin.Model.Value}…", Dim);
            var turns = new List<Gemini.Turn> { new Gemini.Turn { FromPlayer = true, Text = "Are you there? Answer in three words." } };
            Gemini.Ask("You are a villager in a medieval game, answering a test call.", turns, (reply, error) =>
            {
                testing = false;
                if (status == null) return;
                if (error != null) SetStatus("That didn't work: " + error, Bad);
                else SetStatus($"The key works. {Plugin.Model.Value} answered: \"{reply}\"", Good);
            });
        }

        static void ForgetTitle()
        {
            var v = VillageState.Current;
            if (v == null) { SetStatus("Load a village first.", Bad); return; }
            v.Title = null; v.TitleSetBy = null;
            foreach (var k in v.Pending.Where(p => p.Value == "title" || p.Value == "retitle").Select(p => p.Key).ToList()) v.Pending.Remove(k);
            v.Save();
            SetStatus("The next villager you talk to will ask what to call you.", Good);
            Refresh();
        }

        static void SetStatus(string text, Color color)
        {
            if (status == null) return;
            status.text = (text ?? "").Replace("<", "‹").Replace(">", "›");
            status.color = color;
        }
    }

    /// <summary>A "Hearsay" tab in the top bar, after Objectives: a copy of the game's own Objectives button.</summary>
    [HarmonyPatch(typeof(TopLeftPanelView), "Start")]
    internal static class TopBarButton
    {
        const string ObjectName = "Hearsay_TopBarButton";

        static void Postfix(TopLeftPanelView __instance)
        {
            try
            {
                var objectives = Traverse.Create(__instance).Field("objectivesButton").GetValue<SoundButton>();
                if (objectives == null || objectives.transform.parent.Find(ObjectName) != null) return;
                var go = Object.Instantiate(objectives.gameObject, objectives.transform.parent, false);
                go.name = ObjectName;
                go.transform.SetSiblingIndex(objectives.transform.GetSiblingIndex() + 1);
                foreach (var l in go.GetComponentsInChildren<I2.Loc.Localize>(true)) Object.DestroyImmediate(l);
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = "HEARSAY";
                foreach (var tip in go.GetComponentsInChildren<LocalizedTextTooltipView>(true))
                {
                    tip.TextKeys = new List<string>();
                    tip.SetNonLocalizedText("Hearsay settings: replies, Gemini key, model, title");
                }
                var b = go.GetComponent<SoundButton>();
                b.onClick = new Button.ButtonClickedEvent();
                b.interactable = true;
                b.onClick.AddListener(() => Guard(() => SettingsWindow.Open(null)));
                go.SetActive(true);
                if (objectives.transform.parent.GetComponent<LayoutGroup>() == null)
                {
                    // no layout: sit one button-width to the right of Objectives
                    var src = (RectTransform)objectives.transform; var rt = (RectTransform)go.transform;
                    rt.anchoredPosition = src.anchoredPosition + new Vector2(src.rect.width + 4f, 0f);
                }
                Plugin.Log.LogInfo("Hearsay: settings tab added to the top bar");
            }
            catch (Exception e) { Plugin.Log.LogError("Hearsay: top bar button: " + e); }
        }
    }
}
