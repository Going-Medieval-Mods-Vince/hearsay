// Minimal Gemini client (generateContent). Network work runs on a background task; the callback is
// always delivered on Unity's main thread through Scheduler, so it may touch game objects.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hearsay
{
    internal static class Gemini
    {
        internal struct Turn { public bool FromPlayer; public string Text; }

        static readonly HttpClient http;
        static readonly ConcurrentQueue<Action> done = new ConcurrentQueue<Action>();
        static bool pumping;

        static Gemini()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        /// <summary>Ask for one reply. <paramref name="callback"/> gets (reply, null) or (null, error).</summary>
        internal static void Ask(string system, List<Turn> turns, Action<string, string> callback)
        {
            string key = Plugin.ApiKey.Value?.Trim();
            if (string.IsNullOrEmpty(key)) { callback(null, "No Gemini API key. Set ApiKey in BepInEx/config/hearsay.cfg."); return; }
            string body = Body(system, turns, Plugin.Thinking.Value);
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(Plugin.Model.Value.Trim()) + ":generateContent";
            Pump();
            Task.Run(async () =>
            {
                string reply = null, error = null;
                try
                {
                    (reply, error) = await Send(url, key, body);
                    // older models reject the thinking setting: retry once without it
                    if (error != null && error.IndexOf("thinking", StringComparison.OrdinalIgnoreCase) >= 0)
                        (reply, error) = await Send(url, key, Body(system, turns, "off"));
                }
                catch (Exception e) { error = e.GetBaseException().Message; }
                done.Enqueue(() => callback(reply, error));
            });
        }

        static async Task<(string, string)> Send(string url, string key, string body)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                req.Headers.TryAddWithoutValidation("x-goog-api-key", key);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var res = await http.SendAsync(req).ConfigureAwait(false))
                {
                    string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject j = null;
                    try { j = JObject.Parse(text); } catch { }
                    if (!res.IsSuccessStatusCode)
                        return (null, $"HTTP {(int)res.StatusCode}: " + ((string)j?["error"]?["message"] ?? text));
                    var parts = j?["candidates"]?[0]?["content"]?["parts"] as JArray;
                    string reply = parts == null ? null
                        : string.Concat(parts.Where(p => p["thought"]?.Value<bool>() != true).Select(p => (string)p["text"] ?? ""));
                    LogUsage(j?["usageMetadata"]);
                    if (string.IsNullOrWhiteSpace(reply))
                        return (null, "Empty reply (" + ((string)j?["candidates"]?[0]?["finishReason"] ?? (string)j?["promptFeedback"]?["blockReason"] ?? "unknown") + ")");
                    return (reply.Trim(), null);
                }
            }
        }

        static long totalIn, totalOut, requests;

        /// <summary>Writes each reply's token use (as Google counts it) and the running total to the BepInEx log.</summary>
        static void LogUsage(JToken u)
        {
            if (u == null) return;
            long input = (long?)u["promptTokenCount"] ?? 0, output = (long?)u["candidatesTokenCount"] ?? 0, thinking = (long?)u["thoughtsTokenCount"] ?? 0;
            long all = System.Threading.Interlocked.Add(ref totalIn, input);
            long allOut = System.Threading.Interlocked.Add(ref totalOut, output + thinking);
            long n = System.Threading.Interlocked.Increment(ref requests);
            Plugin.Log.LogInfo($"Hearsay: tokens: {input} in, {output} reply + {thinking} thinking | this session: {n} requests, {all} in, {allOut} out");
        }

        static string Body(string system, List<Turn> turns, string thinking)
        {
            // Gemini wants alternating roles: merge neighbours from the same side
            var contents = new JArray();
            foreach (var t in turns)
            {
                string role = t.FromPlayer ? "user" : "model";
                if (contents.Count > 0 && (string)contents.Last["role"] == role)
                    ((JArray)contents.Last["parts"]).Add(new JObject { ["text"] = t.Text });
                else
                    contents.Add(new JObject { ["role"] = role, ["parts"] = new JArray { new JObject { ["text"] = t.Text } } });
            }
            var gen = new JObject { ["maxOutputTokens"] = Plugin.MaxReplyTokens.Value, ["temperature"] = Plugin.Temperature.Value };
            if (!string.IsNullOrEmpty(thinking) && !thinking.Equals("off", StringComparison.OrdinalIgnoreCase))
                gen["thinkingConfig"] = new JObject { ["thinkingLevel"] = thinking.Trim().ToLowerInvariant() };
            return new JObject
            {
                ["systemInstruction"] = new JObject { ["parts"] = new JArray { new JObject { ["text"] = system } } },
                ["contents"] = contents,
                ["generationConfig"] = gen,
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Delivers finished requests on the main thread.</summary>
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
}
