using Harmony;
using System.Reflection;
using Newtonsoft.Json;

namespace ScorchedEarth
{
    public class Settings
    {
        public bool Debug = false;
    }

    public class ScorchedEarth
    {
        internal static string ModDirectory;
        internal static Settings settings;
        public static void Init(string directory, string settingsJSON)
        {
            var harmony = HarmonyInstance.Create("ca.gnivler.ScorchedEarth");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            settings = JsonConvert.DeserializeObject<Settings>(settingsJSON);
            ModDirectory = directory;
        }
    }
}
