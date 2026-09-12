using System.Collections.Generic;
using System.Linq;
using CasualtiesConsumed.Components;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // Shared state for the belly-carry feature. Belly-carry == a KrokMP piggyback
    // that was started by the *carry* interaction (K), which we (a) render as vore
    // instead of a ride, (b) exempt from Consumed's stomach digestion, and (c) keep
    // attached when the passenger presses jump.
    internal static class BellyCarryState
    {
        // passenger netId -> carrier netId. Mirrored on every client via Net.cs.
        public static readonly Dictionary<ushort, ushort> Carries = new();

        // VoredGameObject entries that must never digest (Health pinned to 100).
        // Keyed by the passenger netId so we can find/remove them on release.
        public static readonly Dictionary<ushort, VoredGameObject> BellyEntries = new();
        // Placeholder creature objects used by Consumed's stomach UI so a
        // carried player appears in the circular stomach view. They have no
        // active gameplay components and are destroyed when the carry ends.
        public static readonly Dictionary<ushort, GameObject> BellyIcons = new();

        // The full SpriteRenderer set we hid on a carried body, so we can re-enable
        // it on release. Re-hidden every LateUpdate (Consumed's SpriteController
        // re-enables them whenever the passenger's own stomach stage changes).
        public static readonly Dictionary<ushort, SpriteRenderer[]> HiddenSprites = new();

        // All renderers, including the multiplayer nametag/status renderers. The
        // sprite-only list is kept for backwards-compatible diagnostics.
        public static readonly Dictionary<ushort, Renderer[]> HiddenRenderers = new();

        // carrier netId -> its BodyVoreController.DigestionRate before we zeroed it
        // (so a belly-carry never actually fattens / feeds the carrier).
        public static readonly Dictionary<ushort, float> SavedDigestion = new();

        // HOST ONLY: passenger netId -> when its piggyback link first looked gone,
        // so a still-syncing link gets a grace window before we tear the carry down.
        public static readonly Dictionary<ushort, float> PiggybackMissingSince = new();
        public static readonly Dictionary<ushort, float> ReleaseSuppressUntil = new();

        public static bool IsSafe(VoredGameObject v) => v != null && BellyEntries.ContainsValue(v);

        public static NetBody Resolve(ushort netId)
        {
            foreach (var kv in NetBody.NetIdToNetBody)
                if ((ushort)kv.Key == netId) return kv.Value;
            return null;
        }

        public static NetBody LocalNetBody() => NetPlayer.GetLocalNetBodyNullable();

        public static int PassengersOf(ushort carrierNetId) =>
            Carries.Count(kv => kv.Value == carrierNetId);

        public static bool AmCarrying(ushort passengerNetId, out ushort carrierNetId)
        {
            var me = LocalNetBody();
            if (me != null && Carries.TryGetValue(passengerNetId, out carrierNetId) && carrierNetId == (ushort)me.netId)
                return true;
            carrierNetId = 0;
            return false;
        }
    }
}
