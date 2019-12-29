using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using BattleTech;
using BattleTech.Rendering;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
using static ScorchedEarth.ScorchedEarth;

namespace ScorchedEarth
{
    public static class Extensions
    {
        // thanks StackOverflow
        public static T[] Slice<T>(this T[] source, int index, int length)
        {
            T[] slice = new T[length];
            Array.Copy(source, index, slice, 0, length);
            return slice;
        }
    }

    internal class Helpers
    {
        private static readonly int BtFootstepAlpha = Shader.PropertyToID("_BT_FootstepAlpha");
        private static readonly int BtScorchAlpha = Shader.PropertyToID("_BT_ScorchAlpha");

        // adapted from the assembly
        public static Matrix4x4[][] ProcessFootsteps(out int numFootsteps)
        {
            var instance = FootstepManager.Instance;
            var num = 0;
            while (num < instance.footstepList.Count && num < FootstepManager.maxDecals)
            {
                var terrainDecal = instance.footstepList[num];
                instance.footstepAlphas[num] = Mathf.SmoothStep(0f, 1f, 1f);
                instance.footstepTRS[num] = terrainDecal.transformMatrix;
                num++;
            }

            Shader.SetGlobalFloatArray(BtFootstepAlpha, instance.footstepAlphas);
            numFootsteps = instance.footstepList.Count;
            // split up the results into chunkSize arrays
            var result = new Matrix4x4[modSettings.MaxDecals / chunkSize][];
            for (int i = 0, j = 0; i < numFootsteps; i += chunkSize, j++)
            {
                result[j] = instance.footstepTRS.Slice(i, chunkSize);
            }

            return result;
        }

        // adapted from the assembly
        public static Matrix4x4[][] ProcessScorches(out int numScorches)
        {
            var instance = FootstepManager.Instance;
            int num = 0;
            while (num < instance.scorchList.Count && num < FootstepManager.maxDecals)
            {
                var terrainDecal = instance.scorchList[num];
                instance.scorchAlphas[num] = Mathf.SmoothStep(0f, 1f, 1f);
                instance.scorchTRS[num] = terrainDecal.transformMatrix;
                num++;
            }

            Shader.SetGlobalFloatArray(BtScorchAlpha, instance.scorchAlphas);
            numScorches = instance.scorchList.Count;
            // split up the results into chunkSize arrays
            var result = new Matrix4x4[modSettings.MaxDecals / chunkSize][];
            for (int i = 0, j = 0; i < numScorches; i += chunkSize, j++)
            {
                result[j] = instance.scorchTRS.Slice(i, chunkSize);
            }

            return result;
        }

        // thanks jo!
        internal static float Distance(Matrix4x4 existingScorchPosition, Vector3 newScorchPosition)
        {
            var x = (double) existingScorchPosition.m03 - newScorchPosition.x;
            var y = (double) existingScorchPosition.m13 - newScorchPosition.y;
            var z = (double) existingScorchPosition.m23 - newScorchPosition.z;
            return Mathf.Sqrt((float) (x * x + y * y + z * z));
        }

        internal static IList RecreateDecals(string filename, string decalList, int element)
        {
            var terrainDecal = AccessTools.Inner(typeof(FootstepManager), "TerrainDecal");
            var decals = LoadDecals(filename);
            var result = (IList) new List<object>();
            foreach (var decal in decals[element])
            {
                var newDecal = Activator.CreateInstance(
                    terrainDecal,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.CreateInstance,
                    null,
                    new object[] {decal.pos, decal.rot, decal.scale, float.MaxValue},
                    null,
                    null);
                result.Add(newDecal);
            }

            Log($"{decalList}: {result.Count}");
            return result;
        }

        internal static IList ExtractDecals(string decalProperty)
        {
            var decals = Traverse.Create(FootstepManager.Instance).Property(decalProperty).GetValue<IList>();
            var list = new List<DecalInfo>();

            foreach (var decal in decals)
            {
                var terrainDecal = new DecalInfo();
                var tm = (Matrix4x4) decal.GetType().GetRuntimeFields()
                    .First(x => x.FieldType == typeof(Matrix4x4)).GetValue(decal);
                terrainDecal.pos = new Vector3(tm.m03, tm.m13, tm.m23);
                terrainDecal.rot = tm.rotation;
                terrainDecal.scale = tm.lossyScale;
                list.Add(terrainDecal);
            }

            return list;
        }

        internal static void SaveDecals(List<IList> results, string filename)
        {
            // compress json in memory
            var json = JsonConvert.SerializeObject(results, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Formatting = Formatting.Indented,
            });

            // thanks https://stackoverflow.com/questions/34775652/gzipstream-works-when-writing-to-filestream-but-not-memorystream
            using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                using (var output = new MemoryStream())
                {
                    using (var gZipStream = new GZipStream(output, CompressionLevel.Optimal, true))
                    {
                        input.CopyTo(gZipStream);
                    }

                    File.WriteAllBytes(filename, output.GetBuffer());
                }
            }
        }

        private static List<List<DecalInfo>> LoadDecals(string filename)
        {
            // decompress file to json
            using (var input = new StreamReader(filename))
            {
                using (var output = new MemoryStream())
                {
                    using (var gZipStream = new GZipStream(input.BaseStream, CompressionMode.Decompress, true))
                    {
                        gZipStream.CopyTo(output);
                    }

                    var json = Encoding.UTF8.GetString(output.ToArray());
                    return JsonConvert.DeserializeObject<List<List<DecalInfo>>>(json);
                }
            }
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
                // legacy cleanup for 3.0
                // 2nd save would be required to get rid of both 3.0 and 3.0.1 files for a missing save
                // this block would only be called once
                // TODO remove this eventually because how much do we care about legacy mod save data
                if (modSaves.Any(x => x.Name.EndsWith(".scorches.json")))
                {
                    // no point going through the footsteps separately so just assume they're in pairs
                    foreach (var modSave in modSaves.Where(file => file.Name.EndsWith(".scorches.json")))
                    {
                        // trim off .scorches.json (14 characters)
                        var filename = modSave.Name.Substring(0, modSave.Name.Length - 14);
                        if (!gameSaves.Contains(filename))
                        {
                            Log($"{filename} is no longer present, deleting associated mod save data");
                            modSave.Delete();
                            var footstepsFilename = modSave.ToString().Replace(".scorches", ".footsteps");
                            new FileInfo(footstepsFilename).Delete();
                        }
                    }
                }
                else
                {
                    // cleanup for single file design 3.0.1
                    foreach (var modSave in modSaves)
                    {
                        // trim off .json (5 characters)
                        var filename = modSave.Name.Substring(0, modSave.Name.Length - 5);
                        if (!gameSaves.Contains(filename))
                        {
                            Log($"{filename} is no longer present, deleting associated mod save data");
                            modSave.Delete();
                        }
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
            try
            {
                // maxDecals changes EVERYTHING (all* arrays sizes)
                // make an acceptable value
                var decals = modSettings.MaxDecals;
                if (decals < 125 || decals > 1000 || decals % 125 != 0)
                {
                    modSettings.MaxDecals = 125;
                    Log(
                        "Invalid value in mod.json, using default instead.  MaxDecals must be a multiple of 125 and no greater than 1000.");
                }

                Log($"MaxDecals is {modSettings.MaxDecals}");
                AccessTools.Field(typeof(FootstepManager), "maxDecals").SetValue(null, modSettings.MaxDecals);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
