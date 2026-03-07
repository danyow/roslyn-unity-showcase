using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Showcase.Editor
{
    public static class RegisterScenes
    {
        private static readonly string[] SceneNames =
        {
            "Main",
            "Level0", "Level1", "Level2", "Level3",
            "Level4", "Level5", "Level6", "Level7",
            "Chapter1", "Chapter2", "Chapter3", "Chapter4",
        };

        // Component type names for each scene's Demo GameObject
        private static readonly string[] DemoComponents =
        {
            null, // Main uses MainMenu instead
            "Showcase.Level0.Level0Demo",
            "Showcase.Level1.Level1Demo",
            "Showcase.Level2.Level2Demo",
            "Showcase.Level3.Level3Demo",
            "Showcase.Level4.Level4Demo",
            "Showcase.Level5.Level5Demo",
            "Showcase.Level6.Level6Demo",
            "Showcase.Level7.Level7Demo",
            "Showcase.HarmonyDemo.Chapter1.Chapter1Demo",
            "Showcase.HarmonyDemo.Chapter2.Chapter2Demo",
            "Showcase.HarmonyDemo.Chapter3.Chapter3Demo",
            "Showcase.HarmonyDemo.Chapter4.Chapter4Demo",
        };

        [MenuItem("Showcase/Create All Scenes")]
        public static void CreateAllScenes()
        {
            const string dir = "Assets/Scenes";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var buildScenes = new List<EditorBuildSettingsScene>();

            for (int i = 0; i < SceneNames.Length; i++)
            {
                var sceneName = SceneNames[i];
                var path = $"{dir}/{sceneName}.unity";

                // Create new empty scene
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                if (sceneName == "Main")
                {
                    // Main scene: one GameObject with MainMenu
                    var go = new GameObject("Main");
                    go.AddComponent<Showcase.MainMenu>();
                }
                else
                {
                    // Demo scene: one GameObject with Demo + BackButton
                    var go = new GameObject("Demo");
                    go.AddComponent<Showcase.BackButton>();

                    var compType = FindType(DemoComponents[i]);
                    if (compType != null)
                        go.AddComponent(compType);
                    else
                        Debug.LogWarning($"[RegisterScenes] Component not found: {DemoComponents[i]}");
                }

                EditorSceneManager.SaveScene(scene, path);
                buildScenes.Add(new EditorBuildSettingsScene(path, true));
                Debug.Log($"[RegisterScenes] Created scene: {path}");
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            Debug.Log($"[RegisterScenes] Registered {buildScenes.Count} scenes in Build Settings.");
        }

        private static System.Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
