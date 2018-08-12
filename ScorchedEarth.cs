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
          //  public static void Prefix(FootstepManager __instance)
          //  {
          //      if (__instance.scorchList.Count == FootstepManager.maxDecals)
          //      {
          //          Debug("AddScorch Prefix");
          //          Debug($"at 0 is {__instance.scorchList[0].transformMatrix}");
          //          Debug($"at 124 is {__instance.scorchList[124].transformMatrix}");
//
          //          __instance.scorchList.RemoveRange(0, 10);
//
          //          Debug("element 0 removed");
          //          Debug($"at 0 is {__instance.scorchList[0].transformMatrix}");
          //          Debug($"at 124 is {__instance.scorchList[124].transformMatrix}");
          //      }
          //  }

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
                Debug("AddScorch Postfix");
                Debug($"scorchList is {__instance.scorchList.Count}/{__instance.scorchList.Capacity}");
                Debug($"at 0 is {__instance.scorchList[0].transformMatrix}");
                Debug($"at 124 is {__instance.scorchList[124].transformMatrix}");
            }
        }

        [HarmonyPatch(typeof(FootstepManager.TerrainDecal))]
        public static class PatchTerrainDecal
        {
            public static bool Prefix(Vector3 position, Quaternion rotation, Vector3 scale, FootstepManager.TerrainDecal __instance)
            {
                __instance.transformMatrix = Matrix4x4.TRS(position, rotation, scale);
                __instance.startTime = float.MaxValue;
                return false;
            }
        }

/*
        // lynchping patch that makes scorches permanent by an unused boolean flag in the assembly
        [HarmonyPatch(typeof(MissileEffect), nameof(MissileEffect.PlayImpact))]
        public class PatchPlayImpact
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FileLog.Log("PlayImpact Transpiler Harmony");
                var codes = new List<CodeInstruction>(instructions);
                var sb = new StringBuilder();

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].opcode == OpCodes.Ldc_I4_0)
                        if (codes[i + 1].opcode == OpCodes.Callvirt)
                        {
                            sb.Append("should only have one match for calling addScorch - 0 to 1\n");
                            codes[i].opcode = OpCodes.Ldc_I4_1;
                        }
                }

                return codes.AsEnumerable();
            }
        }
*/
    }
}
