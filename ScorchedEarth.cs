using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        // distances greater than this will be filtered out
        public const float DECALJITTER = 0.195f;

        public static string ModDirectory;
        public static Settings settings;
        public static bool debug = true;

        public static void Init(string directory, string settingsJson)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            ModDirectory = directory;
            try
            {
                settings = JsonConvert.DeserializeObject<Settings>(settingsJson);
            }
            catch (Exception e)
            {
                LogError(e);
                settings = new Settings();
            }

            if (debug)
            {
                LogClear();
            }

            // maxDecals changes EVERYTHING (all* arrays sizes)
            AccessTools.Field(typeof(FootstepManager), "maxDecals").SetValue(null, settings.MaxDecals);
        }

        public class Settings
        {
            // not used..nobody needs debug on this except dev
            //public bool enableDebug = true;

            public int maxDecals;

            public int MaxDecals
            {
                get => maxDecals;

                set
                {
                    // make an acceptable value
                    if (value % 125 != 0 || maxDecals < 125 || maxDecals > 1000)
                    {
                        maxDecals = 125;
                        LogDebug("MaxDecals must be a multiple of 125 and no greater than 1,000");
                    }

                    maxDecals = value;

                    LogDebug($"maxDecals is {MaxDecals}");
                }
            }
        }

        // thanks jo!
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

// every TerrainDecal will have a time property that makes all comparisons practically infinite with MaxValue
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
            deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull, 0, FootstepManager.Instance.footstepMaterial, 0, matrices1, numFootsteps, null);

            Matrix4x4[] matrices2 = FootstepManager.Instance.ProcessScorches(out numScorches);

            // thanks https://stackoverflow.com/a/3517542/6296808 for the splitting code
            var results = matrices2.Select((x, i) => new
                {
                    Key = i / size,
                    Value = x
                })
                .GroupBy(x => x.Key, x => x.Value, (k, g) => g.ToArray())
                .ToArray();

            // send each array element (which is an array) to Unity
            for (int i = 0; i < results.Length; i++)
            {
                deferredDecalsBuffer.DrawMeshInstanced(BTDecal.DecalMesh.DecalMeshFull, 0, FootstepManager.Instance.scorchMaterial, 0, results[i], size, null);
            }

            return false;
        }
    }

// FIFO footsteps
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
                LogDebug("footstepList element 0 removed");
            }
            catch // we don't need the exception
            {
                LogDebug("AddFootstep remove 0 failed");
            }
        }

        // running status line
        public static void Postfix(FootstepManager __instance)
        {
            LogDebug($"footstepList is {__instance.footstepList.Count}/{__instance.footstepList.Capacity}");
        }
    }

// draws scorches without logic checks, also providing FIFO by removing the first scorch element as needed
    [HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.AddScorch))]
    public static class PatchFootstepManagerAddScorch
    {
        public static bool Prefix(FootstepManager __instance, Vector3 position, Vector3 forward, Vector3 scale, bool persistent, ref bool __result)
        {
            if (__instance._scorchTRS.Any(x => Distance(x, position) > DECALJITTER))
            {
                Quaternion rotation = Quaternion.LookRotation(forward);
                rotation = Quaternion.Euler(0.0f, rotation.eulerAngles.y, 0.0f);
                __instance.scorchList.Add(new FootstepManager.TerrainDecal(position, rotation, scale, -1f));
                __result = true;
            }

            // FIFO logic, only act when needed
            if (__instance.scorchList.Count != FootstepManager.maxDecals) return false;
            try
            {
                __instance.scorchList.RemoveAt(0);
                LogDebug("scorchList element 0 removed");
            }
            catch // we don't need the exception

            {
                LogDebug("AddScorch remove 0 failed");
            }

            return false;
        }

        // nop 7 leading codes to skip count checks
        // public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        // {
        //     var codes = new List<CodeInstruction>(instructions);
        //     for (var i = 0; i < 7; i++)
        //     {
        //         codes[i].opcode = OpCodes.Nop;
        //     }
        //
        //     return codes.AsEnumerable();
        // }

        // running status line
        public static void Postfix(FootstepManager __instance)
        {
            LogDebug($"scorchList is {__instance.scorchList.Count}/{__instance.scorchList.Capacity}");
        }
    }
}

