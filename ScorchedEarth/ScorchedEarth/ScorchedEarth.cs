﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech.Rendering;
using Harmony;
using Org.BouncyCastle.Crypto.Parameters;

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // strategy:  intercept calls to realtimeSinceStartup to make things practically infinite
        //            intercept calls for the only int32 on TerrainDecal (MaxDecals) and make it a high value
        // [HarmonyPatch(typeof(FootstepManager), "ProcessFootsteps")]
        // public class PatchProcessFootsteps
        // {
        //
        //     static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        //     {
        //         var sb = new StringBuilder();
        //         var codes = new List<CodeInstruction>(instructions);
        //
        //         sb.Append($"{DateTime.Now.ToLongTimeString()} ScorchedEarth logfile{Environment.NewLine}");
        //         sb.Append($"ProcessFootsteps IL{Environment.NewLine}");
        //         sb.Append($"================================================================================{Environment.NewLine}");
        //         for (var i = 0; i < codes.Count(); i++)
        //         {
        //             sb.Append($"{codes[i].opcode}\t\t");
        //             if (codes[i].operand != null)
        //             {
        //                 sb.Append($"{codes[i].operand}");
        //             }
        //             sb.Append($"{Environment.NewLine}");
        //         }
        //         sb.Append($"================================================================================{Environment.NewLine}");
        //
        //         for (var i = 0; i < codes.Count(); i++)
        //         {
        //             if (codes[i].operand == null)
        //             {
        //                 continue;
        //             }
        //             if (codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
        //             {
        //                 sb.Append($"FOUND {codes[i].operand}.  ");
        //
        //                 codes[i].opcode = OpCodes.Ldc_R4;
        //                 codes[i].operand = float.MinValue;
        //
        //                 sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
        //             }
        //
        //             if (codes[i].operand == null)
        //             {
        //                 continue;
        //             } 
        //             if (codes[i].opcode == OpCodes.Ldsfld &&
        //                 codes[i].operand.ToString().Contains("maxDecals"))
        //             {
        //                 sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");
        //
        //                 codes[i].opcode = OpCodes.Ldc_I4;
        //                 codes[i].operand = 255;
        //
        //                 sb.Append($"Changed to {codes[i].opcode}\t 255{Environment.NewLine}");
        //             }
        //
        //             if (sb.Length > 0)
        //             {
        //                 FileLog.Log(sb.ToString());
        //                 sb.Remove(0, sb.Length);
        //             }
        //         }
        //         return codes.AsEnumerable();
        //     }
        // }

        internal static void ListTheStack(StringBuilder sb, List<CodeInstruction> codes)
        {
            sb.Append(
                $"================================================================================{Environment.NewLine}");

            for (var i = 0; i < codes.Count(); i++)
            {
                sb.Append($"{codes[i].opcode}\t\t");
                if (codes[i].operand != null)
                {
                    sb.Append($"{codes[i].operand}");
                }

                sb.Append($"{Environment.NewLine}");
            }

            sb.Append(
                $"================================================================================{Environment.NewLine}");
        }

        private static void PatchTime(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
            {
                sb.Append($"FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_R4;
                codes[i].operand = -1f / 2;

                sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
            }
        }

        internal static void PatchDecals(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].opcode == OpCodes.Ldsfld &&
                codes[i].operand.ToString().Contains("maxDecals"))
            {
                sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_I4;
                codes[i].operand = 256;

                sb.Append($"Changed to {codes[i].opcode}\t 256{Environment.NewLine}");
            }

        }

        private static void LogStringBuilder(StringBuilder sb)
        {
            if (sb.Length > 0)
            {
                FileLog.Log(sb.ToString());
                sb.Remove(0, sb.Length);
            }
        }

        [HarmonyPatch(typeof(FootstepManager), "ProcessScorches")]
        public class PatchProcessScorches
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                var sb = new StringBuilder();
                //FileLog.Reset();
                sb.Append($"{Environment.NewLine}");
                sb.Append($"ProcessScorches IL{Environment.NewLine}");
                ListTheStack(sb, codes);

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand == null)
                    {
                        continue;
                    }

                    PatchTime(codes, i, sb);
                    PatchDecals(codes, i, sb);
                    LogStringBuilder(sb);
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(FootstepManager), "AddScorch")]
        internal class PatchAddScorch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                var sb = new StringBuilder();
                //FileLog.Reset();
                sb.Append($"{Environment.NewLine}");
                sb.Append($"AddScorch IL{Environment.NewLine}");
                ListTheStack(sb, codes);

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand == null)
                    {
                        continue;
                    }

                    PatchTime(codes, i, sb);
                    PatchDecals(codes, i, sb);
                    LogStringBuilder(sb);
                }

                return codes.AsEnumerable();
            }
        }
    }
}
