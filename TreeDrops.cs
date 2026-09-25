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
    /// ZNetScene.Awake is where every prefab is registered, so it is the first
    /// moment the whole modded set exists and the last moment before anything
    /// is instantiated from it.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    static class Patch_ZNetScene_Awake_TreeDrops
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(ZNetScene __instance) => TreeDrops.Apply(__instance);
    }
}
