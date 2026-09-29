using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// Groups allies into a band and lets the band decide as one.
    ///
    /// The problem this solves: a creature counting allies from its own position
    /// only ever sees its own little neighbourhood, so six greydwarves strung
    /// across a clearing each tally two friends, each decide they are outmatched,
    /// and the pack falls apart one creature at a time even though together they
    /// had the numbers.
    ///
    /// A band is built by flood fill. Start at the creature deciding, pull in its
    /// allies inside ITS rally radius, then pull in their allies inside THEIRS,
    /// and so on - overlapping circles chain into one group. The band evaluates
    /// once, and every member shares the verdict and the re-check timer, so a
    /// camp of twelve costs one evaluation rather than twelve.
    ///
    /// Counting allies who have not noticed the enemy would be dishonest on its
    /// own - a lone greydwarf would charge in on the strength of five oblivious
    /// neighbours. That is why the band also HANDS the target to its members when
    /// it commits: the reinforcements it counted actually turn up.
    /// </summary>
    public static class Band
    {
        // Reused across evaluations; this runs on a timer, not every frame, but
        // allocating four collections per evaluation would still drip garbage.
        static readonly List<Character> _scan = new List<Character>();
        static readonly List<CreatureState> _band = new List<CreatureState>();
        static readonly HashSet<Character> _seen = new HashSet<Character>();
        static readonly Queue<CreatureState> _queue = new Queue<CreatureState>();

        /// <summary>
        /// The band's verdict for <paramref name="me"/> against
        /// <paramref name="target"/>. True means back off.
        /// </summary>
        public static bool ShouldBackOff(CreatureState me, Character target)
        {
            if (me == null || me.Chr == null || target == null) return false;

            // Somebody in this band already did the work this interval.
            if (me.BandVerdictFresh(target)) return me.BandBackOff;

            float mine = Gather(me, target);
            float theirs = OpposingSide(me, target);

            bool backOff = mine < theirs;

            // Publish to every member at once. This is what makes the pack think
            // as one creature instead of as twelve nervous individuals.
            float until = Time.time + Plugin.FearInterval;
            for (int i = 0; i < _band.Count; i++)
                _band[i].SetBandVerdict(target, backOff, until);

            if (Plugin.Verbose && Plugin.FearTickDiagEnabled)
            {
                var ai = me.Ai;
                Plugin.Log.LogInfo(
                    $"[CC fear] {me.Prefab} vs {(target is Player ? "Player" : target.name)}: " +
                    $"mine={mine:0.00} (band {_band.Count}) theirs={theirs:0.00} -> " +
                    (backOff ? "BACK OFF" : "commit") +
                    $" | sees={me.Sight.Pretty()}" +
                    $" alerted={(ai != null && ai.IsAlerted())} " +
                    $"canBeAlerted={(ai != null && ai.m_canBeAlerted)}" +
                    // Spelled out for a player so the numbers can be checked
                    // against the tables rather than taken on trust.
                    (target is Player ? "\n        player: " + PlayerDanger.Describe((Player)target) : ""));
            }

            if (!backOff && Plugin.ChainAggro) Commit(target);
            else if (backOff && Plugin.CallForHelpEnabled) CallForHelp(me, target);

            return backOff;
        }

        // ------------------------------------------------- the cry for help

        /// <summary>
        /// What an outmatched creature does INSTEAD of simply dying alone.
        ///
        /// Counting only the allies already standing next to you is passive:
        /// a straggler twenty metres from its pack tallies itself, decides it
        /// is outmatched, and runs - and nothing it does ever brings the pack
        /// any closer. So a creature that backs off also calls, and the call
        /// carries further than it can see friends. Allies that had noticed
        /// nothing are pointed at the enemy and come running, so by the time
        /// the odds are counted again the pack may well be strong enough.
        ///
        /// Nothing here is per-creature: any creature the fear system drives
        /// calls, and any ally can answer.
        /// </summary>
        static void CallForHelp(CreatureState origin, Character target)
        {
            if (origin == null || origin.Chr == null || target == null) return;

            // Only creatures that take part in the rally at all.
            if (!origin.RalliesWithOthers) return;

            float r = Plugin.HelpRadius;
            if (r <= 0f) return;
            float rSq = r * r;
            Vector3 at = origin.Chr.transform.position;

            _scan.Clear();
            Character.GetCharactersInRange(at, r, _scan);

            int called = 0;
            for (int i = 0; i < _scan.Count; i++)
            {
                var c = _scan[i];
                if (c == null || c == origin.Chr || c.IsDead()) continue;
                if ((c.transform.position - at).sqrMagnitude > rSq) continue;

                // Already in the band - it was handed the verdict directly.
                if (_seen.Contains(c)) continue;

                if (!IsAlly(origin.Chr, c, target)) continue;

                var st = CreatureState.For(c);
                if (st == null || st.Mai == null) continue;
                if (c.IsTamed()) continue;

                // Answering is the same sociability as calling, so whatever is
                // barred from one is barred from the other.
                if (!st.RalliesWithOthers) continue;

                // Already in a fight of its own; pulling it off would be worse
                // than leaving it. SetTarget only fills an empty slot anyway.
                if (st.Mai.GetTargetCreature() != null) continue;

                SetTarget(st.Mai, target);
                if (st.Ai != null) st.Ai.Alert();

                // Answering steadies the nerves for the trip over. Without
                // this a responder re-decides alone halfway there, turns tail,
                // and the rally dissolves before any of it arrives.
                st.MarkRallying();
                called++;
            }

            if (called > 0 && Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC fear] {origin.Prefab} called for help - {called} answered.");

            if (Plugin.DinnerBell) RingDinnerBell(origin);
        }

        /// <summary>
        /// The other half of a cry for help: it carries to everything, and what
        /// answers depends on what is listening. Allies hear a friend in
        /// trouble. Predators hear something small, frightened and worth
        /// eating - and come for the CALLER, not for its enemy.
        ///
        /// Solitary hunters answer this even though they never join a rally.
        /// Refusing to cooperate is not the same as refusing a free meal.
        /// </summary>
        static void RingDinnerBell(CreatureState caller)
        {
            if (caller == null || caller.Chr == null) return;

            float r = Plugin.HelpRadius;
            if (r <= 0f) return;
            float rSq = r * r;
            Vector3 at = caller.Chr.transform.position;

            _scan.Clear();
            Character.GetCharactersInRange(at, r, _scan);

            int drawn = 0;
            for (int i = 0; i < _scan.Count; i++)
            {
                var c = _scan[i];
                if (c == null || c == caller.Chr || c.IsDead()) continue;
                if ((c.transform.position - at).sqrMagnitude > rSq) continue;
                if (c.IsTamed()) continue;

                // Only things that would actually hunt the caller.
                if (!BaseAI.IsEnemy(c, caller.Chr)) continue;

                var st = CreatureState.For(c);
                if (st == null || st.Mai == null) continue;
                if (st.Rule != null && st.Rule.Baby == true) continue;

                // Busy with its own fight - leave it there.
                if (st.Mai.GetTargetCreature() != null) continue;

                SetTarget(st.Mai, caller.Chr);
                if (st.Ai != null) st.Ai.Alert();
                drawn++;
            }

            if (drawn > 0 && Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC fear] {caller.Prefab}'s cry drew {drawn} hunter(s) onto it.");
        }

        // ------------------------------------------------------------ the band

        /// <summary>Flood fill outward through ally links. Fills _band and
        /// returns the band's total threat.</summary>
        static float Gather(CreatureState origin, Character target)
        {
            var lens = origin.Sight;
            _band.Clear();
            _seen.Clear();
            _queue.Clear();

            Vector3 home = origin.Chr.transform.position;
            float spreadSq = Plugin.BandMaxSpread * Plugin.BandMaxSpread;

            _seen.Add(origin.Chr);
            _band.Add(origin);
            _queue.Enqueue(origin);
            origin.BandHop = 0;

            float total = Weighted(origin.Chr, lens);

            while (_queue.Count > 0)
            {
                var cur = _queue.Dequeue();
                if (cur.BandHop >= Plugin.BandMaxHops) continue;
                if (_band.Count >= Plugin.BandMaxSize) break;

                float radius = cur.RallyRadius;
                if (radius <= 0f) continue;

                Vector3 at = cur.Chr.transform.position;
                float radiusSq = radius * radius;

                _scan.Clear();
                Character.GetCharactersInRange(at, radius, _scan);

                for (int i = 0; i < _scan.Count; i++)
                {
                    if (_band.Count >= Plugin.BandMaxSize) break;

                    var c = _scan[i];
                    if (c == null || c.IsDead() || _seen.Contains(c)) continue;
                    if ((c.transform.position - at).sqrMagnitude > radiusSq) continue;

                    // The chain must not be able to walk across the map one
                    // creature at a time, however many links it finds.
                    if ((c.transform.position - home).sqrMagnitude > spreadSq) continue;

                    if (!IsAlly(cur.Chr, c, target)) continue;

                    _seen.Add(c);

                    var st = CreatureState.For(c);
                    total += Weighted(c, lens);

                    // Something with no state of its own still counts toward the
                    // band's strength, it just cannot extend the chain further.
                    if (st == null) continue;

                    _band.Add(st);
                    st.BandHop = cur.BandHop + 1;
                    _queue.Enqueue(st);
                }
            }

            return total;
        }

        /// <summary>
        /// An ally is something that will not fight me AND does have a quarrel
        /// with the thing I am sizing up. The second half is what keeps
        /// bystanders out - a butterfly is untargetable and a neutral boar has no
        /// stake in this, so neither is backup.
        /// </summary>
        static bool IsAlly(Character member, Character candidate, Character target)
        {
            if (candidate == target) return false;
            if (BaseAI.IsEnemy(member, candidate)) return false;
            if (!BaseAI.IsEnemy(candidate, target)) return false;

            // A calf is worth nothing and should never be marched into a fight
            // because its herd banded up.
            var st = CreatureState.For(candidate);
            if (st != null && st.Rule != null && st.Rule.Baby == true) return false;

            // Creatures that read the world differently cannot share a verdict:
            // a band decides ONCE and hands the answer to every member, so a
            // greydwarf that reads your armour and a wolf that cannot must never
            // end up in the same band, or one of them inherits a conclusion it
            // could not have reached.
            var mine = CreatureState.For(member);
            if (mine != null && st != null && mine.Sight != st.Sight) return false;

            return true;
        }

        /// <summary>Everything lined up on the far side: the target plus whoever
        /// would side with it, counted from ITS position rather than mine.</summary>
        static float OpposingSide(CreatureState viewer, Character target)
        {
            var lens = viewer.Sight;
            Character me = viewer.Chr;
            float total = Weighted(target, lens);

            Vector3 at = target.transform.position;
            float r = Plugin.FearRadius;
            float rSq = r * r;

            _scan.Clear();
            Character.GetCharactersInRange(at, r, _scan);

            for (int i = 0; i < _scan.Count; i++)
            {
                var c = _scan[i];
                if (c == null || c == target || c == me || c.IsDead()) continue;
                if ((c.transform.position - at).sqrMagnitude > rSq) continue;
                if (_seen.Contains(c)) continue;          // already on my side

                if (BaseAI.IsEnemy(target, c)) continue;  // not on the target's side
                if (!BaseAI.IsEnemy(c, me)) continue;     // no quarrel with me

                total += Weighted(c, lens);
            }

            return total;
        }

        /// <summary>Threat with the escort discount applied. A tame at someone's
        /// heel is backup, and backup is worth a fraction of the same creature
        /// standing on its own.</summary>
        static float Weighted(Character c, Perception lens)
        {
            float v = Threat.Of(c, lens);
            if (v > Threat.Negligible && c.IsTamed()) v *= Plugin.PetThreatWeight;
            return v;
        }

        // ------------------------------------------------------- aggro transfer

        /// <summary>
        /// Hand the target to every band member that has not noticed it yet.
        /// This is the half that makes counting the unaware honest.
        /// </summary>
        static void Commit(Character target)
        {
            for (int i = 0; i < _band.Count; i++)
            {
                var st = _band[i];
                if (st == null || st.Mai == null || st.Chr == null) continue;
                if (st.Chr.IsTamed()) continue;
                if (st.Rule != null && st.Rule.Baby == true) continue;
                if (st.Mai.GetTargetCreature() != null) continue;   // already busy

                // SetTarget only ever fills an EMPTY target slot and refuses to
                // point a tamed creature at a player, so it cannot pull anything
                // off a fight it is already in or turn a pet on its owner.
                SetTarget(st.Mai, target);
                if (st.Ai != null) st.Ai.Alert();
            }
        }

        delegate void SetTargetDel(MonsterAI self, Character c);
        static SetTargetDel _setTarget;
        static bool _bound;

        static void SetTarget(MonsterAI ai, Character target)
        {
            if (!_bound)
            {
                _bound = true;
                try
                {
                    var mi = AccessTools.Method(typeof(MonsterAI), "SetTarget",
                        new[] { typeof(Character) });
                    if (mi != null) _setTarget = AccessTools.MethodDelegate<SetTargetDel>(mi);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"Aggro chaining unavailable: {e.Message}");
                }
                if (_setTarget == null)
                    Plugin.Log.LogWarning(
                        "MonsterAI.SetTarget not found - bands will share a verdict but " +
                        "will not pull each other in.");
                else
                    Plugin.Log.LogInfo("Aggro chaining bound to MonsterAI.SetTarget.");
            }

            if (_setTarget == null || ai == null) return;
            _setTarget(ai, target);
        }
    }
}
