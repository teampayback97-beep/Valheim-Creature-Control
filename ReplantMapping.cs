using System;
using System.Collections.Generic;

namespace CreatureControl
{
    /// <summary>
    /// Which sapling a logging troll plants back after clearing a stump, by
    /// the ORIGINAL standing tree's own prefab name. Nothing in TreeBase (or
    /// anywhere else in the game's data) links a tree species to a sapling,
    /// and several RtDBiomes species share the exact same stump/log assets -
    /// "Maple Tree" (ForestTree1/2_RtD) and "Magic Tree" (MagicTree1_RtD)
    /// both use MagicStump1/MagicLog1, for one - so guessing the sapling
    /// from the stump's own name would sometimes plant the wrong species.
    /// This is an explicit, editable table instead: add or correct entries
    /// under [Replant] in CreatureControl.Creatures.cfg (config values win
    /// over the defaults below). Anything not covered by either simply isn't
    /// replanted - better silence than a wrong species.
    /// </summary>
    public static class ReplantMapping
    {
        public class Store
        {
            public readonly Dictionary<string, string> ByTreeName =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Best-effort defaults for the species this mod's own tree-drop
        // investigation could identify with real confidence - deliberately
        // NOT every RtDBiomes tree. Left out on purpose (rather than guessed
        // at wrong): the Magic Tree family and anything else built from the
        // same shared assets (Dead Tree, Ancient Tree, Charred Tree,
        // Frostwood variants), Yggdrasil, and every vanilla base-game tree
        // (Birch/Oak/Beech/Fir/Pine) - none of those had a confidently
        // identifiable sapling prefab name to hand. Add them under [Replant]
        // once you know the right name for your install.
        static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Oak1_RtD", "OakSapling_RtD" },
            { "Oak2_RtD", "OakSapling_RtD" },
            { "Acacia1_RtD", "AcaciaSapling_RtD" },
            { "Acacia2_RtD", "AcaciaSapling_RtD" },
            { "Acacia3_RtD", "AcaciaSapling_RtD" },
            { "Acacia4_RtD", "AcaciaSapling_RtD" },
            { "PalmTree1_RtD", "PalmTreeSapling_RtD" },
            { "PalmTree2_RtD", "PalmTreeSapling_RtD" },
            { "WillowTree1_RtD", "WillowSapling_RtD" },
            { "WillowTree2_RtD", "WillowSapling_RtD" },
            { "CottonWood1_RtD", "CottonWoodSapling_RtD" },
            { "CottonWood2_RtD", "CottonWoodSapling_RtD" },
            { "Blossom1_RtD", "BlossomSapling_RtD" },
            { "Blossom2_RtD", "BlossomSapling_RtD" },
            { "Blossom3_RtD", "BlossomSapling_RtD" },
            { "Blossom4_RtD", "BlossomSapling_RtD" },
            { "Blossom5_RtD", "BlossomSapling_RtD" },
            { "RedPine1_RtD", "RedPineSapling_RtD" },
            { "RedPine2_RtD", "RedPineSapling_RtD" },
            { "RedPine3_RtD", "RedPineSapling_RtD" },
            { "RedPine4_RtD", "RedPineSapling_RtD" },
            { "RedPine_RtD", "RedPineSapling_RtD" },
            { "ThinPine1_RtD", "ThinPineSapling_RtD" },
            { "ThinPine2_RtD", "ThinPineSapling_RtD" },
            { "ForestTree1_RtD", "MapleSapling1_RtD" },
            { "ForestTree2_RtD", "MapleSapling1_RtD" },
            { "WinterPine1_RtD", "WinterPineSapling1_RtD" },
            { "WinterPine2_RtD", "WinterPineSapling1_RtD" },
            { "WinterPine3_RtD", "WinterPineSapling1_RtD" },
            { "WinterPine4_RtD", "WinterPineSapling1_RtD" },
            { "WinterPine5_RtD", "WinterPineSapling1_RtD" },
            { "WinterPine1_RtD1", "WinterPineSapling2_RtD" },
            { "WinterPine2_RtD1", "WinterPineSapling2_RtD" },
            { "WinterPine3_RtD1", "WinterPineSapling2_RtD" },
            { "WinterPine4_RtD1", "WinterPineSapling2_RtD" },
            { "WinterPine5_RtD1", "WinterPineSapling2_RtD" },
        };

