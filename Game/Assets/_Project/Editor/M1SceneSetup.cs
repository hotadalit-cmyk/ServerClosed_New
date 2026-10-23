using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ServerClosed.Editor
{
    /// <summary>
    /// Создаёт Bootstrap-сцену для M1
    /// </summary>
    public static class M1SceneSetup
    {
        [MenuItem("ServerClosed/M1: Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            // Создать новую сцену
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Создать Bootstrap GameObject
            GameObject bootstrapObj = new GameObject("Bootstrap");
            bootstrapObj.AddComponent<Presentation.Bootstrap>();

            // Создать Main Camera
            GameObject cameraObj = new GameObject("Main Camera");
            Camera cam = cameraObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cameraObj.tag = "MainCamera";

            // Сохранить сцену
            string scenePath = "Assets/_Project/Scenes/Bootstrap.unity";
            System.IO.Directory.CreateDirectory("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"[M1] Bootstrap scene created: {scenePath}");

            // Добавить в Build Settings
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };

            Debug.Log("[M1] Scene added to Build Settings");
        }
    }
}