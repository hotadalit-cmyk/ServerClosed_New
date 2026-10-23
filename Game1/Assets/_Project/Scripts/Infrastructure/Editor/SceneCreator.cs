using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace ServerClosed.Editor
{
    public static class SceneCreator
    {
        public static void CreateBootstrapScene()
        {
            // Создать новую сцену
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Удалить Main Camera (не нужна для 2D UI)
            var mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null) GameObject.DestroyImmediate(mainCamera);

            // Создать Canvas
            var canvasGO = new GameObject("Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1080, 1920);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Создать EventSystem
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Создать StatusText
            var textGO = new GameObject("StatusText");
            textGO.transform.SetParent(canvasGO.transform, false);

            var rectTransform = textGO.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(800, 400);

            var text = textGO.AddComponent<Text>();
            text.text = "ServerClosed M1\nИнициализация...";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            // Создать Bootstrap GameObject
            var bootstrapGO = new GameObject("Bootstrap");
            var startScreen = bootstrapGO.AddComponent<ServerClosed.Presentation.UI.StartScreen>();

            // Связать StatusText через Reflection (SerializedObject безопаснее, но требует Assembly)
            var field = typeof(ServerClosed.Presentation.UI.StartScreen).GetField("_statusText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(startScreen, text);
            }

            // Сохранить сцену
            string scenePath = "Assets/_Project/Scenes/Bootstrap.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"[SceneCreator] Bootstrap сцена создана: {scenePath}");
        }
    }
}
