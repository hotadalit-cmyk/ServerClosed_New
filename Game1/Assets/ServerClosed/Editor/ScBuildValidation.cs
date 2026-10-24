using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using ServerClosed.Runtime;

namespace ServerClosed.Validation
{
    public static class ScBuildValidation
    {
        public static string Output { get { return Environment.GetEnvironmentVariable("SC_VALIDATION_OUT") ?? Path.GetFullPath("Builds/ServerClosed"); } }
        public static void RunChecks()
        {
            Directory.CreateDirectory(Output);
            var report=new List<string>(); int failed=0;
            foreach(var name in ScRegressionChecks.Cases) {
                try { ScRegressionChecks.Run(name); report.Add("PASS "+name); }
                catch(Exception e) { failed++; report.Add("FAIL "+name+" "+e); }
            }
            File.WriteAllLines(Path.Combine(Output,"regression-results.txt"),report);
            if(failed>0) throw new Exception(failed+" regression checks failed; see report");
            Debug.Log("SERVERCLOSED: all "+report.Count+" regression checks passed.");
        }
        public static void Prepare()
        {
            // Create through Unity APIs; keep the imported scene GUID.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("SERVERCLOSED").AddComponent<ScBootstrap>();
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/ServerClosed.unity");
            var scenes=new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene("Assets/Scenes/ServerClosed.unity",true) };
            foreach(var s in EditorBuildSettings.scenes) if(s.path!="Assets/Scenes/ServerClosed.unity") scenes.Add(new EditorBuildSettingsScene(s.path,false));
            EditorBuildSettings.scenes=scenes.ToArray();
            AssetDatabase.SaveAssets();
        }
        public static void BuildWindows()
        {
            RunChecks(); Prepare();
            Directory.CreateDirectory(Output);
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/ServerClosed.unity"}, locationPathName=Path.Combine(Output,"Windows/ServerClosed.exe"), target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development });
            File.WriteAllText(Path.Combine(Output,"windows-build.txt"),result.summary.result+"\n"+result.summary.totalSize+" bytes\n"+result.summary.totalTime);
            if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Windows build failed");
        }
        public static void BuildAndroid()
        {
            RunChecks(); Prepare();
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion=(AndroidSdkVersions)36;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android,"com.serverclosed.newgame");
            PlayerSettings.Android.useCustomKeystore=false;
            EditorUserBuildSettings.buildAppBundle=false;
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{"Assets/Scenes/ServerClosed.unity"}, locationPathName=Path.Combine(Output,"ServerClosed-Android-arm64.apk"), target=BuildTarget.Android, options=BuildOptions.Development });
            File.WriteAllText(Path.Combine(Output,"android-build.txt"),result.summary.result+"\n"+result.summary.totalSize+" bytes\n"+result.summary.totalTime);
            if(result.summary.result!=BuildResult.Succeeded) throw new Exception("Android build failed");
        }
    }
}
