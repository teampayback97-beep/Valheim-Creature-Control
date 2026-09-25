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

        public bool? StanceCycling;

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
            if (!Threat.HasValue) Threat = lower.Threat;
            if (!Fearless.HasValue) Fearless = lower.Fearless;
            if (!Baby.HasValue) Baby = lower.Baby;
            if (!RallyRadius.HasValue) RallyRadius = lower.RallyRadius;
            if (!Solitary.HasValue) Solitary = lower.Solitary;
            if (!Sight.HasValue) Sight = lower.Sight;
            if (!AlwaysFlee.HasValue) AlwaysFlee = lower.AlwaysFlee;
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
            !StanceCycling.HasValue && !FactionId.HasValue;
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
