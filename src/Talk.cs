// The conversation logic: builds the prompt, sends it, reads the reply's tags.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NSMedieval.State;

namespace Hearsay
{
    internal static class Talk
    {
        static readonly HashSet<string> waiting = new HashSet<string>();
        internal static event Action Changed;
        internal static string LastError;

        internal static bool IsWaiting(HumanoidInstance h) => waiting.Contains(VillageState.SettlerKey(h));

        internal static void Send(HumanoidInstance h, string text)
        {
            var v = VillageState.Current;
            if (v == null || h == null || string.IsNullOrWhiteSpace(text) || IsWaiting(h)) return;
            var chat = v.ChatWith(h);
            chat.Add(new Message { FromPlayer = true, Text = text.Trim(), When = Facts.Now(), Day = Facts.Today() });
            v.Save();
            Request(h, v);
        }

        /// <summary>Ask again for the last reply (after an error).</summary>
        internal static void Retry(HumanoidInstance h)
        {
            var v = VillageState.Current;
            if (v == null || IsWaiting(h)) return;
            var chat = v.ChatWith(h);
            if (chat.Count > 0 && !chat[chat.Count - 1].FromPlayer) chat.RemoveAt(chat.Count - 1);
            if (chat.Count == 0) return;
            Request(h, v);
        }

        static void Request(HumanoidInstance h, VillageState v)
        {
            string key = VillageState.SettlerKey(h);
            var chat = v.ChatWith(h);
            var turns = chat.Skip(Math.Max(0, chat.Count - Plugin.HistoryTurns.Value))
                .Select(m => new Gemini.Turn { FromPlayer = m.FromPlayer, Text = m.Text }).ToList();
            waiting.Add(key); LastError = null;
            Changed?.Invoke();
            Gemini.Ask(SystemPrompt(h, v), turns, (reply, error) =>
            {
                waiting.Remove(key);
                if (error != null) { LastError = error; Plugin.Log.LogWarning("Hearsay: " + error); }
                else
                {
                    reply = ReadTags(reply, h, v);
                    v.ChatWith(h).Add(new Message { FromPlayer = false, Text = reply, When = Facts.Now(), Day = Facts.Today() });
                    v.Save();
                    v.Talked(h);
                }
                Changed?.Invoke();
            });
        }

        static readonly Regex TitleTag = new Regex(@"\[\s*TITLE\s*:\s*([^\]]{1,40})\]", RegexOptions.IgnoreCase);

        static string ReadTags(string reply, HumanoidInstance h, VillageState v)
        {
            foreach (Match m in TitleTag.Matches(reply))
            {
                string t = m.Groups[1].Value.Trim().Trim('"', '\'', '.', ' ');
                if (t.Length == 0) continue;
                if (v.Title == null) v.TitleSetBy = Facts.FullName(h);
                v.Title = t;
                Plugin.Log.LogInfo($"Hearsay: the village now calls you \"{t}\"");
            }
            reply = TitleTag.Replace(reply, "");
            return Regex.Replace(reply, @"\s*\[[A-Z_]+:[^\]]*\]\s*", " ").Trim();   // drop any other stray tag
        }

        static string SystemPrompt(HumanoidInstance h, VillageState v)
        {
            string me = Facts.FirstName(h);
            var sb = new StringBuilder();
            sb.AppendLine($"Play {Facts.FullName(h)}, one of the villagers below. The plague emptied the old towns; this small band settled the land to start again.");
            sb.AppendLine("Whoever writes to you is the head of the village. They set the work, raise the buildings and lead the defence. You know them well; you live under their decisions.");
            sb.AppendLine();
            sb.AppendLine("ADDRESSING THE HEAD OF THE VILLAGE");
            if (string.IsNullOrEmpty(v.Title))
                sb.AppendLine("The village has never settled what to call them. Bring it up now, in your own way: ask what they'd like to be called (a title like Lord or Chief, or just a name). " +
                              "Once they answer, start using it and finish that reply with [TITLE:their answer].");
            else
                sb.AppendLine($"The whole village calls them \"{v.Title}\". Use it when it comes naturally. If they want a different form of address, go along with it in your own way and finish that reply with [TITLE:the new one].");
            sb.AppendLine("Anything in square brackets is a hidden marker for the game; the head of the village never sees it, so don't refer to it.");
            sb.AppendLine();
            sb.AppendLine($"YOUR VOICE ({me})");
            sb.AppendLine("Answer the way a person talks across a table: short, usually a few sentences, simple words of a medieval village.");
            sb.AppendLine("Let your circumstances show: an empty belly, an untended wound, a grudge, a friend lost, good news. You have opinions and moods of your own; you can argue, tease, ask for things or say no.");
            sb.AppendLine("Stay inside your world. Talk only about what you could know: the facts below and what the head of the village tells you. When you don't know, admit it rather than invent it. Never make up amounts, stores, buildings, people or events that the facts below don't mention.");
            sb.AppendLine("The numbers below are for you to understand, not to recite. Nobody in the village speaks of levels or points.");
            sb.AppendLine("Reply with speech only: no stage directions, no lists, no headings.");
            sb.AppendLine();
            sb.AppendLine("THE VILLAGE");
            sb.Append(Facts.Village());
            sb.AppendLine();
            sb.AppendLine($"ABOUT {me.ToUpperInvariant()}");
            sb.Append(Facts.Villager(h));
            return sb.ToString();
        }
    }
}
