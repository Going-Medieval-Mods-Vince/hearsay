// Small UI building blocks shared by Hearsay's windows, made from Unity parts in the game's look.
using System;
using System.Linq;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval.Manager;
using NSMedieval.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hearsay
{
    internal static class Ui
    {
        static bool gameInputOff;

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
        }

        internal static TextMeshProUGUI Text(Transform parent, TMP_Text style, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; }
            t.fontSize = size; t.color = new Color(0.92f, 0.9f, 0.86f);
            t.richText = true; t.enableWordWrapping = true; t.raycastTarget = false;
            return t;
        }

        /// <summary>Any of the game's own buttons that has a text label: its look is reused for ours.</summary>
        internal static SoundButton TextButtonTemplate() =>
            Resources.FindObjectsOfTypeAll<SoundButton>()
                .Where(b => b.gameObject.scene.IsValid() && b.GetComponent<Image>() != null && b.GetComponentInChildren<TMP_Text>(true) != null
                            && !b.gameObject.name.StartsWith("Hearsay"))
                .OrderByDescending(b => ((RectTransform)b.transform).rect.width).FirstOrDefault();

        internal static GameObject Button(Transform parent, SoundButton tpl, TMP_Text style, string label, Action onClick)
        {
            GameObject go;
            if (tpl != null)
            {
                go = Object.Instantiate(tpl.gameObject, parent, false);
                foreach (var l in go.GetComponentsInChildren<I2.Loc.Localize>(true)) Object.DestroyImmediate(l);
                foreach (var tip in go.GetComponentsInChildren<LocalizedTextTooltipView>(true)) Object.DestroyImmediate(tip);
                var texts = go.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts) t.text = "";
                if (texts.Length > 0) { texts[0].text = label; texts[0].gameObject.SetActive(true); }
            }
            else
            {
                go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(SoundButton));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.32f, 0.42f);
                var t = Text(go.transform, style, 20f);
                t.text = label; t.alignment = TextAlignmentOptions.Center;
                Stretch((RectTransform)t.transform, 0, 0, 0, 0);
            }
            go.name = "Hearsay_" + label;
            var btn = go.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => Guard(onClick));
            btn.interactable = true;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = false; le.minWidth = le.preferredWidth = 140f; le.flexibleWidth = 0f;
            go.SetActive(true);
            return go;
        }

        /// <summary>A plain text field made from Unity's parts, in the game's font. Game hotkeys are off while it has focus.</summary>
        internal static TMP_InputField Input(Transform parent, TMP_Text style, string hint, int limit, out TMP_Text placeholder)
        {
            var go = new GameObject("HearsayInput", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
            var area = Rect("TextArea", go.transform);
            Stretch(area, 12f, 6f, 12f, 6f);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, style, 20f);
            ph.text = hint;
            placeholder = ph; ph.fontStyle = FontStyles.Italic; ph.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
            Stretch((RectTransform)ph.transform, 0, 0, 0, 0);
            var txt = Text(area, style, 20f);
            txt.raycastTarget = true;
            Stretch((RectTransform)txt.transform, 0, 0, 0, 0);
            ph.alignment = txt.alignment = TextAlignmentOptions.MidlineLeft;
            var f = go.AddComponent<TMP_InputField>();
            f.textViewport = area; f.textComponent = txt; f.placeholder = ph;
            if (style != null) f.fontAsset = style.font;
            f.pointSize = 20f;
            f.characterLimit = limit;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.caretColor = Color.white; f.customCaretColor = true;
            f.onSelect.AddListener(_ => GameInput(false));
            f.onDeselect.AddListener(_ => GameInput(true));
            go.SetActive(true);
            return f;
        }

        internal static void FitScreen(RectTransform prt, float width, float height)
        {
            var canvas = prt.GetComponentInParent<Canvas>(true)?.rootCanvas;
            if (canvas == null) return;
            var crt = (RectTransform)canvas.transform;
            float s = Mathf.Min(1f, crt.rect.height * 0.92f / height, crt.rect.width * 0.92f / width);
            prt.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>While typing, the game's hotkeys are switched off so letters don't trigger commands, the same way the
        /// game's own text fields do it (ProductionLayoutItemView, RallyPointExtraPanel).</summary>
        internal static void GameInput(bool on)
        {
            if (!MonoSingleton<InputManager>.IsInstantiated()) { gameInputOff = false; return; }
            var im = MonoSingleton<InputManager>.Instance;
            if (!on && !gameInputOff && im.InputEnabled) { im.SetInputEnabled(false); gameInputOff = true; }
            else if (on && gameInputOff) { im.SetInputEnabled(true); gameInputOff = false; }
        }

        /// <summary>Unhandled exceptions in UI callbacks are fatal in this game, so every callback is guarded.</summary>
        internal static void Guard(Action a)
        {
            try { a(); } catch (Exception e) { Plugin.Log.LogError("Hearsay: " + e); }
        }
    }
}
