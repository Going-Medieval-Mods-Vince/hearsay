// A window frame for Hearsay (background, title bar, close button, blur), cloned from one of the game's own popups
// with everything inside it removed, ready for our own content.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval.Manager;
using NSMedieval.UI.ScenarioEditor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Hearsay
{
    internal sealed class Popup
    {
        internal GameObject Root, Panel, Blur, OutClick;
        internal TMP_Text Title;
        internal RectTransform Frame;
        internal float Top;          // height of the title bar, content starts below it
        GameObject holder;
        Action onClose;

        internal bool IsOpen => Panel != null && Panel.activeSelf;

        /// <summary>Builds the frame inside an inactive holder; add content to <see cref="Frame"/>, then call <see cref="Finish"/>.</summary>
        internal static Popup Build(string name, float width, float height, Action onClose)
        {
            var tpl = Resources.FindObjectsOfTypeAll<CharacterEditPopupView>()
                .Where(p => p.gameObject.scene.IsValid())
                .OrderBy(p => p.GetType() == typeof(CharacterEditPopupView) ? 0 : 1).FirstOrDefault();
            if (tpl == null) return null;
            var tr = Traverse.Create(tpl);
            var tPanel = tr.Field("panel").GetValue<GameObject>();
            var tBlur = tr.Field("blur").GetValue<GameObject>();
            var tTitle = tr.Field("popupTitle").GetValue<TMP_Text>();
            var tClose = tr.Field("closeButton").GetValue<SoundButton>();
            var tOut = tr.Field("outClickCloseButton").GetValue<SoundButton>();
            if (tPanel == null) return null;

            var p = new Popup { onClose = onClose };
            p.holder = new GameObject("HearsayHolder");
            p.holder.SetActive(false);   // nothing in the clone wakes up until its popup script is gone
            var clone = Object.Instantiate(tpl.gameObject, p.holder.transform);
            clone.name = name;
            foreach (var v in clone.GetComponents<CharacterEditPopupView>()) Object.DestroyImmediate(v);
            foreach (var l in clone.GetComponentsInChildren<I2.Loc.Localize>(true)) Object.DestroyImmediate(l);
            Transform Map(Component c) => c == null ? null : Follow(clone.transform, PathTo(tpl.transform, c.transform));
            p.Panel = Map(tPanel.transform)?.gameObject;
            p.Blur = Map(tBlur != null ? tBlur.transform : null)?.gameObject;
            p.Title = Map(tTitle)?.GetComponent<TMP_Text>();
            var close = Map(tClose)?.GetComponent<SoundButton>();
            p.OutClick = Map(tOut)?.gameObject;
            if (p.Panel == null) { Object.Destroy(p.holder); return null; }

            // keep only the frame: the title bar and close button, hide everything else the popup had
            var keep = new[] { p.Title?.transform, close?.transform }.Where(t => t != null).ToList();
            HideAllBut(p.Panel.transform, keep);
            foreach (var b in new[] { close, p.OutClick != null ? p.OutClick.GetComponent<SoundButton>() : null }.Where(b => b != null))
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => Ui.Guard(p.Close));
            }

            var prt = p.Frame = (RectTransform)p.Panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(width, height);
            p.Top = 64f;
            if (p.Title != null)
            {
                var c = new Vector3[4];
                ((RectTransform)p.Title.transform).GetWorldCorners(c);
                float t = prt.rect.yMax - prt.InverseTransformPoint(c[0]).y + 10f;
                if (t > 24f && t < 130f) p.Top = t;
            }
            p.Root = clone;
            p.parent = tpl.transform.parent;
            p.height = height; p.width = width;
            return p;
        }

        Transform parent;
        float width, height;

        internal void Finish()
        {
            Root.transform.SetParent(parent, false);
            Object.Destroy(holder);
            Root.SetActive(true);
            Panel.SetActive(false);
            if (Blur != null) Blur.SetActive(false);
            if (OutClick != null) OutClick.SetActive(false);
            Ui.FitScreen(Frame, width, height);
            Plugin.Log.LogInfo("Hearsay: window created: " + Root.name);
        }

        /// <summary>Shows the window on top of everything under <paramref name="over"/>'s canvas (or its own).</summary>
        internal void Show(Transform over = null)
        {
            if (over != null && Root.transform.parent != over.parent) Root.transform.SetParent(over.parent, false);
            Root.transform.SetAsLastSibling();
            Panel.SetActive(true);
            if (Blur != null) Blur.SetActive(true);
            if (OutClick != null) OutClick.SetActive(true);
            if (MonoSingleton<GlobalKeybindingManager>.IsInstantiated())
            {
                MonoSingleton<GlobalKeybindingManager>.Instance.UnsubscribeFromEscapeKey(Close, Root);
                MonoSingleton<GlobalKeybindingManager>.Instance.SubscribeToEscapeKey(Close, Root);
            }
        }

        internal void Close()
        {
            Ui.GameInput(true);
            if (Root == null) return;
            if (MonoSingleton<GlobalKeybindingManager>.IsInstantiated())
                MonoSingleton<GlobalKeybindingManager>.Instance.UnsubscribeFromEscapeKey(Close, Root);
            bool was = IsOpen;
            if (Panel != null) Panel.SetActive(false);
            if (Blur != null) Blur.SetActive(false);
            if (OutClick != null) OutClick.SetActive(false);
            if (was) onClose?.Invoke();
        }

        static void HideAllBut(Transform node, List<Transform> keep)
        {
            foreach (Transform child in node)
            {
                if (keep.Contains(child)) continue;
                if (keep.Any(k => k.IsChildOf(child))) HideAllBut(child, keep);
                else child.gameObject.SetActive(false);
            }
        }

        static List<int> PathTo(Transform root, Transform t)
        {
            var p = new List<int>();
            for (var x = t; x != null && x != root; x = x.parent) p.Insert(0, x.GetSiblingIndex());
            return p;
        }

        static Transform Follow(Transform root, List<int> path)
        {
            var x = root;
            foreach (int i in path) { if (i >= x.childCount) return null; x = x.GetChild(i); }
            return x;
        }
    }
}
