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

        public static void Install(Store overrides)
        {
            var merged = new Store();
            foreach (var kv in Defaults) merged.ByTreeName[kv.Key] = kv.Value;
            if (overrides != null)
                foreach (var kv in overrides.ByTreeName) merged.ByTreeName[kv.Key] = kv.Value;   // config wins
            _active = merged;

            if (Plugin.Verbose)
                Plugin.Log.LogInfo($"[logging] replant mapping active: {_active.ByTreeName.Count} species.");
        }

        public static bool TryGetSapling(string treeName, out string saplingName)
        {
            saplingName = null;
            if (string.IsNullOrEmpty(treeName)) return false;
            return _active.ByTreeName.TryGetValue(treeName, out saplingName);
        }
    }
}
