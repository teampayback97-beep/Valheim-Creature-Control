using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CreatureControl
{
    /// <summary>
    /// Rune Magic only lets a Runestone roll for runes from its own biome and
    /// every biome "before" it in a fixed progression (Meadows, Black Forest,
    /// Swamp, Mountain, Plains, Mistlands, AshLands, in that hardcoded order) -
    /// a Black Forest stone can only ever offer Meadows/Black Forest runes,
    /// never a Plains or AshLands one, whatever you already know. That is
    /// UnlockManager.getBiomesUpTo(Biome): it walks that fixed list and stops
    /// the moment it reaches the biome passed in.
    ///
    /// This makes every Runestone, in every biome, draw from the full set
    /// instead - so the biome you are standing in no longer caps which runes
    /// you can find there. It only ever hands out real runes: every biome
    /// value below is one an actual rune is registered under in Rune Magic's
    /// own piece table (confirmed by reading its compiled code directly, not
    /// guessed) - Ocean and DeepNorth are left out because nothing is
    /// registered there in the current version, so including them would just
    /// let the roll "waste" a pull on a biome with nothing to give.
    ///
    /// Applied from here rather than by editing Rune Magic's own DLL: the
    /// type is resolved by name at runtime, so this quietly does nothing when
    /// Rune Magic isn't installed, and it survives Rune Magic being updated
    /// (unless the method itself is renamed or restructured, in which case it
    /// logs a warning and leaves Rune Magic's own behaviour untouched).
    /// </summary>
    internal static class RuneMagicAllBiomes
    {
        const string TypeName = "ValheimMod.UnlockManager";
        const string MethodName = "getBiomesUpTo";

        public static void TryPatch(Harmony harmony)
        {
            try
            {
                var type = AccessTools.TypeByName(TypeName);
                if (type == null)
                {
                    Plugin.Log.LogInfo("Rune Magic not installed - all-biomes unlock patch not needed.");
                    return;
                }

                var target = AccessTools.Method(type, MethodName);
                if (target == null)
                {
                    Plugin.Log.LogWarning(
                        $"Found {TypeName} but not {MethodName}() - Rune Magic has probably " +
                        "changed shape, so the all-biomes unlock patch was skipped. Runestones " +
                        "will still cap unlocks to their own biome and earlier ones.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(RuneMagicAllBiomes), nameof(Prefix))));

                Plugin.Log.LogInfo(
                    "Rune Magic patched: every Runestone now draws from every biome's runes, " +
                    "not just its own biome and earlier ones.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not apply the Rune Magic all-biomes unlock patch: {e.Message}");
            }
        }

        // Skips Rune Magic's own biome-progression walk entirely and hands
        // back every biome that actually has a rune registered to it, so the
        // biome argument (which biome the Runestone being read is in) no
        // longer matters. A fresh HashSet each call - this only runs when a
        // player physically reads a Runestone, never in a hot loop, so the
        // allocation is irrelevant.
        static bool Prefix(ref HashSet<Heightmap.Biome> __result)
        {
            __result = new HashSet<Heightmap.Biome>
            {
                Heightmap.Biome.Meadows,
                Heightmap.Biome.BlackForest,
                Heightmap.Biome.Swamp,
                Heightmap.Biome.Mountain,
                Heightmap.Biome.Plains,
                Heightmap.Biome.AshLands,
            };
            return false;
        }
    }
}
