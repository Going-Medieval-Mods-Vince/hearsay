// Conversations without an AI: you pick what to say from a list, the villager answers with written lines chosen
// from their live situation, and now and then asks you something back, which you answer by picking too.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NSMedieval.State;
using UnityEngine;

namespace Hearsay
{
    internal static class Scripted
    {
        internal struct Item
        {
            public string Label; public Action Act;
            public Item(string label, Action act) { Label = label; Act = act; }
        }

        internal static event Action Changed;
        static readonly HashSet<string> busy = new HashSet<string>();
        static HumanoidInstance aboutMenuFor;
        internal static HumanoidInstance Naming { get; private set; }   // waiting for you to type a title

        internal static bool IsBusy(HumanoidInstance h) => busy.Contains(VillageState.SettlerKey(h));

        static Scene SceneOf(HumanoidInstance h, VillageState v) => Scene.Of(h, v, Book.Get());

        static HashSet<string> Recent(List<Message> chat) =>
            new HashSet<string>(chat.Skip(Math.Max(0, chat.Count - 40)).Where(m => !m.FromPlayer && m.Line != null)
                .SelectMany(m => m.Line.Split(',')));

        // ================= opening =================
        /// <summary>When the window opens: the first villager you ever talk to asks how to address you; otherwise
        /// a greeting if you haven't spoken today.</summary>
        internal static void Opened(HumanoidInstance h, Want want = null)
        {
            var v = VillageState.Current;
            if (v == null) return;
            aboutMenuFor = null; Naming = null;
            string key = VillageState.SettlerKey(h);
            var chat = v.ChatWith(h);
            if (want != null && !v.Pending.ContainsKey(key))
            {
                // they asked for you: their opening, then perhaps a question
                var s = SceneOf(h, v);
                var (text, k) = Book.Pick(want.Say, s, Recent(chat));
                if (text != null) { Add(v, h, false, text, k, null); v.Talked(h); }
                if (want.Ask != null) Say(h, v, s, want.Ask, text);
                v.Save();
                if (text != null || want.Ask != null) return;
            }
            if (v.Pending.ContainsKey(key)) return;
            if (string.IsNullOrEmpty(v.Title) && !v.Pending.Values.Contains("title"))
            {
                Say(h, v, null, "title", null);
                return;
            }
            if (chat.Count == 0 || chat[chat.Count - 1].Day < Facts.Today())
            {
                var s = SceneOf(h, v);
                var (text, k) = Book.Pick(Book.Get().Greet, s, Recent(chat));
                if (text != null) { Add(v, h, false, text, k, null); v.Save(); }
            }
        }

        // ================= menu =================
        internal static List<Item> Menu(HumanoidInstance h)
        {
            var items = new List<Item>();
            var v = VillageState.Current;
            if (v == null || h == null) return items;
            var book = Book.Get();
            var s = SceneOf(h, v);
            string key = VillageState.SettlerKey(h);

            if (v.Pending.TryGetValue(key, out var askId) && book.AskById(askId) is Ask ask)
            {
                foreach (var c in ask.Options.Where(c => s.All(c.When)))
                {
                    string label = s.Fill(c.Label ?? "");
                    if (!string.IsNullOrEmpty(label)) items.Add(new Item(label, () => Answer(h, ask, c, label)));
                }
                if (items.Count > 0) return items;
                v.Pending.Remove(key);   // nothing to answer with: drop the question
            }

            if (aboutMenuFor == h)
            {
                var about = book.Topics.FirstOrDefault(t => t.About);
                foreach (var o in Facts.Villagers().Where(o => o != h))
                {
                    var who = o;
                    items.Add(new Item(Facts.FirstName(o), () => { aboutMenuFor = null; Choose(h, about, who); }));
                }
                items.Add(new Item("Never mind", () => { aboutMenuFor = null; Changed?.Invoke(); }));
                return items;
            }

            foreach (var t in book.Topics.Where(t => s.All(t.When)).OrderBy(t => t.Order))
            {
                var topic = t;
                if (t.About && s.Pop <= 1) continue;
                string label = s.Fill(t.Label ?? "");
                if (string.IsNullOrEmpty(label)) continue;
                items.Add(new Item(label, () =>
                {
                    if (topic.About) { aboutMenuFor = h; Changed?.Invoke(); }
                    else Choose(h, topic, null);
                }));
            }
            return items;
        }

        // ================= saying things =================
        static void Choose(HumanoidInstance h, Topic t, HumanoidInstance other)
        {
            var v = VillageState.Current;
            if (v == null || t == null || IsBusy(h)) return;
            var s = SceneOf(h, v).About(other, v);
            string label = t.About && other != null ? $"What do you make of {Facts.FirstName(other)}?" : s.Fill(t.Label) ?? t.Label;
            Add(v, h, true, label, null, null);
            var chat = v.ChatWith(h);
            var (text, key) = Book.Compose(t.Parts, s, Recent(chat));
            string ask = t.Ask;
            if (ask == null && !t.Close && !t.NoFollowUp) ask = FollowUp(h, v, s, chat);
            Reply(h, v, s, text ?? (ask != null ? null : "..."), key, ask, t.Close);
        }

