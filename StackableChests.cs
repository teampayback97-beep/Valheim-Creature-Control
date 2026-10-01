using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Lets storage be stacked directly on top of other storage, instead of
    /// needing a shelf or floor piece under every single chest.
    ///
    /// The mechanism, read straight out of Player's placement validation
    /// rather than guessed at:
    ///
    ///     if ((bool)wearNTear &amp;&amp; !wearNTear.m_supports)
    ///         m_placementStatus = PlacementStatus.Invalid;
    ///
    /// That wearNTear is the SURFACE being placed onto - so a chest whose own
    /// WearNTear.m_supports is false simply cannot be built on, and the
    /// attempt fails as "invalid placement" at click time with no warning
    /// beforehand, which is exactly the reported symptom. Nothing on the
    /// Piece component is involved at all: an earlier version of this file
    /// relaxed m_noClipping/m_groundOnly/m_notOnFloor and friends, which was
    /// a complete no-op because a vanilla chest already has every one of
    /// those set to false.
    ///
    /// Patching WearNTear.Awake rather than the prefabs is deliberate. The
    /// check runs against the WearNTear of the already-placed piece you are
    /// aiming at, so editing prefabs alone would only ever fix chests placed
    /// AFTER the change and leave every chest already standing in the world
    /// unstackable. Awake covers both, and needs no scan or timing hook.
    ///
    /// Keyed on the presence of a Container so it applies to modded storage
    /// too, not a vanilla prefab-name list. Only supporting is changed -
    /// health, decay, material and build cost are untouched, so a stacked
    /// chest still falls when whatever holds it up is destroyed.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "Awake")]
    static class Patch_WearNTear_Awake_StackableStorage
    {
        static void Postfix(WearNTear __instance)
        {
            if (!Plugin.StackableChestsEnabled) return;
            if (__instance == null || __instance.m_supports) return;
            if (__instance.GetComponent<Container>() == null) return;

            __instance.m_supports = true;
        }
    }
}
