// Hearsay: talk with your villagers. Entry point and config.
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Hearsay
{
    [BepInPlugin(Guid, "Hearsay", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "hearsay";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> ServerUrl, ApiKey, ModelId, Thinking, Mode;
        internal static ConfigEntry<int> HistoryTurns, MaxReplyTokens;
        internal static ConfigEntry<bool> SocialBoost, AskToTalk, RequestMoods;
        internal static ConfigEntry<int> AsksPerDay;
        internal static ConfigEntry<float> Temperature;

        internal static string Key => ApiKey.Value?.Trim() ?? "";
        internal static string ModelName => ModelId.Value?.Trim() ?? "";
        /// <summary>A server on this computer (Ollama, LM Studio) needs no key.</summary>
        internal static bool IsLocalServer
        {
            get
            {
                string u = ServerUrl.Value ?? "";
                return u.Contains("://localhost") || u.Contains("://127.0.0.1") || u.Contains("://0.0.0.0") || u.Contains("://[::1]");
            }
        }
        /// <summary>The chosen AI service has what it needs to be asked.</summary>
        internal static bool AiReady => !string.IsNullOrEmpty(ModelName) && (Key.Length > 0 || IsLocalServer);

        /// <summary>True when replies come from the AI rather than the written dialogue.</summary>
        internal static bool UseAI => Mode.Value == "AI" || (Mode.Value != "Written" && AiReady);

        void Awake()
        {
            Log = Logger;
            Mode = Config.Bind("Chat", "Mode", "Auto",
                new ConfigDescription("Auto: AI replies when the chosen AI service is set up, written dialogue otherwise. Written: always pick-a-line dialogue, no key needed. AI: always use the AI.",
                    new AcceptableValueList<string>("Auto", "Written", "AI")));
            ServerUrl = Config.Bind("AI", "ServerUrl", "https://openrouter.ai/api/v1",
                "Address of any OpenAI-compatible API, up to but not including /chat/completions.\n" +
                "Gemini: https://generativelanguage.googleapis.com/v1beta/openai  OpenRouter: https://openrouter.ai/api/v1  OpenAI: https://api.openai.com/v1\n" +
                "Ollama (on this computer): http://localhost:11434/v1  LM Studio: http://localhost:1234/v1");
            ApiKey = Config.Bind("AI", "ApiKey", "",
                "API key for that server (not needed for a server on this computer). It is only sent to the ServerUrl above.");
            ModelId = Config.Bind("AI", "Model", "openrouter/auto",
                "Model id as that server names it, e.g. gemini-3.8-flash for Gemini, openrouter/auto or any id from openrouter.ai/models, llama3.1 for Ollama.");
            Thinking = Config.Bind("AI", "Thinking", "low",
                "How much the model thinks before answering: low (fast), medium, high, or off (sends no thinking setting). Models that can't think ignore it.");
            Temperature = Config.Bind("AI", "Temperature", 0.9f, "Higher = more varied replies (0 to 2).");
            HistoryTurns = Config.Bind("Chat", "HistoryTurns", 20, "How many recent messages of a conversation are sent with each request.");
            MaxReplyTokens = Config.Bind("Chat", "MaxReplyTokens", 600, "Upper limit for one reply's length (tokens).");
            SocialBoost = Config.Bind("Chat", "SocialBoost", true,
                "Talking with a villager meets their Social need like a chat with another villager does (once per in-game day each).");
            AskToTalk = Config.Bind("Chat", "VillagersAskToTalk", true,
                "Villagers ask for a word when something is on their mind, good or bad (a notice in the message stack; click it to talk). No AI request is made until you click.");
            AsksPerDay = Config.Bind("Chat", "AsksPerDay", 3, "Most notices per in-game day for the whole village.");
            RequestMoods = Config.Bind("Chat", "RequestMoods", true,
                "Answering a villager's request within a few hours lifts their mood a little (Heard out, +3); ignoring or closing one " +
                "about a worry lowers it (Brushed off, -4 for a day). Either way they remember an unanswered request the next time you talk.");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Wants.Start();
            Log.LogInfo($"Hearsay {Version} loaded ({(UseAI ? $"AI replies: {ModelName} at {ServerUrl.Value}" : "written dialogue")})");
        }
    }
}
