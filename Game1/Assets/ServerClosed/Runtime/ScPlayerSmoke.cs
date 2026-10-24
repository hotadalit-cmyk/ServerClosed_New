using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ServerClosed.Runtime
{
    // Only activated by --sc-smoke; isolates QA from the user's actual saves.
    public class ScPlayerSmoke : MonoBehaviour
    {
        static string output;
        readonly List<string> errors = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                if (intent.Call<bool>("getBooleanExtra", "scSmoke", false)) args = new[] { "--sc-smoke", Path.Combine(Application.persistentDataPath, "Validation") };
#endif
            int index=Array.IndexOf(args,"--sc-smoke");
            if(index<0 || index+1>=args.Length) return;
            Application.runInBackground=true;
            output=args[index+1]; Directory.CreateDirectory(output);
            ScSave.DirectoryOverride=Path.Combine(output,"isolated-save-"+Guid.NewGuid().ToString("N"));
            var go=new GameObject("SC smoke validation"); DontDestroyOnLoad(go); go.AddComponent<ScPlayerSmoke>();
        }
        void OnEnable() { Application.logMessageReceived+=OnLog; }
        void OnDisable() { Application.logMessageReceived-=OnLog; }
        void OnLog(string condition,string trace,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(condition+"\n"+trace); }
        IEnumerator Start()
        {
            for(int i=0;i<150;i++) yield return null;
            var bootstrap=FindAnyObjectByType<ScBootstrap>();
            if(bootstrap==null || bootstrap.World==null) errors.Add("World failed to start");
            else {
                var w=bootstrap.World;
                if(Application.platform==RuntimePlatform.Android && !bootstrap.HudWasDrawn) errors.Add("HUD never entered OnGUI");
                if(!w.IsWalkable(w.data.player.x,w.data.player.y)) errors.Add("Player outside walkable area");
                if(FindAnyObjectByType<ScIsoView>()==null || Camera.main==null) errors.Add("View/camera missing");
                var view=FindAnyObjectByType<ScIsoView>();
                if(view != null && Vector3.Distance(view.RenderedHeroPosition, view.PosToWorld(w.data.player.x,w.data.player.y)+new Vector3(0,0.06f,0))>0.2f) errors.Add("Rendered slime is detached from the player position");
                if(Shader.Find("ServerClosed/VertexColor")==null) errors.Add("Shader stripped from player");
                w.CaptureState(); if(!ScSave.Save(w.data) || ScSave.Load()==null) errors.Add("Player save roundtrip failed: "+ScSave.LastError);
            }
            // Render offscreen as well: Windows can return a black backbuffer for a hidden window.
            if (Camera.main != null)
            {
                var camera=Camera.main;
                var previous=camera.targetTexture;
                var active=RenderTexture.active;
                var target=new RenderTexture(1280,720,24);
                var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
                try {
                    camera.targetTexture=target;
                    camera.Render();
                    RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,1280,720),0,0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(output,"world-render.png"),image.EncodeToPNG());
                    int visible=0;
                    foreach(var pixel in image.GetPixels32()) if(pixel.g>40 && pixel.g>pixel.r) visible++;
                    if(visible<20) errors.Add("World render has no visible green hero/terrain pixels");
                } finally {
                    camera.targetTexture=previous; RenderTexture.active=active;
                    target.Release(); Destroy(target); Destroy(image);
                }
            }
            // Android prepends persistentDataPath itself; an absolute path would be duplicated.
            ScreenCapture.CaptureScreenshot(Application.platform==RuntimePlatform.Android
                ? "Validation/game-screen.png" : Path.Combine(output,"game-screen.png"));
            for(int i=0;i<20;i++) yield return null;
            File.WriteAllText(Path.Combine(output,"player-smoke.txt"),errors.Count==0?"PASS: world, camera, view, shader, save roundtrip; 170 frames without errors.":string.Join("\n",errors));
            Application.Quit(errors.Count==0?0:1);
        }
    }
}
