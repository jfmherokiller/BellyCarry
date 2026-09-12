using System.Collections.Generic;
using System.Linq;
using CasualtiesConsumed.Components;
using CUCoreLib.Registries;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // Per-frame, on every client: mirror BellyCarryState onto the visual/gameplay
    // layer — hide the carried body, give the carrier a non-digesting vore bulge,
    // freeze the carrier's digestion, and show the moodles. The grab/release
    // *decisions* live in CarryHook (KrokMP piggyback + Consumed regurgitate); this
    // class only reflects the resulting state.
    public class BellyCarryDriver : MonoBehaviour
    {
        private float _hb;

        private void Update()
        {
            if (!Plugin.Enabled.Value || !Net.is_client_or_host) return;
            BellyNet.EnsureRegistered();
            Reconcile();
            DigestionGuard();
            Moodles();

            _hb -= Time.unscaledDeltaTime;
            if (_hb <= 0f)
            {
                // Re-send active state frequently enough to repair a missed
                // transition (especially when re-consuming the same player).
                // The message is idempotent and ReliableOrdered.
                _hb = 0.5f;
                BellyNet.ResyncActive();
                var me = BellyCarryState.LocalNetBody();
                Diag.Log($"heartbeat: is_server={Net.is_server} localNetId={(me == null ? "null" : ((ushort)me.netId).ToString())} "
                    + $"Carries={BellyCarryState.Carries.Count} Hidden={BellyCarryState.HiddenSprites.Count} "
                    + $"Belly={BellyCarryState.BellyEntries.Count} SavedDig={BellyCarryState.SavedDigestion.Count}");
            }
        }

        // Consumed's BodySpriteController re-enables a body's sprites whenever its
        // stomach stage changes, so re-assert the hide after everything else has
        // run for the frame.
        private void LateUpdate()
        {
            if (!Plugin.Enabled.Value || BellyCarryState.Carries.Count == 0) return;
            foreach (var kv in BellyCarryState.Carries)
            {
                if (!BellyCarryState.HiddenSprites.TryGetValue(kv.Key, out var set)) continue;
                for (int i = 0; i < set.Length; i++)
                    if (set[i] != null && set[i].enabled) set[i].enabled = false;
                if (!BellyCarryState.HiddenRenderers.TryGetValue(kv.Key, out var renderers)) continue;
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null && renderers[i].enabled) renderers[i].enabled = false;
            }
        }

        // ---- moodles --------------------------------------------------------
        private static int _lastMoodleN = -1;
        private static bool _lastRider;

        private static void Moodles()
        {
            var me = BellyCarryState.LocalNetBody();
            if (me == null) return;
            ushort meId = (ushort)me.netId;

            int n = BellyCarryState.PassengersOf(meId);
            bool rider = BellyCarryState.Carries.ContainsKey(meId);
            if (n != _lastMoodleN || rider != _lastRider)
            {
                Diag.Log($"Moodles state: carrierN={n} rider={rider} (meId={meId})");
                _lastMoodleN = n; _lastRider = rider;
            }

            if (n > 0)
            {
                MoodleRegistry.AddMoodle(
                    3, "voresize3", "Carrying a passenger",
                    n == 1
                        ? "A teammate is riding in your belly. Regurgitate (default H) to let them out."
                        : $"{n} teammates are riding in your belly. Regurgitate (default H) to let one out.",
                    important: true, key: "bellycarry", holdSeconds: 0.5f);
            }

            if (rider)
            {
                MoodleRegistry.AddMoodle(
                    2, "voresize2", "Riding in a belly",
                    "A teammate is carrying you inside them. Mash jump to squirm — or force your way out.",
                    important: true, key: "bellycarry_rider", holdSeconds: 0.5f);
            }
        }

        // ---- digestion freeze --------------------------------------------------
        private static void DigestionGuard()
        {
            var carriers = new HashSet<ushort>(BellyCarryState.Carries.Values);

            foreach (var cId in carriers)
            {
                if (BellyCarryState.SavedDigestion.ContainsKey(cId)) continue;
                var vc = BellyCarryState.Resolve(cId)?.body?.GetComponent<BodyVoreController>();
                if (vc == null) continue;
                BellyCarryState.SavedDigestion[cId] = vc.DigestionRate;
                vc.DigestionRate = 0f;
                Diag.Log($"DigestionGuard: zeroed carrier {cId} DigestionRate (saved {BellyCarryState.SavedDigestion[cId]:0.##})");
            }

            foreach (var cId in BellyCarryState.SavedDigestion.Keys.ToList())
            {
                if (carriers.Contains(cId)) continue;
                var vc = BellyCarryState.Resolve(cId)?.body?.GetComponent<BodyVoreController>();
                if (vc != null) vc.DigestionRate = BellyCarryState.SavedDigestion[cId];
                Diag.Log($"DigestionGuard: restored carrier {cId} DigestionRate -> {BellyCarryState.SavedDigestion[cId]:0.##} (vc {(vc == null ? "NULL - LOST" : "ok")})");
                BellyCarryState.SavedDigestion.Remove(cId);
            }
        }

        // ---- state -> layer -------------------------------------------------
        private void Reconcile()
        {
            // Carries is authoritative shared state driven ONLY by Net.cs messages
            // (the host decides). On a non-owning client the entry arrives by
            // broadcast while KrokMP's piggybacking_on arrives on a separate sync
            // channel, so we must NOT delete a Carries entry just because the
            // piggyback link hasn't landed yet. Reconcile only mirrors Carries
            // onto the visual/gameplay layer.

            // 1) Client fallback: KrokMP can deliver the piggyback attachment
            // before (or without) our custom message. Infer the relationship so
            // the remote passenger is never left visibly riding on the client.
            if (!Net.is_server && Plugin.ReplaceCarry.Value)
            {
                foreach (var nb in NetBody.NetIdToNetBody.Values)
                {
                    if (nb == null) continue;
                    // Depending on which side owns the body, KrokMP may sync
                    // only one half of the relationship.
                    NetBody passengerBody = nb.piggybacking_on != null ? nb : nb.carrying_person;
                    NetBody carrierBody = nb.piggybacking_on != null ? nb.piggybacking_on : nb;
                    if (passengerBody == null || carrierBody == null) continue;
                    ushort passenger = (ushort)passengerBody.netId;
                    ushort carrier = (ushort)carrierBody.netId;
                    if (!BellyCarryState.Carries.ContainsKey(passenger))
                    {
                        BellyCarryState.Carries[passenger] = carrier;
                        Diag.Log($"Client piggyback fallback: inferred carry {passenger}=>{carrier}");
                    }
                }
            }

            // 2) apply the layer for every active carry
            foreach (var kv in BellyCarryState.Carries.ToList())
            {
                var pB = BellyCarryState.Resolve(kv.Key);
                var cB = BellyCarryState.Resolve(kv.Value);
                if (pB?.body == null || cB?.body == null) continue;   // bodies not known yet; retry next frame
                HidePassenger(kv.Key, pB);
                EnsureBelly(kv.Key, cB);
            }

            // 3) undo the layer for anything no longer carried
            var tracked = BellyCarryState.HiddenSprites.Keys
                .Concat(BellyCarryState.BellyEntries.Keys)
                .Distinct().ToList();
            foreach (var passenger in tracked)
            {
                if (BellyCarryState.Carries.ContainsKey(passenger)) continue;
                RestorePassenger(passenger);
                RemoveBelly(passenger);
            }

            // 4) HOST ONLY: end a belly-carry whose underlying piggyback is really
            //    gone (death / distance / disconnect that didn't route through the
            //    StopPiggyback hook). 2s grace so a still-syncing link isn't nuked.
            if (Net.is_server)
            {
                foreach (var kv in BellyCarryState.Carries.ToList())
                {
                    var pB = BellyCarryState.Resolve(kv.Key);
                    bool intact = pB != null && pB.piggybacking_on != null
                                  && (ushort)pB.piggybacking_on.netId == kv.Value;
                    if (intact) { BellyCarryState.PiggybackMissingSince.Remove(kv.Key); continue; }

                    if (!BellyCarryState.PiggybackMissingSince.TryGetValue(kv.Key, out var t))
                    {
                        BellyCarryState.PiggybackMissingSince[kv.Key] = Time.unscaledTime;
                    }
                    else if (Time.unscaledTime - t > 2f)
                    {
                        BellyCarryState.PiggybackMissingSince.Remove(kv.Key);
                        BellyNet.SendGrab(kv.Value, kv.Key, active: false);
                    }
                }
            }
        }

        private static void HidePassenger(ushort id, NetBody pB)
        {
            if (BellyCarryState.HiddenRenderers.ContainsKey(id)) return;
            var set = pB.body.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            foreach (var sr in set) if (sr != null) sr.enabled = false;
            BellyCarryState.HiddenSprites[id] = set;
            var renderers = pB.body.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var renderer in renderers) if (renderer != null) renderer.enabled = false;
            BellyCarryState.HiddenRenderers[id] = renderers;
            Diag.Log($"HidePassenger {id}: disabled {set.Length} SpriteRenderers / {renderers.Length} Renderers");
        }

        private static void RestorePassenger(ushort id)
        {
            if (!BellyCarryState.HiddenSprites.TryGetValue(id, out var set)) return;
            foreach (var sr in set) if (sr != null) sr.enabled = true;
            BellyCarryState.HiddenSprites.Remove(id);
            if (BellyCarryState.HiddenRenderers.TryGetValue(id, out var renderers))
            {
                foreach (var renderer in renderers) if (renderer != null) renderer.enabled = true;
                BellyCarryState.HiddenRenderers.Remove(id);
            }
            Diag.Log($"RestorePassenger {id}: re-enabled {set?.Length ?? 0} SpriteRenderers");
        }

        // Called from the authoritative network transition as well as Update.
        // This removes a frame-order race where the custom message arrives before
        // the persistent driver has had a chance to reconcile it.
        public static void ApplyCarryVisuals(ushort passenger, ushort carrier)
        {
            var pB = BellyCarryState.Resolve(passenger);
            var cB = BellyCarryState.Resolve(carrier);
            if (pB?.body == null || cB?.body == null) return;
            HidePassenger(passenger, pB);
            EnsureBelly(passenger, cB);
        }

        public static void ApplyCarryReleaseVisuals(ushort passenger)
        {
            RestorePassenger(passenger);
            RemoveBelly(passenger);
        }

        public static void ResetAllVisuals()
        {
            foreach (var id in BellyCarryState.HiddenSprites.Keys.ToList()) RestorePassenger(id);
            foreach (var id in BellyCarryState.BellyEntries.Keys.ToList()) RemoveBelly(id);
            BellyCarryState.Carries.Clear();
            BellyCarryState.ReleaseSuppressUntil.Clear();
            BellyCarryState.SavedDigestion.Clear();
            BellyCarryState.PiggybackMissingSince.Clear();
            Diag.Log("Reset all local BellyCarry state and visuals.");
        }

        private static void EnsureBelly(ushort passenger, NetBody carrier)
        {
            if (BellyCarryState.BellyEntries.ContainsKey(passenger)) return;
            var vc = carrier.body.GetComponent<BodyVoreController>();
            if (vc == null) return;
            var icon = new GameObject("trader1");
            icon.SetActive(false);
            // StomachFillSize accrues Size * 0.5 per entry, so map the configured
            // "belly size" ~1:1 onto the visible stomach stage.
            var v = new VoredGameObject
            {
                gameObject = icon,             // drives Consumed's circular stomach icon
                Size = Mathf.Clamp(Plugin.BellySize.Value * 2f, 0.2f, 12f),
                Health = 100f,
                Aggression = 0f,             // never struggles -> never ruptures the stomach
                Strength = 0f,
                StruggleTimer = 999f
            };
            vc.StomachContents.Add(v);
            BellyCarryState.BellyEntries[passenger] = v;
            BellyCarryState.BellyIcons[passenger] = icon;
        }

        private static void RemoveBelly(ushort passenger)
        {
            if (!BellyCarryState.BellyEntries.TryGetValue(passenger, out var v)) return;
            foreach (var vc in Object.FindObjectsOfType<BodyVoreController>())
                vc.StomachContents.Remove(v);
            if (BellyCarryState.BellyIcons.TryGetValue(passenger, out var icon))
            {
                if (icon != null) Object.Destroy(icon);
                BellyCarryState.BellyIcons.Remove(passenger);
            }
            BellyCarryState.BellyEntries.Remove(passenger);
        }
    }
}
