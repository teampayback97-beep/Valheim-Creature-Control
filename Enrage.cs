using System.Collections;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// STATUS: "attack_taunt" is confirmed against Bjorn's real
    /// Bjorn_animator.controller (extracted via AssetRipper) - it's a real
    /// trigger parameter and plays the real "Taunt.anim" clip. Other
    /// creatures aren't confirmed yet and may not have this trigger at all,
    /// so it's still wrapped in a try/catch and treated as a bonus, not a
    /// requirement. The scale-pulse + emission tint below it needs no
    /// animation asset at all and is the actual guaranteed cue; verify
    /// _EmissionColor is the right shader property for whichever creature's
    /// material you test this on.
    ///
    /// Cosmetic only - CreatureState.ApplyEnrageEffects/RemoveEnrageEffects
    /// own the actual resistances/threat/damage; this is purely what the
    /// player sees and hears the instant a creature enters or leaves Enraged.
    /// </summary>
    internal static class EnrageCue
    {
        // "attack_taunt" is Bjorn's real trigger name (confirmed from
        // Bjorn_animator.controller). Left as a list in case other
        // creatures use a different name for their equivalent taunt/roar.
        static readonly string[] BestEffortTriggers = { "attack_taunt" };

        public static void Play(CreatureState st)
        {
            if (st?.Chr == null) return;
            TryBestEffortAnimation(st.Chr);
            st.Chr.StartCoroutine(PulseRoutine(st.Chr));
        }

        static void TryBestEffortAnimation(Character chr)
        {
            var anim = chr.GetComponent<ZSyncAnimation>();
            if (anim == null) return;
            foreach (var name in BestEffortTriggers)
            {
                try { anim.SetTrigger(name); }
                catch { /* purely cosmetic; a miss here changes nothing else */ }
            }
        }

        const float PulseSeconds = 0.6f;
        const float PulseScale = 1.15f;

        static IEnumerator PulseRoutine(Character chr)
        {
            if (chr == null) yield break;
            var tf = chr.transform;
            Vector3 baseScale = tf.localScale;

            var renderers = chr.GetComponentsInChildren<Renderer>();
            var blocks = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                blocks[i] = new MaterialPropertyBlock();
                try
                {
                    renderers[i].GetPropertyBlock(blocks[i]);
                    blocks[i].SetColor("_EmissionColor", Color.red * 1.5f);
                    renderers[i].SetPropertyBlock(blocks[i]);
                }
                catch { /* shader may not expose this property; skip the tint on it */ }
            }

            float t = 0f;
            while (t < PulseSeconds && chr != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(t / PulseSeconds * Mathf.PI);
                tf.localScale = baseScale * (1f + (PulseScale - 1f) * k);
                yield return null;
            }
            if (chr != null) tf.localScale = baseScale;

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                try
                {
                    blocks[i].SetColor("_EmissionColor", Color.black);
                    renderers[i].SetPropertyBlock(blocks[i]);
                }
                catch { /* already skipped above if unsupported */ }
            }
        }
    }
}
