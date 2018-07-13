using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech.Rendering;
using Harmony;

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        public StringBuilder Builder = new StringBuilder();

        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // strategy:  intercept calls to realtimeSinceStartup to make things practically infinite
        //            intercept calls for the only int32 on TerrainDecal (MaxDecals) and make it a high value
        [HarmonyPatch(typeof(FootstepManager), "ProcessFootsteps")]
        public static class PatchProcessFootsteps
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                for (var i = 0; i < codes.Count(); i++)
                {

                    if (codes[i].operand != null && codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
                    {
                        codes[i].opcode = OpCodes.Ldc_R4;
                        codes[i].operand = float.MinValue;
                    }

                    if (codes[i].operand != null && codes[i].operand.ToString()
                            .Contains("FootstepManager/TerrainDecal>::get_Item(int32)"))
                    {
                        codes[i].opcode = OpCodes.Ldc_I4_4;
                        codes[i].operand = int.MaxValue;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(FootstepManager), "ProcessScorches")]
        public static class PatchProcessScorches
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand != null && codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
                    {
                        codes[i].opcode = OpCodes.Ldc_R4;
                        codes[i].operand = float.MinValue;
                    }

                    if (codes[i].operand != null && codes[i].operand.ToString()
                            .Contains("FootstepManager/TerrainDecal>::get_Item(int32)"))
                    {
                        codes[i].opcode = OpCodes.Ldc_I4_4;
                        codes[i].operand = int.MaxValue;
                    }
                }

                return codes.AsEnumerable();
            }
        }

    }
}
