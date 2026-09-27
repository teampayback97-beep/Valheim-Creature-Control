using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// The danger-score model. Every creature is worth some number of Fulings;
    /// a creature commits to a fight when its own side's total matches or beats
    /// the other side's.
    ///
    /// There is deliberately no pair table and no boldness multiplier. Two
    /// numbers - a threat value and a fearless flag - reproduce every ratio we
    /// wanted, and ratios stay linear at any group size because both sides are
    /// plain sums.
    /// </summary>
    public static class Threat
    {
        /// <summary>Anything at or below this is treated as weightless, which
        /// is how babies end up contributing nothing to their herd.</summary>
        public const float Negligible = 0.0001f;

        // ------------------------------------------------------------ values

        /// <summary>
        /// A single character's contribution, stars included.
        ///
        /// <paramref name="seenBy"/> matters only for PLAYERS: a goblin, a wolf
        /// and a skeleton look at the same person and see three different
        /// things. Creature-versus-creature threat is identical for everyone,
        /// so for anything that is not a player the lens is simply ignored.
        /// </summary>
        public static float Of(Character c, Perception seenBy)
        {
            if (c == null) return 0f;

            if (c is Player) return PlayerDanger.For((Player)c, seenBy);

            var st = CreatureState.For(c);
            var rule = st?.Rule;

            // A baby is worth nothing to its side. That is the whole point: a
            // lox with a calf is still worth 3, so a predator that would take
            // the lox alone still takes it, and the calf is not a bodyguard.
            if (rule != null && rule.Baby == true) return 0f;

            // ThreatValue, not rule.Threat: it folds in the day/night profile, so
            // a creature that is genuinely more dangerous after dark is also
            // READ as more dangerous by everything deciding whether to fight it.
            float value = st != null ? st.ThreatValue : (rule?.Threat ?? Plugin.DefaultThreat);

            // Creature Level & Loot Control hands out up to five stars at +100%
            // health and +50% damage each. Flat threat would have a five-star
            // fuling make the same decision as a fresh one.
            int stars = c.GetLevel() - 1;
            if (stars > 0) value *= 1f + Plugin.StarThreatScale * stars;

            return value;
        }

        /// <summary>Creature-only threat, for callers with no player in hand
        /// (the spawn log, for instance). A player passed here reads as zero,
        /// which is deliberate: nothing should judge a person without saying
        /// what it is looking through.</summary>
        public static float OfCreature(Character c) => Of(c, Perception.None);

        // The old OfPlayer - base + sqrt(armor), capped - is gone. It returned
        // one number for every creature in the world, which is precisely what
        // this rework exists to undo. PlayerDanger owns that job now.

        // Side-counting now lives in Band, which flood-fills allies and lets the
        // whole group decide once. Keeping a second per-creature implementation
        // here would be two sources of truth for the same question.

        // ------------------------------------------------- borrowed vanilla calls

        // Flee is protected, so it needs an open delegate.
        delegate bool FleeDel(BaseAI self, float dt, Vector3 from);

        // BaseAI.UpdateAI is public but VIRTUAL, and MonsterAI overrides it.
        // Calling it normally would dispatch straight back into the override we
        // are standing in front of, so this delegate is bound with
        // virtualCall: false to get the non-virtual "base.UpdateAI" call.
        delegate bool BaseTickDel(BaseAI self, float dt);

        static FleeDel _flee;
        static BaseTickDel _baseTick;
        static bool _bound;

        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                var fl = AccessTools.Method(typeof(BaseAI), "Flee",
                    new[] { typeof(float), typeof(Vector3) });
                if (fl != null) _flee = AccessTools.MethodDelegate<FleeDel>(fl);

                var up = AccessTools.Method(typeof(BaseAI), "UpdateAI",
                    new[] { typeof(float) });
                if (up != null)
                    _baseTick = AccessTools.MethodDelegate<BaseTickDel>(up, null, false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Fear system could not bind to the AI: {e.Message}");
            }

            if (_flee == null || _baseTick == null)
                Plugin.Log.LogWarning(
                    "Fear disabled this session: " +
                    (_flee == null ? "BaseAI.Flee " : "") +
                    (_baseTick == null ? "BaseAI.UpdateAI " : "") + "could not be resolved.");
            else
                Plugin.Log.LogInfo("Fear system bound to BaseAI.Flee and BaseAI.UpdateAI.");
        }

        public static bool Flee(BaseAI ai, float dt, Vector3 from)
        {
            Bind();
            if (_flee == null || ai == null) return false;
            return _flee(ai, dt, from);
        }

        /// <summary>
        /// The shared per-tick work every AI needs: the ownership gate, takeoff
        /// and landing, regeneration, and the jump / random-move / time-since-hurt
        /// timers. MonsterAI.UpdateAI runs it first and bails when it returns
        /// false, so anything that takes the tick over has to do the same - skip
        /// it and the creature keeps its target and its intentions but stops
        /// moving, regenerating or ageing its timers.
        /// </summary>
        public static bool BaseTick(BaseAI ai, float dt)
        {
            Bind();
            if (_baseTick == null || ai == null) return false;
            return _baseTick(ai, dt);
        }

        /// <summary>Whether both borrowed calls resolved. Binds on first ask.</summary>
        public static bool Available
        {
            get { Bind(); return _flee != null && _baseTick != null; }
        }
    }
}
