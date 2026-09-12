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
        // Run before KrokMP's rule check as well as after it.  The server's
        // carry receiver rejects the request immediately when this returns
        // false, so a postfix alone can be too late if another patch has
        // already consumed the value.
        private static bool Prefix(ref bool __result)
        {
            if (Plugin.Enabled.Value && Plugin.ReplaceCarry.Value && Plugin.AllowConsciousCarry.Value)
            {
                __result = true;
                return false;
            }
            return true;
        }

        private static void Postfix(ref bool __result)
        {
            if (Plugin.Enabled.Value && Plugin.ReplaceCarry.Value && Plugin.AllowConsciousCarry.Value)
                __result = true;
        }
    }
}
