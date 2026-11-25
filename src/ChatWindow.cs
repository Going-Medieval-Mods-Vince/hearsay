// The chat window. The frame (background, title bar, close button, blur) is a clone of one of the game's own
// popups; everything inside it (the scrolling conversation, the text field, the buttons row) is built here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval.Manager;
using NSMedieval.State;
using NSMedieval.UI;
using NSMedieval.UI.ScenarioEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static Hearsay.Ui;

namespace Hearsay
{
    internal static class ChatWindow
    {
        const float Width = 860f, Height = 680f, FooterHeight = 48f, Pad = 18f;
        const int Columns = 2, MaxRows = 7;
        const float CellHeight = 34f, Gap = 4f, ChoicesHeader = 30f;
        static readonly Color ChoiceText = new Color(0.9f, 0.87f, 0.8f), Accent = new Color(0.79f, 0.64f, 0.35f);
        static TMP_Text choicesHeader;
        static readonly Color PlayerColor = new Color(0.91f, 0.76f, 0.44f), VillagerColor = new Color(0.62f, 0.83f, 0.6f);

        static Popup popup;
        static TMP_Text title, transcript;
        static ScrollRect scroll;
        static TMP_InputField input;
        static GameObject send, retry;
        static RectTransform transcriptArea, optionsArea, inputRow;
        static TMP_Text placeholder;
        static readonly List<GameObject> optionButtons = new List<GameObject>();
        static readonly List<Action> optionActions = new List<Action>();
        static SoundButton buttonTemplate;
        static TMP_Text fontStyle;
        static int page;
        static HumanoidInstance who;
        static bool hooked;

        static bool IsOpen => popup != null && popup.IsOpen;
        internal static HumanoidInstance Showing => IsOpen ? who : null;

        internal static void Open(HumanoidInstance h)
        {
            if (!Ensure()) { Plugin.Log.LogWarning("Hearsay: no popup frame found to build the chat window from"); return; }
            if (!hooked) { Talk.Changed += () => Guard(Refresh); Scripted.Changed += () => Guard(Refresh); hooked = true; }
            who = h; page = 0;
            popup.Show();
            if (title != null) title.text = Facts.FullName(h).ToUpperInvariant();
            input.text = "";
            var (want, open) = Wants.Take(h);   // they asked for a word: they speak first
            if (!Plugin.UseAI) Scripted.Opened(h, want);
            else if (want != null) Talk.Opener(h, want, open);
            Refresh();
            if (input.gameObject.activeInHierarchy) input.ActivateInputField();
        }

        internal static void Close() => popup?.Close();

        // ================= building =================
        static bool Ensure()
        {
            if (popup != null && popup.Panel != null) return true;
            // a new game or a loaded save rebuilds the game's interface: anything we made for the old one is gone
            optionButtons.Clear(); optionActions.Clear(); choicesHeader = null;
            var p = Popup.Build("HearsayChat", Width, Height, null);
            if (p == null) return false;
            title = p.Title;
            fontStyle = title;
            BuildTranscript(p.Frame, p.Top, title);
            BuildFooter(p.Frame, title);
            BuildOptions(p.Frame);
            p.Finish();
            popup = p;
            return true;
        }

