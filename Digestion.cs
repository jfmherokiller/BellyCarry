using CasualtiesConsumed.Components;
using HarmonyLib;

namespace CasualtiesUnknown.BellyCarry
{
    // BodyVoreController.Update() runs digestion:
    //     stomachContent.Health -= digestionRate * ... ;
    //     StomachContents = StomachContents.Where(x => x.Health > 0f).ToList();
    // We can't add a field to VoredGameObject, so after Update runs we simply
    // pin Health back to 100 for the entries we own (BellyCarryState.BellyEntries).
    // The per-frame delta is tiny, so the entry never hits 0 within one frame and
    // survives the Where() filter.
    [HarmonyPatch(typeof(BodyVoreController), nameof(BodyVoreController.Update))]
    internal static class BodyVoreController_Update
    {
        private static void Prefix(BodyVoreController __instance)
        {
            if (BellyCarryState.BellyEntries.Count == 0 || __instance?.CharBody == null) return;
            foreach (var kv in BellyCarryState.BellyEntries)
            {
                if (!BellyCarryState.Carries.TryGetValue(kv.Key, out var carrierId)) continue;
                var carrier = BellyCarryState.Resolve(carrierId);
                if (carrier?.body == __instance.CharBody)
                {
                    // Must happen before Consumed.Update computes its digestion
                    // delta; changing it from the driver afterward is too late
                    // and causes weightOffset to climb every frame.
                    __instance.DigestionRate = 0f;
                    break;
                }
            }
        }

        private static void Postfix(BodyVoreController __instance)
        {
            if (BellyCarryState.BellyEntries.Count == 0) return;
            foreach (var v in __instance.StomachContents)
                if (BellyCarryState.IsSafe(v))
                    v.Health = 100f;
        }
    }
}
