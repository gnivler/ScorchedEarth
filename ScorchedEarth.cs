using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech.Rendering;
using BattleTech.UI;
using Harmony;

namespace ScorchedEarth
{
    public class ScorchedEarth
    {
        public const int DECALS = 1023;

        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            AccessTools.Field(typeof(FootstepManager), nameof(FootstepManager.maxDecals)).SetValue(null, DECALS);
        }


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

        private static void LogStringBuilder(StringBuilder sb)
        {
            if (sb.Length > 0)
            {
                FileLog.Log(sb.ToString());
                sb.Remove(0, sb.Length);
            }
        }

        private static void TransTimeSinceStartup(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
            {
                sb.Append($"FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_R4;
                codes[i].operand = float.MinValue;

                sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
            }
        }

        private static void TransDecalCount(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].opcode == OpCodes.Ldsfld &&
                codes[i].operand.ToString().Contains("maxDecals"))
            {
                sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_I4;
                codes[i].operand = DECALS;

                sb.Append($"Changed to {codes[i].opcode}\t {DECALS}{Environment.NewLine}");
            }
        }

        private static void TransScorchStartTime(List<CodeInstruction> codes, int i, StringBuilder sb, ref int y, ref int z)
        {
            if (codes[i].opcode == OpCodes.Callvirt &&
                codes[i].operand.ToString().Contains("get_startTime"))
            {
                if (y == 0)
                {
                    y++;
                    sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                    codes[i].opcode = OpCodes.Ldc_R4;
                    codes[i].operand = -1f; // this is coded to mean persistent decals.......

                    sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
                }
            }/*
            else if (codes[i].opcode == OpCodes.Ldc_R4 &&
                     codes[i].operand.ToString() == "0")
            {
                z++;
                if (z == 2)  // the 3rd occurence only at     this.scorchAlphas[index] = Mathf.SmoothStep(0.0f, 1f, 1f - num1);
                {
                    sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                    codes[i].opcode = OpCodes.Ldc_R4;
                    codes[i].operand = 1f;

                    sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
                }
            }*/
        }

        private static void TransDecalLife(List<CodeInstruction> codes, int i, StringBuilder sb)
        {
            if (codes[i].operand.ToString().Contains("footstepLife"))  // this is used for scorches as well
            {
                sb.Append($"FOUND {codes[i].operand}.  ");

                codes[i].opcode = OpCodes.Ldc_R4;
                codes[i].operand = float.MaxValue;

                sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
            }
        }

        private static void TransDecalFadeTime(List<CodeInstruction> codes, int i, StringBuilder sb, ref bool flag)
        {
            if (codes[i].opcode == OpCodes.Ldsfld &&
                codes[i].operand.ToString().Contains("decalFadeTime"))
            {
                if (!flag)
                {
                    sb.Append($"FOUND FIRST {codes[i].operand}.  ");

                    codes[i].opcode = OpCodes.Ldc_R4;
                    codes[i].operand = -10000f;

                    sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
                }
                else
                {
                    {
                        sb.Append($"FOUND SECOND {codes[i].operand}.  ");

                        codes[i].opcode = OpCodes.Ldc_R4;
                        codes[i].operand = 1f;
                        sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");

                    }
                }
            }
        }

        private static void TransJumpIntoAddScorch(List<CodeInstruction> codes, int i, StringBuilder sb)
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
        }  // not sure how to jump (what's the operand?)

        private static void TransPlayImpactPersistent(List<CodeInstruction> codes, int i, StringBuilder sb, ref int y)
        {
            if (codes[i].opcode == OpCodes.Ldc_I4_0)
            {
                if (y == 3) // 4th instance only
                {
                    sb.Append($"{Environment.NewLine}FOUND {codes[i].opcode}.  ");

                    codes[i].opcode = OpCodes.Ldc_I4_1;

                    sb.Append($"Changed to {codes[i].opcode}\t{codes[i].operand}{Environment.NewLine}");
                }
                y++;
            }
        }

        [HarmonyPatch(typeof(FootstepManager))]
        [HarmonyPatch("footstepList", PropertyMethod.Getter)]
        static class FootstepManager_footstepList_Patch
        {
            static void Prefix(FootstepManager __instance)
            {
                var instance = Traverse.Create(__instance);
                var _footstepList = instance.Field("_footstepList");
                if (_footstepList.GetValue() == null)
                {
                    Type variableType = typeof(List<>).MakeGenericType(new Type[] { AccessTools.Inner(typeof(FootstepManager), "TerrainDecal") });
                    _footstepList.SetValue(Activator.CreateInstance(variableType, new object[] { ScorchedEarth.DECALS }));
                }
            }
        }

        //strategy:  intercept calls to realtimeSinceStartup to make things practically infinite
        //           intercept calls for the only int32 on TerrainDecal (MaxDecals) and make it a high value
        [HarmonyPatch(typeof(FootstepManager), "ProcessFootsteps")]
        public class PatchProcessFootsteps
        {

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var sb = new StringBuilder();
                var codes = new List<CodeInstruction>(instructions);

                sb.Append($"{Environment.NewLine}");
                sb.Append($"ProcessFootsteps IL{Environment.NewLine}");

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand == null)
                    {
                        continue;
                    }
                    if (codes[i].operand.ToString().Contains("get_realtimeSinceStartup"))
                    {
                        sb.Append($"FOUND {codes[i].operand}.  ");

                        codes[i].opcode = OpCodes.Ldc_R4;
                        codes[i].operand = float.MinValue;

                        sb.Append($"Changed to {codes[i].opcode}\t\t{codes[i].operand}{Environment.NewLine}");
                    }

                    if (codes[i].operand == null)
                    {
                        continue;
                    }
                    if (codes[i].opcode == OpCodes.Ldsfld &&
                        codes[i].operand.ToString().Contains("maxDecals"))
                    {
                        sb.Append($"{Environment.NewLine}FOUND {codes[i].operand}.  ");

                        codes[i].opcode = OpCodes.Ldc_I4;
                        codes[i].operand = 255;

                        sb.Append($"Changed to {codes[i].opcode}\t 255{Environment.NewLine}");
                    }
                    ListTheStack(sb, codes);
                    LogStringBuilder(sb);
                }
                return codes.AsEnumerable();
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
                int y = 0;

                sb.Append($"{Environment.NewLine}");
                sb.Append($"ProcessScorches IL{Environment.NewLine}");

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand == null)
                    {
                        continue;
                    }

                    TransTimeSinceStartup(codes, i, sb);
                    TransDecalCount(codes, i, sb);
                }
                ListTheStack(sb, codes);
                LogStringBuilder(sb);

                return codes.AsEnumerable();
            }
        }


        [HarmonyPatch(typeof(MissileEffect), "PlayImpact")]
        public class PatchPlayImpact
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                var sb = new StringBuilder();
                var y = 0;

                sb.Append($"{Environment.NewLine}");
                sb.Append($"PlayImpact IL{Environment.NewLine}");

                for (var i = 0; i < codes.Count(); i++)
                {
                    if (codes[i].operand == null)
                    {
                        continue;
                    }

                    TransPlayImpactPersistent(codes, i, sb, ref y);
                }
                ListTheStack(sb, codes);
                LogStringBuilder(sb);

                return codes.AsEnumerable();
            }
        }
    }
}