        /// <summary>A scroll view holding one text block that grows with the conversation.</summary>
        static void BuildTranscript(RectTransform frame, float top, TMP_Text style)
        {
            var area = Rect("HearsayTranscript", frame);
            transcriptArea = area;
            Stretch(area, Pad, Pad + FooterHeight + 10f, Pad, top);
            area.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;

            var viewport = Rect("Viewport", area);
            Stretch(viewport, 12f, 8f, 12f, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            scroll.viewport = viewport; scroll.content = content;
            transcript = Text(content, style, 22f);
        }

        static void BuildFooter(RectTransform frame, TMP_Text style)
        {
            var row = Rect("HearsayFooter", frame);
            inputRow = row;
            row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(1f, 0f); row.pivot = new Vector2(0.5f, 0f);
            row.offsetMin = new Vector2(Pad, Pad); row.offsetMax = new Vector2(-Pad, Pad + FooterHeight);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            input = MakeInput(row, style);
            var le = input.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minWidth = 200f;
            var tpl = buttonTemplate = TextButtonTemplate();
            send = Button(row, tpl, style, "Send", SendTyped);
            retry = Button(row, tpl, style, "Try again", () => { if (who != null) Talk.Retry(who); });
        }

        static TMP_InputField MakeInput(Transform parent, TMP_Text style)
        {
            var f = Input(parent, style, "Say something, then press Enter…", 400, out placeholder);
            f.lineType = TMP_InputField.LineType.MultiLineSubmit;   // wraps long lines, Enter sends
            f.onSubmit.AddListener(_ => Guard(SendTyped));
            return f;
        }

        /// <summary>The things you can say (or answer), as a grid of the game's buttons.</summary>
        static void BuildOptions(RectTransform frame)
        {
            optionsArea = Rect("HearsayOptions", frame);
            optionsArea.anchorMin = new Vector2(0f, 0f); optionsArea.anchorMax = new Vector2(1f, 0f); optionsArea.pivot = new Vector2(0.5f, 0f);
            var g = optionsArea.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2((Width - 2 * Pad - (Columns - 1) * Gap) / Columns, CellHeight);
            g.spacing = new Vector2(Gap, Gap);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = Columns;
            g.childAlignment = TextAnchor.UpperLeft;
            choicesHeader = Text(frame, fontStyle, 17f);
            var hr = (RectTransform)choicesHeader.transform;
            hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(0.5f, 0f);
            choicesHeader.alignment = TextAlignmentOptions.BottomLeft;
            choicesHeader.fontStyle = FontStyles.Italic;
            choicesHeader.color = new Color(0.7f, 0.68f, 0.62f);
        }

        /// <summary>One dialogue choice: a text row that lights up under the mouse, like a choice in a story game.</summary>
        static GameObject OptionButton(int i)
        {
            while (optionButtons.Count <= i)
            {
                int n = optionButtons.Count;
                optionActions.Add(null);
                var go = new GameObject("Hearsay_Choice" + n, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(optionsArea, false);
                var img = go.GetComponent<Image>();
                img.color = Color.white;
                var btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                var cb = btn.colors;
                cb.normalColor = new Color(1f, 1f, 1f, 0.035f);
                cb.highlightedColor = new Color(Accent.r, Accent.g, Accent.b, 0.22f);
                cb.pressedColor = new Color(Accent.r, Accent.g, Accent.b, 0.38f);
                cb.selectedColor = cb.normalColor;
                cb.disabledColor = new Color(1f, 1f, 1f, 0.015f);
                cb.fadeDuration = 0.08f;
                btn.colors = cb;
                btn.onClick.AddListener(() => Guard(() => optionActions[n]?.Invoke()));
                // a thin gold mark on the left, like a quill margin
                var bar = Rect("Mark", go.transform);
                bar.anchorMin = new Vector2(0f, 0.18f); bar.anchorMax = new Vector2(0f, 0.82f); bar.pivot = new Vector2(0f, 0.5f);
                bar.sizeDelta = new Vector2(3f, 0f); bar.anchoredPosition = new Vector2(6f, 0f);
                var barImg = bar.gameObject.AddComponent<Image>(); barImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.7f); barImg.raycastTarget = false;
                var t = Text(go.transform, fontStyle, 19f);
                Stretch((RectTransform)t.transform, 18f, 1f, 8f, 1f);
                t.alignment = TextAlignmentOptions.MidlineLeft; t.color = ChoiceText;
                t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 19f;
                t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
                optionButtons.Add(go);
            }
            return optionButtons[i];
        }

        /// <summary>Lays out the options grid, the text row (when shown) and the conversation above them.</summary>
        static void ShowOptions(List<Scripted.Item> items, bool showInput, bool enabled)
        {
            int perPage = Columns * MaxRows;
            var shown = items;
            if (items.Count > perPage)
            {
                int pages = (items.Count + perPage - 2) / (perPage - 1);
                page %= pages;
                shown = items.Skip(page * (perPage - 1)).Take(perPage - 1).ToList();
                shown.Add(new Scripted.Item("More…", () => { page++; Refresh(); }));
            }
            for (int i = 0; i < Math.Max(shown.Count, optionButtons.Count); i++)
            {
                if (i >= shown.Count) { optionButtons[i].SetActive(false); continue; }
                var b = OptionButton(i);
                var texts = b.GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length > 0) texts[0].text = $"<color=#{ColorUtility.ToHtmlStringRGB(Accent)}>{i + 1}.</color>  {Esc(shown[i].Label)}";
                optionActions[i] = shown[i].Act;
                b.GetComponent<Button>().interactable = enabled;
                b.SetActive(true);
            }
            int rows = Math.Min(MaxRows, (shown.Count + Columns - 1) / Columns);
            float optH = rows == 0 ? 0 : rows * CellHeight + (rows - 1) * Gap;
            inputRow.gameObject.SetActive(showInput);
            float y = Pad + (showInput ? FooterHeight + 10f : 0f);
            optionsArea.offsetMin = new Vector2(Pad, y); optionsArea.offsetMax = new Vector2(-Pad, y + optH);
            bool header = optH > 0 && choicesHeader != null;
            if (choicesHeader != null)
            {
                choicesHeader.gameObject.SetActive(header);
                var hr = (RectTransform)choicesHeader.transform;
                hr.offsetMin = new Vector2(Pad + 4f, y + optH + 2f); hr.offsetMax = new Vector2(-Pad, y + optH + ChoicesHeader);
            }
            float bottom = y + optH + (header ? ChoicesHeader + 4f : optH > 0 ? 10f : 0f);
            transcriptArea.offsetMin = new Vector2(Pad, bottom);
        }

