// The AI behind the villagers, whichever service it is. Hearsay speaks the OpenAI chat/completions format, which
// Gemini, OpenRouter, OpenAI, DeepSeek, Groq, Mistral, and local servers such as Ollama and LM Studio all accept;
// only the server address, key and model differ.
// Network work runs on a background task; the callback is always delivered on Unity's main thread.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hearsay
{
    internal static class Llm
    {
        internal struct Turn { public bool FromPlayer; public string Text; }

        /// <summary>One message of the request: who speaks and the pieces of text they say.</summary>
        internal sealed class Msg { public bool User; public List<string> Parts = new List<string>(); public string Text => string.Join("\n\n", Parts); }

        internal interface IProvider
        {
            string Name { get; }
            /// <summary>Sends one request; returns (reply, null) or (null, error). <paramref name="drop"/> names settings
            /// the server refused last time, to leave out on a retry.</summary>
            Task<(string reply, string error, Usage usage)> Send(string system, List<Msg> msgs, HashSet<string> drop);
        }

        internal struct Usage { public long In, Cached, Out, Thinking; }

        static readonly ConcurrentQueue<Action> done = new ConcurrentQueue<Action>();
        static bool pumping;
        internal static readonly HttpClient Http;

        static Llm()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        internal static IProvider Current => new OpenAiCompatible();

        /// <summary>Ask for one reply. <paramref name="present"/> (may be null) is put in front of the newest player
        /// message, after everything that repeats between requests, so the repeated start can be cached.</summary>
        internal static void Ask(string system, List<Turn> turns, string present, Action<string, string> callback)
        {
            var provider = Current;
            if (!Plugin.AiReady) { callback(null, "The AI isn't set up: add a server, key and model in the HEARSAY settings (top bar)."); return; }
            var msgs = Build(turns, present);
            Pump();
            Task.Run(async () =>
            {
                string reply = null, error = null;
                try { (reply, error) = await Exchange(provider, system, msgs).ConfigureAwait(false); }
                catch (Exception e) { error = e.GetBaseException().Message; }
                done.Enqueue(() => callback(reply, error));
            });
        }

        /// <summary>Turns the chat into request messages: same-speaker turns merged, <paramref name="present"/> first in the newest player message.</summary>
        internal static List<Msg> Build(List<Turn> turns, string present)
        {
            var msgs = new List<Msg>();
            foreach (var t in turns)
            {
                if (msgs.Count > 0 && msgs[msgs.Count - 1].User == t.FromPlayer) msgs[msgs.Count - 1].Parts.Add(t.Text);
                else msgs.Add(new Msg { User = t.FromPlayer, Parts = { t.Text } });
            }
            if (!string.IsNullOrEmpty(present))
            {
                if (msgs.Count == 0 || !msgs[msgs.Count - 1].User) msgs.Add(new Msg { User = true });
                msgs[msgs.Count - 1].Parts.Insert(0, present);
            }
            return msgs;
        }

        /// <summary>Sends the request; a server that refuses one of our settings (thinking, temperature, ...) is asked again without it, up to 4 times.</summary>
        internal static async Task<(string reply, string error)> Exchange(IProvider provider, string system, List<Msg> msgs)
        {
            string reply = null, error = null;
            var drop = new HashSet<string>();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Usage usage;
                (reply, error, usage) = await provider.Send(system, msgs, drop).ConfigureAwait(false);
                if (error == null) { LogUsage(usage); break; }
                string refused = Refused(error, drop);
                if (refused == null) break;
                drop.Add(refused);
                Plugin.Log.LogInfo($"Hearsay: the server refused '{refused}', asking again without it");
            }
            return (reply, error);
        }

        static readonly string[] Optional = { "thinking", "reasoning", "max_tokens", "temperature" };

        static string Refused(string error, HashSet<string> drop)
        {
            if (!error.StartsWith("HTTP 400") && !error.StartsWith("HTTP 422")) return null;
            return Optional.FirstOrDefault(o => !drop.Contains(o) && error.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static async Task<(JObject json, string error)> Post(string url, string body, Action<HttpRequestMessage> headers)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                headers?.Invoke(req);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var res = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject j = null;
                    try { j = JObject.Parse(text); } catch { }
                    if (!res.IsSuccessStatusCode)
                    {
                        string msg = (string)j?["error"]?["message"] ?? (string)j?["error"] ?? (text.Length > 300 ? text.Substring(0, 300) : text);
                        return (null, $"HTTP {(int)res.StatusCode}: {msg}");
                    }
                    if (j == null) return (null, "The server's answer wasn't JSON. Check the server address.");
                    return (j, null);
                }
            }
        }

        static long totalIn, totalOut, totalCached, requests;

        /// <summary>Writes each reply's token use (as the service counts it) and the running total to the BepInEx log.</summary>
        static void LogUsage(Usage u)
        {
            long all = Interlocked.Add(ref totalIn, u.In);
            long allOut = Interlocked.Add(ref totalOut, u.Out + u.Thinking);
            long allCached = Interlocked.Add(ref totalCached, u.Cached);
            long n = Interlocked.Increment(ref requests);
            Plugin.Log.LogInfo($"Hearsay: tokens: {u.In} in ({u.Cached} of them cached), {u.Out} reply + {u.Thinking} thinking | " +
                               $"this session: {n} requests, {all} in ({allCached} cached), {allOut} out");
        }

        static void Pump()
        {
            if (pumping) return;
            pumping = true;
            Scheduler.Start(Loop(), e => pumping = false);
        }

        static System.Collections.IEnumerator Loop()
        {
            while (true)
            {
                while (done.TryDequeue(out var a))
                {
                    try { a(); } catch (Exception e) { Plugin.Log.LogError(e); }
                }
                yield return null;
            }
        }
    }

    /// <summary>Any server that speaks the OpenAI chat/completions format.</summary>
    internal sealed class OpenAiCompatible : Llm.IProvider
    {
        public string Name => "OpenAI-compatible";

        public async Task<(string, string, Llm.Usage)> Send(string system, List<Llm.Msg> msgs, HashSet<string> drop)
        {
            string baseUrl = Plugin.ServerUrl.Value?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return (null, "No server address set.", default);
            bool openRouter = baseUrl.IndexOf("openrouter.ai", StringComparison.OrdinalIgnoreCase) >= 0;
            var messages = new JArray { new JObject { ["role"] = "system", ["content"] = system } };
            foreach (var m in msgs) messages.Add(new JObject { ["role"] = m.User ? "user" : "assistant", ["content"] = m.Text });
            var req = new JObject { ["model"] = Plugin.ModelName, ["messages"] = messages };
            // newer OpenAI models want max_completion_tokens; everyone else max_tokens
            req[drop.Contains("max_tokens") ? "max_completion_tokens" : "max_tokens"] = Plugin.MaxReplyTokens.Value;
            if (!drop.Contains("temperature")) req["temperature"] = Plugin.Temperature.Value;
            string thinking = Plugin.Thinking.Value?.Trim().ToLowerInvariant();
            if (!drop.Contains("reasoning") && !string.IsNullOrEmpty(thinking) && thinking != "off")
            {
                if (openRouter) req["reasoning"] = new JObject { ["effort"] = thinking, ["exclude"] = true };
                else req["reasoning_effort"] = thinking;
            }
            string key = Plugin.Key;
            var (j, error) = await Llm.Post(baseUrl + "/chat/completions", req.ToString(Newtonsoft.Json.Formatting.None), r =>
            {
                if (!string.IsNullOrEmpty(key)) r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                if (openRouter) r.Headers.TryAddWithoutValidation("X-Title", "Hearsay");
            }).ConfigureAwait(false);
            if (error != null) return (null, error, default);
            var u = j["usage"];
            long thought = (long?)u?["completion_tokens_details"]?["reasoning_tokens"] ?? 0;
            var usage = new Llm.Usage
            {
                In = (long?)u?["prompt_tokens"] ?? 0, Cached = (long?)u?["prompt_tokens_details"]?["cached_tokens"] ?? 0,
                Out = Math.Max(0, ((long?)u?["completion_tokens"] ?? 0) - thought), Thinking = thought,
            };
            var content = j["choices"]?[0]?["message"]?["content"];
            string reply = content is JArray arr ? string.Concat(arr.Select(p => (string)p["text"] ?? "")) : (string)content;
            if (string.IsNullOrWhiteSpace(reply))
                return (null, "Empty reply (" + ((string)j["choices"]?[0]?["finish_reason"] ?? (string)j["error"]?["message"] ?? "unknown") + ")", usage);
            return (StripThinking(reply).Trim(), null, usage);
        }

        /// <summary>Some local models write their reasoning inline as &lt;think&gt;...&lt;/think&gt;: keep only the answer.</summary>
        static string StripThinking(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"<think>[\s\S]*?</think>", "");
    }
}
