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
        public const int DECALS = 1023;
        public static string ModDirectory;
        public static readonly bool EnableDebug = false;

        public static void Init(string directory)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModDirectory = directory;
            if (EnableDebug)
            {
                Clear();
            }
        }

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

        [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
        public static class AddScorchPatch
        {
            public static void Prefix(FootstepManager __instance)
            {
                Debug("AddScorch Prefix");
                if (__instance.scorchList.Count == FootstepManager.maxDecals)
                {
                    __instance.scorchList.RemoveAt(0);
                    Debug("element 0 removed");
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
                Debug($"scorchList is {__instance.scorchList.Count}/{__instance.scorchList.Capacity}");
            }

            [HarmonyPatch(typeof(FootstepManager.TerrainDecal))]
            [HarmonyPatch(new[] {typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(float)})]
            public static class Patch_TerrainDecalCtor
            {
                public static bool Prefix(FootstepManager.TerrainDecal __instance, Vector3 position, Quaternion rotation, Vector3 scale)
                {
                    __instance.transformMatrix = Matrix4x4.TRS(position, rotation, scale);
                    __instance.startTime = float.MaxValue;
                    return false;
                }
            }
        }
    }
}
