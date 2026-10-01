using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Vanilla's own taming loop only runs while the creature is actually
    /// loaded: Tameable.TamingUpdate (the timer) and MonsterAI.UpdateConsumeItem
    /// (the "find and eat nearby food" behaviour) are both driven by that
    /// GameObject's own Update, which only exists while a player is close
    /// enough to keep the zone simulated. Leave the area and both stop cold -
    /// no memory, no catch-up, whatever food is sitting there just waits.
    ///
    /// This patches Tameable.Awake (fires on first spawn AND every time the
    /// creature re-instantiates after being unloaded) to work out how long it
    /// was actually gone, then replays what MonsterAI.UpdateConsumeItem would
    /// have done for that whole stretch: chain through however many nearby
    /// matching food items (MonsterAI.m_consumeItems - the same list a taming
    /// mod's consumeItems config populates) it would have needed to eat to
    /// stay fed the entire time, actually consuming them from the world, and
    /// crediting the full stretch of taming progress that food bought. Once
    /// the supply runs out it stops exactly where vanilla's own hunger check
    /// would - it can never progress further than a real player standing
    /// there feeding it by hand would have gotten it, just without requiring
    /// anyone to physically watch it happen.
    ///
    /// UpdateConsumeItem is MonsterAI-only (AnimalAI has no such thing), so
    /// this only ever applies to MonsterAI-driven tames - which covers every
    /// creature big enough to need a multi-feed taming in the first place.
    /// </summary>
    static class OfflineTaming
    {
        public const string ZdoLastSeenKey = "CC_tameLastSeen";
        static int _itemMask = -1;
        static readonly Collider[] _hits = new Collider[64];

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

                // Never seen before (freshly spawned, or the very first Awake
                // since this feature shipped - there is no prior stamp to
                // diff against either way) - nothing to catch up, just start
                // the clock. Logged, not silent: a first-ever sighting and a
                // broken patch both produce zero further output otherwise,
                // and they need to look different in a report.
                if (lastSeenTicks < 0)
                {
                    if (Plugin.Verbose)
                    {
                        Plugin.Log.LogInfo($"[CC tame-catchup] {__instance.name}: first sighting, starting the clock.");
                        LogConsumeList(__instance);
                    }
                    return;
                }
                if (__instance.IsTamed()) return;

                double awaySeconds = new TimeSpan(nowTicks - lastSeenTicks).TotalSeconds;
                if (awaySeconds <= 0) return;

                // No record of what happened while unloaded, so a creature
                // that comes back mid-fight is left alone rather than guessed
                // at - skip the whole catch-up instead of rewarding or
                // punishing a fight nobody watched.
                var mai = __instance.GetComponent<MonsterAI>();
                if (mai == null)
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo($"[CC tame-catchup] {__instance.name}: no MonsterAI, skipping (away {awaySeconds:0}s).");
                    return;
                }
                if (mai.IsAlerted())
                {
                    if (Plugin.Verbose)
                        Plugin.Log.LogInfo($"[CC tame-catchup] {__instance.name}: alerted on reload, skipping (away {awaySeconds:0}s).");
                    return;
                }

                float fedDuration = __instance.m_fedDuration;
                if (fedDuration <= 0f) return;

                long lastFedTicks = zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L);
                double sinceFedAtDeparture = new TimeSpan(lastSeenTicks - lastFedTicks).TotalSeconds;
                double stillFedFor = fedDuration - sinceFedAtDeparture;

                if (awaySeconds <= stillFedFor)
                {
                    // Was never going to go hungry during the whole gap - no
                    // food needed, so no physics scan and no race to worry
                    // about. Credit it right now.
                    Credit(zdo, __instance, awaySeconds, awaySeconds, eaten: 0, nowTicks, bumpFeeding: false);
                    return;
                }

                // Needs food, which means a physics scan for nearby items.
                // Tameable.Awake fires the instant THIS creature's own
                // sector finishes loading, with no guarantee a neighbouring
                // sector the food pile sits in has finished too - scanning
                // right now can find nothing even though the pile is sitting
                // right there, because its GameObjects simply don't exist
                // yet. Deferring the scan a few seconds gives that neighbour
                // time to load, same reasoning as vanilla scheduling its own
                // TamingUpdate 3s out rather than running it inline here.
                __instance.StartCoroutine(
                    DeferredConsume(__instance, mai, zdo, awaySeconds, stillFedFor, fedDuration, nowTicks));
            }
        }

        /// <summary>Logs exactly what this creature will accept as food, and
        /// how close it has to get. Added because "it isn't sensing the food"
        /// is impossible to tell apart from "that isn't food to it" or "it
        /// can't physically reach it" without seeing the real list - and the
        /// list lives in prefab data as references, not names, so only a
        /// runtime read resolves it.
        ///
        /// m_consumeRange is the one that catches people out: a creature can
        /// SEE food across m_consumeSearchRange (10m for a boar) but has to
        /// get within m_consumeRange (1m) to actually eat it. Penned up with
        /// food just out of reach, it starves next to a full pile.</summary>
        static void LogConsumeList(Tameable tameable)
        {
            var mai = tameable != null ? tameable.GetComponent<MonsterAI>() : null;
            if (mai == null) return;

            var items = mai.m_consumeItems;
            if (items == null || items.Count == 0)
            {
                Plugin.Log.LogInfo($"[CC tame] {tameable.name}: eats NOTHING - empty consume list.");
                return;
            }

            var names = new List<string>(items.Count);
            foreach (var it in items)
            {
                if (it == null) { names.Add("<null>"); continue; }
                string n = it.name;
                var shared = it.m_itemData?.m_shared;
                if (shared != null && !string.IsNullOrEmpty(shared.m_name)) n += $" ({shared.m_name})";
                names.Add(n);
            }

            Plugin.Log.LogInfo(
                $"[CC tame] {tameable.name}: eats [{string.Join(", ", names)}] - " +
                $"must be within {mai.m_consumeRange}m to eat (searches {mai.m_consumeSearchRange}m).");
        }

        static IEnumerator DeferredConsume(
            Tameable tameable, MonsterAI mai, ZDO zdo,
            double awaySeconds, double stillFedFor, float fedDuration, long nowTicks)
        {
            yield return new WaitForSeconds(Plugin.OfflineTamingScanDelay);

            // Re-check rather than trust state from several seconds ago - the
            // creature could have been tamed, killed, or picked a real fight
            // in the meantime.
            if (tameable == null || tameable.IsTamed()) yield break;
            if (mai == null || mai.IsAlerted()) yield break;

            double alreadyFed = Math.Max(0, stillFedFor);
            double gapNeedingFood = awaySeconds - alreadyFed;
            int neededEats = (int)Math.Ceiling(gapNeedingFood / fedDuration);

            int eaten = ConsumeNearbyFood(tameable, mai, neededEats);
            double covered = Math.Min(awaySeconds, alreadyFed + eaten * (double)fedDuration);

            // Real history predates whatever we just fed it - if that
            // bridged the entire gap, the stored feeding time is stale and
            // would read hungry a moment from now purely because we didn't
            // also move it forward. If it DIDN'T bridge the whole gap, the
            // real stale timestamp already (correctly) reads hungry on its
            // own - leave it be.
            bool bumpFeeding = eaten > 0 && covered >= awaySeconds - 0.01;
            Credit(zdo, tameable, awaySeconds, covered, eaten, nowTicks, bumpFeeding);
        }

        static void Credit(
            ZDO zdo, Tameable tameable, double awaySeconds, double covered,
            int eaten, long nowTicks, bool bumpFeeding)
        {
            if (bumpFeeding) zdo.Set(ZDOVars.s_tameLastFeeding, nowTicks);
            if (covered <= 0)
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[CC tame-catchup] {tameable.name}: away {awaySeconds:0}s, already hungry " +
                        $"before it unloaded and nothing nearby to eat - no credit.");
                return;
            }

            float remaining = zdo.GetFloat(ZDOVars.s_tameTimeLeft, tameable.m_tamingTime);
            remaining = Mathf.Max(0f, remaining - (float)covered);
            zdo.Set(ZDOVars.s_tameTimeLeft, remaining);

            // Deliberately not calling Tame() here even if remaining hit 0 -
            // Awake() already scheduled TamingUpdate 3s out (same
            // InvokeRepeating vanilla always starts), and that tick's own
            // GetRemainingTime() <= 0 check finishes the job with all of
            // vanilla's normal side effects (message, effects, MakeTame).
            // Duplicating that here would just be racing it.

            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC tame-catchup] {tameable.name}: away {awaySeconds:0}s, ate " +
                    $"{eaten} item(s), credited {covered:0}s, {remaining:0}s taming left.");
        }

        /// <summary>
        /// Replays MonsterAI.UpdateConsumeItem's search-and-eat loop up to
        /// <paramref name="maxEats"/> times, using the creature's own
        /// m_consumeItems/m_consumeSearchRange, and actually removes what it
        /// eats via ItemDrop.RemoveOne - the same call vanilla itself makes,
        /// so stack counts, network sync and despawn-when-empty all behave
        /// exactly as if it had eaten them for real. Movement and pathing are
        /// deliberately not simulated: over any gap long enough for this to
        /// matter, a real MonsterAI would have had ample time to walk the few
        /// metres from itself to food already within its search range.
        /// </summary>
        static int ConsumeNearbyFood(Tameable tameable, MonsterAI mai, int maxEats)
        {
            if (maxEats <= 0) return 0;
            var consumeItems = mai.m_consumeItems;
            if (consumeItems == null || consumeItems.Count == 0) return 0;

            if (_itemMask < 0) _itemMask = LayerMask.GetMask("item");

            int count = Physics.OverlapSphereNonAlloc(
                tameable.transform.position, mai.m_consumeSearchRange, _hits, _itemMask);

            int eaten = 0;
            for (int i = 0; i < count && eaten < maxEats; i++)
            {
                var rb = _hits[i].attachedRigidbody;
                if (rb == null) continue;
                var drop = rb.GetComponent<ItemDrop>();
                if (drop == null) continue;
                var dropView = drop.GetComponent<ZNetView>();
                if (dropView == null || !dropView.IsValid()) continue;
                if (!Matches(consumeItems, drop.m_itemData)) continue;

                int stackHere = drop.m_itemData.m_stack;
                for (int n = 0; n < stackHere && eaten < maxEats; n++)
                {
                    if (!drop.RemoveOne()) break;
                    eaten++;
                }
            }
            return eaten;
        }

        static bool Matches(List<ItemDrop> consumeItems, ItemDrop.ItemData item)
        {
            // m_pickedUp is vanilla's own permanent per-item flag - false on
            // anything freshly spawned (a kill's loot, a container's
            // contents) and set true forever the instant it enters ANY
            // player's inventory (Player.OnInventoryChanged), surviving
            // drop/pickup and stacking since it travels with the ItemData
            // through Save/Load. Requiring it here means offline catch-up
            // can only ever credit food a player actually carried and placed
            // - never food a creature stumbled onto on its own, like a boar's
            // own meat drop lying where it died.
            if (Plugin.RequirePlayerHandledTamingFood && !item.m_pickedUp) return false;

            foreach (var c in consumeItems)
                if (c != null && c.m_itemData != null && c.m_itemData.m_shared.m_name == item.m_shared.m_name)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Same provenance rule as OfflineTaming.Matches, applied to vanilla's
    /// own LIVE self-taming loop so a wild creature can't start (or
    /// continue) taming itself off food it happened to wander onto - only
    /// food a player has actually carried counts, whether the creature is
    /// loaded or not.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), "CanConsume")]
    static class Patch_MonsterAI_CanConsume_RequireHandled
    {
        static void Postfix(MonsterAI __instance, ItemDrop.ItemData item, ref bool __result)
        {
            // Logged even when nothing is being blocked, because the silent
            // case is the confusing one: this postfix only ever sees items
            // vanilla ALREADY judged edible, so "not food to me at all" never
            // reached here and looked exactly like "blocked by the rule".
            // Naming the creature matters too - refusals were being recorded
            // with no indication of which animal they came from, so a wolf
            // rejecting meat was indistinguishable from the boar rejecting
            // berries.
            if (Plugin.Verbose && Plugin.LoggingDiagEnabled && __result)
                Plugin.Log.LogInfo(
                    $"[CC tame] {(__instance != null ? __instance.name : "?")}: " +
                    $"'{item?.m_shared?.m_name}' IS edible " +
                    $"(pickedUp={item?.m_pickedUp})");

            if (!__result || !Plugin.RequirePlayerHandledTamingFood || item.m_pickedUp) return;

            // TAMING food only. Once a creature is tamed, eating is just
            // keeping it fed - a trough, a food chest or a feeder mod's
            // dispensed items never pass through a player's hands, and
            // starving a tame because of that was never the point of this
            // rule. Wild creatures are still held to it exactly as before.
            var chr = __instance != null ? __instance.GetComponent<Character>() : null;
            if (chr != null && chr.IsTamed()) return;

            __result = false;

            // Used to reject silently, which made this rule indistinguishable
            // from "the creature just isn't taming" - a tame could sit next
            // to food for days with nothing anywhere saying why. Logged now
            // so the cause is visible: food a player never actually carried
            // (dungeon-chest loot, mod-spawned, or moved straight between
            // containers) never gets vanilla's m_pickedUp flag, and is
            // refused here by design. Turn off "Taming Food Must Be
            // Player-Handled" if that isn't wanted.
            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC tame] {(__instance != null ? __instance.name : "?")}: refused " +
                    $"'{item.m_shared?.m_name}' - not player-handled (m_pickedUp false). " +
                    "This is the Taming Food Must Be Player-Handled rule.");
        }
    }
}
