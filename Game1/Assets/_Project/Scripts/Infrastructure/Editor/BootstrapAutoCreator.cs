using UnityEditor;

namespace ServerClosed.Editor
{
    [InitializeOnLoad]
    public static class BootstrapAutoCreator
    {
        private const string CREATED_KEY = "ServerClosed_BootstrapCreated";

        static BootstrapAutoCreator()
        {
            // Создать сцену только один раз при первом запуске Editor
            if (!EditorPrefs.GetBool(CREATED_KEY, false))
            {
                EditorApplication.delayCall += () =>
                {
                    SceneCreator.CreateBootstrapScene();
                    EditorPrefs.SetBool(CREATED_KEY, true);

                    // Добавить в Build Settings
                    var scenePath = "Assets/_Project/Scenes/Bootstrap.unity";
                    AddSceneToBuildSettings(scenePath);
                };
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            var sceneExists = false;

            foreach (var scene in scenes)
            {
                if (scene.path == scenePath)
                {
                    sceneExists = true;
                    break;
                }
            }

            if (!sceneExists)
            {
                var newScenes = new EditorBuildSettingsScene[scenes.Length + 1];
                System.Array.Copy(scenes, newScenes, scenes.Length);
                newScenes[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
                EditorBuildSettings.scenes = newScenes;

                UnityEngine.Debug.Log($"[BootstrapAutoCreator] Сцена добавлена в Build Settings: {scenePath}");
            }
        }

        [MenuItem("ServerClosed/M1/Reset Bootstrap Creation Flag")]
        private static void ResetFlag()
        {
            EditorPrefs.DeleteKey(CREATED_KEY);
            UnityEngine.Debug.Log("[BootstrapAutoCreator] Флаг сброшен. Перезапусти Unity для повторного создания.");
        }
    }
}
