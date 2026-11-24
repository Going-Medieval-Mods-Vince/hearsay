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
        internal static ConfigEntry<string> ApiKey, Model, Thinking, Mode;
        internal static ConfigEntry<int> HistoryTurns, MaxReplyTokens;
        internal static ConfigEntry<bool> SocialBoost;
        internal static ConfigEntry<float> Temperature;

        /// <summary>True when replies come from Gemini rather than the written dialogue.</summary>
        internal static bool UseAI =>
            Mode.Value == "AI" || (Mode.Value != "Written" && !string.IsNullOrWhiteSpace(ApiKey.Value));

        void Awake()
        {
            Log = Logger;
            Mode = Config.Bind("Chat", "Mode", "Auto",
                new ConfigDescription("Auto: AI replies when a Gemini key is set, written dialogue otherwise. Written: always pick-a-line dialogue, no key needed. AI: always use Gemini.",
                    new AcceptableValueList<string>("Auto", "Written", "AI")));
            ApiKey = Config.Bind("Gemini", "ApiKey", "",
                "Your Gemini API key (aistudio.google.com/apikey). It is only sent to Google's Gemini API.");
            Model = Config.Bind("Gemini", "Model", "gemini-3.8-flash",
                "Gemini model id. If Google retires it, the error message in the chat names the replacement.");
            Thinking = Config.Bind("Gemini", "Thinking", "low",
                "How much the model thinks before answering: low (fast), medium, high, or off (sends no thinking setting).");
            Temperature = Config.Bind("Gemini", "Temperature", 0.9f, "Higher = more varied replies (0 to 2).");
            HistoryTurns = Config.Bind("Chat", "HistoryTurns", 20, "How many recent messages of a conversation are sent with each request.");
            MaxReplyTokens = Config.Bind("Chat", "MaxReplyTokens", 600, "Upper limit for one reply's length (tokens).");
            SocialBoost = Config.Bind("Chat", "SocialBoost", true,
                "Talking with a villager meets their Social need like a chat with another villager does (once per in-game day each).");
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"Hearsay {Version} loaded ({(UseAI ? "AI replies, model " + Model.Value : "written dialogue")})");
        }
    }
}
