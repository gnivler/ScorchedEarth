using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech.Rendering;
using UnityEngine;
using BattleTech.UI;
using BattleTech;
using HBS.Collections;

namespace ScorchedEarth {
    class Patch {
        [HarmonyPatch(typeof(BTDecal.DecalController))]
        [HarmonyPatch("MaxInstances", PropertyMethod.Getter)]
        public static class MaxInstancesPatch {
            public static bool Prefix(ref int __result) {
                Logger.Debug("MaxInstances!");
                __result = 500;
                return false;
            }
        }

        [HarmonyPatch(typeof(FootstepManager))]
        public static class AccessInternalClass {

            public static void Prefix(FootstepManager __instance, float __result) {
                Logger.Debug("startTime!");
                var fuck = Traverse.Create(__instance).Type("TerrainDecal");
                //__result = Traverse.Create(fuck).Property("startTime").GetValue<float>();
                [HarmonyPatch(typeof(AccessInternalClass.fuck))]
            public static void Prefix(float __result) {

            }


        }

        [HarmonyPatch(typeof(BTDecalSpawner), "Awake")]
        public static class PatchAwake {
            static void Prefix(BTDecalSpawner __instance) {
                Logger.Debug("Awake!");
                __instance.fadeMin = float.MinValue;
                __instance.fadeMax = float.MaxValue;
            }
        }

        [HarmonyPatch(typeof(BTDecalSpawner), "OnDestroy")]
        public static class PatchOnDestroy {
            static bool Prefix() {
                Logger.Debug("OnDestroy!");
                return false;
            }
        }

        [HarmonyPatch(typeof(BTDecalSpawner), "ClearDecal")]
        public static class ClearDecalPatch {
            public static bool Prefix() {
                Logger.Debug("ClearDecal!");
                return false;
            }
        }

    }
}











