// The OpenAI-compatible client against a local mock server: request shape, retry on refused settings, reply and usage parsing.
using System.Collections.Generic;
using System.Linq;
using Hearsay;
using Newtonsoft.Json.Linq;

public static class LlmTests
{
    static List<Llm.Turn> Turns(params (bool player, string text)[] t) => t.Select(x => new Llm.Turn { FromPlayer = x.player, Text = x.text }).ToList();

    static (string reply, string error) Ask(string system = "sys", string present = "NOW")
    {
        var msgs = Llm.Build(Turns((true, "hi"), (false, "Hello.")), present);
        return Llm.Exchange(new OpenAiCompatible(), system, msgs).GetAwaiter().GetResult();
    }

    public static void TestBuildMergesTurnsAndPutsPresentInNewestPlayerMessage()
    {
        var m = Llm.Build(Turns((true, "a"), (true, "b"), (false, "c"), (true, "d")), "NOW");
        Assert.Equal(3, m.Count, "messages after merging same-speaker turns");
        Assert.Equal("a\n\nb", m[0].Text, "merged player turns");
        Assert.Equal("NOW\n\nd", m[2].Text, "present goes first in the newest player message");

        var opener = Llm.Build(Turns((true, "a"), (false, "b")), "NOW");
        Assert.Equal(3, opener.Count, "after the villager spoke, present gets its own player message");
        Assert.True(opener[2].User && opener[2].Text == "NOW", "present-only player message");
        Assert.Equal(2, Llm.Build(Turns((true, "a"), (false, "b")), null).Count, "no present, nothing added");
    }

    public static void TestRequestShape()
    {
        var ai = Env.Ai().Reply("Fine.");
        Plugin.Temperature.Value = 0.7f;
        var (reply, error) = Ask("You are Osmund.");
        Assert.Equal(null, error, "error");
        Assert.Equal("Fine.", reply, "reply");
        var r = ai.Requests.Single();
        Assert.Equal("/v1/chat/completions", r.Path, "endpoint");
        Assert.Equal("Bearer test-key", r.Authorization, "auth header");
        Assert.Equal("test-model", (string)r.Body["model"], "model");
        var msgs = (JArray)r.Body["messages"];
        Assert.Equal("system", (string)msgs[0]["role"], "system message first");
        Assert.Equal("You are Osmund.", (string)msgs[0]["content"], "system text");
        Assert.Equal("user", (string)msgs.Last["role"], "newest message is the player's");
        Assert.True(((string)msgs.Last["content"]).StartsWith("NOW"), "present note leads the newest message");
        Assert.Equal(300, (int)r.Body["max_tokens"], "max_tokens");
        Assert.Equal(0.7, System.Math.Round((double)r.Body["temperature"], 2), "temperature");
        Assert.True(r.Body["reasoning_effort"] == null && r.Body["reasoning"] == null, "thinking off sends no reasoning setting");
    }

    public static void TestNoKeyNoAuthHeader()
    {
        var ai = Env.Ai();
        Plugin.ApiKey.Value = "";
        Ask();
        Assert.Equal(null, ai.Requests.Single().Authorization, "no Authorization header without a key");
    }

    public static void TestThinkingSettings()
    {
        var ai = Env.Ai();
        Plugin.Thinking.Value = "low";
        Ask();
        Assert.Equal("low", (string)ai.Requests.Single().Body["reasoning_effort"], "other servers get reasoning_effort");

        ai = Env.Ai();
        Plugin.Thinking.Value = "low";
        Plugin.ServerUrl.Value = ai.Url.Replace("/v1", "/openrouter.ai/v1");   // the client spots OpenRouter by its address
        Ask();
        var body = ai.Requests.Single().Body;
        Assert.True(body["reasoning"] is JObject o && (string)o["effort"] == "low" && (bool)o["exclude"], "OpenRouter gets reasoning {effort, exclude}");
    }

    public static void TestRefusedSettingsAreDroppedAndRetried()
    {
        var ai = Env.Ai().Refuse("temperature").Refuse("max_tokens").Reply("Got there.");
        var (reply, error) = Ask();
        Assert.Equal("Got there.", reply, "reply after two retries");
        Assert.Equal(3, ai.Requests.Count, "requests");
        Assert.True(ai.Requests[1].Body["temperature"] == null, "second request leaves out temperature");
        var last = ai.Requests[2].Body;
        Assert.True(last["temperature"] == null && last["max_tokens"] == null && last["max_completion_tokens"] != null, "third uses max_completion_tokens");
    }

    public static void TestGivesUpAfterFourTries()
    {
        var ai = Env.Ai();
        foreach (var p in new[] { "thinking", "reasoning", "max_tokens", "temperature", "temperature" }) ai.Refuse(p);
        var (reply, error) = Ask();
        Assert.True(reply == null && error != null && error.StartsWith("HTTP 400"), "ends with the server's error");
        Assert.True(ai.Requests.Count <= 4, "at most 4 requests, got " + ai.Requests.Count);
    }

    public static void TestOtherErrorsAreNotRetried()
    {
        var ai = Env.Ai().Answer(500, "{\"error\":{\"message\":\"overloaded\"}}");
        var (reply, error) = Ask();
        Assert.Equal("HTTP 500: overloaded", error, "error text");
        Assert.Equal(1, ai.Requests.Count, "no retry on a server error");

        Env.Ai().Answer(200, "<html>not json</html>");
        Assert.True(Ask().error.Contains("wasn't JSON"), "non-JSON answer explained");
    }

    public static void TestThinkBlocksStrippedAndPartsJoined()
    {
        Env.Ai().Reply("<think>hmm, what to say</think>  Good morning.");
        Assert.Equal("Good morning.", Ask().reply, "inline <think> removed");

        Env.Ai().Answer(200, "{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Two \"},{\"type\":\"text\",\"text\":\"parts.\"}]}}]}");
        Assert.Equal("Two parts.", Ask().reply, "content given as an array of parts");

        Env.Ai().Answer(200, "{\"choices\":[{\"message\":{\"content\":\"\"},\"finish_reason\":\"length\"}]}");
        Assert.True(Ask().error.Contains("length"), "empty reply reports the finish reason");
    }

    public static void TestUsageIsLogged()
    {
        Env.Ai().Reply("Hi.", JObject.Parse("{\"prompt_tokens\":2000,\"prompt_tokens_details\":{\"cached_tokens\":1500},\"completion_tokens\":120,\"completion_tokens_details\":{\"reasoning_tokens\":80}}"));
        var seen = new List<string>();
        var listener = new Listener(seen);
        BepInEx.Logging.Logger.Listeners.Add(listener);
        try { Ask(); }
        finally { BepInEx.Logging.Logger.Listeners.Remove(listener); }
        Assert.True(seen.Any(l => l.Contains("2000 in (1500 of them cached), 40 reply + 80 thinking")), "usage line: " + string.Join(" | ", seen));
    }

    sealed class Listener : BepInEx.Logging.ILogListener
    {
        readonly List<string> seen;
        public Listener(List<string> seen) { this.seen = seen; }
        public void LogEvent(object sender, BepInEx.Logging.LogEventArgs e) => seen.Add(e.Data?.ToString() ?? "");
        public void Dispose() { }
    }
}
