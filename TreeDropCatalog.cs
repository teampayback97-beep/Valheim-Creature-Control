using System;
using System.Collections.Generic;
using System.Linq;

namespace CreatureControl
{
    /// <summary>
    /// Every item name a tree, stump, or rock/ore can drop - built once, live,
    /// by reading the actual DropTable on every registered prefab in
    /// ZNetScene, rather than hand-maintaining a list. This is what lets the
    /// logging troll reserve a dedicated carry slot for anything a tree can
    /// possibly drop (vanilla or any tree mod, RtDBiomes included) without
    /// ever needing to be told about a specific mod's items by name.
    ///
    /// Scoped the same way TrollLogging's own world scan is: TreeBase/TreeLog
    /// for trees and logs, name-matched WearNTear for stumps (a stump is
    /// never a TreeBase/TreeLog - see TrollLogging.AddStumps), and
    /// MineRock/MineRock5 for rock and ore, so a random bush or mushroom's
    /// DropOnDestroyed table never pollutes the set.
    /// </summary>
    public static class TreeDropCatalog
    {
        static HashSet<string> _items;

        public static void Reset() => _items = null;

        public static bool Contains(string itemName)
        {
            EnsureBuilt();
            return _items != null && itemName != null && _items.Contains(itemName);
        }

        static void EnsureBuilt()
        {
            if (_items != null) return;
            if (ZNetScene.instance == null) return;
            var prefabs = ZNetScene.instance.m_prefabs;
            if (prefabs == null) return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var go in prefabs)
            {
                if (go == null) continue;

                var tree = go.GetComponent<TreeBase>();
                if (tree != null) AddDrops(set, tree.m_dropWhenDestroyed);

                var log = go.GetComponent<TreeLog>();
                if (log != null) AddDrops(set, log.m_dropWhenDestroyed);

                // Stumps and permanently-small tree variants are Destructible
                // + DropOnDestroyed, never WearNTear, and their prefab names
                // follow no reliable convention (see
                // TrollLogging.IsTreeDestructible - this was the root cause of
                // stumps never being detected at all). DestructibleType.Tree
                // is the precise filter: Destructible alone would also pull in
                // every barrel and crate's loot table.
                var dest = go.GetComponent<Destructible>();
                if (dest != null && dest.m_destructibleType == DestructibleType.Tree)
                {
                    var stumpDrop = go.GetComponent<DropOnDestroyed>();
                    if (stumpDrop != null) AddDrops(set, stumpDrop.m_dropWhenDestroyed);
                }

                var rock = go.GetComponent<MineRock>();
                if (rock != null) AddDrops(set, rock.m_dropItems);

                var rock5 = go.GetComponent<MineRock5>();
                if (rock5 != null) AddDrops(set, rock5.m_dropItems);

                // LetMeTameYou's own taming eggs (NeckEgg_LMTY,
                // SerpentEgg_LMTY, ...) aren't tree drops at all - a
                // different mod's items entirely - but the troll should
                // still scoop one up if it walks past one. Matched by its
                // naming convention rather than listed by hand, so a new
                // creature's egg in a future LMTY update needs no code
                // change here to be picked up.
                if (go.name.IndexOf("Egg", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    go.name.EndsWith("_LMTY", StringComparison.OrdinalIgnoreCase) &&
                    go.GetComponent<ItemDrop>() != null)
                {
                    set.Add(go.name);
                }
            }

            // Confirmed by name in the log, not guessed: "vh_egg" comes from
            // Valharvest ("Added vh_egg drop to seagulls"), a plain fixed
            // name with no shared convention to pattern-match like LMTY's
            // "*_LMTY" suffix - so it's just listed explicitly here.
            foreach (var extra in ManualAdditions) set.Add(extra);

            _items = set;
            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[logging] tree/ore drop catalog built: {set.Count} distinct item(s) - " +
                    string.Join(", ", set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        }

        // Same name hints TrollLogging.AddStumps uses - kept in one place
        // would require cross-referencing at build time for no real benefit,
        // since both lists are this short and change together in practice.
        static readonly string[] StumpNameHints = { "stump", "stub" };

        // Confirmed tree/wildlife-adjacent drops that don't show up in any
        // scanned DropTable and have no naming convention to pattern-match -
        // add more here by hand as they turn up.
        static readonly string[] ManualAdditions = { "vh_egg" };

        static void AddDrops(HashSet<string> set, DropTable table)
        {
            if (table?.m_drops == null) return;
            foreach (var d in table.m_drops)
                if (d.m_item != null) set.Add(d.m_item.name);
        }
    }
}
