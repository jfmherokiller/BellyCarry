using HarmonyLib;
using KrokoshaCasualtiesMP;

namespace CasualtiesUnknown.BellyCarry
{
    // KrokMP normally exposes Carry only for downed bodies unless its global
    // AlwaysAllowCarry rule is enabled. BellyCarry can own that exception so
    // the host does not need to change the multiplayer rules preset.
    [HarmonyPatch(typeof(NetBody), nameof(NetBody.CanBeCarriedBySomeone))]
    internal static class RulePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.Enabled.Value && Plugin.ReplaceCarry.Value && Plugin.AllowConsciousCarry.Value)
                __result = true;
        }
    }
}
