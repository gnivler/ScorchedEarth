using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech.Rendering;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using static ScorchedEarth.ScorchedEarth;

// ReSharper disable NotAccessedVariable
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        // must be at least this far away to not be filtered out
        public const float DecalDistance = 4.25f;

        internal static readonly HarmonyInstance harmony = HarmonyInstance.Create("ca.gnivler.BattleTech.ScorchedEarth");

        private static Settings modSettings;

        public static void Init(string settingsJson)
        {
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settingsJson);
            }
            catch (Exception ex)
            {
                Log(ex);
                modSettings = new Settings();
            }

            SetMaxDecals();
            ManualPatches.Init();
        }

        private static void SetMaxDecals()
        {
            // maxDecals changes EVERYTHING (all* arrays sizes)
            // make an acceptable value
            var decals = modSettings.MaxDecals;
            if (decals < 125 || decals > 1000 || decals % 125 != 0)
            {
                modSettings.MaxDecals = 125;
                Log("Invalid value in mod.json, using default instead.  MaxDecals must be a multiple of 125 and no greater than 1,000.");
            }

            Log($"maxDecals is {modSettings.MaxDecals}");
            AccessTools.Field(typeof(FootstepManager), "maxDecals").SetValue(null, modSettings.MaxDecals);
        }

        internal static void Log(object input)
        {
            //FileLog.Log($"[ScorchedEarth] {input}");
        }

        private class Settings
        {
            public int MaxDecals;
        }
    }

    public class ManualPatches
    {
        internal static void Init()
        {
            try
            {
                var terrainDecal = AccessTools.Inner(typeof(FootstepManager), "TerrainDecal");
                var original = AccessTools.Constructor(terrainDecal, new[] {typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float)});
                var transpiler = AccessTools.Method(typeof(ManualPatches), nameof(TerrainDecalCtorTranspiler));
                harmony.Patch(original, null, null, new HarmonyMethod(transpiler));

                var drawDecals = AccessTools.Method(typeof(BTCustomRenderer), "DrawDecals");
                var prefix = AccessTools.Method(typeof(ManualPatches), nameof(DrawDecalsPrefix));
                harmony.Patch(drawDecals, new HarmonyMethod(prefix));
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static IEnumerable<CodeInstruction> TerrainDecalCtorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            codes[9].opcode = OpCodes.Ldc_R4;
            codes[9].operand = float.MaxValue;

            return codes.AsEnumerable();
        }

        // chop it up into blocks of 125
        // thanks m22spencer for this silver bullet idea!
        private const int chunkSize = 125;

        private static bool DrawDecalsPrefix(BTCustomRenderer __instance, Camera camera)
        {
            try
            {
                var instance = Traverse.Create(__instance);
                var customCommandBuffers = instance.Method("UseCamera", camera).GetValue<object>();

                if (customCommandBuffers == null)
                {
                    return false;
                }

                var terrainGenerator = instance.Property("terrainGenerator").GetValue<TerrainGenerator>();
                var isUrban = terrainGenerator != null && terrainGenerator.biome.biomeSkin == Biome.BIOMESKIN.urbanHighTech;
                var deferredDecalsBuffer = Traverse.Create(customCommandBuffers).Field("deferredDecalsBuffer").GetValue<CommandBuffer>();
                var skipDecals = instance.Field("skipDecals").GetValue<bool>();

                if (!skipDecals)
                {
                    BTDecal.DecalController.ProcessCommandBuffer(deferredDecalsBuffer, camera);
                }

                if (!Application.isPlaying || BTCustomRenderer.EffectsQuality <= 0)
                {
                    return false;
                }

                int numFootsteps;
                var matrices1 = FootstepManager.Instance.ProcessFootsteps(out numFootsteps, isUrban);
                var uniforms = AccessTools.Inner(typeof(BTCustomRenderer), "Uniforms");
                var nameID = (int) AccessTools.Field(uniforms, "_FootstepScale").GetValue(null);
                deferredDecalsBuffer.SetGlobalFloat(nameID, !isUrban ? 1f : 2f);

                // thanks https://stackoverflow.com/a/3517542/6296808 for the splitting code
                // send each array element (which is an array) to Unity
                var results1 = matrices1
                    .Select((x, i) => new {Key = i / chunkSize, Value = x})
                    .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                    .ToArray();

                for (var i = 0; i < results1.Length; i++)
                    deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull,
                        0, FootstepManager.Instance.footstepMaterial, 0, results1[i], chunkSize, null);

                int numScorches;
                var matrices2 = FootstepManager.Instance.ProcessScorches(out numScorches);
                var results2 = matrices2
                    .Select((x, i) => new {Key = i / chunkSize, Value = x})
                    .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                    .ToArray();

                for (int i = 0; i < results2.Length; i++)
                {
                    deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull,
                        0, FootstepManager.Instance.scorchMaterial, 0, results2[i], chunkSize, null);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddFootstep))]
    public static class FootstepManagerAddFootstep
    {
        public static void Prefix(List<object> ____footstepList)
        {
            Log($"Footstep count: {____footstepList.Count}/{____footstepList.Capacity}");

            // FIFO logic, only act when needed
            if (____footstepList.Count == FootstepManager.maxDecals)
            {
                Log("maxDecals exceeded");
                try
                {
                    ____footstepList.RemoveAt(0);
                    Log("oldest footstep removed");
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }
    }

    // draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
    public class FootstepManagerAddScorch
    {
        // thanks jo!
        private static float Distance(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m03 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m13 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m23 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        public static bool Prefix(Vector3 position, List<object> ____scorchList)
        {
            try
            {
                Log($"Scorch count: {____scorchList.Count}/{____scorchList.Capacity}");
                if (____scorchList.All(
                    terrainDecal => Distance((Matrix4x4) terrainDecal.GetType().GetRuntimeFields().FirstOrDefault(
                             fieldInfo => fieldInfo.Name.Contains("transformMatrix")).GetValue(terrainDecal), position) > DecalDistance))
                {
                    if (____scorchList.Count == FootstepManager.maxDecals)
                    {
                        Log("maxDecals exceeded");
                        try
                        {
                            ____scorchList.RemoveAt(0);
                            Log("oldest scorch removed");
                        }
                        catch (Exception ex)
                        {
                            Log(ex);
                        }
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            Log("======================================================== dropped decal");
            return false;
        }
    }
}
