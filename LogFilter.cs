using BepInEx.Logging;
using HarmonyLib;

namespace CreatureControl
{
    /// <summary>
    /// Drops known log spam from OTHER mods before it reaches the console or
    /// the disk log - without touching the global BepInEx log level, which
    /// this mod's own Verbose logging depends on.
    ///
    /// Currently just "No Rain Damage" (uk.co.oliapps.valheim.noraindamage),
    /// which logs an Info line for every nearby piece on every rain tick:
    ///     [Info :NoRainDamage] piece_workbench_ext3(Clone): 'I'm wet!'
    /// Harmless - its own damage settings are what actually matter - but it
    /// buries everything else in the log. If a future update of that mod
    /// removes the line, this patch simply stops matching anything.
    /// </summary>
    [HarmonyPatch(typeof(ManualLogSource), nameof(ManualLogSource.Log),
        new[] { typeof(LogLevel), typeof(object) })]
    static class Patch_ManualLogSource_Log
    {
        const string NoisySource = "NoRainDamage";

        [HarmonyPriority(Priority.First)]
        static bool Prefix(ManualLogSource __instance, LogLevel level)
        {
            if (!Plugin.SquelchNoisyLogs) return true;
            if (level != LogLevel.Info) return true;
            if (__instance == null || __instance.SourceName != NoisySource) return true;
            return false;   // swallow it - never reaches console or disk
        }
    }
}
