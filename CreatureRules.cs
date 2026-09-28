using System;
using System.Collections.Generic;

namespace CreatureControl
{
    /// <summary>Everything the config can say about one creature.
    /// Null means "we have no opinion - keep the game's own value".</summary>
    public class CreatureRule
    {
        public string Source;               // for logging: prefab name, @Faction, or *

        public BehaviorMode? WildBehavior;
        public BehaviorMode? TamedBehavior;

        public float? WildView;
        public float? WildHear;
        public float? TamedView;
        public float? TamedHear;

        public float? AlertRange;
        public float? MaxChaseDistance;
        public bool? EnableHuntPlayer;
        public bool? FleeIfNotAlerted;
        public float? FleeIfLowHealth;

        public string FactionName;
        public int? FactionId;

        // --- the danger-score model -------------------------------------------
        /// <summary>What this creature is worth in a fight, in Fulings.</summary>
        public float? Threat;
        /// <summary>Opts out of the fear check entirely. Elites, constructs,
        /// the mindless and everything tamed.</summary>
        public bool? Fearless;
        /// <summary>How far this creature looks for FRIENDS. Never for enemies -
        /// widening it must not make anything spot you sooner.</summary>
        public float? RallyRadius;
        /// <summary>Hunts and dies alone: never calls for help and never answers
        /// anyone else's call. For solitary hunters (a prowler, a bear, a shark)
        /// and for things with nothing to call with - insects, birds, critters.
        /// Fearless creatures already cannot call, since they never get far
        /// enough to be afraid; this also stops them answering.</summary>
        public bool? Solitary;
        /// <summary>How this creature reads a PLAYER: gear, instinct, holy or
        /// none. Creature-versus-creature threat is never affected by it.</summary>
        public Perception? Sight;
        /// <summary>Runs from anything it registers as an enemy, always, without
        /// weighing anything. For prey that would never fight back under any
        /// circumstances. Unlike a baby it is not worth zero to its side.</summary>
        public bool? AlwaysFlee;
        /// <summary>Worth nothing to its side and runs from everything it sees
        /// as an enemy. Matched by name, never by suffix - 'Hatchling' is a
        /// grown drake, not a baby.</summary>
        public bool? Baby;
        /// <summary>Opts into the herd fear-alert system (FearAlert.cs): the
        /// moment this creature panics, nearby creatures of the SAME prefab
        /// within Fear Alert Radius get told, after Fear Alert Delay. Separate
        /// from Solitary/rallying - this is reflex, not threat-weighing.</summary>
        public bool? SharesFear;

        // --- fire ---------------------------------------------------------------
        // Instinctive, independent of the fear system: nothing here is weighing
        // a fight. A Deathsquito does not decide a bonfire is too strong, it
        // just will not fly through smoke. So it needs no target and applies
        // even to a creature the config marks Fearless.

        /// <summary>The WEAKEST fire that deters this creature. Torch means even
        /// a hand torch turns it; Bonfire means only a bonfire will. None - the
        /// default - means fire is left entirely to the game's own handling.</summary>
        public FireTier? FireFear;

        /// <summary>What it does about it. Circle holds its target and waits at
        /// the edge, which is what makes a wolf sit outside your campfire light
        /// instead of losing interest. Flee drops the target and bolts.</summary>
        public FireReaction? FireReact;

        /// <summary>Multiplier on the tier's standoff distance, for creatures
        /// that want a wider berth than the tier alone gives. 1 = the tier's
        /// own reach.</summary>
        public float? FireBuffer;

        // --- day / night ---------------------------------------------------------
        // One table per half of the day. Symmetric on purpose: a nocturnal
        // hunter is weak in Day and strong in Night, a diurnal one is the same
        // values swapped, and neither needs new code. See Phase.cs.

        /// <summary>Numbers that apply while it is day. Null = no opinion.</summary>
        public PhaseStats Day;
        /// <summary>Numbers that apply while it is night. Null = no opinion.</summary>
        public PhaseStats Night;

