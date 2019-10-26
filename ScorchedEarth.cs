using System;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;

// ReSharper disable NotAccessedVariable
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        // must be at least this far away to not be filtered out
        public const float DecalDistance = 4.25f;
        internal const int chunkSize = 125;
        internal static readonly HarmonyInstance harmony = HarmonyInstance.Create("ca.gnivler.BattleTech.ScorchedEarth");
        internal static Settings modSettings;

        public static void Init(string settingsJson, string modDirectory)
        {
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settingsJson);
                modSettings.SaveDirectory = modDirectory + "\\Saves\\";
            }
            catch (Exception ex)
            {
                Log(ex);
                modSettings = new Settings();
            }

            Helpers.SetMaxDecals();
            Patches.Init();
        }

        internal static void Log(object input)
        {
            //FileLog.Log($"[ScorchedEarth] {input}");
        }
    }

    internal class Settings
    {
        public int MaxDecals;
        public bool SaveState = true;
        public string SaveDirectory;
    }

    internal class DecalInfo
    {
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
    }
}