        static Store _active = new Store();
        static Store _configured = new Store();
        static Dictionary<string, string> _discovered;
        // Separate from _discovered being non-null: Install() runs at CONFIG
        // LOAD time, long before ZNetScene exists, so the first discovery
        // attempt legitimately finds nothing. Caching that empty result as
        // "done" is what stopped it ever retrying once the world was actually
        // up - confirmed live as "0 discovered from Plant.m_grownPrefabs" and
        // therefore no vanilla species ever replanting. Only a run that had a
        // real ZNetScene to read counts as complete.
        static bool _discoveryComplete;

        public static void Install(Store overrides)
        {
            _configured = overrides ?? new Store();
            _discovered = null;     // re-derive against the new prefab set
            _discoveryComplete = false;
            Rebuild();
        }

        /// <summary>Resets the live-derived half so it rebuilds on next use -
        /// call when ZNetScene changes (world load), same as TreeDropCatalog.</summary>
        public static void Reset()
        {
            _discovered = null;
            _discoveryComplete = false;
        }

        static void Rebuild()
        {
            var merged = new Store();

            // Widest layer first: every sapling the GAME ITSELF knows about,
            // derived from real data rather than a hand-kept name list. A
            // Plant component's m_grownPrefabs says exactly what that sapling
            // turns into, so inverting it gives grown-tree -> sapling for
            // vanilla and for any mod's trees alike, with no per-species
            // maintenance. This is what the Defaults table below could never
            // cover: it deliberately left out every vanilla tree
            // (Birch/Oak/Beech/Fir/Pine) for want of a confident sapling
            // name, which meant a troll working a vanilla forest completed
            // the whole fell -> log -> stump chain and then silently planted
            // nothing at all.
            if (!_discoveryComplete)
            {
                _discovered = DiscoverFromPlants(out _discoveryComplete);
            }
            foreach (var kv in _discovered) merged.ByTreeName[kv.Key] = kv.Value;

            // Then the curated table, which exists to DISAMBIGUATE where the
            // game's own data is genuinely ambiguous (several RtDBiomes
            // species share one stump/log asset), so it outranks discovery.
            foreach (var kv in Defaults) merged.ByTreeName[kv.Key] = kv.Value;

            // Config always has the last word.
            foreach (var kv in _configured.ByTreeName) merged.ByTreeName[kv.Key] = kv.Value;

            _active = merged;

            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[logging] replant mapping active: {_active.ByTreeName.Count} species " +
                    $"({_discovered.Count} discovered from Plant.m_grownPrefabs, " +
                    $"{Defaults.Count} curated, {_configured.ByTreeName.Count} from config).");
        }

        /// <summary>grown-tree prefab name -> sapling prefab name, read off
        /// every Plant component registered in ZNetScene.</summary>
        static Dictionary<string, string> DiscoverFromPlants(out bool complete)
        {
            complete = false;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ZNetScene.instance == null) return map;
            var prefabs = ZNetScene.instance.m_prefabs;
            if (prefabs == null) return map;
            complete = true;   // a real prefab list was read - don't retry

            foreach (var go in prefabs)
            {
                if (go == null) continue;
                var plant = go.GetComponent<Plant>();
                if (plant == null || plant.m_grownPrefabs == null) continue;

                foreach (var grown in plant.m_grownPrefabs)
                {
                    if (grown == null) continue;
                    // First sapling that claims a given grown tree wins, so a
                    // later duplicate can't quietly overwrite a good match.
                    if (!map.ContainsKey(grown.name)) map[grown.name] = go.name;
                }
            }
            return map;
        }

        public static bool TryGetSapling(string treeName, out string saplingName)
        {
            saplingName = null;
            if (string.IsNullOrEmpty(treeName)) return false;

            // ZNetScene isn't up yet at config-load time, so the discovered
            // half is built on first real use instead - and keeps retrying
            // until a run actually had a prefab list to read.
            if (!_discoveryComplete) Rebuild();

            return _active.ByTreeName.TryGetValue(treeName, out saplingName);
        }
    }
}