//[HarmonyPatch(typeof(FootstepManager), nameof(FootstepManager.ProcessScorches), new Type[] {typeof(int)}, new ArgumentType[] {ArgumentType.Out})]
//public static class ProcessScorchesPatch
//{
//    public static bool Prefix(FootstepManager __instance, out int numScorches, ref Matrix4x4[] __result)
//    {
//        for (int index = 0; index < __instance.scorchList.Count; ++index)
//        {
//            FootstepManager.TerrainDecal scorch = __instance.scorchList[index];
//            __instance.scorchAlphas[index] = Mathf.SmoothStep(0.0f, 1f, 1f);
//            __instance.scorchTRS[index] = scorch.transformMatrix;
//        }
//
//        Shader.SetGlobalFloatArray("_BT_ScorchAlpha", __instance.scorchAlphas);
//        numScorches = __instance.scorchList.Count;
//        __result = __instance.scorchTRS;
//        return false;
//    }
//}

/*   [HarmonyPatch(typeof(MissileEffect), nameof(MissileEffect.PlayImpact), MethodType.Normal)]
   public class PatchMissileEffectPlayImpact
   {
       // copied from decompile
       public static bool Prefix(MissileEffect __instance)
       {
           __instance.PlayImpactAudio();
           if (__instance.isSRM)
           {
               int num1 = (int) WwiseManager.PostEvent(AudioEventList_srm.srm_projectile_stop, __instance.projectileAudioObject, null, null);
           }
           else
           {
               int num2 = (int) WwiseManager.PostEvent(AudioEventList_lrm.lrm_projectile_stop, __instance.projectileAudioObject, null, null);
           }

           if (!string.IsNullOrEmpty(__instance.impactVFXBase) && __instance.hitInfo.hitLocations[__instance.hitIndex] != 0)
           {
               string str1 = string.Empty;
               bool flag = false;
               ICombatant combatantByGuid = __instance.Combat.FindCombatantByGUID(__instance.hitInfo.targetId);
               string str2 = string.Empty;
               if (__instance.hitInfo.hitLocations[__instance.hitIndex] == 65536)
               {
                   flag = true;
                   MapTerrainDataCell cellAt = __instance.weapon.parent.Combat.MapMetaData.GetCellAt(__instance.endPos);
                   if (cellAt != null)
                   {
                       str2 = cellAt.GetVFXNameModifier();
                       switch (cellAt.GetAudioSurfaceType())
                       {
                           case AudioSwitch_surface_type.dirt:
                               str1 = "_dirt";
                               break;
                           case AudioSwitch_surface_type.metal:
                               str1 = "_metal";
                               break;
                           case AudioSwitch_surface_type.snow:
                               str1 = "_snow";
                               break;
                           case AudioSwitch_surface_type.wood:
                               str1 = "_wood";
                               break;
                           case AudioSwitch_surface_type.brush:
                               str1 = "_brush";
                               break;
                           case AudioSwitch_surface_type.concrete:
                               str1 = "_concrete";
                               break;
                           case AudioSwitch_surface_type.debris_glass:
                               str1 = "_debris_glass";
                               break;
                           case AudioSwitch_surface_type.gravel:
                               str1 = "_gravel";
                               break;
                           case AudioSwitch_surface_type.ice:
                               str1 = "_ice";
                               break;
                           case AudioSwitch_surface_type.lava:
                               str1 = "_lava";
                               break;
                           case AudioSwitch_surface_type.mud:
                               str1 = "_mud";
                               break;
                           case AudioSwitch_surface_type.sand:
                               str1 = "_sand";
                               break;
                           case AudioSwitch_surface_type.water_deep:
                           case AudioSwitch_surface_type.water_shallow:
                               str1 = "_water";
                               break;
                           default:
                               str1 = "_dirt";
                               break;
                       }
                   }
               }
               else if (__instance.hitInfo.hitLocations[__instance.hitIndex] != 65536 &&
                        combatantByGuid != null &&
                        __instance.weapon.DamagePerShotAdjusted(__instance.weapon.parent.occupiedDesignMask) >
                        (double) combatantByGuid.ArmorForLocation(__instance.hitInfo.hitLocations[__instance.hitIndex]))
               {
                   str1 = "_crit";
               }
               else if (__instance.impactVFXVariations != null && __instance.impactVFXVariations.Length > 0)
               {
                   str1 = "_" + __instance.impactVFXVariations[Random.Range(0, __instance.impactVFXVariations.Length)];
               }

               __instance.SpawnImpactExplosion(string.Format("{0}{1}{2}", __instance.impactVFXBase, str1, str2));
               if (flag)
               {
                   // create the Vector3 ahead of time
                   float num3 = Random.Range(20f, 25f) * (!__instance.isSRM ? 1f : 0.75f);
                   var randomVector1 = new Vector3(Random.Range(0.0f, 1f), 0.0f, Random.Range(0.0f, 1f)).normalized;

                   // enumerate the existing scorch TRS array for decals considered close enough
                   // thanks jo!
                   if (FootstepManager.Instance._scorchTRS.Any(x => Distance(x, randomVector1) > DECALJITTER))
                   {
                       FootstepManager.Instance.AddScorch(__instance.endPos, randomVector1, new Vector3(num3, num3, num3), false);
                   }
               }
           }

           __instance.PlayImpactDamageOverlay();
           if (__instance.projectileMeshObject != null)
               __instance.projectileMeshObject.SetActive(false);
           if (__instance.projectileLightObject != null)
               __instance.projectileLightObject.SetActive(false);
           __instance.OnImpact(0.0f);

           return false;
       }
   }
   */

