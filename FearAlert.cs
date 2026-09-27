using System.Collections.Generic;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// A herd panic mechanic, deliberately separate from Band.cs. Bands weigh
    /// threat together and reach one shared verdict for a pack that hunts as a
    /// unit (wolves, fulings). This is the opposite kind of thing: no threat
    /// weighing at all, just "my neighbour just started running, so I will
    /// too" - the way a real deer doesn't do arithmetic, it just reacts to the
    /// herd bolting around it.
    ///
    /// Scoped by sharesFear in the config (currently only Deer, as a test
    /// case) so nothing else in the mod changes behaviour. Same-prefab only -
    /// a deer alerts other deer, not nearby boars - and delivery is delayed by
    /// Fear Alert Delay so it reads as a reaction, not instant hive-mind
    /// knowledge.
    /// </summary>
    public static class FearAlert
    {
        struct Pending
        {
            public CreatureState Receiver;
            public Character Target;
            public float DeliverAt;
        }

        static readonly List<Pending> _pending = new List<Pending>();
        static readonly List<Character> _scan = new List<Character>();

        /// <summary>
        /// Called the instant a creature newly decides to flee (the edge, not
        /// every re-check). Finds same-prefab neighbours within Fear Alert
        /// Radius that also opted in, and queues each one a delayed alert.
        /// </summary>
        public static void Broadcast(CreatureState source, Character target)
        {
            if (source == null || target == null || source.Chr == null) return;

            float radius = Plugin.FearAlertRadius;
            if (radius <= 0f) return;

            Vector3 at = source.Chr.transform.position;
            float rSq = radius * radius;

            _scan.Clear();
            Character.GetCharactersInRange(at, radius, _scan);

            float deliverAt = Time.time + Plugin.FearAlertDelay;

            for (int i = 0; i < _scan.Count; i++)
            {
                var c = _scan[i];
                if (c == null || c == source.Chr || c.IsDead()) continue;
                if ((c.transform.position - at).sqrMagnitude > rSq) continue;

                var st = CreatureState.For(c);
                if (st == null || st == source) continue;
                if (!st.SharesFear) continue;

                // "Its own kind" - a deer alerts other deer, nothing else.
                if (!string.Equals(st.Prefab, source.Prefab,
                        System.StringComparison.OrdinalIgnoreCase)) continue;

                if (st.IsAlreadyScaredOf(target)) continue;

                _pending.Add(new Pending { Receiver = st, Target = target, DeliverAt = deliverAt });
            }
        }

        /// <summary>Delivers any alert whose reaction delay has elapsed. Called
        /// once per Plugin.Update tick; the list is only ever a handful of
        /// entries for a fraction of a second, so a linear scan costs nothing.</summary>
        public static void Tick()
        {
            if (_pending.Count == 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (Time.time < p.DeliverAt) continue;

                if (p.Receiver != null && p.Receiver.Chr != null && !p.Receiver.Chr.IsDead())
                    p.Receiver.ReceiveFearAlert(p.Target, Plugin.FearInterval * 4f);

                _pending.RemoveAt(i);
            }
        }

        /// <summary>World unload / world load: a stale reference to a
        /// destroyed creature would just be skipped by Tick's null checks, but
        /// there is no reason to carry a dead world's alerts into the next.</summary>
        public static void Reset() => _pending.Clear();
    }
}
