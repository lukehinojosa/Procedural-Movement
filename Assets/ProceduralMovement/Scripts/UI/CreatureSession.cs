using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProceduralMovement
{
    /// <summary>Carries a custom creature between scenes and saves / loads creature JSON files.</summary>
    public static class CreatureSession
    {
        public const string CreatorScene = "CreatureCreator";
        public const string SpiderScene = "SpiderDemo";
        public const string HumanScene = "HumanDemo";
        public const string HorseScene = "HorseDemo";

        /// <summary>Creature built in the creator; the demo bootstrap spawns it instead of its preset when set.</summary>
        public static CreatureDefinition Custom;
        public static bool UseCustomOnNextLoad;
        /// <summary>Creator state, so returning to the creator resumes where you left off.</summary>
        public static string CreatorStateJson;

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "Creatures");

        static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Creature" : name.Trim();
        }

        [System.Serializable]
        public class SavedCreature
        {
            public CreatorModel model;
            public CreatureDefinition definition;
        }

        public static string Save(CreatorModel model, CreatureDefinition def)
        {
            Directory.CreateDirectory(SaveDirectory);
            string path = Path.Combine(SaveDirectory, SafeName(def.name) + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(new SavedCreature { model = model, definition = def }, true));
            return path;
        }

        public static List<string> ListSaved()
        {
            var list = new List<string>();
            if (!Directory.Exists(SaveDirectory)) return list;
            foreach (var f in Directory.GetFiles(SaveDirectory, "*.json")) list.Add(Path.GetFileNameWithoutExtension(f));
            list.Sort();
            return list;
        }

        public static SavedCreature Load(string name)
        {
            string path = Path.Combine(SaveDirectory, SafeName(name) + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                var saved = JsonUtility.FromJson<SavedCreature>(File.ReadAllText(path));
                return saved != null && saved.definition != null && saved.definition.limbs != null ? saved : null;
            }
            catch (System.Exception e) { Debug.LogWarning($"Could not load creature '{name}': {e.Message}"); return null; }
        }

        public static void Delete(string name)
        {
            string path = Path.Combine(SaveDirectory, SafeName(name) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
