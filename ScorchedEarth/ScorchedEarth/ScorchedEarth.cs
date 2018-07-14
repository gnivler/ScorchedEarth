using System;
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

        private static void ListTheStack(StringBuilder sb, List<CodeInstruction> codes)
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

        private static void PatchTimeSinceStartup(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
            {
                sb.Append($"FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_R4;
                codes[i].operand = -1f;

                sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
            }
        }

        private static void PatchDecalLife(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].operand.ToString().Contains("footstepLife"))  // this is used for scorches as well
            {
                sb.Append($"FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_R4;
                codes[i].operand = float.MaxValue;

                sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
            }
        }

        private static void PatchDecals(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].opcode == OpCodes.Ldsfld &&
                codes[i].operand.ToString().Contains("maxDecals"))
            {
                sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                var decals = 10000;
                codes[i].opcode = OpCodes.Ldc_I4;
                codes[i].operand = decals;

                sb.Append($"Changed to {codes[i].opcode}\t {decals}{Environment.NewLine}");
            }
        }

        private static void PatchJumpIntoAddScorch(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].opcode == OpCodes.Ldsfld &&
                codes[i].operand.ToString().Contains("maxDecals"))
            {
                sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                var decals = 10000;
                codes[i].opcode = OpCodes.Ldc_I4;
                codes[i].operand = decals;

                sb.Append($"Changed to {codes[i].opcode}\t {decals}{Environment.NewLine}");
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
            #region Method IL
            /*
            ProcessScorches IL
            ================================================================================
            call		Single get_realtimeSinceStartup()
            stloc.0		
            ldc.i4.0		
            stloc.1		
            br		    System.Reflection.Emit.Label
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            ldloc.1		
            callvirt	BattleTech.Rendering.FootstepManager+TerrainDecal get_Item(Int32)
            stloc.2		
            ldc.r4		0
            stloc.3		
            ldloc.2		
            callvirt	Single get_startTime()
            ldc.r4		-1
            beq 		System.Reflection.Emit.Label
            ldloc.0		
            ldloc.2		
            callvirt	Single get_startTime()
            sub		
            ldsfld		System.Single scorchLife
            bgt.un		System.Reflection.Emit.Label
            ldloc.2		
            callvirt	Single get_startTime()
            ldsfld		System.Single footstepLife
            add		
            ldsfld		System.Single decalFadeTime
            sub		
            stloc.s		System.Single (4)
            ldloc.0		
            ldloc.s		System.Single (4)
            sub		
            ldsfld		System.Single decalFadeTime
            div		
            stloc.3		
            br	    	System.Reflection.Emit.Label
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            ldloc.1		
            callvirt	Void RemoveAt(Int32)
            ldloc.1		
            ldc.i4.1		
            sub		
            stloc.1		
            ldloc.1		
            ldc.i4.1		
            add		
            stloc.1		
            br		    System.Reflection.Emit.Label
            ldarg.0		
            call		System.Single[] get_scorchAlphas()
            ldloc.1		
            ldc.r4		0
            ldc.r4		1
            ldc.r4		1
            ldloc.3		
            sub		
            call		Single SmoothStep(Single, Single, Single)
            stelem.r4		
            ldarg.0		
            call		UnityEngine.Matrix4x4[] get_scorchTRS()
            ldloc.1		
            ldloc.2		
            callvirt	Matrix4x4 get_transformMatrix()
            stelem		UnityEngine.Matrix4x4
            br		    System.Reflection.Emit.Label
            ldloc.1		
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            callvirt	Int32 get_Count()
            bge 		System.Reflection.Emit.Label
            ldloc.1		
            ldsfld		System.Int32 maxDecals
            blt	    	System.Reflection.Emit.Label
            ldstr		_BT_ScorchAlpha
            ldarg.0		
            call		System.Single[] get_scorchAlphas()
            call		Void SetGlobalFloatArray(System.String, System.Single[])
            ldarg.1		
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            callvirt	Int32 get_Count()
            stind.i4		
            ldarg.0		
            call		UnityEngine.Matrix4x4[] get_scorchTRS()
            ret		
            ================================================================================*/
            #endregion

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
                    PatchTimeSinceStartup(codes, i, sb);                                                        // maybe try the decal fade time instead of startime?
                    PatchDecals(codes, i, sb);                                                      // why does the renderer break
                    LogStringBuilder(sb);
                }

                return codes.AsEnumerable();
            }
        }

      //[HarmonyPatch(typeof(FootstepManager), "AddScorch")]
      //internal class PatchAddScorch
      //{
      //    #region Method IL
            /*
            AddScorch IL
            ================================================================================
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            callvirt	Int32 get_Count()
            ldsfld		System.Int32 maxDecals
            blt		    System.Reflection.Emit.Label
            ldc.i4.0		
            ret		
            ldarg.s		4
            brfalse		System.Reflection.Emit.Label
            ldc.r4		-1
            br		    System.Reflection.Emit.Label
            call		Single get_realtimeSinceStartup()
            stloc.0		
            ldarg.2		
            call		Quaternion LookRotation(Vector3)
            stloc.3		
            ldloca.s	UnityEngine.Quaternion (3)
            call		Vector3 get_eulerAngles()
            ldfld		System.Single y
            stloc.1		
            ldc.r4		0
            ldloc.1		
            ldc.r4		0
            call		Quaternion Euler(Single, Single, Single)
            stloc.2		
            ldarg.0		
            call		System.Collections.Generic.List`1[BattleTech.Rendering.FootstepManager+TerrainDecal] get_scorchList()
            ldarg.1		
            ldloc.2		
            ldarg.3		
            ldloc.0		
            newobj		Void .ctor(Vector3, Quaternion, Vector3, Single)
            callvirt	Void Add(BattleTech.Rendering.FootstepManager+TerrainDecal)
            ldc.i4.1		
            ret		
            ================================================================================
            */
            #endregion
      //
      //    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
      //    {
      //        var codes = new List<CodeInstruction>(instructions);
      //        var sb = new StringBuilder();
      //        //FileLog.Reset();
      //        sb.Append($"{Environment.NewLine}");
      //        sb.Append($"AddScorch IL{Environment.NewLine}");
      //        ListTheStack(sb, codes);
      //
      //        for (int i = 0; i < 11; i++)
      //        {
      //            codes[0].opcode = OpCodes.Nop;
      //            sb.Append($"Nop {i} - {codes[i].opcode}{Environment.NewLine}");
      //        }
      //
      //        for (var i = 0; i < codes.Count(); i++)
      //        {
      //            if (codes[i].operand == null)
      //            {
      //                continue;
      //            }
      //
      //            PatchDecals(codes, i, sb);
      //            LogStringBuilder(sb);
      //        }
      //
      //        return codes.AsEnumerable();
      //    }
      //}
    }
}
