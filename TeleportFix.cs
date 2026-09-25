using System;
using HarmonyLib;
using UnityEngine;

namespace CreatureControl
{
    /// <summary>
    /// BetterTames teleports a pet by writing transform.position. But a Valheim
    /// Character is driven by a non-kinematic Rigidbody, and Unity rewrites the
    /// transform from the rigidbody on the next FixedUpdate - so the pet shows
    /// up at the destination for a frame and is then dragged back to wherever
    /// physics still believes it is. That is the flicker-and-return.
    ///
    /// The cure is to move the rigidbody too, kill its momentum, and push the
    /// new position into the ZDO right away instead of waiting for a sync tick.
    ///
    /// Applied from here rather than by editing BetterTames' IL again: the type
    /// is resolved by name at runtime, so this quietly does nothing when
    /// BetterTames isn't installed, and it survives BetterTames being updated.
    /// </summary>
    internal static class TeleportFix
    {
        const string TypeName = "BetterTames.DistanceTeleportLogic";
        const string MethodName = "ExecuteTeleportBehindPlayer";

        public static void TryPatch(Harmony harmony)
        {
            try
            {
                var type = AccessTools.TypeByName(TypeName);
                if (type == null)
                {
                    Plugin.Log.LogInfo("BetterTames not installed - teleport fix not needed.");
                    return;
                }

                var target = AccessTools.Method(type, MethodName);
                if (target == null)
                {
                    Plugin.Log.LogWarning(
                        $"Found {TypeName} but not {MethodName}() - BetterTames has probably " +
                        "changed shape, so the teleport fix was skipped. Pets may snap back.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(TeleportFix), nameof(Postfix))));

                Plugin.Log.LogInfo("Teleport fix applied to BetterTames.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not apply the BetterTames teleport fix: {e.Message}");
            }
        }

        // Parameter name matches BetterTames' own signature so Harmony can bind it.
        static void Postfix(Character characterToTeleport)
        {
            var chr = characterToTeleport;
            if (chr == null) return;

            var tf = chr.transform;
            if (tf == null) return;

            Vector3 pos = tf.position;
            Quaternion rot = tf.rotation;

            var body = chr.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = pos;
                body.rotation = rot;
                // Leftover momentum would otherwise fling the pet off the moment
                // it lands, or drag it back along its old path.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            var sync = chr.GetComponent<ZSyncTransform>();
            if (sync != null) sync.SyncNow();

            if (Plugin.Verbose)
                Plugin.Log.LogInfo(
                    $"[CC] teleport landed: {CreatureRules.CleanName(chr.gameObject.name)} " +
                    $"at {pos} (rigidbody + ZDO synced)");
        }
    }
}
