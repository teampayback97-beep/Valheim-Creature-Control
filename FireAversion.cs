using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Instinctive fire/smoke avoidance. Deliberately NOT part of the
    /// danger-score fear system in Threat.cs / CreatureState.WantsToFlee:
    /// this has nothing to do with weighing a fight. A Deathsquito does not
    /// decide fire is "too strong to fight" - it just will not fly through
    /// smoke, the same way a real mosquito won't. So it applies with no
    /// target required, and it applies even to creatures the config marks
    /// Fearless for the combat system.
    ///
    /// Uses the same EffectArea list the game itself checks to see whether a
    /// point is standing in fire (cooking, burning damage, etc.), so this
    /// reacts to anything that actually counts as fire in the game's own
    /// terms - campfires, bonfires, hearths, surtling cores in a smelter -
    /// rather than a hand-maintained list of prefab names.
    /// </summary>
    public static class FireAversion
    {
        /// <summary>
        /// Looks for the nearest active Fire-type EffectArea within range.
        /// Returns true and the source position (to flee FROM) if one is found.
        /// </summary>
        public static bool NearFire(Vector3 pos, float radius, out Vector3 firePos)
        {
            firePos = Vector3.zero;
            var areas = EffectArea.s_allAreas;
            if (areas == null || areas.Count == 0) return false;

            float bestSqr = radius * radius;
            bool found = false;

            for (int i = 0; i < areas.Count; i++)
            {
                var a = areas[i];
                if (a == null) continue;
                if ((a.m_type & EffectArea.Type.Fire) == 0) continue;

                float sq = (a.transform.position - pos).sqrMagnitude;
                if (sq <= bestSqr)
                {
                    bestSqr = sq;
                    firePos = a.transform.position;
                    found = true;
                }
            }

            return found;
        }
    }
}
