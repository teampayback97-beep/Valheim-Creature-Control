using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// A Neutral tame within Plugin.NeutralDefenseRadius of the player steps
    /// in against whatever is currently targeting the player - even if that
    /// thing has never actually hit the tame itself. Guard's rule 1 (see
    /// CreatureState.SensesGuardThreat) already covers this exact idea, but
    /// measured from the TAME's own position via guardRadius, which is a
    /// per-creature combat-tuning value (Bjorn's default is 15) - too small
    /// for "notice a fight breaking out near me" at any real remove, and not
    /// every creature configures guardRadius at all. This is deliberately
    /// separate: measured from the PLAYER, one flat radius, no per-creature
    /// config required, and - unlike Guard, which just widens what counts as
    /// sensed and lets vanilla's own FindEnemy pick among however many
    /// eligible threats exist - this actively chooses the single most
    /// dangerous (highest current HP) attacker itself, since FindEnemy has no
    /// concept of "pick the toughest one" on its own.
    /// </summary>
    static class NeutralDefense
    {
        static float _nextScan;

        public static void Tick()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 1f;

            var player = Player.m_localPlayer;
            if (player == null) return;

            float rSq = Plugin.NeutralDefenseRadius * Plugin.NeutralDefenseRadius;

            foreach (var st in CreatureState.AllTracked)
            {
                if (st == null || st.Chr == null || st.Mai == null) continue;
                if (!st.Chr.IsTamed() || st.Chr.IsDead()) continue;
                if (st.Mode != BehaviorMode.Neutral) continue;

                // Already fighting (this or anything else) - never yank an
                // in-progress target to re-evaluate who has the most HP;
                // that's a job for whatever it's already doing, not this.
                if (st.Mai.HaveTarget()) continue;

                // A player's own standing order always wins - this system
                // only ever fills an otherwise-idle Neutral tame's empty
                // target slot, never competes with an explicit command.
                if (st.ForcedTarget != null) continue;
                if (Time.time < st.NextNeutralDefenseAt) continue;

                if ((st.Chr.transform.position - player.transform.position).sqrMagnitude > rSq) continue;

                // Don't hand out an order this creature cannot actually KEEP.
                // SensesForcedTarget stands a forced target down the moment
                // it sits outside the creature's own m_alertRange, so picking
                // purely on "the troll is near the player" produced two
                // systems judging by different rulers: this one engaged
                // (troll near player), SensesForcedTarget immediately stood
                // it down (target far from troll), and the pair alternated
                // every single tick - confirmed live as ~40 engage/stand-down
                // pairs against one fox, which in-game reads as the troll
                // locking up and jittering the instant anything alerts it.
                // Applying the same range gate here makes the two agree by
                // construction rather than by luck.
                float keepRange = st.Mai.m_alertRange;
                bool rangeSane = keepRange > 0f && keepRange < 9000f;

                Character best = null;
                float bestHp = float.NegativeInfinity;

                foreach (var c in Character.GetAllCharacters())
                {
                    if (c == null || c == st.Chr || c.IsDead() || c.IsTamed()) continue;
                    var cai = c.GetBaseAI();
                    if (cai == null || cai.GetTargetCreature() != player) continue;

                    if (rangeSane &&
                        (c.transform.position - st.Chr.transform.position).sqrMagnitude > keepRange * keepRange)
                        continue;

                    float hp = c.GetHealth();
                    if (hp > bestHp) { bestHp = hp; best = c; }
                }

                if (best == null) continue;

                if (Plugin.Verbose)
                    Plugin.Log.LogInfo(
                        $"[CC neutral-defend] {st.Prefab} steps in against " +
                        $"{best.name} (HP {bestHp:0}) currently attacking the player.");

                // Same grudge channel Grudges.PropagateToPets already uses for
                // "your owner just got hit" - this is the same justification
                // one step earlier (the fight is already happening, not yet
                // landed on anyone this tame follows), so it earns the exact
                // same standing to actually swing under IsEnemy's Neutral
                // case (HoldsGrudge), not just perceive the threat.
                st.Remember(best);

                // MUST go through the tracked ForcedTarget slot, not the raw
                // ForcedTarget.Force() field write - CanHearTarget/CanSeeTarget's
                // Guard bypass (Patches.cs) only keeps re-validating this target
                // as "sensed" every subsequent tick if st.SensesForcedTarget(it)
                // is true, which checks st.ForcedTarget specifically. Skipping
                // that and only raw-writing the field was the actual bug behind
                // "stuck cycling Alert on and off" - this operates at 30m,
                // clean outside a troll's own short natural sensing range, so
                // vanilla's real perception check dropped the target as
                // unseen the very next tick, and this Tick() just re-forced it
                // a second later, forever. SetForcedTarget already calls
                // Alert() itself.
                st.SetForcedTarget(best);
                st.NextNeutralDefenseAt = Time.time + 5f;
            }
        }
    }
}