        /// <summary>A tamed creature keeps its ordinary numbers round the clock.
        /// A pet that goes half-blind every morning is a broken pet, not a
        /// nocturnal one.</summary>
        public bool? PhaseExemptTamed;

        // --- stalking ------------------------------------------------------------

        /// <summary>How long it circles a fresh target before committing to the
        /// charge. 0 - the default - means it closes straight in, as anything
        /// does now.</summary>
        public float? StalkSeconds;
        /// <summary>How far out it circles while stalking.</summary>
        public float? StalkRadius;
        /// <summary>Inside this it stops stalking and commits, however much
        /// stalking time is left.</summary>
        public float? PounceRange;

        public bool? StanceCycling;

        // --- enrage ----------------------------------------------------------
        // Reactive, not scheduled - unlike Day/Night this is decided by a live
        // Band.ShouldBackOff verdict, read directly rather than through
        // WantsToFlee. That is deliberate: WantsToFlee (and therefore the fear
        // system) is completely untouched by any of this, which is what lets a
        // Fearless creature use it at all - Fearless is only ever a bar on
        // fleeing, never on knowing the numbers.
        /// <summary>Opts a creature into becoming Enraged whenever its own
        /// band would otherwise be judged outnumbered.</summary>
        public bool? EnrageWhenOutnumbered;
        /// <summary>Also enrages against an opponent of the exact same
        /// prefab, regardless of the numbers - two of the same apex predator
        /// meeting is a territorial fight, not an arithmetic one.</summary>
        public bool? EnrageVsSameKind;
        /// <summary>Flat threat bonus while enraged - added on top of the
        /// base Threat value, not randomised. Makes the creature read as
        /// more dangerous to everything ELSE weighing a fight against it,
        /// not just harder to kill.</summary>
        public float? EnrageThreatBonus;
        /// <summary>Resistance level applied to blunt/slash/pierce while
        /// enraged (see HitData.DamageModifier - Resistant, VeryResistant,
        /// etc). Null leaves physical resistance untouched.</summary>
        public HitData.DamageModifier? EnragePhysicalResist;
        public bool? EnragePoisonImmune;
        /// <summary>Multiplier on damage DEALT while enraged. Takes over
        /// from any Day/Night DamageMult for as long as it lasts.</summary>
        public float? EnrageDamageMult;

        // --- always-on aggression pacing --------------------------------------
        // Not a phase, not an enrage trigger - a flat replacement for how
        // readily this creature re-engages after attacking. Vanilla's own
        // MonsterAI periodically disengages a creature to circle its target
        // (m_circleTargetInterval) and/or wanders around it between attacks
        // (m_circulateWhileCharging) - fine for most wildlife, but not for
        // something meant to fight like it never lets up.
        public float? MinAttackInterval;
        /// <summary>0 disables vanilla's periodic forced disengage-and-circle
        /// outright - the check it drives is gated on this being > 0.</summary>
        public float? CircleTargetInterval;
        public bool? CirculateWhileCharging;

        // --- troll logging ---------------------------------------------------
        /// <summary>Config permission for the logging toggle. Unlike
        /// StanceCycling this defaults to OFF (null / false both block) - the
        /// feature drives a tamed creature into a totem-bound work loop, and
        /// that should be an explicit opt-in per creature (Troll) rather than
        /// something every tame quietly inherits.</summary>
        public bool? LoggingMode;

