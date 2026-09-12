using System;
using CasualtiesConsumed.Components;
using CasualtiesConsumed.Utility;
using KrokoshaCasualtiesMP;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // Two custom KrokMP messages, both client -> server -> all-clients:
    //   MsgGrab (28777):    carrier, passenger, active   -> BellyCarryState.Carries
    //   MsgStruggle (28778): passenger                   -> carrier's stomach Struggle()
    // The attach/position sync is plain KrokMP piggyback.
    internal static class BellyNet
    {
        public const ushort MsgGrab = 28777;      // outside KrokMP (10xxx) and Consumed (29xxx)
        public const ushort MsgStruggle = 28778;
        public const ushort MsgVoreRequest = 28779;
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered || !Net.is_client_or_host) return;
            _registered = true;
            try
            {
                if (Net.is_server)
                {
                    ServerMain.RegisterServerReceiver(MsgGrab, OnServerGrab);
                    ServerMain.RegisterServerReceiver(MsgStruggle, OnServerStruggle);
                    ServerMain.RegisterServerReceiver(MsgVoreRequest, OnServerVoreRequest);
                }
                else
                {
                    ClientMain.RegisterClientReceiver(MsgGrab, OnClientGrab);
                    ClientMain.RegisterClientReceiver(MsgStruggle, OnClientStruggle);
                }
                Plugin.Logger.LogInfo($"BellyCarry net receivers registered ({(Net.is_server ? "server" : "client")}).");
            }
            catch (Exception e)
            {
                _registered = false;
                Plugin.Logger.LogWarning($"BellyCarry net register failed: {e.Message}");
            }
        }

        public static void SendVoreRequest(ushort passenger)
        {
            if (Net.is_server) return;
            var w = Net.CreateWriter(MsgVoreRequest);
            w.Put(passenger);
            Net.Client_Send(DeliveryMethod.ReliableOrdered, w);
        }

        private static void OnServerVoreRequest(knetid sender, ref NetDataReader r)
        {
            ushort passengerId = r.GetUShort();
            if (!NetPlayer.TryGetNetPlayerAndNetBodyFromClientId(sender, out var plr, out var carrier)) return;
            var passenger = BellyCarryState.Resolve(passengerId);
            if (passenger == null || carrier == null || passenger == carrier || !plr.body.conscious) return;
            if (passenger.carrying_person != null || passenger.piggybacking_on != null) return;
            if (passenger.StartPiggyback(carrier, check_distance: true, force: true))
                SendGrab((ushort)carrier.netId, passengerId, active: true);
        }

        // ---- grab / release ---------------------------------------------------
        public static void SendGrab(ushort carrier, ushort passenger, bool active)
        {
            try
            {
                bool changed = ApplyGrab(carrier, passenger, active);   // optimistic / local-authoritative
                if (Net.is_server)
                {
                    if (changed || !active) BroadcastGrab(carrier, passenger, active);
                }
                else if (changed)
                {
                    var w = Net.CreateWriter(MsgGrab);
                    w.Put(carrier); w.Put(passenger); w.Put(active);
                    Net.Client_Send(DeliveryMethod.ReliableOrdered, w);
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"BellyCarry SendGrab failed: {e.Message}"); }
        }

        private static void OnServerGrab(knetid sender, ref NetDataReader r)
        {
            ushort carrier = r.GetUShort(), passenger = r.GetUShort();
            bool active = r.GetBool();
            bool changed = ApplyGrab(carrier, passenger, active);
            if (changed || !active) BroadcastGrab(carrier, passenger, active);
        }

        private static void BroadcastGrab(ushort carrier, ushort passenger, bool active)
        {
            var w = Net.CreateWriter(MsgGrab);
            w.Put(carrier); w.Put(passenger); w.Put(active);
            // The VeryReliable wrapper enters the Steam transport adapter in
            // some builds and can throw while converting the client-id list.
            // ReliableOrdered is sufficient for these idempotent state
            // transitions and uses the same path as the working struggle
            // messages.
            Net.Server_SendToClients(DeliveryMethod.ReliableOrdered, in w, ServerMain.AllClientIds);
        }

        public static void ResyncActive()
        {
            if (!Net.is_server) return;
            foreach (var kv in BellyCarryState.Carries)
            {
                BellyCarryDriver.ApplyCarryVisuals(kv.Key, kv.Value);
                BroadcastGrab(kv.Value, kv.Key, active: true);
            }
        }

        private static void OnClientGrab(knetid _, ref NetDataReader r)
            => ApplyGrab(r.GetUShort(), r.GetUShort(), r.GetBool());

        // Returns true if BellyCarryState.Carries actually changed.
        private static bool ApplyGrab(ushort carrier, ushort passenger, bool active)
        {
            bool changed;
            if (active)
            {
                changed = !(BellyCarryState.Carries.TryGetValue(passenger, out var cur) && cur == carrier);
                BellyCarryState.Carries[passenger] = carrier;
            }
            else
            {
                BellyCarryState.ReleaseSuppressUntil[passenger] = Time.unscaledTime + 5f;
                changed = BellyCarryState.Carries.Remove(passenger);
                if (changed && BellyCarryState.SavedDigestion.TryGetValue(carrier, out var saved))
                {
                    var body = BellyCarryState.Resolve(carrier)?.body;
                    var vc = body?.GetComponent<BodyVoreController>();
                    if (vc != null) vc.DigestionRate = saved;
                    BellyCarryState.SavedDigestion.Remove(carrier);
                }
                if (changed)
                {
                    BellyCarryDriver.ApplyCarryReleaseVisuals(passenger);
                    if (!Net.is_server)
                    {
                        var localPassenger = BellyCarryState.Resolve(passenger);
                        if (localPassenger?.piggybacking_on != null)
                        {
                            try { localPassenger.StopPiggyback(); } catch { }
                        }
                    }
                }
                // On the server, drive the real KrokMP teardown so every client detaches.
                // Carries no longer holds `passenger`, so the StopPiggyback hook is a no-op here.
                if (changed && Net.is_server)
                {
                    var pB = BellyCarryState.Resolve(passenger);
                    if (pB != null && pB.piggybacking_on != null)
                    {
                        try { pB.StopPiggyback(); } catch { }
                    }
                }
            }
            if (active)
                BellyCarryDriver.ApplyCarryVisuals(passenger, carrier);
            if (changed) Gulp(carrier);
            Diag.Log($"ApplyGrab side={(Net.is_server ? "server" : "client")} carrier={carrier} passenger={passenger} active={active} changed={changed} CarriesNow={BellyCarryState.Carries.Count}");
            return changed;
        }

        private static void Gulp(ushort carrierNetId)
        {
            var cB = BellyCarryState.Resolve(carrierNetId);
            if (cB?.body == null) return;
            try
            {
                var clip = AudioLoader.GetRandomClipInPath(SpriteLoader.GetAsTruePath("sounds\\gulps"));
                if (clip != null)
                    Sound.Play(clip, cB.body.transform.position, twoDimensional: false, pitchShift: true,
                               follow: null, volume: AudioLoader.VolumeMultiplier);
            }
            catch { }
        }

        // ---- struggle ("let me out") ----------------------------------------
        public static void SendStruggle(ushort passenger)
        {
            try
            {
                if (Net.is_server) { ServerBroadcastStruggle(passenger); }
                else
                {
                    var w = Net.CreateWriter(MsgStruggle);
                    w.Put(passenger);
                    Net.Client_Send(DeliveryMethod.ReliableUnordered, w);
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"BellyCarry SendStruggle failed: {e.Message}"); }
        }

        private static void OnServerStruggle(knetid sender, ref NetDataReader r)
            => ServerBroadcastStruggle(r.GetUShort());

        private static void ServerBroadcastStruggle(ushort passenger)
        {
            ApplyStruggle(passenger);
            var w = Net.CreateWriter(MsgStruggle);
            w.Put(passenger);
            Net.Server_SendToClients(DeliveryMethod.ReliableUnordered, w, ServerMain.AllClientIds);
        }

        private static void OnClientStruggle(knetid _, ref NetDataReader r)
            => ApplyStruggle(r.GetUShort());

        private static void ApplyStruggle(ushort passenger)
        {
            if (!BellyCarryState.Carries.TryGetValue(passenger, out var carrierId)) return;
            var cB = BellyCarryState.Resolve(carrierId);
            if (cB?.body == null) return;
            var vc = cB.body.GetComponent<BodyVoreController>();
            if (vc == null) return;

            vc.Struggle(1.5f);    // visible stomach shake + stronger thump; Aggression stays 0

            // extra nudge on the carrier's own screen
            if (cB.is_local && PlayerCamera.main != null)
                PlayerCamera.main.DoAlert("Something's squirming in your belly...", false);
        }
    }
}
