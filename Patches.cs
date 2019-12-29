using System;
using System.Collections;
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
using static ScorchedEarth.ScorchedEarth;

// ReSharper disable UnusedMember.Local
// ReSharper disable NotAccessedVariable
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
                var original = AccessTools.Constructor(terrainDecal,
                    new[] {typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float)});
                var transpiler = AccessTools.Method(typeof(Patches), nameof(TerrainDecal_Ctor_Transpiler));
                harmony.Patch(original, null, null, new HarmonyMethod(transpiler));
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static IEnumerable<CodeInstruction> TerrainDecal_Ctor_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            codes[9].opcode = OpCodes.Ldc_R4;
            codes[9].operand = float.MaxValue;

            return codes.AsEnumerable();
        }

        [HarmonyPatch(typeof(BTCustomRenderer), "OnPreCull")]
        public static class BTCustomRenderer_OnPreCull_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // replace DrawDecals method call with our own and add argument for CustomCommandBuffers (for speed)
                // note that this is using the publicized assembly https://github.com/CabbageCrow/AssemblyPublicizer
                var codes = instructions.ToList();
                var original = AccessTools.Method(typeof(BTCustomRenderer), nameof(BTCustomRenderer.DrawDecals));
                var replacement = AccessTools.Method(typeof(Patches), nameof(DrawDecals),
                    new[] {typeof(BTCustomRenderer), typeof(Camera), typeof(BTCustomRenderer.CustomCommandBuffers)});
                var index = codes.FindIndex(c => c.operand is MethodInfo methodInfo && methodInfo == original);
                codes.Insert(index, new CodeInstruction(OpCodes.Ldloc_1));
                codes[index + 1].operand = replacement;
                return codes.AsEnumerable();
            }
        }

        public static void DrawDecals(object instance, Camera camera, BTCustomRenderer.CustomCommandBuffers customCommandBuffers)
        {
            //var timer = new Stopwatch();
            //timer.Restart();
            var deferredDecalsBuffer = customCommandBuffers.deferredDecalsBuffer;
            var skipDecals = ((BTCustomRenderer) instance).skipDecals;
            if (!skipDecals)
            {
                BTDecal.DecalController.ProcessCommandBuffer(deferredDecalsBuffer, camera);
            }

            int numFootsteps;
            var footsteps = Helpers.ProcessFootsteps(out numFootsteps);
            foreach (var chunk in footsteps.Where(x => x != null))
            {
                deferredDecalsBuffer.DrawMeshInstanced(
                    BTDecal.DecalMesh.DecalMeshFull,
                    0,
                    FootstepManager.Instance.footstepMaterial,
                    0,
                    chunk,
                    chunkSize);
            }

            int numScorches;
            var scorches = Helpers.ProcessScorches(out numScorches);
            foreach (var chunk in scorches.Where(x => x != null))
            {
                deferredDecalsBuffer.DrawMeshInstanced(
                    BTDecal.DecalMesh.DecalMeshFull,
                    0,
                    FootstepManager.Instance.scorchMaterial,
                    0,
                    chunk,
                    chunkSize);
            }

            //var time = timer.ElapsedTicks;
            //if (time > 200000)
            //{
            //    Log("SLOW: " + timer.ElapsedTicks);
            //    Log(new string('*', 100));
            //}
        }
    }


    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddFootstep))]
    public static class FootstepManager_AddFootstep_Patch
    {
        internal static int previous;

        public static bool Prefix(Vector3 position, List<FootstepManager.TerrainDecal> ____footstepList)
        {
            try
            {
                if (____footstepList.Count > previous)
                {
                    Log($"Footstep count: {____footstepList.Count}/{____footstepList.Capacity}");
                    previous = ____footstepList.Count;
                }

                if (____footstepList.All(terrainDecal => Helpers.Distance(terrainDecal.transformMatrix, position) > FootstepDistance))
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
        public static bool Prefix(Vector3 position, List<FootstepManager.TerrainDecal> ____scorchList)
        {
            try
            {
                Log($"Scorch count: {____scorchList.Count}/{____scorchList.Capacity}");
                // ReSharper disable once PossibleNullReferenceException
                if (____scorchList.All(terrainDecal =>
                    Helpers.Distance(terrainDecal.transformMatrix, position) > DecalDistance))
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

                Log("GameInstance_SaveComplete_Patch");
                Helpers.CleanupSaves();

                if (!Directory.Exists(modSettings.SaveDirectory))
                {
                    Directory.CreateDirectory(modSettings.SaveDirectory);
                }

                var filename = modSettings.SaveDirectory + "/" + message.Slot.FileID.Substring(4) + ".gzip";
                var results = new List<IList>
                {
                    Helpers.ExtractDecals("scorchList"),
                    Helpers.ExtractDecals("footstepList")
                };

                Helpers.SaveDecals(results, filename);
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

        // BUG if you load a game with data then complete the mission and take another one, it uses the last saved fileID instead of zeroing
        // save the fileID here because it's not easily available at Briefing.InitializeContractComplete
        [HarmonyPatch(typeof(GameInstance), "Load")]
        public static class GameInstance_Load_Patch
        {
            public static void Postfix(GameInstanceSave save)
            {
                if (!modSettings.SaveState)
                {
                    return;
                }

                fileID = save.FileID;
            }
        }

        [HarmonyPatch(typeof(CombatGameState), nameof(CombatGameState._Init))]
        public static class CombatGameState__Init_Patch
        {
            private static bool completed;

            public static void Postfix(CombatGameState __instance)
            {
                if (!__instance.WasFromSave && !completed)
                {
                    Log("Clearing fileID");
                    fileID = "";
                    completed = true;
                }
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

                    if (string.IsNullOrEmpty(fileID))
                    {
                        Log("New instance, not loading");
                        FootstepManager_AddFootstep_Patch.previous = 0;
                        return;
                    }

                    var filename = modSettings.SaveDirectory + "/" + fileID.Substring(4) + ".gzip";
                    if (!Directory.Exists(modSettings.SaveDirectory) ||
                        !File.Exists(filename))
                    {
                        Log("SaveState disabled, or missing data");
                        return;
                    }

                    var results = new List<IList>();
                    Log("Hydrate scorches and footsteps");
                    // first element is always scorches, 2nd footsteps
                    results.Add(Helpers.RecreateDecals(filename, "scorchList", 0));
                    results.Add(Helpers.RecreateDecals(filename, "footstepList", 1));
                    var scorchList = Traverse.Create(FootstepManager.Instance).Property("scorchList").GetValue<IList>();
                    var footstepList = Traverse.Create(FootstepManager.Instance).Property("footstepList").GetValue<IList>();

                    foreach (var decal in results[0])
                    {
                        scorchList.Add(decal);
                    }

                    foreach (var decal in results[1])
                    {
                        footstepList.Add(decal);
                    }

                    // just for silencing the 'added footstep' logging
                    Log("Resetting previous footstep count");
                    FootstepManager_AddFootstep_Patch.previous = footstepList.Count;
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }
    }
}