//[HarmonyPatch(typeof(BTDecal.DecalController), "RemoveDecal", MethodType.Normal)]
//public static class RemoveDecalPatch
//{
//    public static bool Prefix(BTDecal decal)
//    {
//        //LogDebug("RemoveDecal Prefix");
//        List<BTDecal> list;
//        if (BTDecal.DecalController.decalDict.TryGetValue(decal.decalMaterial, out list))
//        {
//            if (list.Count == 0)
//            {
//                BTDecal.DecalController.decalDict.Remove(decal.decalMaterial);
//            }
//        }
//
//        return false;
//    }
//}
// trying to verify that the value going into the game is correct when it should be (apparently yes but it doesn't work)
//[HarmonyPatch(typeof(BTDecal.DecalController), nameof(BTDecal.DecalController.ProcessCommandBuffer))]
//public static class PatchProcessCommandBuffer
//{
//    private static bool said;
//
//    public static void Prefix()
//    {
//        if (said) return;
//        said = true;
//        //LogDebug($"ProcessCommandBuffer says max is {BTDecal.DecalController.MaxInstances}");
//    }
//}

//    this bogs down / freezes the game although it once just bogged it down.  I think it's too far in the call chain anyway, trying DrawDecals
//[HarmonyPatch(typeof(CommandBuffer), "DrawMeshInstanced")]
//[HarmonyPatch(new Type[] {typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(Matrix4x4[]), typeof(int), typeof(MaterialPropertyBlock)})]
//public static class DrawMeshInstancedPatch
//{
//    //private static bool initialized;
//    //private static Traverse drawMesh;
//
//    private static readonly MethodInfo drawMeshMethodInfo = AccessTools.Method(typeof(CommandBuffer), "Internal_DrawMeshInstanced",
//        new Type[] {typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(Matrix4x4[]), typeof(int), typeof(MaterialPropertyBlock)});
//
//    public static bool Prefix(
//        CommandBuffer __instance,
//        Mesh mesh,
//        int submeshIndex,
//        Material material,
//        int shaderPass,
//        Matrix4x4[] matrices,
//        [DefaultValue("matrices.Length")] int count,
//        [DefaultValue("null")] MaterialPropertyBlock properties)
//    {
//        if (!SystemInfo.supportsInstancing)
//        {
//            throw new InvalidOperationException("DrawMeshInstanced is not supported.");
//        }
//
//        if (mesh == null)
//        {
//            throw new ArgumentNullException("mesh");
//        }
//
//        if (submeshIndex < 0 || submeshIndex >= mesh.subMeshCount)
//        {
//            throw new ArgumentOutOfRangeException("submeshIndex", "submeshIndex out of range.");
//        }
//
//        if (material == null)
//        {
//            throw new ArgumentNullException("material");
//        }
//
//        if (matrices == null)
//        {
//            throw new ArgumentNullException("matrices");
//        }
//
//        int kMaxDrawMeshInstanceCount = Traverse.Create(typeof(Graphics)).Field("kMaxDrawMeshInstanceCount").GetValue<int>();
//        if (count < 0 || count > Mathf.Min(kMaxDrawMeshInstanceCount, matrices.Length))
//        {
//            throw new ArgumentOutOfRangeException("count", string.Format("Count must be in the range of 0 to {0}.", Mathf.Min(kMaxDrawMeshInstanceCount, matrices.Length)));
//        }
//
//        if (material.name.Contains("ScorchMaterial"))
//        {
//            ////LogDebug("found scorches");
//        }
//
//        if (count > 0)
//        {
//            //if (!initialized)
//            {
//                ////LogDebug("trying traverses");
//
//                initialized = true;
//                var drawMeshMethodInfo = AccessTools.Method(typeof(CommandBuffer), "Internal_DrawMeshInstanced",
//                    new Type[] {typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(Matrix4x4[]), typeof(int), typeof(MaterialPropertyBlock)});
//
//                drawMeshMethodInfo.Invoke(null, new object[] {mesh, submeshIndex, material, shaderPass, matrices, count, properties});
//
//                drawMesh = Traverse.Create(__instance).Method("Internal_DrawMeshInstanced",
//                    new Type[] {typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(Matrix4x4[]), typeof(int), typeof(MaterialPropertyBlock)});
//                
//                drawMesh.SetValue(new object[] {mesh, submeshIndex, material, shaderPass, matrices, count, properties});
//            }
//
//            // trying to execute private method overload on this
//            //Traverse.Create(__instance)
//            //    .Method("Internal_DrawMeshInstanced",
//            //        new Type[] {typeof(Mesh), typeof(int), typeof(Material), typeof(Matrix4x4[]), typeof(int), typeof(int), typeof(MaterialPropertyBlock)})
//            //    .SetValue(new object[] {mesh, submeshIndex, material, shaderPass, matrices, count, properties});
//           
//            // count and properties have a default value annotation:  [DefaultValue("matrices.Length")]   [DefaultValue("null")]
//            //__instance.Internal_DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count = matrices.Length, properties = null);
//        }
//
//        if (count <= 0)
//        {
//            return false;
//            
//        }
//        return false;
//    }
//}

