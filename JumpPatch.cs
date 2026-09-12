using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // KrokMP's Body.Jump prefix calls NetBody.StopPiggyback() on the jumper, so a
    // belly-carried player pressing jump would drop straight out onto the carrier's
    // back. We run first and, when the local player is being belly-carried, eat the
    // jump entirely: it becomes a "let me out" struggle instead, and only the
    // carrier's regurgitate actually releases them.
    //
    // Safety valve: mashing jump AllowPassengerForceOut times inside 4s forces the
    // release, so nobody gets permanently stuck if the carrier can't act.
    [HarmonyPatch(typeof(Body), "Jump")]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("KrokoshaCasualtiesMP")]
    internal static class Body_Jump_BellyCarry
    {
        // KrokMP's own Body.Jump prefix stops a carrier's passenger too. Keep
        // that internal stop from running while still allowing the carrier to
        // perform the ordinary jump.
        internal static bool SuppressPiggybackStop;
        private static float _struggleCooldown;
        private static float _mashWindowEnds;
        private static int _mashCount;

        private static bool Prefix(Body __instance)
        {
            if (!Plugin.Enabled.Value) return true;

            var me = BellyCarryState.LocalNetBody();
            var jumpedNet = __instance != null ? __instance.GetComponent<NetBody>() : null;
            if (jumpedNet != null && BellyCarryState.Carries.ContainsKey((ushort)jumpedNet.netId)
                && (me == null || __instance != me.body))
            {
                // Server relays a remote passenger jump through Body.Jump too;
                // block KrokMP's StopPiggyback there as well.
                return false;
            }
            if (me == null || __instance != me.body) return true;

            ushort meId = (ushort)me.netId;
            bool rider = BellyCarryState.Carries.ContainsKey(meId);
            bool carrier = BellyCarryState.PassengersOf(meId) > 0;
            if (!rider && !carrier) return true;

            if (carrier)
            {
                SuppressPiggybackStop = true;
                return true; // preserve the carrier's jump; StopPiggyback is filtered below
            }

            float now = Time.unscaledTime;

            // fresh key press (KrokMP keeps calling Jump every frame while held)
            if (Input.GetKeyDown(KeyBinds.GetBind("jump")))
            {
                Diag.Log($"Jump intercepted while belly-carried (meId={meId}); blocking KrokMP StopPiggyback");
                if (now > _mashWindowEnds) { _mashCount = 0; _mashWindowEnds = now + 4f; }
                _mashCount++;

                if (Plugin.AllowPassengerForceOut.Value
                    && _mashCount >= Mathf.Max(2, Plugin.ForceOutTaps.Value))
                {
                    _mashCount = 0;
                    if (BellyCarryState.Carries.TryGetValue(meId, out var carrierId))
                    {
                        BellyNet.SendGrab(carrierId, meId, active: false);
                        Plugin.Logger.LogInfo("BellyCarry: passenger forced their way out.");
                    }
                    return false;
                }

                if (now >= _struggleCooldown)
                {
                    _struggleCooldown = now + 0.6f;
                    BellyNet.SendStruggle(meId);
                }
            }

            return false;   // never let KrokMP's Body.Jump prefix (StopPiggyback) run
        }

        private static void Postfix() => SuppressPiggybackStop = false;
    }
}
