using CasualtiesConsumed.Components;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // How a belly-carry starts and ends — entirely off KrokMP's own piggyback,
    // so it feels like a native interaction and needs no keybind of ours.
    //
    //   Swallow  = the *carry* interaction (K / radial). Every carry path — a
    //              client's predicted NetBody.StartPiggyback, the server's 10033
    //              handler, the host carrying directly — funnels through
    //              NetBody.StartPiggyback with check_distance:true. The *piggyback*
    //              interaction (P) passes check_distance:false, so it stays a
    //              normal ride.
    //   Release  = the carrier's Consumed regurgitate (H). Anything that ends the
    //              underlying piggyback (death, distance, disconnect) also ends the
    //              belly-carry via the StopPiggyback hook.
    internal static class CarryHook
    {
        [HarmonyPatch(typeof(NetBody), "Update")]
        internal static class NetBody_Update
        {
            private static void Postfix(NetBody __instance)
            {
                if (__instance == null || !Plugin.Enabled.Value || !Plugin.ReplaceCarry.Value) return;
                NetBody passengerBody = __instance.piggybacking_on != null ? __instance : __instance.carrying_person;
                NetBody carrierBody = __instance.piggybacking_on != null ? __instance.piggybacking_on : __instance;
                if (passengerBody != null && carrierBody != null)
                {
                    ushort p = (ushort)passengerBody.netId, c = (ushort)carrierBody.netId;
                    if (!BellyCarryState.Carries.ContainsKey(p)) BellyNet.SendGrab(c, p, active: true);
                    BellyCarryDriver.ApplyCarryVisuals(p, c);
                    return;
                }
                ushort id = (ushort)__instance.netId;
                if (BellyCarryState.Carries.ContainsKey(id))
                {
                    BellyCarryState.Carries.Remove(id);
                    BellyCarryDriver.ApplyCarryReleaseVisuals(id);
                }
            }
        }

        [HarmonyPatch(typeof(NetBody), "HandlePiggybackUpdate")]
        internal static class NetBody_HandlePiggybackUpdate
        {
            private static void Postfix(NetBody __instance)
            {
                if (__instance == null) return;
                ushort self = (ushort)__instance.netId;
                if (__instance.piggybacking_on == null && __instance.carrying_person == null
                    && BellyCarryState.Carries.ContainsKey(self))
                {
                    BellyCarryState.Carries.Remove(self);
                    BellyCarryDriver.ApplyCarryReleaseVisuals(self);
                    Diag.Log($"Detached-state cleanup: released stale carry {self}");
                    return;
                }
                if (__instance?.carrying_person == null || !Plugin.Enabled.Value || !Plugin.ReplaceCarry.Value)
                    return;
                ushort carrier = (ushort)__instance.netId;
                ushort passenger = (ushort)__instance.carrying_person.netId;
                if (!BellyCarryState.Carries.TryGetValue(passenger, out var cur) || cur != carrier)
                    BellyNet.SendGrab(carrier, passenger, active: true);
            }
        }

        [HarmonyPatch(typeof(Net), nameof(Net.TransportCreated))]
        internal static class Net_TransportCreated
        {
            private static void Postfix() => BellyNet.EnsureRegistered();
        }

        // --- swallow: a carry-interaction piggyback succeeded -----------------
        [HarmonyPatch(typeof(NetBody), nameof(NetBody.StartPiggyback))]
        internal static class NetBody_StartPiggyback
        {
            private static bool Prefix(NetBody __instance)
            {
                if (__instance == null) return true;
                return !BellyCarryState.ReleaseSuppressUntil.ContainsKey((ushort)__instance.netId);
            }

            // __instance = the carried person, target = the carrier.
            private static void Postfix(NetBody __instance, NetBody target, bool check_distance, bool __result)
            {
                Diag.Log($"StartPiggyback Postfix: carried={(__instance == null ? "null" : ((ushort)__instance.netId).ToString())} "
                    + $"carrier={(target == null ? "null" : ((ushort)target.netId).ToString())} "
                    + $"check_distance={check_distance} result={__result} enabled={Plugin.Enabled.Value} replace={Plugin.ReplaceCarry.Value}");

                // A remote carry can arrive as KrokMP's ordinary sync path
                // without MsgGrab. Apply the visual state immediately here,
                // before the driver has a chance to run (or if it is delayed).
                bool linked = __instance != null && target != null
                    && (__instance.piggybacking_on == target || target.carrying_person == __instance);
                if (linked && !check_distance && Plugin.Enabled.Value
                    && Plugin.ReplaceCarry.Value && __instance != null && target != null)
                {
                    ushort p = (ushort)__instance.netId;
                    ushort c = (ushort)target.netId;
                    if (BellyCarryState.ReleaseSuppressUntil.ContainsKey(p))
                    {
                        // Hold suppression until KrokMP has actually cleared
                        // the relationship; a fixed timeout can re-enter while
                        // stale sync packets are still arriving.
                        if (__instance.piggybacking_on == null && target.carrying_person != __instance)
                            BellyCarryState.ReleaseSuppressUntil.Remove(p);
                        else
                            return;
                    }
                    bool missing = !BellyCarryState.Carries.ContainsKey(p);
                    if (missing)
                        BellyNet.SendGrab(c, p, active: true);
                    BellyCarryState.Carries[p] = c;
                    BellyCarryDriver.ApplyCarryVisuals(p, c);
                    Diag.Log($"Client StartPiggyback fallback: inferred carry {p}=>{c}");
                }

                if (!__result || !check_distance) return;                 // not a carry interaction
                if (!Plugin.Enabled.Value || !Plugin.ReplaceCarry.Value) return;
                if (__instance?.body == null || target?.body == null) return;

                ushort pId = (ushort)__instance.netId;
                ushort cId = (ushort)target.netId;

                if (!BellyCarryState.Carries.ContainsKey(pId)
                    && BellyCarryState.PassengersOf(cId) >= Mathf.Max(1, Plugin.MaxPassengers.Value))
                {
                    Diag.Log($"  over capacity ({BellyCarryState.PassengersOf(cId)}/{Plugin.MaxPassengers.Value}) - not grabbing");
                    return;                                              // KrokMP's PiggybackMaxStack also caps this
                }

                Diag.Log($"  -> SendGrab({cId}, {pId}, true)");
                BellyNet.SendGrab(cId, pId, active: true);
            }
        }

        // --- release: the underlying piggyback ended -------------------------
        [HarmonyPatch(typeof(NetBody), nameof(NetBody.StopPiggyback))]
        internal static class NetBody_StopPiggyback
        {
            private static bool Prefix(NetBody __instance, bool isinternal, out NetBody __state)
            {
                __state = __instance != null ? __instance.piggybacking_on : null;
                if (Body_Jump_BellyCarry.SuppressPiggybackStop && __instance != null)
                {
                    ushort id = (ushort)__instance.netId;
                    bool activePassenger = BellyCarryState.Carries.ContainsKey(id);
                    bool activeCarrier = __instance.carrying_person != null
                        && BellyCarryState.Carries.ContainsKey((ushort)__instance.carrying_person.netId);
                    // Keep the underlying KrokMP link intact while a belly carry
                    // is active. Authoritative release removes Carries first, so
                    // this guard does not block regurgitate/force-out teardown.
                    if (activePassenger || activeCarrier)
                    {
                        Diag.Log($"StopPiggyback blocked during belly carry id={id} internal={isinternal}");
                        return false;
                    }
                }
                return true;
            }

            private static void Postfix(NetBody __instance, NetBody __state, bool __runOriginal)
            {
                if (!__runOriginal) return;
                if (__instance == null || __state == null) return;
                ushort pId = (ushort)__instance.netId;
                ushort cId = (ushort)__state.netId;
                if (!BellyCarryState.Carries.ContainsKey(pId)) return;    // not a belly-carry (or already torn down)

                if (!Net.is_server)
                {
                    // A client may miss the custom release packet, but it still
                    // receives KrokMP's authoritative detach. Restore locally.
                    BellyCarryState.ReleaseSuppressUntil[pId] = Time.unscaledTime + 5f;
                    BellyCarryState.Carries.Remove(pId);
                    BellyCarryDriver.ApplyCarryReleaseVisuals(pId);
                    return;
                }

                BellyNet.SendGrab(cId, pId, active: false);
            }
        }

        // --- carrier presses regurgitate (H) with nothing edible to bring up --
        [HarmonyPatch(typeof(BodyVoreController), nameof(BodyVoreController.TryRegurgitate))]
        internal static class TryRegurgitate
        {
            private static void Postfix(BodyVoreController __instance, ref bool __result)
            {
                if (!Plugin.Enabled.Value || __result) return;           // something edible already came up
                var me = BellyCarryState.LocalNetBody();
                if (me == null || __instance.CharBody != me.body) return;

                ushort meId = (ushort)me.netId;
                NetBody nearest = null;
                float best = float.MaxValue;
                foreach (var kv in BellyCarryState.Carries)
                {
                    if (kv.Value != meId) continue;
                    var pB = BellyCarryState.Resolve(kv.Key);
                    if (pB?.body == null) continue;
                    float d = Vector2.Distance(me.body.transform.position, pB.body.transform.position);
                    if (d < best) { best = d; nearest = pB; }
                }
                if (nearest == null) return;

                nearest.StopPiggyback();                                  // local visual detach
                BellyNet.SendGrab(meId, (ushort)nearest.netId, false);    // tell the server to tear it down everywhere
                __result = true;                                          // consumed the regurgitate press
                Plugin.Logger.LogInfo($"BellyCarry: regurgitated {nearest.playername}.");
            }
        }
    }
}
