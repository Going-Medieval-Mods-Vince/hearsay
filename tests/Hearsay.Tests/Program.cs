// A tiny test runner: every public static void method named Test* in this assembly is a test; a thrown exception fails it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Hearsay;

static class Program
{
    static int Main(string[] args)
    {
        Env.Setup();
        string only = args.FirstOrDefault();
        var tests = typeof(Program).Assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.Name.StartsWith("Test") && m.GetParameters().Length == 0 && (only == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name).ToList();
        int failed = 0;
        foreach (var t in tests)
        {
            string name = t.DeclaringType.Name + "." + t.Name;
            Env.Warnings.Clear();
            try { t.Invoke(null, null); Console.WriteLine("  ok    " + name); }
            catch (TargetInvocationException e)
            {
                failed++;
                Console.WriteLine("  FAIL  " + name + "\n        " + (e.InnerException?.Message ?? e.Message).Replace("\n", "\n        "));
            }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        Env.Server?.Dispose();
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>Stands in for the game: a temp config folder, Hearsay's settings, and a log that records warnings.</summary>
static class Env
{
    internal static readonly List<string> Warnings = new List<string>();
    internal static MockAi Server;
    internal static string Temp;

    sealed class Capture : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs e) { if (e.Level <= LogLevel.Warning) Warnings.Add(e.Data?.ToString()); }
        public void Dispose() { }
    }

    internal static void Setup()
    {
        Temp = Path.Combine(Path.GetTempPath(), "hearsay-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Temp);
        // BepInEx normally learns its folders from the game; point them at a temp "game" so ConfigPath (read by Book and VillageState) works
        var set = typeof(Paths).GetMethod("SetExecutablePath", BindingFlags.NonPublic | BindingFlags.Static);
        var args = new object[set.GetParameters().Length];
        args[0] = Path.Combine(Temp, "Going Medieval.exe");
        if (args.Length > 1) args[1] = Path.Combine(Temp, "BepInEx");
        set.Invoke(null, args);
        Directory.CreateDirectory(Paths.ConfigPath);
        Plugin.Log = new ManualLogSource("Hearsay.Tests");
        BepInEx.Logging.Logger.Sources.Add(Plugin.Log);
        BepInEx.Logging.Logger.Listeners.Add(new Capture());
        var cfg = new ConfigFile(Path.Combine(Temp, "hearsay.cfg"), false);
        Plugin.ServerUrl = cfg.Bind("AI", "ServerUrl", "");
        Plugin.ApiKey = cfg.Bind("AI", "ApiKey", "");
        Plugin.ModelId = cfg.Bind("AI", "Model", "test-model");
        Plugin.Thinking = cfg.Bind("AI", "Thinking", "off");
        Plugin.Temperature = cfg.Bind("AI", "Temperature", 0.9f);
        Plugin.Mode = cfg.Bind("Chat", "Mode", "Auto");
        Plugin.MaxReplyTokens = cfg.Bind("Chat", "MaxReplyTokens", 300);
        Plugin.HistoryTurns = cfg.Bind("Chat", "HistoryTurns", 12);
    }

    /// <summary>A mock AI server, started on first use; each test resets it.</summary>
    internal static MockAi Ai()
    {
        if (Server == null) Server = new MockAi();
        Server.Reset();
        Plugin.ServerUrl.Value = Server.Url;
        Plugin.ApiKey.Value = "test-key";
        Plugin.Thinking.Value = "off";
        return Server;
    }
}

static class Assert
{
    internal static void True(bool ok, string what) { if (!ok) throw new Exception(what); }
    internal static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected <{expected}>, got <{actual}>");
    }
}
