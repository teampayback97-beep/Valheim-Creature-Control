using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    // ---------------------------------------------------------------- spawn-time

    /// <summary>MonsterAI and AnimalAI both override Awake, so each is patched
    /// directly - patching BaseAI.Awake alone would never fire for them.</summary>
    [HarmonyPatch(typeof(MonsterAI), "Awake")]
    static class Patch_MonsterAI_Awake
    {
        static void Postfix(MonsterAI __instance) => Setup.Attach(__instance);
    }

    [HarmonyPatch(typeof(AnimalAI), "Awake")]
    static class Patch_AnimalAI_Awake
    {
        static void Postfix(AnimalAI __instance) => Setup.Attach(__instance);
    }

    internal static class Setup
    {
        /// <summary>
        /// Attaches state only. The actual rule resolution happens in
        /// CreatureState.Start(), which Unity guarantees to run after every
        /// Awake - including FactionAssigner's, so by then the creature's
        /// faction has settled and [@Faction] rules resolve correctly.
        /// </summary>
        public static CreatureState Attach(BaseAI ai)
        {
            if (ai == null) return null;

            // Relations alone are reason enough to track a creature: grudges,
            // Neutral relations and stance cycling all need per-creature state
            // even when no rule names it by prefab.
            bool wanted = CreatureRules.AnyRules || FactionRegistry.HasAnyRelations;

            var go = ai.gameObject;
            var existing = go.GetComponent<CreatureState>();
            if (!wanted) return existing;   // caller decides whether to restore

            var chr = go.GetComponent<Character>();
            if (chr == null) return null;

            var st = existing ?? go.AddComponent<CreatureState>();
            st.Chr = chr;
            st.Ai = ai;
            st.Mai = ai as MonsterAI;
            st.Nview = go.GetComponent<ZNetView>();
            st.Prefab = CreatureRules.CleanName(go.name);
            st.Register();
            st.CaptureOriginals();
            // NB: the stored stance is read in ResolveAndApply, not here - the
            // ZDO isn't reliably live yet during Awake.
            return st;
        }
    }

    /// <summary>
    /// Tamed and wild creatures can want different senses and stances, so redo
    /// the resolution when that flips - but ONLY when it actually flips.
    /// SetTamed early-returns on an unchanged value while this postfix still
    /// runs, and MonsterAI.MakeTame() calls SetTamed(true) unconditionally. Mods
    /// that call MakeTame on an already-tamed pet (BetterTames does, on every
    /// teleport tick) would otherwise drag a full AI re-apply along with them
    /// several times a second, ZDO writes and all.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.SetTamed))]
    static class Patch_Character_SetTamed
    {
        static void Postfix(Character __instance)
        {
            var st = CreatureState.For(__instance);
            // Only redo the work when the tamed state genuinely flipped. This
            // postfix also runs when SetTamed was a no-op, which BetterTames
            // triggers on every pet teleport by way of MakeTame().
            if (st != null && st.TamedStateChanged()) st.ResolveAndApply();
        }
    }

    // ------------------------------------------------------------- the main hook

    /// <summary>
    /// Every hostility decision in the game funnels through this one static
    /// method - the instance overload is literally "return IsEnemy(m_character,
    /// other)" - which makes it the right and only place to express stances,
    /// custom faction relationships and the tamed/wild split.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    static class Patch_BaseAI_IsEnemy
    {
        // Most paths here return false, which also suppresses any prefix another
        // mod has on this method. Running last means we only do that after
        // everyone else has had their say.
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(Character a, Character b, ref bool __result)
        {
            if (a == null || b == null || a == b) return true;

            // The player's own view of the world stays vanilla's: hostile to
            // everything except Dvergr and their own tames. This table is
            // SYMMETRIC, so without this a "Friendly" written to stop boars
            // charging you would equally stop you swinging at boars - green
            // health bar, no valid target. A creature's peacefulness belongs
            // to its stance and to its own side of the relation, never to
            // making it unattackable.
            if (a is Player) return true;

            // GetFaction() is a plain field read - it does NOT collapse to
            // Players for tamed creatures, so a tame keeps its real faction.
            int fa = (int)a.GetFaction();
            int fb = (int)b.GetFaction();

            // --- 1. untargetable -----------------------------------------------
            // Checked first so that even a faction whose default stance is Enemy
            // cannot pick a fight with a butterfly. This is also what stops your
            // pets breaking off to swat insects: vanilla's tamed rule returns
            // true against anything untamed, ambient wildlife included.
            // Untargetable factions are always custom ids, so two integer
            // compares rule out the common case before we touch a hash set.
            // This runs on every hostility check in the game, so it matters.
            if (FactionRegistry.AnyUntargetable &&
                (FactionRegistry.IsCustom(fa) || FactionRegistry.IsCustom(fb)) &&
                (FactionRegistry.IsUntargetable(fa) || FactionRegistry.IsUntargetable(fb)))
            {
                __result = false;
                return false;
            }

            bool aTamed = a.IsTamed(), bTamed = b.IsTamed();

            // --- 2. tamed creatures are protected, unconditionally -------------
            // Hoisted above everything else so no later rule - not a grudge, not
            // a faction table - can ever turn a pet on its owner or on another
            // pet. Mirrors what vanilla does, just earlier.
            if (aTamed && bTamed) { __result = false; return false; }
            if (aTamed && fb == (int)Character.Faction.Players) { __result = false; return false; }
            if (bTamed && fa == (int)Character.Faction.Players) { __result = false; return false; }

            // --- 3. behaviour mode ---------------------------------------------
            // Gated on 'a' only, and that is deliberate: the game always asks
            // "is this an enemy of ME", passing self first. So a passive creature
            // stops picking fights without becoming immune to being attacked.
            var st = CreatureState.For(a);
            if (st != null)
            {
                switch (st.Mode)
                {
                    case BehaviorMode.Passive:
                        __result = false;
                        return false;

                    case BehaviorMode.Neutral:
                        // Holding a grudge settles it outright. Falling through
                        // to vanilla instead would let it answer "same faction,
                        // not an enemy" and quietly drop the retaliation.
                        __result = st.HoldsGrudge(b);
                        return false;
                }
            }

            // --- 4. tamed vs its own wild kind ---------------------------------
            // Vanilla vetoes on the shared creature "group" before it ever
            // reaches its tamed logic, which is the sole reason a tamed wolf
            // won't defend you from a wild one. A tamed creature has left its
            // old pack, so that veto shouldn't apply to it.
            if (aTamed != bTamed)
            {
                var group = a.GetGroup();
                if (!string.IsNullOrEmpty(group) && group == b.GetGroup())
                {
                    __result = true;
                    return false;
                }
            }

            // --- 5. the relation table -----------------------------------------
            // Gated on there being any relations at all, not on custom factions:
            // re-wiring two vanilla factions against each other is legitimate.
            if (!FactionRegistry.HasAnyRelations) return true;

            // A pair the config names outright wins over everything below,
            // including the creature group that normally stops one troll
            // attacking another. That check is precisely what would defeat
            // "Feral : Feral = Enemy", so an explicit line has to outrank it.
            if (FactionRegistry.TryGetExplicitRelation(fa, fb, out var explicitRel))
            {
                __result = Resolve(explicitRel, st, b);
                return false;
            }

            var ga = a.GetGroup();
            if (!string.IsNullOrEmpty(ga) && ga == b.GetGroup()) { __result = false; return false; }

            if (FactionRegistry.TryGetRelation(fa, fb, out var rel))
            {
                __result = Resolve(rel, st, b);
                return false;
            }

            return true;
        }

        /// <summary>Neutral means "ignores, but remembers who hurt it".</summary>
        static bool Resolve(Relation rel, CreatureState st, Character other)
        {
            switch (rel)
            {
                case Relation.Enemy: return true;
                case Relation.Neutral: return st != null && st.HoldsGrudge(other);
                default: return false;
            }
        }
    }

    // ------------------------------------------------------------------- fear

    /// <summary>
    /// The danger-score check. Runs as an UpdateAI prefix because deciding not
    /// to engage has to happen before the AI acts on its target.
    ///
    /// BetterTames already holds two prefixes here - a knockout timer and a stun
    /// guard - and both exist to return false and skip the tick entirely. Ours
    /// must therefore run LAST, so a stunned or knocked-out pet is never asked
    /// whether it feels brave, and it must tolerate being skipped on any given
    /// tick: every piece of state it keeps is driven by Time.time rather than by
    /// a tick count.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    [HarmonyAfter("Koro.bettertames")]
    static class Patch_MonsterAI_UpdateAI
    {
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            // If BaseAI.Flee could not be bound there is nothing to route the
            // creature into, and taking the tick anyway would leave it standing
            // still forever. Fall through to vanilla instead.
            if (!Plugin.FearEnabled || !Threat.Available) return true;

            var st = CreatureState.For(__instance.gameObject.GetComponent<Character>());
            if (st == null || !st.FearApplies) return true;

            // A creature we tried and failed to move is handed back to vanilla
            // for a while. Worst case the fear system does nothing; it must never
            // be worse than not having it.
            if (st.FearBroken) return true;

            // No target means nothing to be afraid of, and it also means vanilla
            // needs this tick to go looking for one.
            var target = __instance.GetTargetCreature();
            if (target == null) { st.ForgetFear(); return true; }

            if (!st.WantsToFlee(target, out var from)) return true;

            // From here we are taking the tick over, so we owe the creature
            // everything MonsterAI.UpdateAI would have done before its own flee
            // branch. Its first two statements are:
            //
            //     if (!base.UpdateAI(dt)) return false;
            //     UpdateSleep(dt); if (IsSleeping()) return true;
            //
            // base.UpdateAI is the shared tick - the ZNetView ownership gate,
            // takeoff/landing, regeneration, and the jump, random-move and
            // time-since-hurt timers. Skipping it is why the first build left
            // creatures standing still: they had decided to run and then never
            // got the tick that moves them.
            if (!Threat.BaseTick(__instance, dt)) { __result = false; return false; }

            // A sleeping creature has no opinion about the odds. Vanilla checks
            // this before it would ever reach a flee, and waking on fear alone
            // would make every sleeping troll and Bjorn bolt on sight.
            if (__instance.IsSleeping()) { __result = true; return false; }

            // BaseAI.Flee ends with:
            //     MoveTo(dt, m_fleeTarget, 1f, IsAlerted());
            // and MoveTo's fourth argument is `run`. An UNALERTED creature
            // therefore ambles away from what it fears; only an alerted one
            // actually sprints. Alerting it first is the whole difference
            // between a deer strolling off and a deer bolting - and it brings
            // the alerted animation and sound along with it.
            //
            // SetAlerted early-returns when the value is unchanged, so this is
            // free after the first tick and writes no ZDO while it runs.
            __instance.Alert();

            // NB: BaseAI.Flee returns MoveTo's result, and MoveTo returns TRUE
            // when it has STOPPED - either arrived or failed to path - and FALSE
            // while it is still running. It is not a success flag, so it tells us
            // nothing useful about whether the creature got away. Progress is
            // measured by whether it actually moved.
            Threat.Flee(__instance, dt, from);
            st.NoteFleeProgress(__instance.transform.position);

            __result = true;
            return false;
        }
    }

    // ------------------------------------------------------------- retaliation

    // MonsterAI and AnimalAI each override OnDamaged, so both are patched.
    // Recording a grudge is idempotent, so an extra call costs nothing.

    [HarmonyPatch(typeof(MonsterAI), "OnDamaged")]
    static class Patch_MonsterAI_OnDamaged
    {
        static void Postfix(MonsterAI __instance, Character attacker) =>
            Grudges.Record(__instance, attacker);
    }

    [HarmonyPatch(typeof(AnimalAI), "OnDamaged")]
    static class Patch_AnimalAI_OnDamaged
    {
        static void Postfix(AnimalAI __instance, Character attacker) =>
            Grudges.Record(__instance, attacker);
    }

    /// <summary>
    /// The authoritative damage hook. BaseAI.OnDamaged(float, Character) turned
    /// out to be reached only through the Character.m_onDamaged delegate and is
    /// never called directly by the game, which made it a shaky foundation for
    /// retaliation - and it tells us nothing when the PLAYER is hit.
    ///
    /// ApplyDamage is declared on Character and is not overridden by Humanoid
    /// or Player, so this single postfix covers every creature and the player.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    static class Patch_Character_ApplyDamage
    {
        static void Postfix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null) return;

            var attacker = hit.GetAttacker();
            if (attacker == null || attacker == __instance) return;

            // 1. The victim remembers who hurt it.
            Grudges.Record(__instance, attacker);

            // 2. A pet should defend its owner, not just itself. Without this a
            //    Neutral pet only ever fights things that hit IT, so it watches
            //    you get mauled - which is not what "neutral" should mean for
            //    something that chose to follow you.
            if (__instance is Player) Grudges.PropagateToPets(__instance, attacker);
        }
    }

    internal static class Grudges
    {
        public static void Record(BaseAI ai, Character attacker)
        {
            if (ai == null) return;
            Record(ai.gameObject.GetComponent<Character>(), attacker);
        }

        public static void Record(Character victim, Character attacker)
        {
            if (victim == null || attacker == null) return;
            var st = CreatureState.For(victim);
            if (st == null) return;

            st.Remember(attacker);

            // A neutral creature has been ignoring the world; nudge it awake or
            // it will stand there until its own slow target scan comes round.
            if (st.Mode == BehaviorMode.Neutral && st.Ai != null) st.Ai.Alert();
        }

        /// <summary>Hand the owner's attacker to every tame currently following
        /// them. The pet's own stance still decides what it does with that: a
        /// Passive pet is filtered out before the grudge is ever consulted.</summary>
        // Reused so this costs no allocation in combat. Snapshotting also keeps
        // us safe if Alert() ends up removing a creature from the registry
        // mid-iteration.
        static readonly System.Collections.Generic.List<CreatureState> _petBuffer =
            new System.Collections.Generic.List<CreatureState>();

        public static void PropagateToPets(Character owner, Character attacker)
        {
            if (owner == null || attacker == null) return;
            var ownerGo = owner.gameObject;

            _petBuffer.Clear();
            foreach (var s in CreatureState.AllTracked) _petBuffer.Add(s);

            foreach (var st in _petBuffer)
            {
                if (st == null || st.Mai == null || st.Chr == null) continue;
                if (!st.Chr.IsTamed()) continue;
                if (st.Chr == attacker) continue;
                if (st.Mai.GetFollowTarget() != ownerGo) continue;

                st.Remember(attacker);
                if (st.Mode == BehaviorMode.Neutral && st.Ai != null) st.Ai.Alert();
            }
        }
    }

    // ----------------------------------------------------------- hover readout

    /// <summary>
    /// Shows the current stance on the pet's hover panel. We deliberately do
    /// NOT patch Tameable.Interact: vanilla already uses alt-interact there to
    /// rename a pet, and hold-interact repeats every frame. Stance cycling is
    /// a hotkey in Plugin.Update instead.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    static class Patch_Tameable_GetHoverText
    {
        static void Postfix(Tameable __instance, ref string __result)
        {
            if (!Plugin.AllowStanceCycling) return;
            if (!__instance.IsTamed()) return;

            var st = CreatureState.For(__instance.gameObject.GetComponent<Character>());
            if (st == null || st.Rule == null || st.Rule.StanceCycling == false) return;

            __result += $"\n[<color=yellow><b>{Plugin.CycleKeyLabel}</b></color>] stance: " +
                        $"<color=orange>{st.Mode.Pretty()}</color>";
        }
    }
}
