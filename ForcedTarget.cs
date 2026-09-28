using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Player-issued "attack this" command. Look at anything and press the
    /// hotkey: every tamed creature you have is handed the target, overriding
    /// whatever stance, Guard or fear verdict it would otherwise have reached
    /// on its own. A tame only actually acts on it while the target stays
    /// alive and within THAT tame's own leash (alertRange) - see
    /// CreatureState.SensesForcedTarget, which is what stands the order down;
    /// this class only issues it and writes the live target slot.
    /// </summary>
    static class ForcedTarget
    {
        static AccessTools.FieldRef<MonsterAI, Character> _targetField;
        static bool _bound;

        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                _targetField = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Force Target unavailable: {e.Message}");
            }
        }

        /// <summary>Overwrites the live target slot outright - unlike vanilla's
        /// own SetTarget (used for aggro-chaining band members), which only
        /// ever fills an empty slot. Replacing whatever is already there is
        /// the entire point of an override.</summary>
        public static void Force(MonsterAI ai, Character target)
        {
            Bind();
            if (_targetField == null || ai == null) return;
            if (_targetField(ai) == target) return;
            _targetField(ai) = target;
        }

        /// <summary>Hotkey entry point - see Plugin.HandleForceTargetKey.
        /// Hands the target to every tamed creature currently tracked; each
        /// one's own leash then decides whether it actually does anything
        /// about it. Returns how many tames received the order.</summary>
        public static int Command(Character target)
        {
            if (target == null) return 0;
            int n = 0;
            foreach (var st in CreatureState.AllTracked)
            {
                if (st == null || st.Chr == null || st.Mai == null) continue;
                if (!st.Chr.IsTamed() || st.Chr == target) continue;
                st.SetForcedTarget(target);
                n++;
            }
            return n;
        }
    }
}
