using UnityEngine;

namespace CreatureControl
{
    public enum DayPhase { Day = 0, Night = 1 }

    /// <summary>
    /// One phase's worth of a creature's numbers. Everything is nullable and
    /// means "no opinion - use whatever the creature would otherwise have", so
    /// a profile can say one thing or twelve.
    ///
    /// Deliberately SYMMETRIC: there is no "night bonus" field anywhere. A
    /// nocturnal hunter is weak in [day] and strong in [night]; a diurnal one
    /// is the same numbers the other way round. That is the whole reason this
    /// is a phase table rather than a list of night buffs - the next creature
    /// that wants day/night behaviour needs config, not code.
    ///
    /// Adding a new stat is one field here, one line in PhaseStats.FillFrom,
    /// one case in the config parser and one use site. Nothing else in the mod
    /// has to know it exists.
    /// </summary>
    public class PhaseStats
    {
        /// <summary>Sight radius during this phase. Overrides viewRange.</summary>
        public float? ViewRange;
        /// <summary>Hearing radius during this phase. Overrides hearRange.</summary>
        public float? HearRange;
        /// <summary>What it is worth in a fight during this phase, in Fulings.
        /// Overrides threat, so the fear model reads it too.</summary>
        public float? Threat;
        /// <summary>Gives up the chase past this during this phase.</summary>
        public float? MaxChaseDistance;
        /// <summary>Multiplier on the damage it DEALS during this phase.</summary>
        public float? DamageMult;
        /// <summary>Multiplier on how fast it moves during this phase.</summary>
        public float? SpeedMult;
        /// <summary>Multiplier on how far ANYTHING ELSE can see or hear it
        /// during this phase. 0.5 means everything notices it at half the
        /// distance. Only ever reduces - it cannot make a creature easier to
        /// spot than the observer's own senses allow.</summary>
        public float? Stealth;

        public void FillFrom(PhaseStats lower)
        {
            if (lower == null) return;
            if (!ViewRange.HasValue) ViewRange = lower.ViewRange;
            if (!HearRange.HasValue) HearRange = lower.HearRange;
            if (!Threat.HasValue) Threat = lower.Threat;
            if (!MaxChaseDistance.HasValue) MaxChaseDistance = lower.MaxChaseDistance;
            if (!DamageMult.HasValue) DamageMult = lower.DamageMult;
            if (!SpeedMult.HasValue) SpeedMult = lower.SpeedMult;
            if (!Stealth.HasValue) Stealth = lower.Stealth;
        }

        public bool IsEmpty =>
            !ViewRange.HasValue && !HearRange.HasValue && !Threat.HasValue &&
            !MaxChaseDistance.HasValue && !DamageMult.HasValue &&
            !SpeedMult.HasValue && !Stealth.HasValue;
    }

    /// <summary>
    /// Which half of the day it is, and noticing when that changes.
    ///
    /// The numbers in a PhaseStats are written onto the creature by
    /// CreatureState.Apply, so something has to re-apply them when the sun
    /// comes up. Rather than have every creature poll the clock, this watches
    /// it once and pushes the change out - two passes over the tracked
    /// creatures per in-game day, instead of a timer per creature.
    /// </summary>
    public static class Phase
    {
        static DayPhase _current = DayPhase.Day;
        static bool _known;

        public static DayPhase Current => _current;
        public static bool IsNight => _current == DayPhase.Night;

        /// <summary>Reads the world clock. EnvMan.IsNight is a static flag read,
        /// so this is cheap enough to call every frame; before a world is
        /// loaded it simply reads as day.</summary>
        public static DayPhase Read()
        {
            try { return EnvMan.IsNight() ? DayPhase.Night : DayPhase.Day; }
            catch { return DayPhase.Day; }
        }

        /// <summary>
        /// Called once per frame from the plugin. Returns true on the tick the
        /// phase actually turned over.
        /// </summary>
        public static bool Tick()
        {
            var now = Read();
            if (_known && now == _current) return false;

            bool flipped = _known;
            _known = true;
            _current = now;

            if (!flipped) return false;    // first read of the session, not a change

            int n = 0;
            foreach (var st in CreatureState.AllTracked)
            {
                if (st == null || !st.HasPhaseProfile) continue;
                st.Apply();
                n++;
            }

            if (n > 0)
                Plugin.Log.LogInfo($"{(_current == DayPhase.Night ? "Nightfall" : "Daybreak")}: " +
                                   $"re-applied day/night stats to {n} creature(s).");
            return true;
        }

        public static void Forget() { _known = false; _current = DayPhase.Day; }
    }
}