        /// <summary>Copy any value this rule has no opinion about from a less
        /// specific rule. Called least-specific-last, so the first writer wins.</summary>
        public void FillFrom(CreatureRule lower)
        {
            if (lower == null) return;
            if (!WildBehavior.HasValue) WildBehavior = lower.WildBehavior;
            if (!TamedBehavior.HasValue) TamedBehavior = lower.TamedBehavior;
            if (!WildView.HasValue) WildView = lower.WildView;
            if (!WildHear.HasValue) WildHear = lower.WildHear;
            if (!TamedView.HasValue) TamedView = lower.TamedView;
            if (!TamedHear.HasValue) TamedHear = lower.TamedHear;
            if (!AlertRange.HasValue) AlertRange = lower.AlertRange;
            if (!MaxChaseDistance.HasValue) MaxChaseDistance = lower.MaxChaseDistance;
            if (!EnableHuntPlayer.HasValue) EnableHuntPlayer = lower.EnableHuntPlayer;
            if (!FleeIfNotAlerted.HasValue) FleeIfNotAlerted = lower.FleeIfNotAlerted;
            if (!FleeIfLowHealth.HasValue) FleeIfLowHealth = lower.FleeIfLowHealth;
            if (!StanceCycling.HasValue) StanceCycling = lower.StanceCycling;
            if (!LoggingMode.HasValue) LoggingMode = lower.LoggingMode;
            if (!EnrageWhenOutnumbered.HasValue) EnrageWhenOutnumbered = lower.EnrageWhenOutnumbered;
            if (!EnrageVsSameKind.HasValue) EnrageVsSameKind = lower.EnrageVsSameKind;
            if (!EnrageThreatBonus.HasValue) EnrageThreatBonus = lower.EnrageThreatBonus;
            if (!EnragePhysicalResist.HasValue) EnragePhysicalResist = lower.EnragePhysicalResist;
            if (!EnragePoisonImmune.HasValue) EnragePoisonImmune = lower.EnragePoisonImmune;
            if (!EnrageDamageMult.HasValue) EnrageDamageMult = lower.EnrageDamageMult;
            if (!MinAttackInterval.HasValue) MinAttackInterval = lower.MinAttackInterval;
            if (!CircleTargetInterval.HasValue) CircleTargetInterval = lower.CircleTargetInterval;
            if (!CirculateWhileCharging.HasValue) CirculateWhileCharging = lower.CirculateWhileCharging;
            if (!Threat.HasValue) Threat = lower.Threat;
            if (!Fearless.HasValue) Fearless = lower.Fearless;
            if (!Baby.HasValue) Baby = lower.Baby;
            if (!StalkSeconds.HasValue) StalkSeconds = lower.StalkSeconds;
            if (!StalkRadius.HasValue) StalkRadius = lower.StalkRadius;
            if (!PounceRange.HasValue) PounceRange = lower.PounceRange;
            if (!PhaseExemptTamed.HasValue) PhaseExemptTamed = lower.PhaseExemptTamed;

            // Merged field by field, not wholesale: a creature may name one
            // night stat and inherit the rest of the profile from its faction.
            if (lower.Day != null)
            {
                if (Day == null) Day = new PhaseStats();
                Day.FillFrom(lower.Day);
            }
            if (lower.Night != null)
            {
                if (Night == null) Night = new PhaseStats();
                Night.FillFrom(lower.Night);
            }

            if (!FireFear.HasValue) FireFear = lower.FireFear;
            if (!FireReact.HasValue) FireReact = lower.FireReact;
            if (!FireBuffer.HasValue) FireBuffer = lower.FireBuffer;
            if (!RallyRadius.HasValue) RallyRadius = lower.RallyRadius;
            if (!Solitary.HasValue) Solitary = lower.Solitary;
            if (!Sight.HasValue) Sight = lower.Sight;
            if (!AlwaysFlee.HasValue) AlwaysFlee = lower.AlwaysFlee;
            if (!SharesFear.HasValue) SharesFear = lower.SharesFear;
            // FactionId is deliberately NOT inherited: a [@Faction] section
            // describes creatures already in that faction, it does not move
            // anything into it.
        }

