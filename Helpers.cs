using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using BattleTech.Rendering;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
using static ScorchedEarth.ScorchedEarth;

namespace ScorchedEarth
{
    internal class Helpers
    {
        // thanks jo!
        internal static float Distance(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m03 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m13 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m23 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        internal static void CreateDecals(string filename, string decalList)
        {
            var terrainDecal = AccessTools.Inner(typeof(FootstepManager), "TerrainDecal");
            var decals = JsonConvert.DeserializeObject<List<DecalInfo>>(File.ReadAllText(filename));
            var list = Traverse.Create(FootstepManager.Instance).Property(decalList).GetValue<IList>();
            foreach (var decal in decals)
            {
                var newDecal = Activator.CreateInstance(
                    terrainDecal,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
                    null,
                    new object[] {decal.pos, decal.rot, decal.scale, float.MaxValue},
                    null,
                    null);
                list.Add(newDecal);
            }
            Log($"{decalList}: {list.Count}");
        }

        internal static void BuildDecalList(string filename, string decalList)
        {
            var scorchList = Traverse.Create(FootstepManager.Instance).Property(decalList).GetValue<IList>();
            var list = new List<DecalInfo>();

            foreach (var scorch in scorchList)
            {
                var terrainDecal = new DecalInfo();
                var tm = (Matrix4x4) scorch.GetType().GetRuntimeFields()
                    .First(x => x.FieldType == typeof(Matrix4x4)).GetValue(scorch);
                terrainDecal.pos = new Vector3(tm.m03, tm.m13, tm.m23);
                terrainDecal.rot = tm.rotation;
                terrainDecal.scale = tm.lossyScale;
                list.Add(terrainDecal);
            }

            File.WriteAllText(filename, JsonConvert.SerializeObject(list, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented,
            }));
        }

        internal static void CleanupSaves()
        {
            try
            {
                Log("Cleaning mod saves");
                var modSaves = new DirectoryInfo(modSettings.SaveDirectory).GetFiles();
                var slots = UnityGameInstance.BattleTechGame.SaveManager.GameInstanceSaves.GetAllSlots();
                var gameSaves = new List<string>();

                foreach (var slot in slots)
                {
                    gameSaves.Add(slot.FileID.Substring(5));
                }

                // if the scorch file has no match save file, delete it 
                // no point going through the footsteps separately so just assume they're in pairs
                foreach (var scorchFilename in modSaves.Where(file => file.Name.EndsWith(".scorches.json")))
                {
                    // trim off .scorches.json (14 characters)
                    var filename = scorchFilename.Name.Substring(0, scorchFilename.Name.Length - 14);
                    if (!gameSaves.Contains(filename))
                    {
                        Log($"{filename} is no longer present, deleting associated mod save data");
                        scorchFilename.Delete();
                        var footstepsFilename = scorchFilename.ToString().Replace(".scorches", ".footsteps");
                        new FileInfo(footstepsFilename).Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        internal static void SetMaxDecals()
        {
            // maxDecals changes EVERYTHING (all* arrays sizes)
            // make an acceptable value
            var decals = modSettings.MaxDecals;
            if (decals < 125 || decals > 1000 || decals % 125 != 0)
            {
                modSettings.MaxDecals = 125;
                Log("Invalid value in mod.json, using default instead.  MaxDecals must be a multiple of 125 and no greater than 1000.");
            }

            Log($"MaxDecals is {modSettings.MaxDecals}");
            AccessTools.Field(typeof(FootstepManager), "maxDecals").SetValue(null, modSettings.MaxDecals);
        }
    }
}
