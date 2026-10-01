using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProceduralMovement.EditorTools
{
    /// <summary>Creates the demo scenes and the creator scene, and registers them in the build settings.</summary>
    public static class DemoSceneBuilder
    {
        const string SceneFolder = "Assets/Scenes";
        const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        [MenuItem("Procedural Movement/Build Demo Scenes")]
        public static void BuildAll()
        {
            var scenes = new List<string>
            {
                CreateScene(CreatureSession.SpiderScene, go => go.AddComponent<DemoBootstrap>().startCreature = CreaturePreset.Spider),
                CreateScene(CreatureSession.HumanScene, go => go.AddComponent<DemoBootstrap>().startCreature = CreaturePreset.Human),
                CreateScene(CreatureSession.HorseScene, go => go.AddComponent<DemoBootstrap>().startCreature = CreaturePreset.Horse),
                CreateScene(CreatureSession.CreatorScene, go => go.AddComponent<CreatureCreator>()),
            };
            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in scenes) list.Add(new EditorBuildSettingsScene(s, true));
            EditorBuildSettings.scenes = list.ToArray();
            EditorSceneManager.OpenScene(scenes[0]);
            Debug.Log("Procedural Movement: built " + scenes.Count + " scenes.");
        }

        static string CreateScene(string name, System.Action<GameObject> setup)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null)
            {
                var vol = new GameObject("Global Volume").AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }
            var root = new GameObject(name == CreatureSession.CreatorScene ? "Creator" : "Demo");
            setup(root);
            string path = $"{SceneFolder}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }
    }
}
