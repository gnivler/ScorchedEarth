using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech;
using BattleTech.Rendering;
using Harmony;
using UnityEngine;
using static ScorchedEarth.Logger;

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        public const int DECALS = 125;
        public static string ModDirectory;
        public static bool EnableDebug = false;

        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModDirectory = directory;
            if (EnableDebug)
            {
                Clear();
            }

            AccessTools.Field(typeof(FootstepManager), nameof(FootstepManager.maxDecals)).SetValue(null, DECALS);
        }

        // only useful to dump method IL
        private static void ListTheStack(List<CodeInstruction> codes)
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

        
        // every TerrainDecal will have a time property that makes all comparisons practically infinite
        [HarmonyPatch(typeof(FootstepManager.TerrainDecal))]
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
        
        // patch the property which is supposed to return 125 or 500 for OpenGL.  Testing with -force-opengl resulted in at least 1023 decals, though
        // fires constantly
        [HarmonyPatch(typeof(BTDecal.DecalController))]
        [HarmonyPatch("MaxInstances", PropertyMethod.Getter)]
        public static class PatchMaxInstances
        {
            public static bool Prefix(ref int __result)
            {
                //Debug($"MaxInstances returning {DECALS}");
                __result = DECALS;
                return false;
            }
        }

        // trying to verify that the value going into the game is correct when it should be (apparently yes but it doesn't work)
        [HarmonyPatch(typeof(BTDecal.DecalController), nameof(BTDecal.DecalController.ProcessCommandBuffer))]
        public static class PatchProcessCommandBuffer
        {
            private static bool said;

            public static void Prefix()
            {
                if (!said)
                {
                    Debug($"ProcessCommandBuffer says max is {BTDecal.DecalController.MaxInstances}");
                    said = true;
                }
            }
        }

        [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddFootstep))]
        public static class PatchAddFootstep
        {
            public static void Prefix(FootstepManager __instance)
            {
                if (__instance.footstepList.Count == FootstepManager.maxDecals)
                {
                    __instance.footstepList.RemoveAt(0);
                    Debug("footstepList element 0 removed");
                }
                
                
            }
            
            public static void Postfix(FootstepManager __instance)
            {
                Debug($"footstepList is {__instance.footstepList.Count}/{__instance.footstepList.Capacity} (max: {FootstepManager.maxDecals})");
            }

        }

        // draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
        [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
        public static class PatchAddScorch
        {
            public static void Prefix(FootstepManager __instance)
            {
                if (__instance.scorchList.Count == FootstepManager.maxDecals)
                {
                    __instance.scorchList.RemoveAt(0);
                    Debug("scorchList element 0 removed");
                }
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);

                // nop 7 leading codes to skip count checks
                for (int i = 0; i < 7; i++)
                {
                    codes[i].opcode = OpCodes.Nop;
                }

                //ListTheStack(codes);
                return codes.AsEnumerable();
            }

            public static void Postfix(FootstepManager __instance)
            {
                Debug($"scorchList is {__instance.scorchList.Count}/{__instance.scorchList.Capacity} (max: {FootstepManager.maxDecals})");
            }
        }
        
        // only when dev build is running
        [HarmonyPatch(typeof(CombatGameState), nameof(CombatGameState.Update))]
        public static class PatchCgsUpdate
        {
            public static void Prefix()
            {
                if (EnableDebug)
                {
                    UnityEngine.Debug.developerConsoleVisible = false;
                }
            }
        }
    }
}
