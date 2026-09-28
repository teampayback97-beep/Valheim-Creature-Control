using System;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Vanilla's own taming timer only ticks while the creature is actually
    /// loaded: Tameable.TamingUpdate runs on an InvokeRepeating tied to the
    /// GameObject's lifetime, which only exists while a player is close
    /// enough to keep that zone simulated. Leave the area and the countdown
    /// simply stops - it has no memory of how long you were gone, and it
    /// never makes up the difference on its own.
    ///
    /// This patches Tameable.Awake (which fires every time the creature's
    /// GameObject re-instantiates - on first spawn AND every time you come
    /// back into range) to stamp a last-seen timestamp in its ZDO, then on
    /// the next Awake retroactively credits the elapsed time as taming
    /// progress - capped at however long it would genuinely have stayed fed
    /// for. A short trip resumes as if nothing happened; a long one leaves
    /// it hungry at exactly the point it would have gone hungry anyway, on
    /// its own vanilla feeding math - never further ahead than a player
    /// standing there the whole time would have gotten it.
    ///
    /// Applies to every Tameable creature in the game, vanilla or modded -
    /// this touches only the base game's own component, nothing specific to
    /// any particular taming mod's config.
    /// </summary>
    static class OfflineTaming
    {
        public const string ZdoLastSeenKey = "CC_tameLastSeen";

        [HarmonyPatch(typeof(Tameable), "Awake")]
        static class Patch_Tameable_Awake_Catchup
        {
            static void Postfix(Tameable __instance)
            {
                if (!Plugin.OfflineTamingEnabled) return;

                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

                var zdo = nview.GetZDO();
                if (zdo == null) return;

                long lastSeenTicks = zdo.GetLong(ZdoLastSeenKey, -1L);
                long nowTicks = ZNet.instance.GetTime().Ticks;
                zdo.Set(ZdoLastSeenKey, nowTicks);

                // Never seen before (freshly spawned, never yet unloaded) -
                // nothing to catch up, just start the clock.
                if (lastSeenTicks < 0) return;
                if (__instance.IsTamed()) return;

                double awaySeconds = new TimeSpan(nowTicks - lastSeenTicks).TotalSeconds;
                if (awaySeconds <= 0) return;

                // No record of what happened while unloaded, so a creature
                // that comes back already fighting is left alone rather than
                // guessed at - skip the credit entirely instead of rewarding
                // or punishing a fight nobody watched.
                var mai = __instance.GetComponent<MonsterAI>();
                if (mai != null && mai.IsAlerted()) return;

                // How much of the time away happens to fall BEFORE it would
                // have gone hungry, using vanilla's own feeding math
                // (Tameable.IsHungry: now - lastFed > fedDuration). Credit
                // only that portion, so this produces exactly the same
                // "hungry or not" verdict vanilla would reach on its own the
                // moment it re-loads - it just also advances the timer for
                // the stretch that was genuinely still fed.
                long lastFedTicks = zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L);
                double sinceFedAtDeparture = new TimeSpan(lastSeenTicks - lastFedTicks).TotalSeconds;
                double stillFedFor = __instance.m_fedDuration - sinceFedAtDeparture;
                double credit = Math.Max(0, Math.Min(awaySeconds, stillFedFor));
                if (credit <= 0) return;

                float remaining = zdo.GetFloat(ZDOVars.s_tameTimeLeft, __instance.m_tamingTime);
                remaining = Mathf.Max(0f, remaining - (float)credit);
                zdo.Set(ZDOVars.s_tameTimeLeft, remaining);

                // Deliberately not calling Tame() here even if remaining hit
                // 0 - Awake() already scheduled TamingUpdate 3s out (same
                // InvokeRepeating vanilla always starts), and that tick's own
                // GetRemainingTime() <= 0 check finishes the job with all of
                // vanilla's normal side effects (message, effects, MakeTame).
                // Duplicating that here would just be racing it.

                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[CC tame-catchup] {__instance.name}: credited {credit:0}s away " +
                        $"(of {awaySeconds:0}s), {remaining:0}s taming left.");
            }
        }
    }
}
