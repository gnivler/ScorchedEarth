using System;
using System.Linq;
using System.Reflection;
using BattleTech.Rendering;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
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

        private static Settings modSettings;

        public static void Init(string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.BattleTech.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settingsJson);
            }
            catch (Exception ex)
            {
                Log(ex);
                modSettings = new Settings();
            }

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

    // this lets footsteps be infinite easily
    // TerrainDecal will have a time property that makes all comparisons practically infinite with MaxValue
    [HarmonyPatch(typeof(FootstepManager.TerrainDecal), MethodType.Constructor)]
    [HarmonyPatch(new[] {typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float)})]
    public static class PatchTerrainDecalCtor
    {
        public static bool Prefix(FootstepManager.TerrainDecal __instance, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            __instance.transformMatrix = Matrix4x4.TRS(position, rotation, scale);
            __instance.startTime = float.MaxValue;
            return false;
        }
    }

    [HarmonyPatch(typeof(BTCustomRenderer), nameof(BTCustomRenderer.DrawDecals))]
    public static class PatchBTCustomRendererDrawDecals
    {
        // chop it up into blocks of 125
        // thanks m22spencer for this silver bullet idea!
        private const int chunkSize = 125;

        public static bool Prefix(BTCustomRenderer __instance, Camera camera)
        {
            var customCommandBuffers = __instance.UseCamera(camera);
            if (customCommandBuffers == null)
                return false;
            var isUrban = __instance.terrainGenerator != null && __instance.terrainGenerator.biome.biomeSkin == Biome.BIOMESKIN.urbanHighTech;
            var deferredDecalsBuffer = customCommandBuffers.deferredDecalsBuffer;
            if (!__instance.skipDecals)
                BTDecal.DecalController.ProcessCommandBuffer(deferredDecalsBuffer, camera);
            if (!Application.isPlaying || BTCustomRenderer.effectsQuality <= 0)
                return false;

            int numFootsteps;
            var matrices1 = FootstepManager.Instance.ProcessFootsteps(out numFootsteps, isUrban);
            deferredDecalsBuffer.SetGlobalFloat(BTCustomRenderer.Uniforms._FootstepScale, !isUrban ? 1f : 2f);

            // thanks https://stackoverflow.com/a/3517542/6296808 for the splitting code
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

            // send each array element (which is an array) to Unity
            for (int i = 0; i < results2.Length; i++)
            {
                deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull,
                    0, FootstepManager.Instance.scorchMaterial, 0, results2[i], chunkSize, null);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddFootstep))]
    public static class PatchFootstepManagerAddFootstep
    {
        public static void Prefix(FootstepManager __instance)
        {
            // FIFO logic, only act when needed
            if (__instance.footstepList.Count != FootstepManager.maxDecals)
            {
                return;
            }

            try
            {
                __instance.footstepList.RemoveAt(0);
                Log("oldest footstep removed");
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        // running status line
        public static void Postfix(FootstepManager __instance)
        {
            Log($"Footstep count: {__instance.footstepList.Count}/{__instance.footstepList.Capacity}");
        }
    }

    // draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
    public static class PatchFootstepManagerAddScorch
    {
        // thanks jo!
        private static float Distance(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m03 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m13 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m23 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        public static bool Prefix(FootstepManager __instance, Vector3 position, Vector3 forward, Vector3 scale)
        {
            // only allow decals which are sufficiently distant to be added (mitigating stacking decals which are not discernible)
            if (__instance.scorchList.Count == 0 ||
                __instance.scorchList.All(x => Distance(x.transformMatrix, position) > DecalDistance))
            {
                var rotation = Quaternion.LookRotation(forward);
                rotation = Quaternion.Euler(0.0f, rotation.eulerAngles.y, 0.0f);
                __instance.scorchList.Add(new FootstepManager.TerrainDecal(position, rotation, scale, -1f));
            }

            Log($"Scorch count: {__instance.scorchList.Count}/{__instance.scorchList.Capacity}");
            if (__instance.scorchList.Count != FootstepManager.maxDecals)
            {
                return false;
            }

            try
            {
                __instance.scorchList.RemoveAt(0);
                Log("oldest scorch removed");
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            return false;
        }
    }
}
