using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using ServerClosed.Core;

namespace ServerClosed.Runtime
{
    // Development-only fixtures for interactive QA. Never touches normal player saves.
    public class ScReviewSession : MonoBehaviour
    {
        string folder, previousPhase;
        bool autoFlow;
        int errors;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!Debug.isDebugBuild) return;
            var args=Environment.GetCommandLineArgs();
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
            using(var intent=activity.Call<AndroidJavaObject>("getIntent"))
                if(intent.Call<bool>("getBooleanExtra","scFlow",false)) args=new[]{"--sc-review",Path.Combine(Application.persistentDataPath,"ReviewV13"),"--auto-flow"};
#endif
            int index=Array.IndexOf(args,"--sc-review");
            if(index<0 || index+1>=args.Length) return;
            var folder=args[index+1]; Directory.CreateDirectory(folder);
            Application.runInBackground=true; Application.targetFrameRate=60;
            ScSave.DirectoryOverride=folder;
            if(Array.IndexOf(args,"--auto-flow")>=0 || !ScSave.HasSave()) { var w=ScWorld.NewGame(123456,1); w.CaptureState(); ScSave.Save(w.data); }
            var go=new GameObject("SC interactive QA"); DontDestroyOnLoad(go);
            var review=go.AddComponent<ScReviewSession>(); review.folder=folder; review.autoFlow=Array.IndexOf(args,"--auto-flow")>=0;
        }
        void OnEnable() { Application.logMessageReceived+=Log; }
        void OnDisable() { Application.logMessageReceived-=Log; }
        void Log(string message,string trace,LogType type)
        {
            if(folder==null || (type!=LogType.Error && type!=LogType.Exception && type!=LogType.Assert)) return;
            errors++; File.AppendAllText(Path.Combine(folder,"review-errors.txt"),message+"\n"+trace+"\n");
        }
        void Update()
        {
            var app=FindAnyObjectByType<ScBootstrap>(); if(app==null || app.World==null) return;
            var w=app.World; var k=Keyboard.current;
            if(k!=null && k.f6Key.wasPressedThisFrame)
            {
                LeaveDungeon(w);
                var dar=w.Npc("dar"); w.data.player.x=dar.x; w.data.player.y=dar.y+0.6f;
                w.data.village.raidPending=true; w.data.village.raidDeadline=w.clock.TotalDays+2;
                w.data.senses.unlocked=new[]{"touch","chemo","vision"};
                w.data.creatures.RemoveAll(c=>c.dungeonId==null && ScRng.Dist(c.x,c.y,dar.x,dar.y)<6);
                w.HealPlayerPosition(); w.UpdateProximity();
            }
            if(k!=null && k.f7Key.wasPressedThisFrame)
            {
                LeaveDungeon(w); var gate=ScDungeon.Catacombs.gatePos(w.data);
                w.data.player.x=gate.x; w.data.player.y=gate.y;
                w.data.senses.unlocked=new[]{"touch","chemo","vision"};
                w.HealPlayerPosition(); w.UpdateProximity();
            }
            if(k!=null && k.f9Key.wasPressedThisFrame && w.DungeonActive)
            {
                var floor=w.Dun.floors[w.Dun.depth-1];
                w.data.player.x=floor.downX+0.5f; w.data.player.y=floor.downY+0.5f;
                w.UpdateProximity();
            }
            if(k!=null && k.f8Key.wasPressedThisFrame) w.KillPlayer("old_age","Проверка экрана суда");
            var phase=app.ReviewPhase;
            if(phase!=previousPhase)
            {
                previousPhase=phase;
                File.AppendAllText(Path.Combine(folder,"review-phases.txt"),phase+"\n");
                StartCoroutine(CaptureSettled(phase));
            }
            File.WriteAllText(Path.Combine(folder,"review-state.txt"),phase+"\nerrors="+errors+"\nlife="+w.data.lifeNo+"\ndungeon="+w.data.activeDungeon+"\n");
        }
        IEnumerator CaptureSettled(string phase)
        {
            yield return new WaitForSecondsRealtime(0.35f);
            var app=FindAnyObjectByType<ScBootstrap>();
            if(app!=null && app.ReviewPhase==phase) Capture(phase);
        }
        void Capture(string phase) {
            string name="phase-"+phase.Replace(':','-')+".png";
            ScreenCapture.CaptureScreenshot(Application.platform==RuntimePlatform.Android?"ReviewV13/"+name:Path.Combine(folder,name));
        }
        void Verify(bool condition,string message) { if(!condition) { errors++; File.AppendAllText(Path.Combine(folder,"review-errors.txt"),message+"\n"); } }
        IEnumerator Start()
        {
            if(!autoFlow) yield break;
            ScBootstrap app=null;
            for(int i=0;i<300 && (app==null || app.World==null);i++) { app=FindAnyObjectByType<ScBootstrap>(); yield return null; }
            if(app==null || app.World==null) { Verify(false,"World did not initialize"); Finish(); yield break; }
            var w=app.World; var originalMap=w.data.mapJson;
            yield return new WaitForSecondsRealtime(1);
            LeaveDungeon(w); var dar=w.Npc("dar");
            w.data.player.x=dar.x;w.data.player.y=dar.y+0.6f;
            w.data.village.raidPending=true;w.data.village.raidDeadline=w.clock.TotalDays+2;
            w.data.senses.unlocked=new[]{"touch","chemo","vision"};
            w.data.creatures.RemoveAll(c=>c.dungeonId==null && ScRng.Dist(c.x,c.y,dar.x,dar.y)<6);
            w.HealPlayerPosition();w.UpdateProximity();
            Verify(app.TalkToNearbyNpc(),"Cannot open NPC dialogue near Dar");
            yield return new WaitForSecondsRealtime(1);
            Verify(app.ChooseDialogueOption("start_raid"),"Cannot start defense from Dar's dialogue");
            yield return new WaitForSecondsRealtime(1);
            var chess=w.data.chess;
            Verify(chess!=null && chess.phase=="setup","Chess setup missing");
            if(chess==null) { Finish(); yield break; }
            for(int i=0;i<chess.playerRoster.Count && i<chess.reserveLimit;i++) Verify(app.PlaceDefenseUnit(chess.playerRoster[i].id,3,i),"Cannot place defense unit");
            Capture("chess-placed");
            yield return new WaitForSecondsRealtime(1);
            app.BeginDefenseBattle();
            yield return new WaitForSecondsRealtime(1);
            Time.timeScale=8;
            int frames=0; while(chess.phase=="battle" && frames++<1800) yield return null;
            Time.timeScale=1;
            Verify(chess.phase=="result" && chess.resultApplied,"Chess result missing or not applied");
            yield return new WaitForSecondsRealtime(1);
            app.CloseChess();
            var gate=ScDungeon.Catacombs.gatePos(w.data);w.data.player.x=gate.x;w.data.player.y=gate.y;
            w.HealPlayerPosition();w.UpdateProximity();
            yield return new WaitForSecondsRealtime(1);
            Verify(w.EnterDungeon("catacombs"),"Cannot enter Catacombs");
            for(int depth=1;depth<=3;depth++) {
                yield return new WaitForSecondsRealtime(1);
                Verify(w.DungeonActive && w.Dun.depth==depth,"Unexpected Catacombs depth");
                if(depth<3) { var f=w.Dun.floors[w.Dun.depth-1];w.data.player.x=f.downX+0.5f;w.data.player.y=f.downY+0.5f;Verify(w.DescendStairs(),"Cannot descend"); }
            }
            Verify(w.ActiveCreatures().Exists(c=>c.species=="myco_monarch"),"Catacombs boss missing");
            w.KillPlayer("old_age","Проверка экрана суда");
            yield return new WaitForSecondsRealtime(1);
            Verify(app.ReviewPhase=="judgment" && w.data.judged==1,"Judgment not applied");
            app.ContinueAfterJudgment();
            yield return new WaitForSecondsRealtime(1);
            Verify(app.ReviewPhase=="reincarnation","Body selection missing");
            app.ReincarnateWith(ScSoul.ReincarnationOptions(w.data.soul)[0].id);
            yield return new WaitForSecondsRealtime(1);
            Verify(w.data.lifeNo==2 && ReferenceEquals(originalMap,w.data.mapJson),"Reincarnation replaced world");
            w.CaptureState();Verify(ScSave.Save(w.data) && ScSave.Load()!=null,"Flow save/load failed");
            Finish();
        }
        void Finish() {
            Time.timeScale=1;
            File.WriteAllText(Path.Combine(folder,"flow-result.txt"),errors==0?"PASS: Dar dialogue, defense setup/battle/result, Catacombs floors 1-3 and boss, judgment, body selection, reincarnation, save/load.":"FAIL errors="+errors);
            Application.Quit(errors==0?0:1);
        }

        static void LeaveDungeon(ScWorld w)
        {
            if(w.data.activeDungeon=="birth_nest") {
                w.data.senses.biomassTotal=8; var f=w.Dun.floors[0];
                w.data.player.x=f.upX;w.data.player.y=f.upY;w.ExitBirthNest();
            }
            else for(int i=0;i<4 && w.DungeonActive;i++) {
                var f=w.Dun.floors[w.Dun.depth-1]; w.data.player.x=f.upX+0.5f;w.data.player.y=f.upY+0.5f;
                if(!w.AscendStairs()) break;
            }
            w.SetInput(0,0,false);
        }
        void OnGUI() {
            GUI.matrix=Matrix4x4.identity;
            GUI.Label(new Rect(10,Screen.height-55,900,24),"QA: F6 — Дар/набег · F7 — врата Катакомб · F9 — лестница вниз · F8 — суд");
        }
    }
}
