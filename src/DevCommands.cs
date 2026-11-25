// Hearsay commands for the game's developer console (opened by NightlyX DevTools): raise or lapse villager requests on demand.
using System.Linq;
using HarmonyLib;
using NSEipix.Base;
using NSMedieval.DevConsole;

namespace Hearsay
{
    [HarmonyPatch(typeof(DeveloperConsoleController), "Start")]
    static class DevCommands
    {
        static void Postfix(DeveloperConsoleController __instance)
        {
            __instance.AddCommand("hearsay_want", new WantCommand());
            __instance.AddCommand("hearsay_lapse", new LapseCommand());
            __instance.AddCommand("hearsay_state", new StateCommand());
            __instance.AddCommand("hearsay_reasons", new ReasonsCommand());
            Plugin.Log.LogInfo("Hearsay: console commands added (hearsay_want, hearsay_lapse, hearsay_state, hearsay_reasons)");
        }

        static string Try(System.Func<string> f)
        {
            try { return f(); }
            catch (System.Exception e) { Plugin.Log.LogError("Hearsay: console command failed: " + e); return "Hearsay error: " + e.Message; }
        }

        static void Reply(string text) => MonoSingleton<DeveloperConsoleController>.Instance.ReturnCommandResult(text);

        abstract class HearsayCommand : ConsoleCommand
        {
            public override string Command { get; protected set; }
            public override string Description { get; protected set; }
            public override string Help { get; protected set; }
        }

        sealed class WantCommand : HearsayCommand
        {
            public WantCommand()
            {
                Command = "hearsay_want"; Argument = "reason [villager]";
                Description = "A villager asks for a word now (skips time, cooldown and chance)";
                Help = "hearsay_want <reason id> [first name]. hearsay_reasons lists the ids; with no name, the first villager asks.";
            }
            void CommandMethod(string id) => Reply(Try(() => Wants.DebugRaise(id, null)));
            void CommandMethod(string id, string who) => Reply(Try(() => Wants.DebugRaise(id, who)));
        }

        sealed class LapseCommand : HearsayCommand
        {
            public LapseCommand() { Command = "hearsay_lapse"; Description = "Every open request goes unanswered now (moods, snubs)"; Help = Description; }
            void CommandMethod() => Reply(Try(Wants.DebugLapse));
        }

        sealed class StateCommand : HearsayCommand
        {
            public StateCommand() { Command = "hearsay_state"; Description = "Title, open requests and snubs for this village"; Help = Description; }
            void CommandMethod() => Reply(Try(Wants.DebugState));
        }

        sealed class ReasonsCommand : HearsayCommand
        {
            public ReasonsCommand() { Command = "hearsay_reasons"; Description = "Ids of every reason a villager can ask for a word"; Help = Description; }
            void CommandMethod() => Reply(string.Join(", ", Book.Get().Wants.Select(w => w.Id + (w.Good ? "+" : "")) ) + "   (+ = good news)");
        }
    }
}
