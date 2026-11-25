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

        /// <summary>They asked for a word: they open the conversation, about <paramref name="open"/>.</summary>
        internal static void Opener(HumanoidInstance h, Want want, OpenWant open)
        {
            var v = VillageState.Current;
            if (v == null || h == null || IsWaiting(h)) return;
            string about = open?.Note ?? want.Note ?? "something you wanted to tell them";
            Request(h, v, $"You asked to have a word with the head of the village, and they have just come over. Speak first. " +
                          $"What's on your mind: {about}. " + (want.Good ? "It's good news; let it show." : "Say it plainly, in your own way."));
        }

        static void Request(HumanoidInstance h, VillageState v, string opening = null)
        {
            string key = VillageState.SettlerKey(h);
            var chat = v.ChatWith(h);
            var turns = chat.Skip(Math.Max(0, chat.Count - Plugin.HistoryTurns.Value))
                .Select(m => new Llm.Turn { FromPlayer = m.FromPlayer, Text = m.Text }).ToList();
            waiting.Add(key); LastError = null;
            Changed?.Invoke();
            Llm.Ask(SystemPrompt(h, v), turns, Present(h, opening), (reply, error) =>
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
            sb.AppendLine("The facts below are written like a game's notes; turn them into how a villager would say it. Never use game words such as forbidden, " +
                          "off-limits, marked, stockpile, zone, stat, effect, buff, need, priority, schedule or job. Say it as life: \"you told us to leave that be\", " +
                          "\"we've nothing put by\", \"I'm set to hauling\".");
            sb.AppendLine("Reply with speech only: no stage directions, no lists, no headings.");
            sb.AppendLine();
            sb.AppendLine("WHAT YOU NOTICE AND WHAT YOU SAY");
            sb.AppendLine("Each message from the head of the village comes with a note of how things stand right now. You live in the middle of it, so you know " +
                          "all of it, but you don't recite it. Answer what you were asked. Raise something on your own only when it touches you or the things " +
                          "close to your heart, or when it is serious (hunger, a bad wound, danger); at most one such worry per reply, and not one you've just " +
                          "raised. Someone who cares about animals speaks up for a suffering beast; someone who doesn't might not even notice.");
            sb.AppendLine("Nothing outside the notes and this conversation exists for you: if it isn't mentioned, you don't know about it.");
            sb.AppendLine();
            sb.AppendLine($"WHO {me.ToUpperInvariant()} IS");
            sb.Append(Facts.Villager(h));
            return sb.ToString();
        }

        /// <summary>A request for a word they made that you never answered, for them to bring up (or not) as they like.</summary>
        static string Snubbed(HumanoidInstance h)
        {
            var v = VillageState.Current;
            if (v == null || !v.Snubs.TryGetValue(VillageState.SettlerKey(h), out var sn)) return null;
            int ago = Facts.Today() - sn.Day;
            string when = ago <= 0 ? "Earlier today" : ago == 1 ? "Yesterday" : "A few days ago";
            string about = string.IsNullOrEmpty(sn.Note) ? "" : $" ({sn.Note})";
            string what = sn.Waved ? "they waved you off without coming over" : "they never came";
            return sn.Good
                ? $"Not answered: {when} you asked the head of the village for a word to share some good news{about}, and {what}. " +
                  "You might mention it lightly, a little put out or just glad to tell it now; don't make much of it."
                : $"Not answered: {when} you asked the head of the village for a word about something that worried you{about}, and {what}. " +
                  "It stung. You may bring it up once, plainly or with an edge, as suits you, or let them notice your coolness; don't dwell on it, " +
                  "and if they make amends, let it go.";
        }

        /// <summary>The state of things at this moment. Sent after the conversation, with the newest message, so that
        /// everything before it (rules, who the villager is, the conversation so far) stays the same from one message
        /// to the next and Google can serve it from its cache.</summary>
        static string Present(HumanoidInstance h, string opening = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[How things stand right now. For you only; the head of the village did not say this.]");
            sb.AppendLine("THE VILLAGE");
            sb.Append(Facts.Village());
            sb.AppendLine($"{Facts.FirstName(h).ToUpperInvariant()} RIGHT NOW");
            sb.Append(Facts.VillagerNow(h));
            string snub = Snubbed(h);
            if (snub != null) sb.AppendLine(snub);
            sb.AppendLine(opening == null ? "[End of note. What the head of the village says to you:]" : $"[End of note. {opening}]");
            return sb.ToString();
        }
    }
}
