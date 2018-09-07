using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech;
using BattleTech.Rendering;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using static ScorchedEarth.Logger;
using static ScorchedEarth.ScorchedEarth;

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        // must be at least this far away to not be filtered out
        public const float DecalDistance = 4.25f;
        public const float MissSpread = 5f;

        public static string modDirectory;
        public static Settings modSettings;

        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            modDirectory = directory;
            FileLog.logPath = Path.Combine(directory, "log.txt");
            try
            {
                modSettings = JsonConvert.DeserializeObject<Settings>(settingsJson);
            }
            catch (Exception e)
            {
                LogError(e);
                modSettings = new Settings();
            }

            if (modSettings.enableDebug) LogClear();

            // maxDecals changes EVERYTHING (all* arrays sizes)
            // make an acceptable value
            int decals = modSettings.MaxDecals;
            if (decals < 125 || decals > 1000 || decals % 125 != 0)
            {
                modSettings.MaxDecals = 125;
                LogDebug("Invalid value in mod.json, using default instead.  MaxDecals must be a multiple of 125 and no greater than 1,000.");
            }

            LogDebug($"maxDecals is {modSettings.MaxDecals}");
            AccessTools.Field(typeof(FootstepManager), "maxDecals").SetValue(null, modSettings.MaxDecals);
        }

        public class Settings
        {
            public bool enableDebug;
            public int MaxDecals;
        }

        // thanks jo!
        public static float DistanceForward(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m00 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m11 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m22 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        public static float Distance(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m03 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m13 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m23 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        // only useful to dump method IL
        public static void ListTheStack(List<CodeInstruction> codes)
        {
            var sb = new StringBuilder();
            sb.Append(new string(c: '=', count: 80) + "\n");
            for (var i = 0; i < codes.Count(); i++)
            {
                sb.Append($"{codes[i].opcode}\t\t");
                if (codes[i].operand != null)
                {
                    sb.Append($"{codes[i].operand}");
                }

                sb.Append($"\n");
            }

            sb.Append(new string(c: '=', count: 80) + "\n");
            FileLog.Log(sb.ToString());
        }
    }

    // makes missile impacts more spread out
    [HarmonyPatch(typeof(MechRepresentation), nameof(MechRepresentation.GetMissPosition), MethodType.Normal)]
    public static class MechRepresentation_GetMissPosition_Patch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // CRASH
            //if (UnityGameInstance.BattleTechGame != null)
            //{
            //    LogDebug("not null");
            //}
            //
            
            var codes = new List<CodeInstruction>(instructions);
            var index = codes.FindIndex(c => c.operand != null && c.operand.ToString().Contains("get_normalized"));

            codes.Insert(index + 4, new CodeInstruction(OpCodes.Mul));
            codes.Insert(index + 4, new CodeInstruction(OpCodes.Ldc_R4, MissSpread));

            return codes.AsEnumerable();
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

    [HarmonyPatch(typeof(BTCustomRenderer), nameof(BTCustomRenderer.DrawDecals), MethodType.Normal)]
    public static class PatchBTCustomRendererDrawDecals
    {
        // chop it up into blocks of 125
        // thanks m22spencer for this silver bullet idea!
        private static int size = 125;
        private static int numFootsteps;
        private static int numScorches;
        static CommandBuffer deferredDecalsBuffer = new CommandBuffer();

        public static bool Prefix(BTCustomRenderer __instance, Camera camera)
        {
            BTCustomRenderer.CustomCommandBuffers customCommandBuffers = __instance.UseCamera(camera);

            if (customCommandBuffers == null)
            {
                return false;
            }

            if (!Application.isPlaying || BTCustomRenderer.effectsQuality <= 0)
            {
                return false;
            }

            deferredDecalsBuffer = customCommandBuffers.deferredDecalsBuffer;
            BTDecal.DecalController.ProcessCommandBuffer(deferredDecalsBuffer, camera);

            Matrix4x4[] matrices1 = FootstepManager.Instance.ProcessFootsteps(out numFootsteps);
            Matrix4x4[] matrices2 = FootstepManager.Instance.ProcessScorches(out numScorches);

            // thanks https://stackoverflow.com/a/3517542/6296808 for the splitting code
            var results1 = matrices1.Select((x, i) => new
                {
                    Key = i / size,
                    Value = x
                })
                .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                .ToArray();

            for (int i = 0; i < results1.Length; i++)
            {
                deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull,
                    0, FootstepManager.Instance.footstepMaterial, 0, results1[i], size, null);
            }

            var results2 = matrices2.Select((x, i) => new
                {
                    Key = i / size,
                    Value = x
                })
                .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                .ToArray();

            // send each array element (which is an array) to Unity
            for (int i = 0; i < results2.Length; i++)
            {
                deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull,
                    0, FootstepManager.Instance.scorchMaterial, 0, results2[i], size, null);
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
            if (__instance.footstepList.Count != FootstepManager.maxDecals) return;
            try
            {
                __instance.footstepList.RemoveAt(0);
                LogDebug("footstep removed");
            }
            catch
            {
                LogDebug("footstep remove failed");
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            for (var i = 0; i < 7; i++)
            {
                codes[i].opcode = OpCodes.Nop;
            }

            return codes.AsEnumerable();
        }

        // running status line
        public static void Postfix(FootstepManager __instance)
        {
            LogDebug($"Footstep count: {__instance.footstepList.Count}/{__instance.footstepList.Capacity}");
        }
    }

    // draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
    public static class PatchFootstepManagerAddScorch
    {
        public static bool Prefix(FootstepManager __instance, Vector3 position, Vector3 forward, Vector3 scale, bool persistent)
        {
            // only allow decals which are sufficiently distant to be added
            if (__instance.scorchList.Count == 0 ||
                __instance.scorchList.All(x => Distance(x.transformMatrix, position) > DecalDistance))
            {
                Quaternion rotation = Quaternion.LookRotation(forward);
                rotation = Quaternion.Euler(0.0f, rotation.eulerAngles.y, 0.0f);
                __instance.scorchList.Add(new FootstepManager.TerrainDecal(position, rotation, scale, -1f));
            }

            LogDebug($"Scorch count: {__instance.scorchList.Count}/{__instance.scorchList.Capacity}");
            if (__instance.scorchList.Count != FootstepManager.maxDecals) return false;

            try
            {
                __instance.scorchList.RemoveAt(0);
                LogDebug("scorch removed");
            }
            catch
            {
                LogDebug("scorch remove failed");
            }

            return false;
        }
    }
}