        public bool IsEmpty =>
            !WildBehavior.HasValue && !TamedBehavior.HasValue &&
            !WildView.HasValue && !WildHear.HasValue &&
            !TamedView.HasValue && !TamedHear.HasValue &&
            !AlertRange.HasValue && !MaxChaseDistance.HasValue &&
            !EnableHuntPlayer.HasValue && !FleeIfLowHealth.HasValue &&
            !FleeIfNotAlerted.HasValue &&
            !Threat.HasValue && !Fearless.HasValue && !Baby.HasValue &&
            !RallyRadius.HasValue && !Solitary.HasValue && !Sight.HasValue && !AlwaysFlee.HasValue &&
            !SharesFear.HasValue &&
            !StanceCycling.HasValue && !LoggingMode.HasValue && !FactionId.HasValue &&
            !EnrageWhenOutnumbered.HasValue && !EnrageVsSameKind.HasValue &&
            !EnrageThreatBonus.HasValue &&
            !EnragePhysicalResist.HasValue && !EnragePoisonImmune.HasValue && !EnrageDamageMult.HasValue &&
            !MinAttackInterval.HasValue && !CircleTargetInterval.HasValue && !CirculateWhileCharging.HasValue &&
            !FireFear.HasValue && !FireReact.HasValue && !FireBuffer.HasValue &&
            !StalkSeconds.HasValue && !StalkRadius.HasValue && !PounceRange.HasValue &&
            !PhaseExemptTamed.HasValue &&
            (Day == null || Day.IsEmpty) && (Night == null || Night.IsEmpty);
    }

    /// <summary>
    /// The active rule set. Swapped in wholesale so a half-parsed config can
    /// never leave the game running on an empty table.
    /// </summary>
    public static class CreatureRules
    {
        public class Store
        {
            public readonly Dictionary<string, CreatureRule> ByPrefab =
                new Dictionary<string, CreatureRule>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, CreatureRule> ByFaction =
                new Dictionary<string, CreatureRule>(StringComparer.OrdinalIgnoreCase);
            public CreatureRule Fallback;

            public CreatureRule GetOrCreatePrefab(string prefab)
            {
                if (!ByPrefab.TryGetValue(prefab, out var r))
                { r = new CreatureRule { Source = prefab }; ByPrefab[prefab] = r; }
                return r;
            }

            public CreatureRule GetOrCreateFaction(string faction)
            {
                if (!ByFaction.TryGetValue(faction, out var r))
                { r = new CreatureRule { Source = "@" + faction }; ByFaction[faction] = r; }
                return r;
            }

            public bool Any => ByPrefab.Count > 0 || ByFaction.Count > 0 || Fallback != null;
        }

        static Store _active = new Store();

        public static int PrefabCount => _active.ByPrefab.Count;
        public static int FactionCount => _active.ByFaction.Count;
        public static bool AnyRules => _active.Any;

        public static void Install(Store s) => _active = s ?? new Store();

        /// <summary>Look up only the by-name rule. Used before a creature's
        /// faction is settled, to decide whether we reassign its faction.</summary>
        public static CreatureRule FindByPrefab(string prefab)
        {
            if (prefab != null && _active.ByPrefab.TryGetValue(prefab, out var r)) return r;
            return null;
        }

        /// <summary>
        /// The effective rule for a creature: its own entry, then its faction's
        /// entry, then the [*] fallback. More specific always wins.
        /// </summary>
        public static CreatureRule Resolve(string prefab, int factionId)
        {
            CreatureRule own = FindByPrefab(prefab);
            CreatureRule fac = null;

            var factionName = FactionRegistry.NameOf(factionId);
            if (factionName != null) _active.ByFaction.TryGetValue(factionName, out fac);

            if (own == null && fac == null && _active.Fallback == null) return null;

            // Work on a copy so the stored rules stay pristine across reuse.
            var merged = new CreatureRule { Source = own?.Source ?? fac?.Source ?? "*" };
            merged.FillFrom(own);
            // The faction rule may legitimately supply a faction-wide default
            // for everything except the faction assignment itself.
            merged.FillFrom(fac);
            merged.FillFrom(_active.Fallback);

            // Faction assignment only ever comes from the creature's own entry.
            merged.FactionId = own?.FactionId;
            merged.FactionName = own?.FactionName;

            return merged.IsEmpty ? null : merged;
        }

        /// <summary>Runtime GameObject names carry a "(Clone)" suffix; the config
        /// is written in terms of plain prefab names.</summary>
        public static string CleanName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return goName;
            int i = goName.IndexOf("(Clone)", StringComparison.Ordinal);
            return i >= 0 ? goName.Substring(0, i).Trim() : goName;
        }
    }
}
