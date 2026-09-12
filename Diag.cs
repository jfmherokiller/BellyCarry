using System.Linq;
using System.Text;
using CasualtiesConsumed.Components;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using UnityEngine;

namespace CasualtiesUnknown.BellyCarry
{
    // Diagnostics: everything goes through Plugin.Logger -> BepInEx/LogOutput.log
    // (console output is NOT captured there, but Logger.LogInfo is). Gated on
    // [Debug] Verbose. Adds a cheat-free `bcdump` console command that dumps the
    // full BellyCarry + per-body vore state in one line block.
    internal static class Diag
    {
        public static void Log(string msg)
        {
            if (Plugin.Verbose == null || Plugin.Verbose.Value)
                Plugin.Logger.LogInfo("[BC] " + msg);
        }

        public static string Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== bcdump ===");
            sb.AppendLine($"Enabled={Plugin.Enabled.Value} ReplaceCarry={Plugin.ReplaceCarry.Value} "
                + $"is_server={Net.is_server} is_client_or_host={Net.is_client_or_host} "
                + $"BellySize={Plugin.BellySize.Value} MaxPassengers={Plugin.MaxPassengers.Value}");

            var me = BellyCarryState.LocalNetBody();
            sb.AppendLine(me == null
                ? "localNetBody = NULL"
                : $"localNetBody netId={(ushort)me.netId} '{me.playername}' "
                  + $"piggybacking_on={Id(me.piggybacking_on)} carrying_person={Id(me.carrying_person)}");

            sb.AppendLine($"Carries({BellyCarryState.Carries.Count}): "
                + string.Join(", ", BellyCarryState.Carries.Select(k => $"{k.Key}=>{k.Value}")));
            sb.AppendLine($"HiddenSprites: "
                + string.Join(", ", BellyCarryState.HiddenSprites.Select(k => $"{k.Key}:{k.Value?.Length ?? 0}")));
            sb.AppendLine($"HiddenRenderers: "
                + string.Join(", ", BellyCarryState.HiddenRenderers.Select(k => $"{k.Key}:{k.Value?.Length ?? 0}")));
            sb.AppendLine($"BellyEntries: {BellyCarryState.BellyEntries.Count}   "
                + $"SavedDigestion: " + string.Join(", ", BellyCarryState.SavedDigestion.Select(k => $"{k.Key}:{k.Value:0.##}")));

            foreach (var nb in NetBody.NetIdToNetBody.Values)
            {
                if (nb == null || nb.body == null) continue;
                var vc = nb.body.GetComponent<BodyVoreController>();
                string vcs = vc == null
                    ? "vc=none"
                    : $"digRate={vc.DigestionRate:0.##} stomach={vc.StomachContents.Count} "
                      + $"fill={vc.StomachFillSize:0.00} stage={vc.StomachSizeStage} added={vc.AddedWeight:0.0}";
                sb.AppendLine($"  NB {(ushort)nb.netId} '{nb.playername}' local={nb.is_local} "
                    + $"pig_on={Id(nb.piggybacking_on)} carry={Id(nb.carrying_person)} "
                    + $"alive={nb.body.alive} weightOffset={nb.body.weightOffset:0.0} {vcs}");
            }
            return sb.ToString();
        }

        private static string Id(NetBody nb) => nb == null ? "-" : ((ushort)nb.netId).ToString();

        [HarmonyPatch(typeof(ConsoleScript), "RegisterAllCommands")]
        internal static class RegisterBcdump
        {
            private static void Postfix()
            {
                if (ConsoleScript.Commands.Any(c => c.name == "bcdump")) return;
                var cmd = new Command("bcdump", "BellyCarry: dump internal + vore state to LogOutput.log",
                    delegate { Plugin.Logger.LogInfo("[BC] " + Dump()); }, null);
                cmd.custom = true;
                ConsoleScript.Commands.Add(cmd);
                var reset = new Command("bcrecover", "BellyCarry: clear stale local state and restore visuals",
                    delegate { BellyCarryDriver.ResetAllVisuals(); }, null);
                reset.custom = true;
                ConsoleScript.Commands.Add(reset);
                Plugin.Logger.LogInfo("[BC] bcdump command registered.");
            }
        }
    }
}
