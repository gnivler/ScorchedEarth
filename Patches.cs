using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using BattleTech.Rendering;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure.Messages;
using BattleTech.UI;
using Harmony;
using UnityEngine;
using UnityEngine.Rendering;
using static ScorchedEarth.ScorchedEarth;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming

namespace ScorchedEarth
{
    internal class Patches
    {
        internal static void Init()
        {
            try
            {
                var terrainDecal = AccessTools.Inner(typeof(FootstepManager), "TerrainDecal");
                var original = AccessTools.Constructor(terrainDecal, new[] {typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float)});
                var transpiler = AccessTools.Method(typeof(Patches), nameof(TerrainDecal_Ctor_Transpiler));
                harmony.Patch(original, null, null, new HarmonyMethod(transpiler));
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static IEnumerable<CodeInstruction> TerrainDecal_Ctor_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            codes[9].opcode = OpCodes.Ldc_R4;
            codes[9].operand = float.MaxValue;

            return codes.AsEnumerable();
        }

        [HarmonyPatch(typeof(BTCustomRenderer), "DrawDecals")]
        public static class BTCustomRenderer_DrawDecals_Patch
        {
            internal static bool Prefix(BTCustomRenderer __instance, Camera camera)
            {
                try
                {
                    // code copied from original method
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
                    deferredDecalsBuffer.SetGlobalFloat(nameID, !isUrban
                        ? 1f
                        : 2f);

                    // magic here to exceed 125 decal cap
                    // chop it up into blocks of 125
                    // thanks m22spencer for this silver bullet idea!
                    // thanks https://stackoverflow.com/a/3517542/6296808 for the splitting code
                    // send each array element (which is an array) to Unity
                    var results1 = matrices1
                        .Select((x, i) => new {Key = i / chunkSize, Value = x})
                        .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                        .ToArray();

                    foreach (var array in results1)
                    {
                        deferredDecalsBuffer.DrawMeshInstanced(
                            BTDecal.DecalMesh.DecalMeshFull,
                            0,
                            FootstepManager.Instance.footstepMaterial,
                            0,
                            array,
                            chunkSize,
                            null);
                    }

                    int numScorches;
                    var matrices2 = FootstepManager.Instance.ProcessScorches(out numScorches);
                    var results2 = matrices2
                        .Select((x, i) => new {Key = i / chunkSize, Value = x})
                        .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                        .ToArray();

                    foreach (var array in results2)
                    {
                        deferredDecalsBuffer.DrawMeshInstanced(
                            BTDecal.DecalMesh.DecalMeshFull,
                            0,
                            FootstepManager.Instance.scorchMaterial,
                            0,
                            array,
                            chunkSize,
                            null);
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
        public static class FootstepManager_AddFootstep_Patch
        {
            public static bool Prefix(Vector3 position, List<object> ____footstepList)
            {
                try
                {
                    Log($"Footstep count: {____footstepList.Count}/{____footstepList.Capacity}");
                    // ReSharper disable once PossibleNullReferenceException
                    if (____footstepList.All(terrainDecal =>
                        Helpers.Distance((Matrix4x4) terrainDecal.GetType().GetRuntimeFields().FirstOrDefault(
                                fieldInfo => fieldInfo.Name.Contains("transformMatrix"))
                            .GetValue(terrainDecal), position) > 1f))
                    {
                        // FIFO logic, only act when needed
                        if (____footstepList.Count == FootstepManager.maxDecals)
                        {
                            Log("maxDecals exceeded");

                            ____footstepList.RemoveAt(0);
                            Log("oldest footstep removed");
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                Log("======================================================== dropped footstep");
                return false;
            }
        }

        // draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
        [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
        public class FootstepManager_AddScorch_Patch
        {
            public static bool Prefix(Vector3 position, List<object> ____scorchList)
            {
                try
                {
                    Log($"Scorch count: {____scorchList.Count}/{____scorchList.Capacity}");
                    // ReSharper disable once PossibleNullReferenceException
                    if (____scorchList.All(terrainDecal =>
                        Helpers.Distance((Matrix4x4) terrainDecal.GetType().GetRuntimeFields().FirstOrDefault(
                                fieldInfo => fieldInfo.Name.Contains("transformMatrix"))
                            .GetValue(terrainDecal), position) > DecalDistance))
                    {
                        // FIFO logic, only act when needed
                        if (____scorchList.Count == FootstepManager.maxDecals)
                        {
                            Log("maxDecals exceeded");
                            ____scorchList.RemoveAt(0);
                            Log("oldest scorch removed");
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }

                Log("======================================================== dropped scorch");
                return false;
            }
        }

        [HarmonyPatch(typeof(GameInstance), "SaveComplete")]
        public static class GameInstance_SaveComplete_Patch
        {
            public static void Postfix(StructureSaveComplete message)
            {
                try
                {
                    if (!modSettings.SaveState)
                    {
                        return;
                    }

                    Log("Dehydrate");
                    Helpers.CleanupSaves();

                    if (!Directory.Exists(modSettings.SaveDirectory))
                    {
                        Directory.CreateDirectory(modSettings.SaveDirectory);
                    }

                    var filename = modSettings.SaveDirectory + "\\" + message.Slot.FileID.Substring(4) + ".scorches.json";
                    Helpers.BuildDecalList(filename, "scorchList");
                    filename = modSettings.SaveDirectory + "\\" + message.Slot.FileID.Substring(4) + ".footsteps.json";
                    Helpers.BuildDecalList(filename, "footstepList");
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        public class Hydrate
        {
            private static string fileID;

            // save the fileID here because it's not easily available at Briefing.InitializeContractComplete
            [HarmonyPatch(typeof(GameInstance), "CreateCombatFromSave")]
            public static class GameInstance_CreateCombatFromSave_Patch
            {
                static void Postfix(GameInstanceSave save)
                {
                    if (!modSettings.SaveState)
                    {
                        return;
                    }

                    fileID = save.FileID;
                }
            }

            [HarmonyPatch(typeof(Briefing), "InitializeContractComplete")]
            public static class Briefing_InitializeContractComplete_Patch
            {
                public static void Postfix()
                {
                    try
                    {
                        if (!modSettings.SaveState)
                        {
                            return;
                        }

                        var scorchFile = modSettings.SaveDirectory + "\\" + fileID.Substring(4) + ".scorches.json";
                        var footstepFile = modSettings.SaveDirectory + "\\" + fileID.Substring(4) + ".footsteps.json";
                        if (!Directory.Exists(modSettings.SaveDirectory) ||
                            !File.Exists(scorchFile) ||
                            !File.Exists(footstepFile))
                        {
                            Log("SaveState disabled, or missing data");
                            return;
                        }

                        Log("Hydrate scorches and footsteps");
                        Helpers.CreateDecals(scorchFile, "scorchList");
                        Helpers.CreateDecals(footstepFile, "footstepList");
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                    }
                }
            }
        }
    }
}
