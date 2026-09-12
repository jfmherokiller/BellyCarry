using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // Adds a dedicated action below KrokMP's interaction grid.  Keeping this
    // as a separate button lets native Carry remain available for ordinary
    // incapacitated-player transport.
    [HarmonyPatch(typeof(UIInGame), "_GUI_PlayerInteractionGui")]
    internal static class MenuPatch
    {
        private static void Postfix()
        {
            if (!Plugin.Enabled.Value || !Plugin.ReplaceCarry.Value || UIInGame.interaction_menu_target_body == null)
                return;
            if (NetPlayer.LOCAL_PLAYER == null || NetPlayer.LOCAL_PLAYER.playerbody == null)
                return;

            float scale = UIBullshit.uiScale;
            float w = 100f * scale;
            float h = 45f * scale;
            Vector2 p = UIInGame.interaction_menu_target_pos;
            // Third row, first column. The native menu is 2.5 rows tall;
            // this position stays directly beneath it without covering any
            // existing Carry/Piggyback/Push button.
            Rect r = new Rect(p.x - 1.5f * w, p.y + 1f * h, w, h);
            bool enabled = UIInGame.interaction_menu_target_body != NetPlayer.LOCAL_PLAYER.playerbody
                && UIInGame.interaction_menu_target_body.CanBeCarriedBySomeone()
                && UIInGame.interaction_menu_target_body.carrying_person == null
                && UIInGame.interaction_menu_target_body.piggybacking_on == null;
            if (!enabled) GUI.enabled = false;
            if (GUI.Button(r, "Vore") && enabled)
            {
                StartVore(UIInGame.interaction_menu_target_body);
                UIInGame.StopPlayerInteractionMenu();
            }
            GUI.enabled = true;
        }

        private static void StartVore(NetBody passenger)
        {
            NetBody carrier = NetPlayer.LOCAL_PLAYER.playerbody;
            if (passenger == null || carrier == null || passenger == carrier) return;
            try
            {
                // H/release installs a short stale-packet guard. A deliberate
                // click on Vore is fresh intent and must be allowed through it.
                BellyCarryState.ReleaseSuppressUntil.Remove((ushort)passenger.netId);
                // A client cannot authoritatively attach a server-owned body
                // by calling StartPiggyback directly. Use KrokMP's existing
                // request helper for that direction; the StartPiggyback hook
                // will publish the BellyCarry state after confirmation.
                if (!Net.is_server && !passenger.is_local)
                {
                    ClientMain._PLRINT_Carry(passenger);
                    return;
                }
                if (passenger.StartPiggyback(carrier, check_distance: true, force: true))
                    BellyNet.SendGrab((ushort)carrier.netId, (ushort)passenger.netId, active: true);
                else
                    PlayerCamera.main?.DoAlert("Vore failed: target could not be attached.", false);
            }
            catch (System.Exception e)
            {
                Plugin.Logger.LogWarning($"Vore menu action failed: {e.Message}");
            }
        }
    }
}
