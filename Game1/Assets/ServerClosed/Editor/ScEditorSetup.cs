// SERVERCLOSED — Editor: автоматическая сборка игровой сцены одним пунктом меню.
// Unity Editor → меню "SERVERCLOSED" → «Создать сцену игры» — готово, жмите Play.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ServerClosed.Runtime
{
    public static class ScEditorSetup
    {
        [MenuItem("SERVERCLOSED/Создать сцену игры")]
        public static void CreateGameScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SERVERCLOSED");
            go.AddComponent<ScBootstrap>();
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/ServerClosed.unity");
            EditorUtility.DisplayDialog("SERVERCLOSED",
                "Сцена создана: Assets/Scenes/ServerClosed.unity\nНажмите Play — игра начнётся с родовой норки.",
                "Отлично");
        }
    }
}
#endif