        // ================= behaviour =================
        static void SendTyped()
        {
            if (who == null || input == null) return;
            string text = input.text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (Scripted.Naming == who)
            {
                input.text = "";
                GameInput(true);
                Scripted.Named(who, text);
                return;
            }
            if (!Plugin.UseAI || Talk.IsWaiting(who)) return;
            input.text = "";
            Talk.Send(who, text);
            input.ActivateInputField();
        }

        /// <summary>What you can say right now: picked from a list without an AI; with one, the same topics are
        /// quick prompts next to the text field.</summary>
        static List<Scripted.Item> Options()
        {
            if (Scripted.Naming == who)
                return new List<Scripted.Item> { new Scripted.Item("Never mind", Scripted.CancelNaming) };
            if (!Plugin.UseAI) return Scripted.Menu(who);
            var v = VillageState.Current;
            if (v != null && string.IsNullOrEmpty(v.Title))
                // they're about to ask (or have asked) what to call you: offer the usual answers
                return new[] { "Lord", "Lady", "Chief", "Captain", "Just call me friend" }
                    .Select(t => new Scripted.Item(t.StartsWith("Just") ? t + "." : "Call me " + t + ".", () =>
                    {
                        if (!Talk.IsWaiting(who)) Talk.Send(who, t.StartsWith("Just") ? t + "." : "Call me " + t + ".");
                    })).ToList();
            var s = Scene.Of(who, v, Book.Get(), false);
            return Book.Get().Topics.Where(t => !t.About && !t.Close && s.All(t.When)).OrderBy(t => t.Order).Take(Columns * 2)
                .Select(t => s.Fill(t.Label)).Where(l => !string.IsNullOrEmpty(l))
                .Select(l => new Scripted.Item(l, () => { if (!Talk.IsWaiting(who)) Talk.Send(who, l); })).ToList();
        }

        static void Refresh()
        {
            if (!IsOpen || who == null || transcript == null) return;
            var state = VillageState.Current;
            var chat = state?.ChatWith(who) ?? new List<Message>();
            string name = Facts.FirstName(who);
            var sb = new StringBuilder();
            if (chat.Count == 0) sb.AppendLine($"<i><color=#a8a8a8>{name} looks up from what they were doing.</color></i>\n");
            const int Shown = 120;   // the window draws the latest messages only, so long chats stay quick
            if (chat.Count > Shown) sb.AppendLine($"<i><color=#8a8a8a>… {chat.Count - Shown} earlier messages not shown</color></i>\n");
            foreach (var m in chat.Skip(Math.Max(0, chat.Count - Shown)))
            {
                string who_ = m.FromPlayer ? (string.IsNullOrEmpty(state?.Title) ? "You" : char.ToUpperInvariant(state.Title[0]) + state.Title.Substring(1)) : name;
                var col = m.FromPlayer ? PlayerColor : VillagerColor;
                string stamp = $"<size=68%><color=#7f7a70>{Esc(m.When)}</color></size>";
                string head = m.FromPlayer
                    ? $"{stamp}  <b><color=#{ColorUtility.ToHtmlStringRGB(col)}>{Esc(who_)}</color></b>"
                    : $"<b><color=#{ColorUtility.ToHtmlStringRGB(col)}>{Esc(who_)}</color></b>  {stamp}";
                // the villager speaks from the left, you from the right, each leaving room on the other side
                sb.Append(m.FromPlayer ? "<align=right><margin-left=18%>" : "<align=left><margin-right=12%>");
                sb.Append(head).Append("\n");
                sb.Append(m.FromPlayer ? "<color=#e9dfc8>" : "<color=#ece8df>").Append(Esc(m.Text)).Append("</color>");
                sb.Append("</margin></align>\n<size=45%> </size>\n");
            }
            bool ai = Plugin.UseAI;
            bool waiting = ai ? Talk.IsWaiting(who) : Scripted.IsBusy(who);
            bool failed = ai && !waiting && Talk.LastError != null && chat.Count > 0 && chat[chat.Count - 1].FromPlayer;
            if (waiting) sb.Append($"<i><color=#a8a8a8>{name} thinks it over…</color></i>");
            if (failed) sb.Append($"<color=#e3826f>No answer: {Esc(Talk.LastError)}</color>");
            transcript.text = sb.ToString();
            bool naming = Scripted.Naming == who;
            bool showInput = ai || naming;
            bool wasShown = inputRow.gameObject.activeSelf;
            placeholder.text = naming ? "How should the village address you? Type it, then press Enter…" : "Say something, then press Enter…";
            input.characterLimit = naming ? 30 : 400;
            send.GetComponent<Button>().interactable = !waiting;
            retry.SetActive(failed);
            if (choicesHeader != null)
            {
                var v = VillageState.Current;
                bool asked = v != null && v.Pending.ContainsKey(VillageState.SettlerKey(who));
                choicesHeader.text = naming ? "Or…" : ai ? "Or pick something to say:" : asked ? "Your answer:" : "What do you say?";
            }
            ShowOptions(Options(), showInput, !waiting);
            if (showInput && !wasShown) input.ActivateInputField();
            if (!showInput) GameInput(true);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

    }
}
