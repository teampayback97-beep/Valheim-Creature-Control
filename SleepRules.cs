using HarmonyLib;

namespace CreatureControl
{
    /// <summary>
    /// Drops the "you need a fire" requirement for sleeping, without touching
    /// the shelter requirement.
    ///
    /// Vanilla gates sleeping behind two independent checks on Bed:
    ///   CheckExposure - must be under a roof, and at least 80% covered
    ///   CheckFire     - must be inside an EffectArea.Type.Heat area
    /// They're separate methods, so suppressing the second leaves the first
    /// completely intact: a bed still has to be properly sheltered, it just
    /// no longer has to sit next to a lit fire. That keeps the part of the
    /// rule that's about building a real house and drops the part that
    /// dictates where the hearth has to go.
    ///
    /// Note this also covers torch sconces and any other heat source
    /// implicitly - the requirement isn't being redefined or widened to new
    /// fire types, it's simply not asked at all, so nothing needs to know
    /// which props count as a fire.
    /// </summary>
    [HarmonyPatch(typeof(Bed), "CheckFire")]
    static class Patch_Bed_CheckFire_NoFireNeeded
    {
        static bool Prefix(ref bool __result)
        {
            if (!Plugin.SleepWithoutFire) return true;   // vanilla behaviour

            __result = true;
            return false;   // skip the original - never reports "$msg_bednofire"
        }
    }
}