        static void Answer(HumanoidInstance h, Ask ask, Choice c, string label)
        {
            var v = VillageState.Current;
            if (v == null || IsBusy(h)) return;
            if (c.Name) { Naming = h; Changed?.Invoke(); return; }   // the window shows a text field
            v.Pending.Remove(VillageState.SettlerKey(h));
            if (!string.IsNullOrEmpty(c.Title)) SetTitle(v, h, c.Title);
            Add(v, h, true, label, null, null);
            var s = SceneOf(h, v);
            var (text, key) = Book.Pick(c.Reply, s, Recent(v.ChatWith(h)));
            Reply(h, v, s, text, key, c.Ask, false);
        }

        /// <summary>The player typed how they want to be addressed.</summary>
        internal static void Named(HumanoidInstance h, string typed)
        {
            var v = VillageState.Current;
            typed = typed?.Trim().Trim('"', '\'', '.', '!', ' ');
            if (v == null || h == null || string.IsNullOrEmpty(typed)) return;
            if (typed.Length > 30) typed = typed.Substring(0, 30);
            string key = VillageState.SettlerKey(h);
            v.Pending.TryGetValue(key, out var askId);
            var ask = Book.Get().AskById(askId);
            var c = ask?.Options.FirstOrDefault(o => o.Name);
            Naming = null;
            v.Pending.Remove(key);
            SetTitle(v, h, typed);
            Add(v, h, true, $"Call me {typed}.", null, null);
            var s = SceneOf(h, v);
            var (text, k) = Book.Pick(c?.Reply, s, Recent(v.ChatWith(h)));
            Reply(h, v, s, text ?? $"{typed} it is.", k, c?.Ask, false);
        }

        internal static void CancelNaming() { Naming = null; Changed?.Invoke(); }

        static void SetTitle(VillageState v, HumanoidInstance h, string title)
        {
            if (string.IsNullOrEmpty(v.Title)) v.TitleSetBy = Facts.FullName(h);
            v.Title = title;
            Plugin.Log.LogInfo($"Hearsay: the village now calls you \"{title}\"");
        }

        /// <summary>A villager's own question after a reply: one whose conditions hold, not asked lately, by chance.</summary>
        static string FollowUp(HumanoidInstance h, VillageState v, Scene s, List<Message> chat)
        {
            int today = Facts.Today();
            var candidates = Book.Get().Asks.Where(a => !a.Manual && s.All(a.When)
                    && !chat.Any(m => m.Ask == a.Id && today - m.Day < Math.Max(0, a.Cooldown)))
                .ToList();
            // the most specific question first; each gets its chance
            foreach (var a in candidates.OrderByDescending(a => a.When.Count).ThenBy(_ => UnityEngine.Random.value))
                if (UnityEngine.Random.value * 100f < a.Chance) return a.Id;
            return null;
        }

        /// <summary>The villager answers after a short pause, and may ask something.</summary>
        static void Reply(HumanoidInstance h, VillageState v, Scene s, string text, string key, string ask, bool close)
        {
            string k = VillageState.SettlerKey(h);
            busy.Add(k);
            v.Save();
            Changed?.Invoke();
            float delay = Mathf.Clamp(0.4f + (text?.Length ?? 0) * 0.008f, 0.5f, 1.6f);
            Scheduler.Start(After(delay, () =>
            {
                busy.Remove(k);
                if (!string.IsNullOrWhiteSpace(text)) { Add(v, h, false, text, key, null); v.Talked(h); }
                if (ask != null) Say(h, v, s, ask, text);
                v.Save();
                Changed?.Invoke();
                if (close) Scheduler.Start(After(1.4f, () => { if (ChatWindow.Showing == h) ChatWindow.Close(); }), null);
            }), e => { busy.Remove(k); Plugin.Log.LogError("Hearsay: " + e); });
        }

        /// <summary>The villager puts a question to you; you answer from its options.</summary>
        static void Say(HumanoidInstance h, VillageState v, Scene s, string askId, string after)
        {
            var ask = Book.Get().AskById(askId);
            if (ask == null) { Plugin.Log.LogWarning("Hearsay: no question called " + askId); return; }
            s = s ?? SceneOf(h, v);
            var chat = v.ChatWith(h);
            var (text, key) = Book.Pick(ask.Say, s, Recent(chat));
            if (text == null) return;
            // asked right after their reply: part of the same message
            var last = chat.Count > 0 ? chat[chat.Count - 1] : null;
            if (after != null && last != null && !last.FromPlayer && last.Ask == null)
            {
                last.Text += " " + text; last.Line += "," + key; last.Ask = askId;
            }
            else Add(v, h, false, text, key, askId);
            v.Pending[VillageState.SettlerKey(h)] = askId;
            v.Save();
            Changed?.Invoke();
        }

        static void Add(VillageState v, HumanoidInstance h, bool fromPlayer, string text, string key, string ask) =>
            v.ChatWith(h).Add(new Message { FromPlayer = fromPlayer, Text = text, When = Facts.Now(), Day = Facts.Today(), Line = key, Ask = ask });

        static IEnumerator After(float seconds, Action a)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
            a();
        }
    }
}
