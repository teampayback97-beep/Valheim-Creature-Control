using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Rewrites birch drop tables once, at load, rather than intercepting every
    /// swing. A birch drops from two places - the standing tree and the log it
    /// falls into - and each carries its own DropTable, so both are filtered.
    /// </summary>
    public static class TreeDrops
    {
        const string Prefix = "Birch";
        const string Fine = "FineWood";
        const string Seeds = "BirchSeeds";

        public static void Apply(ZNetScene scene)
        {
            if (scene == null || !Plugin.BirchFineWoodOnly) return;
            var prefabs = scene.m_prefabs;
            if (prefabs == null) return;

            int tables = 0, removed = 0;

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                if (!go.name.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase)) continue;

                var tree = go.GetComponent<TreeBase>();
                if (tree != null && Filter(go.name, "tree", tree.m_dropWhenDestroyed, ref removed)) tables++;

                var log = go.GetComponent<TreeLog>();
                if (log != null && Filter(go.name, "log", log.m_dropWhenDestroyed, ref removed)) tables++;
            }

            Plugin.Log.LogInfo(
                $"Birch drops: {removed} entr(ies) stripped from {tables} table(s); " +
                (Plugin.BirchKeepSeeds ? "seeds kept." : "seeds removed too."));
        }

        /// <summary>Keep FineWood (and optionally seeds); drop everything else.
        /// Returns true if this table was touched.</summary>
        static bool Filter(string prefab, string what, DropTable table, ref int removed)
        {
            if (table == null || table.m_drops == null || table.m_drops.Count == 0) return false;

            var kept = new List<DropTable.DropData>();
            var lost = new List<string>();

            foreach (var d in table.m_drops)
            {
                string item = d.m_item != null ? d.m_item.name : "(none)";

                bool keep =
                    item.Equals(Fine, System.StringComparison.OrdinalIgnoreCase) ||
                    (Plugin.BirchKeepSeeds &&
                     item.Equals(Seeds, System.StringComparison.OrdinalIgnoreCase));

                if (keep) kept.Add(d);
                else { lost.Add(item); removed++; }
            }

            if (lost.Count == 0) return false;

            // Everything that was in here is gone. Leaving an empty table would
            // make the birch drop nothing at all, which is not what "only fine
            // wood" means - so put the table back untouched and say so. This
            // happens on the parts of a birch that never carried FineWood.
            if (kept.Count == 0)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"Birch drops: left {prefab} ({what}) alone - it only had " +
                        $"{string.Join(", ", lost.ToArray())} and no FineWood to fall back on.");
                removed -= lost.Count;
                return false;
            }

            table.m_drops = kept;

            // With the other entries gone, FineWood now carries all the weight.
            // Guarantee the roll actually happens, or a stripped table can still
            // come up empty and the tree yields nothing.
            table.m_dropChance = 1f;
            if (table.m_dropMin < 1) table.m_dropMin = 1;
            if (table.m_dropMax < table.m_dropMin) table.m_dropMax = table.m_dropMin;

            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"Birch drops: {prefab} ({what}) -> kept FineWood, removed " +
                    string.Join(", ", lost.ToArray()));
            return true;
        }
    }

    /// <summary>
    /// Shared by every per-species wood rebalance below (Cotton Wood, Willow,
    /// the Oak family, and whatever comes next). Stumps are never touched by
    /// this - they keep RtDBiomes' original amount AND original item mix,
    /// untouched. All of the increase lives on the felled log's half-log
    /// instead: its own doubling, PLUS the stump's original amount (what
    /// doubling the stump would have added) split across the two half-logs
    /// every felled log spawns. The exact numbers are computed per species
    /// (see each class below) and passed in as an explicit stack range
    /// rather than derived here, since the stump's original range doesn't
    /// divide evenly for every species.
    /// </summary>
    static class WoodYieldRework
    {
        /// <summary>Drop everything but <paramref name="keepItem"/> from a
        /// half-log's single-pick table and set its stack to an explicit
        /// range. Returns true if the table had that item to keep.</summary>
        public static bool SetHalfLogStack(string label, string prefab, DropTable table, string keepItem, int stackMin, int stackMax)
        {
            if (table == null || table.m_drops == null || table.m_drops.Count == 0) return false;

            DropTable.DropData keep = default;
            bool found = false;
            foreach (var d in table.m_drops)
            {
                if (d.m_item != null && d.m_item.name.Equals(keepItem, System.StringComparison.OrdinalIgnoreCase))
                {
                    keep = d;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo($"{label}: left {prefab} alone - no {keepItem} entry found.");
                return false;
            }

            keep.m_stackMin = stackMin;
            keep.m_stackMax = stackMax;
            keep.m_weight = 1f;

            table.m_drops = new List<DropTable.DropData> { keep };
            table.m_dropChance = 1f;
            table.m_dropMin = 1;
            table.m_dropMax = 1;

            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"{label}: {prefab} -> {keepItem} only, x{stackMin}-{stackMax}.");
            return true;
        }
    }

    /// <summary>
    /// RtDBiomes' Cotton Wood tree (plantable only, no wild spawn) normally
    /// splits its payout 1:1 between Wood and FineWood on both the stump
    /// (DropOnDestroyed, 12-14 picks) and the felled log's half-log
    /// (TreeLog, one weighted pick of a 15-stack). The stump is left exactly
    /// as RtDBiomes shipped it. CottonWoodHalf is pinned to Wood-only and
    /// set to x36-37: its own doubling (15 -> 30) plus half of what doubling
    /// the stump would have added (avg 13 / 2 halves = 6.5, expressed as a
    /// 36-37 range since the stump's range doesn't split evenly).
    /// </summary>
    public static class CottonWoodDrops
    {
        const string Half = "CottonWoodHalf";
        const string Keep = "Wood";

        public static void Apply(ZNetScene scene)
        {
            if (scene == null || !Plugin.CottonWoodDoubleWoodOnly) return;
            var prefabs = scene.m_prefabs;
            if (prefabs == null) return;

            int tables = 0;

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                if (!go.name.Equals(Half, System.StringComparison.OrdinalIgnoreCase)) continue;

                var log = go.GetComponent<TreeLog>();
                if (log != null && WoodYieldRework.SetHalfLogStack("Cotton Wood drops", go.name, log.m_dropWhenDestroyed, Keep, 36, 37)) tables++;
            }

            Plugin.Log.LogInfo($"Cotton Wood drops: {tables} table(s) pinned to Wood-only, stump left vanilla.");
        }
    }

    /// <summary>
    /// RtDBiomes' Willow tree already drops nothing but Wood on both its
    /// stump (30 picks) and half-log (25-stack) - no second type to strip.
    /// The stump is left exactly as RtDBiomes shipped it. WillowLogHalf is
    /// set to x65: its own doubling (25 -> 50) plus half of what doubling
    /// the stump would have added (30 / 2 halves = 15).
    /// </summary>
    public static class WillowDrops
    {
        const string Half = "WillowLogHalf";
        const string Keep = "Wood";

        public static void Apply(ZNetScene scene)
        {
            if (scene == null || !Plugin.WillowDoubleWood) return;
            var prefabs = scene.m_prefabs;
            if (prefabs == null) return;

            int tables = 0;

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                if (!go.name.Equals(Half, System.StringComparison.OrdinalIgnoreCase)) continue;

                var log = go.GetComponent<TreeLog>();
                if (log != null && WoodYieldRework.SetHalfLogStack("Willow drops", go.name, log.m_dropWhenDestroyed, Keep, 65, 65)) tables++;
            }

            Plugin.Log.LogInfo($"Willow drops: {tables} table(s) set, stump left vanilla.");
        }
    }

    /// <summary>
    /// Every RtDBiomes prefab with "oak" in its name that carries a wood
    /// payout - currently OakWoodStump and OakWoodHalf (OakWoodLog only
    /// carries a Resin/Feathers bonus, and the seed/sapling items have no
    /// drop table at all, so both are harmlessly skipped). Matched by
    /// substring rather than an exact list so any future oak-family variant
    /// RtDBiomes adds picks this up automatically. Normally splits 1:1
    /// between Wood and FineWood on both the stump (30 picks) and the
    /// half-log (15-stack). The stump is left exactly as RtDBiomes shipped
    /// it. OakWoodHalf is pinned to FineWood-only and set to x45: its own
    /// doubling (15 -> 30) plus half of what doubling the stump would have
    /// added (30 / 2 halves = 15).
    /// </summary>
    public static class OakFamilyDrops
    {
        const string Identifier = "oak";
        const string HalfSuffix = "half";
        const string Keep = "FineWood";

        public static void Apply(ZNetScene scene)
        {
            if (scene == null || !Plugin.OakFamilyFineWoodOnly) return;
            var prefabs = scene.m_prefabs;
            if (prefabs == null) return;

            int tables = 0;

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                if (go.name.IndexOf(Identifier, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Only the half-log gets touched - the stump and the full
                // log (bonus Resin/Feathers only, no wood anyway) are left
                // exactly as RtDBiomes shipped them.
                if (go.name.IndexOf(HalfSuffix, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                var log = go.GetComponent<TreeLog>();
                if (log != null && WoodYieldRework.SetHalfLogStack("Oak drops", go.name, log.m_dropWhenDestroyed, Keep, 45, 45)) tables++;
            }

            Plugin.Log.LogInfo($"Oak drops: {tables} table(s) pinned to FineWood-only, stump left vanilla.");
        }
    }

    /// <summary>
    /// ZNetScene.Awake is where every prefab is registered, so it is the first
    /// moment the whole modded set exists and the last moment before anything
    /// is instantiated from it.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    static class Patch_ZNetScene_Awake_TreeDrops
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(ZNetScene __instance)
        {
            TreeDrops.Apply(__instance);
            CottonWoodDrops.Apply(__instance);
            WillowDrops.Apply(__instance);
            OakFamilyDrops.Apply(__instance);
        }
    }
}
