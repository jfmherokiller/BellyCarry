using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using UnityEngine;
// Used for CUCoreLib features. Remove if you don't use any of said features :)
using CUCoreLib.Helpers;
using CUCoreLib.Registries;
using CUCoreLib.Data;
using CUCoreLib.Saving;
using Newtonsoft.Json.Linq;

namespace CasualtiesUnknown.BellyCarry
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("insufferablemeower.mods.casualtiesconsumed", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.jfmherokiller.bellycarry";
        public const string ModName = "BellyCarry";
        public const string ModVersion = "0.5.0";

        internal static new ManualLogSource Logger;
        private readonly Harmony _harmony = new(ModGUID);
        public static Plugin Instance { get; private set; } = null!;

        // --- config (BepInEx/config/com.jfmherokiller.bellycarry.cfg) ---
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> ReplaceCarry;   // every KrokMP "carry" becomes a belly-carry
        public static ConfigEntry<bool> AllowConsciousCarry;
        public static ConfigEntry<float> BellySize;     // vore bulge per carried player (StomachFillSize units)
        public static ConfigEntry<int> MaxPassengers;   // how many you can hold at once
        public static ConfigEntry<bool> AllowPassengerForceOut;  // passenger can escape by mashing jump
        public static ConfigEntry<int> ForceOutTaps;    // jump presses within 4s to force out
        public static ConfigEntry<bool> Verbose;        // [BC] diagnostic logging to LogOutput.log

        public void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            Enabled       = Config.Bind("General", "Enabled", true,
                "Master switch for the belly-carry (no-digestion vore carry) feature.");
            ReplaceCarry  = Config.Bind("General", "ReplaceCarry", true,
                "When true, KrokMP's carry interaction (default K on a downed teammate) swallows them into your "
                + "belly instead of a bridal carry. Regurgitate (default H) lets them back out. "
                + "Piggyback (default P) is unchanged.");
            AllowConsciousCarry = Config.Bind("General", "AllowConsciousCarry", true,
                "Allow BellyCarry's Carry action on a standing teammate without requiring KrokMP's AlwaysAllowCarry rule.");
            BellySize     = Config.Bind("General", "BellySize", 3.0f,
                "Vore belly bulge added per carried player (0-6 stomach fill units).");
            MaxPassengers = Config.Bind("General", "MaxPassengers", 3,
                "Max teammates you can carry at once (also bounded by KrokMP's PiggybackMaxStack rule).");
            AllowPassengerForceOut = Config.Bind("General", "AllowPassengerForceOut", true,
                "Let a belly-carried player escape on their own by mashing jump, so nobody gets "
                + "stuck if the carrier can't regurgitate them.");
            ForceOutTaps = Config.Bind("General", "ForceOutTaps", 5,
                "Jump presses within 4 seconds needed to force your way out (min 2).");
            Verbose = Config.Bind("Debug", "Verbose", true,
                "Log every belly-carry decision to BepInEx/LogOutput.log (prefix [BC]). "
                + "Also registers the `bcdump` console command.");

            _harmony.PatchAll();
            Logger.LogInfo($"Plugin {ModName} v{ModVersion} loaded (KrokMP piggyback + Consumed belly, no digestion).");

            var go = new GameObject("BellyCarryDriver");
            DontDestroyOnLoad(go);
            go.AddComponent<BellyCarryDriver>();
        }
    }
}
