using HarmonyLib;

namespace CreatureControl
{
    /// <summary>
    /// Stops tamed creatures damaging what the player built, without taking away
    /// anything else they can hit.
    ///
    /// WearNTear is the component buildable pieces carry. Trees, rocks and the
    /// other world destructibles use TreeBase / MineRock / Destructible instead
    /// and never come through here at all - so a pet can still flatten a forest,
    /// it just cannot knock a hole in the wall behind the boar it is chasing.
    ///
    /// This is separate from m_attackPlayerObjects, which decides whether a
    /// creature DELIBERATELY goes after structures. The damage this catches is
    /// the accidental kind: a stray swing that happens to land on a wall.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    static class Patch_WearNTear_Damage
    {
        static bool Prefix(WearNTear __instance, HitData hit)
        {
            if (!Plugin.TamesSpareStructures) return true;
            if (__instance == null || hit == null) return true;

            // Environmental damage (rain, fire, water) has no attacker.
            var attacker = hit.GetAttacker();
            if (attacker == null || !attacker.IsTamed()) return true;

            // Only shield what the player actually raised. A tame should still be
            // able to break something the world placed, like a dungeon fitting.
            var piece = __instance.GetComponent<Piece>();
            if (piece == null || !piece.IsPlacedByPlayer()) return true;

            // Swallowed here rather than later: Damage() is what sends the damage
            // out over the network, so nothing is told about the hit at all.
            return false;
        }
    }
}