// this one is probably needed.. eventually
//[HarmonyPatch(typeof(CommandBuffer), "DrawMeshInstanced", new[] {typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(Matrix4x4[]), typeof(int), typeof(MaterialPropertyBlock)}, MethodType.Constructor)]
//public static class DrawMeshInstancedPatch
//{
//    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
//    {
//        var codes = new List<CodeInstruction>(instructions);
//
//        for (int i = 0; i < codes.Count; i++)
//        {
//            if (codes[i].operand == null) continue;
//            if (codes[i].opcode == OpCodes.Call &&
//                codes[i].operand.ToString().Contains("DrawMeshInstanced"))
//            {
//                codes[i - 2].opcode = OpCodes.Ldc_I4;
//                codes[i - 2].operand = DECALS;
//            }
//        }
//
//        ListTheStack(codes);
//        return codes.AsEnumerable();
//    }
//}

// no real point to this if AccessTools can invoke it implicitly in Init()
//[HarmonyPatch(typeof(FootstepManager), MethodType.StaticConstructor)]
//public static class FootstepManagerStatiCtorPatch
//{
//    [HarmonyPrefix]
//    public static bool FootstepManager()
//    {
//        //LogDebug("Static ctor!");
//        int maxDecals = DECALS;
//        float footstepLife = 1000000;
//        float scorchLife = 1000000;
//        float decalFadeTime = 4f;
//        return false;
//    }
//}

// this fires but not sure what decals if any it prevents from disappearing
//[HarmonyPatch(typeof(BTUIDecal), nameof(BTUIDecal.DecalInvisible), MethodType.Normal)]
//public static class DecalInvisiblePatch
//{
//    public static bool Prefix()
//    {
//        //LogDebug("DecalInvisible");
//        return false;
//    }
//}

// patch the property which is supposed to return 125 or 500 for OpenGL.  Does nothing.  Testing with -force-opengl resulted in at least 1023 decals, though
// fires constantly
//[HarmonyPatch(typeof(BTDecal.DecalController), nameof(BTDecal.DecalController.MaxInstances), MethodType.Getter)]
//public static class MaxInstancesPatch
//{
//    private static bool said;
//
//    public static bool Prefix(ref int __result)
//    {
//        if (!said)
//        {
//            said = true;
//            //LogDebug($"MaxInstances returning {BTDecal.DecalController.MaxInstances}\nStopping after one verification");
//        }
//
//        __result = DECALS;
//        return false;
//    }
//}
