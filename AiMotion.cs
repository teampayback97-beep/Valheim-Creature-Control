using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Borrowed BaseAI movement verbs.
    ///
    /// Threat.cs already binds the two calls the fear system needs (Flee and
    /// the non-virtual base tick). This is the other half: the primitives a
    /// BEHAVIOUR module needs to move a creature somewhere deliberate rather
    /// than just away. Kept separate so the danger-score model stays about
    /// scoring, and so the modules still to come - a prowler circling to your
    /// flank, a wolf pack fanning out - have one place to reach for.
    ///
    /// Every call degrades to "did nothing, returned false" if the game changed
    /// shape underneath us. Callers must treat a false as "vanilla keeps the
    /// tick", never as "the creature is now standing still".
    /// </summary>
    public static class AiMotion
    {
        // BaseAI.RandomMovementArroundPoint - vanilla's own spelling. This is
        // what AvoidFire uses to circle a flame, and it is the orbit primitive
        // for anything that wants to hold a distance instead of closing.
        delegate void OrbitDel(BaseAI self, float dt, Vector3 point, float radius, bool run);

        // BaseAI.MoveTo(dt, point, dist, run). NB vanilla returns TRUE when it
        // has STOPPED - arrived or failed to path - not when it succeeded.
        delegate bool MoveToDel(BaseAI self, float dt, Vector3 point, float dist, bool run);

        static OrbitDel _orbit;
        static MoveToDel _moveTo;
        static bool _bound;

        static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                var o = AccessTools.Method(typeof(BaseAI), "RandomMovementArroundPoint",
                    new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) });
                if (o != null) _orbit = AccessTools.MethodDelegate<OrbitDel>(o);

                var m = AccessTools.Method(typeof(BaseAI), "MoveTo",
                    new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) });
                if (m != null) _moveTo = AccessTools.MethodDelegate<MoveToDel>(m);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Movement verbs could not be bound: {e.Message}");
            }

            if (_orbit == null)
                Plugin.Log.LogWarning(
                    "BaseAI.RandomMovementArroundPoint could not be resolved; " +
                    "fire circling falls back to the game's own handling.");
        }

        /// <summary>Circles a point at roughly the given radius. Vanilla's own
        /// fire-avoidance movement, so the pathing is already proven.</summary>
        public static bool Orbit(BaseAI ai, float dt, Vector3 point, float radius, bool run)
        {
            Bind();
            if (_orbit == null || ai == null) return false;
            _orbit(ai, dt, point, radius, run);
            return true;
        }

        /// <summary>Walks towards a point, stopping within dist of it.</summary>
        public static bool MoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run)
        {
            Bind();
            if (_moveTo == null || ai == null) return false;
            _moveTo(ai, dt, point, dist, run);
            return true;
        }

        public static bool CanOrbit { get { Bind(); return _orbit != null; } }
        public static bool CanMoveTo { get { Bind(); return _moveTo != null; } }
    }
}
