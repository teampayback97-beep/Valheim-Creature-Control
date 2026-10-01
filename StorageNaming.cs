using System;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Lets the player name a vanilla Container (chest) - which has no naming
    /// support of its own, unlike ships/portals/tamed creatures - and exposes
    /// whether a given item may be deposited into a named one. This is a
    /// filter on the LOGGING TROLL's own automated deposit only; a player can
    /// always put anything into any chest by hand, named or not. An unnamed
    /// chest is a catch-all and still accepts anything from the troll too.
    /// </summary>
    public static class StorageNaming
    {
        public const string ZdoNameKey = "CC_storageName";

        public static string GetName(Container container)
        {
            if (container == null) return "";
            var nview = container.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return "";
            return nview.GetZDO().GetString(ZdoNameKey, "");
        }

        /// <summary>True if a chest with this name (empty = unnamed/catch-all)
        /// may receive this item from the troll. Compares with spaces,
        /// dashes and underscores stripped so "Fine Wood" (what a player
        /// reads and types) matches the "FineWood" prefab name (what the
        /// troll's carry slot is actually keyed by).</summary>
        public static bool Accepts(string storageName, string item)
        {
            if (string.IsNullOrEmpty(storageName)) return true;

            string wanted = Normalize(storageName);
            string have = Normalize(item);

            if (string.Equals(wanted, have, StringComparison.OrdinalIgnoreCase)) return true;

            // Category names accept a whole family rather than one exact
            // item, so one chest can hold every kind of a thing without
            // needing a chest per species.
            return MatchesCategory(wanted, have);
        }

        /// <summary>Family matching for chests named after a CATEGORY rather
        /// than a single item. Kept to substrings of the item's own prefab
        /// name so it works for modded items too, with no per-item list.
        ///
        /// "Seed"/"Seeds" covers everything plantable: anything with "seed"
        /// in its name (BeechSeeds, CarrotSeeds, AncientSeed, and any mod's
        /// equivalent), plus the two families that are seeds in function but
        /// never say so in their name - cones (FirCone, PineCone) and acorns.
        /// </summary>
        static bool MatchesCategory(string normalizedStorageName, string normalizedItem)
        {
            if (normalizedStorageName.Equals("seed", StringComparison.OrdinalIgnoreCase) ||
                normalizedStorageName.Equals("seeds", StringComparison.OrdinalIgnoreCase))
            {
                return Has(normalizedItem, "seed")
                    || Has(normalizedItem, "cone")
                    || Has(normalizedItem, "acorn");
            }

            return false;
        }

        static bool Has(string haystack, string needle) =>
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        static string Normalize(string s) =>
            s == null ? "" : s.Replace(" ", "").Replace("-", "").Replace("_", "");

        /// <summary>Bridges vanilla's own rename dialog (the same TextInput
        /// prompt ships/portals/tamed creatures use) onto a Container.
        /// Returns false if the hovered object isn't a chest at all, so the
        /// caller knows whether to fall through to anything else the same
        /// key might otherwise do.</summary>
        public static bool TryOpenRename(GameObject hovered)
        {
            if (hovered == null) return false;
            var container = hovered.GetComponentInParent<Container>();
            if (container == null) return false;
            var nview = container.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;

            TextInput.instance.RequestText(new Receiver(nview), "$hud_rename", 20);
            return true;
        }

        class Receiver : TextReceiver
        {
            readonly ZNetView _nview;
            public Receiver(ZNetView nview) { _nview = nview; }

            public string GetText() => _nview.IsValid() ? _nview.GetZDO().GetString(ZdoNameKey, "") : "";

            public void SetText(string text)
            {
                if (!_nview.IsValid()) return;
                if (!_nview.IsOwner()) _nview.ClaimOwnership();
                if (!_nview.IsOwner()) return;
                _nview.GetZDO().Set(ZdoNameKey, text ?? "");
            }
        }
    }

    /// <summary>
    /// Shows a named chest's name on its own hover text ("Chest : WOOD"),
    /// and the rename hotkey on every chest's hover text regardless of
    /// whether it's named yet - vanilla has no idea this feature exists, so
    /// nothing shows either without this.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    static class Patch_Container_GetHoverText_StorageName
    {
        static void Postfix(Container __instance, ref string __result)
        {
            string storageName = StorageNaming.GetName(__instance);
            if (!string.IsNullOrEmpty(storageName))
            {
                var inv = __instance.GetInventory();
                bool empty = inv == null || inv.NrOfItems() == 0;
                __result = $"{__instance.m_name} : {storageName.ToUpperInvariant()}" +
                           (empty ? " ( $piece_container_empty )" : "");
            }

            __result += $"\n[<color=yellow><b>{Plugin.RenameStorageKeyLabel}</b></color>] Rename Storage";
        }
    }
}
