using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ServerClosed.Editor
{
    /// <summary>
    /// Помощник для создания сцены Bootstrap M1
    /// </summary>
    public static class M1SceneSetup
    {
        [MenuItem("ServerClosed/M1/Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            // Создать Canvas
            var canvasGO = new GameObject("Canvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // EventSystem
            var eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Текст статуса (крупный, читаемый)
            var textGO = new GameObject("StatusText");
            textGO.transform.SetParent(canvasGO.transform, false);
            var rectTransform = textGO.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(800, 400);
            rectTransform.anchoredPosition = Vector2.zero;

            var text = textGO.AddComponent<Text>();
            text.text = "ServerClosed M1\nИнициализация...";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            // Bootstrap GameObject
            var bootstrapGO = new GameObject("Bootstrap");
            bootstrapGO.AddComponent<Presentation.UI.StartScreen>();

            // Назначить ссылку на текст
            var startScreen = bootstrapGO.GetComponent<Presentation.UI.StartScreen>();
            var serializedObject = new SerializedObject(startScreen);
            serializedObject.FindProperty("_statusText").objectReferenceValue = text;
            serializedObject.ApplyModifiedProperties();

            Debug.Log("[M1SceneSetup] Bootstrap сцена создана. Сохрани как Assets/_Project/Scenes/Bootstrap.unity");
        }
    }
}
