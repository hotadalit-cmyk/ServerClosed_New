using System;
using System.Collections.Generic;
using System.IO;
using ServerClosed.Core;
using ServerClosed.Runtime;

namespace ServerClosed.Validation
{
    public static class ScRegressionChecks
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static readonly string[] Cases = {
            "SeedDeterminism", "MissingSenseMultiplier", "PartialSenseMultiplier", "DigestionExactlyOnce",
            "NestCombatExit", "SaveRoundtrip", "RngContinuation", "DayCounterContinuation",
            "PausedWorld", "DeadBodyCommands", "ReincarnationKeepsWorld", "RepairInvalidPosition",
            "SaveFallback", "CorruptSaveRejected", "MalformedJsonRejected", "SprintSpeed", "VisionExploresMap", "FogHidesDistantEntities", "SimulationSoak"
        };
        static ScWorld World() { return ScWorld.NewGame(123456, 1); }
        public static void Run(string name)
        {
            var w = World();
            switch (name)
            {
                case "SeedDeterminism":
                    var second = World();
                    Check(ScJson.ToJson(w.data) == ScJson.ToJson(second.data), "Same seed produced different worlds");
                    break;
                case "MissingSenseMultiplier":
                    var s = new SensoryState();
                    ScSenses.GrantBiomass(s, 1, new Dictionary<string,double>(), null);
                    Check(s.unlocked.Length == 1, "One meal unlocked all senses");
                    break;
                case "PartialSenseMultiplier":
                    var partial = new SensoryState();
                    ScSenses.GrantBiomass(partial, 1, new Dictionary<string,double>{{"chemo",0.5}}, null);
                    Check(partial.unlocked.Length == 1, "Absent multipliers became zero");
                    ScSenses.NextGoal(partial, new Dictionary<string,double>(), out _, out _, out var need);
                    Check(need > 1, "HUD threshold lost");
                    break;
                case "DigestionExactlyOnce":
                    var d = new DigestiveState(); var finished = new List<StoredItem>();
                    ScDigestion.PutItem(d, new StoredItem { mass=5, hardness=1 });
                    ScDigestion.TickDigestive(d, 100, 1, 1, finished);
                    ScDigestion.TickDigestive(d, 100, 1, 1, finished);
                    Check(finished.Count == 1 && d.c0 == null, "Duplicate digestion completion");
                    break;
                case "NestCombatExit":
                    // Place existing nest prey within reach; exercise actual attack/absorb rules.
                    var prey = w.ActiveCreatures().Find(c=>c.species=="slug");
                    Check(prey != null, "No prey in birth nest");
                    prey.x=w.data.player.x+0.3f; prey.y=w.data.player.y;
                    w.data.creatures.RemoveAll(c=>c.dungeonId=="birth_nest" && c!=prey);
                    for(int i=0;i<6000 && !prey.dead;i++) {
                        double dx=prey.x-w.data.player.x, dy=prey.y-w.data.player.y, distance=Math.Sqrt(dx*dx+dy*dy);
                        w.SetInput(distance>0.6?dx/distance:0,distance>0.6?dy/distance:0,false);
                        if(distance<1.1) w.PlayerAttack(); w.Update(0.02,"explore");
                    }
                    w.SetInput(0,0,false);
                    Check(prey.dead, "Cannot kill starter prey");
                    w.data.player.x=prey.x; w.data.player.y=prey.y;
                    Check(w.AbsorbNearest(), "Cannot absorb corpse");
                    Check(!w.AbsorbNearest(), "Corpse absorbed twice");
                    for(int i=0;i<2000;i++) w.Update(0.02,"explore");
                    Check(w.data.player.biomass>0 && w.data.digestion.c0==null, "Digestion did not yield biomass");
                    Check(w.data.senses.unlocked.Length<7, "All senses unlocked from one slug");
                    w.data.senses.biomassTotal=8;
                    var floor=w.Dun.floors[0]; w.data.player.x=floor.upX; w.data.player.y=floor.upY;
                    Check(w.ExitBirthNest() && !w.DungeonActive && w.IsWalkable(w.data.player.x,w.data.player.y), "Invalid exit from nest");
                    break;
                case "SaveRoundtrip":
                    for(int i=0;i<10;i++) w.Update(0.02,"explore");
                    w.data.digestion.c0=new StoredItem { mass=7,hardness=1,progress=0.4,diet=new[]{1.0,1.0},genePool=new[]{"test"} };
                    w.CaptureState(); var json=ScJson.ToJson(w.data);
                    var restored=ScJson.FromJson<WorldData>(json);
                    Check(ScJson.ToJson(restored)==json, "Save data changed on roundtrip (terrain/exploration/digestion/RNG)");
                    ScWorld.FromData(restored).Update(0.02,"explore");
                    break;
                case "RngContinuation":
                    for(int i=0;i<97;i++) w.rng.Next(); w.CaptureState();
                    var reloaded=ScWorld.FromData(ScJson.FromJson<WorldData>(ScJson.ToJson(w.data)));
                    for(int i=0;i<100;i++) Check(w.rng.Next()==reloaded.rng.Next(), "Reload reset RNG stream");
                    break;
                case "DayCounterContinuation":
                    w.data.dayTicks=8; w.data.minutes=8*1440+420;
                    var loaded=ScWorld.FromData(w.data); loaded.Update(0.02,"explore");
                    Check(loaded.data.dayTicks==8, "Reload reset day tick counter");
                    break;
                case "PausedWorld":
                    w.SetInput(1,0,false); var before=ScJson.ToJson(w.data); w.Update(1,"pause");
                    Check(before==ScJson.ToJson(w.data), "Pause changed simulation");
                    w.Update(double.NaN,"explore"); w.Update(-1,"explore");
                    Check(before==ScJson.ToJson(w.data), "Invalid timestep changed state");
                    break;
                case "DeadBodyCommands":
                    w.data.deathCause="test"; var snapshot=ScJson.ToJson(w.data);
                    w.PlayerAttack(); Check(!w.AbsorbNearest(), "Dead body absorbed food"); w.Update(1,"explore");
                    Check(snapshot==ScJson.ToJson(w.data), "Dead body changed state"); break;
                case "ReincarnationKeepsWorld":
                    var data=w.data; var map=data.mapJson; var population=data.creatures.Find(c=>c.dungeonId==null);
                    data.minutes=8000; w.clock.minutes=8000; data.player.knownGenes.Add("memory");
                    data.deathCause="test"; int oldLife=data.lifeNo;
                    Check(w.Reincarnate(), "Reincarnation refused");
                    Check(ReferenceEquals(w.data,data) && ReferenceEquals(map,w.data.mapJson) && w.data.creatures.Contains(population), "World/population replaced");
                    Check(w.data.minutes==8000 && w.data.lifeNo==oldLife+1 && w.data.player.knownGenes.Contains("memory"), "World time or soul memory reset");
                    Check(!w.Reincarnate(), "Reincarnation applied twice"); break;
                case "RepairInvalidPosition":
                    w.data.player.x=float.NaN; w.data.player.y=float.PositiveInfinity;
                    Check(w.HealPlayerPosition() && w.IsWalkable(w.data.player.x,w.data.player.y), "Invalid nest position not repaired");
                    w.data.activeDungeon=null; w.data.player.x=-10000; w.data.player.y=-10000;
                    Check(w.HealPlayerPosition() && w.IsWalkable(w.data.player.x,w.data.player.y), "Invalid outdoor position not repaired"); break;
                case "SaveFallback":
                case "CorruptSaveRejected":
                    string previous=ScSave.DirectoryOverride;
                    var dir=Path.Combine(Path.GetTempPath(),"ServerClosed-validation-"+Guid.NewGuid().ToString("N"));
                    try {
                        ScSave.DirectoryOverride=dir; w.CaptureState();
                        Check(ScSave.Save(w.data), "First save failed: "+ScSave.LastError);
                        w.data.player.biomass=12; Check(ScSave.Save(w.data), "Second save failed");
                        File.WriteAllText(Path.Combine(dir,"sc_save_1.json"),"{broken");
                        if(name=="SaveFallback") { var fallback=ScSave.Load(); Check(fallback!=null && fallback.player.biomass==0, "Did not restore previous valid slot"); }
                        else { File.WriteAllText(Path.Combine(dir,"sc_save_0.json"),"{broken"); Check(ScSave.Load()==null && ScSave.HasSave() && ScSave.LastError!=null, "Corrupt save silently became new campaign"); }
                    } finally { ScSave.DirectoryOverride=previous; }
                    break;
                case "MalformedJsonRejected":
                    foreach(var invalid in new[]{"", "{", "{\"seed\":1", "{\"seed\":1}garbage", "[]", "{\"player\":7}"}) {
                        bool rejected=false; try { var bad=ScJson.FromJson<WorldData>(invalid); ScSave.Validate(bad); } catch(Exception) { rejected=true; }
                        Check(rejected, "Malformed JSON accepted: "+invalid);
                    } break;
                case "SprintSpeed":
                    var slow=World(); var fast=World(); slow.SetInput(0,1,false); fast.SetInput(0,1,true);
                    slow.Update(0.02,"explore"); fast.Update(0.02,"explore");
                    Check(fast.data.player.y-w.data.player.y>(slow.data.player.y-w.data.player.y)*1.3,"Sprint capped at walking speed"); break;
                case "VisionExploresMap":
                    w.Update(0.02,"explore"); int blind=w.Dun.explored[0].Count;
                    w.data.senses.unlocked=new[]{"touch","vision"}; w.Update(0.02,"explore");
                    Check(w.Dun.explored[0].Count>blind*2,"Vision did not reveal surrounding tiles"); break;
                case "FogHidesDistantEntities":
                    var distant=w.ActiveCreatures()[0]; distant.x=16; distant.y=10;
                    w.Update(0.02,"explore"); Check(!w.IsEntitySensed(distant.id),"Distant creature leaked through fog");
                    distant.x=w.data.player.x+0.4f; distant.y=w.data.player.y;
                    w.Update(0.02,"explore"); Check(w.IsEntitySensed(distant.id),"Nearby creature invisible to touch"); break;
                case "SimulationSoak":
                    for(int seed=1;seed<=12;seed++) {
                        var sim=ScWorld.NewGame(seed,1);
                        for(int frame=0;frame<3000;frame++) { sim.SetInput(Math.Sin(frame*0.01),Math.Cos(frame*0.01),false); sim.Update(0.02,frame%300<100?"fast":"explore"); }
                        Check(sim.IsWalkable(sim.data.player.x,sim.data.player.y),"Soak left walkable map");
                        sim.CaptureState(); ScSave.Validate(ScJson.FromJson<WorldData>(ScJson.ToJson(sim.data)));
                    } break;
                default: throw new ArgumentException(name);
            }
        }
    }
}
