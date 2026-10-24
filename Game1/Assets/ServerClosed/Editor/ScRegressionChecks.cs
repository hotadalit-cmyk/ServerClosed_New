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
            "SaveFallback", "CorruptSaveRejected", "MalformedJsonRejected", "SprintSpeed", "VisionExploresMap", "FogHidesDistantEntities", "SimulationSoak",
            "SoulNewGameDefaults", "SoulLevelThresholds", "RegionWeightFormula", "RegionRollDeterminism",
            "JudgmentAppliesOnce", "JudgmentOldAgeUnlocks", "ReincarnationRegionBirth", "SoulSaveRoundtrip",
            "RegionHazards", "WorldAdvanceAfterDeath", "OldSaveMigration"
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
                    // Новый поток (порт engine.ts reincarnate): мир/часы/популяция живут, время уходит вперёд,
                    // суд применён к душе один раз, записи ведомости стёрты, регион рождения применён.
                    var data=w.data; var map=data.mapJson; var population=data.creatures.Find(c=>c.dungeonId==null && !c.dead);
                    data.minutes=8000; w.clock.minutes=8000; data.player.knownGenes.Add("memory");
                    int oldLife=data.lifeNo; int oldSoulLife=data.soul.life;
                    w.KillPlayer("killed","тестовая смерть");
                    Check(data.judged==1, "Суд не применён в момент смерти");
                    Check(!string.IsNullOrEmpty(data.birthRegionNext), "Регион рождения не разыгран при суде");
                    Check(w.Reincarnate(), "Reincarnation refused");
                    Check(ReferenceEquals(w.data,data) && ReferenceEquals(map,w.data.mapJson) && w.data.creatures.Contains(population), "World/population replaced");
                    Check(w.data.minutes>8000 && w.data.lifeNo==oldLife+1 && data.soul.life==oldSoulLife+1, "World time or soul life not advanced");
                    Check(data.soul.pastLives.Count==1, "Past life record missing");
                    Check(data.karmaEvents.Count==0, "Karma ledger not cleared after judgment");
                    Check(ScRegions.Region(data.birthRegion).id==data.birthRegion && data.soul.birthRegion==data.birthRegion, "Region of birth not applied");
                    Check(w.data.player.biomass==8*data.soul.level, "Biomass legacy from soul level missing");
                    Check(w.data.player.knownGenes.Count==Math.Max(0,Math.Min(2,data.soul.level-1)), "Gene carry rule (§11.3) broken");
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
                // ===== v1.2: душа, суд, регионы =====
                case "SoulNewGameDefaults":
                    var soul=w.data.soul;
                    Check(soul.life==1 && soul.xp==0 && soul.level==1 && Math.Abs(soul.karma)<1e-9, "Soul start values wrong");
                    Check(Math.Abs(soul.traits.resilience-25)<1e-9 && Math.Abs(soul.traits.depth-5)<1e-9 && Math.Abs(soul.traits.predator-5)<1e-9 && Math.Abs(soul.traits.memory)<1e-9, "Default traits wrong");
                    Check(soul.memories.Count==6 && soul.memories[0].id=="m_first" && soul.memories[0].unlocked, "Memories wrong");
                    for(int i=1;i<6;i++) Check(!soul.memories[i].unlocked, "Locked memory unlocked at start");
                    Check(w.data.achievements.Count==16 && w.data.achievements.TrueForAll(a=>!a.got), "Achievements wrong");
                    Check(soul.birthRegion=="quiet_cradle" && w.data.birthRegion=="quiet_cradle", "Birth region wrong");
                    Check(w.data.judged==0 && w.data.deathCause==null, "Death flags set on new game");
                    break;
                case "SoulLevelThresholds":
                    Check(ScSoul.SoulLevelOf(0)==1 && ScSoul.SoulLevelOf(59)==1 && ScSoul.SoulLevelOf(60)==2, "Level 2 threshold wrong");
                    Check(ScSoul.SoulLevelOf(150)==3 && ScSoul.SoulLevelOf(300)==4 && ScSoul.SoulLevelOf(520)==5 && ScSoul.SoulLevelOf(9999)==5, "Level thresholds wrong");
                    Check(ScSoul.SOUL_LEVELS.Length==5, "SOUL_LEVELS size changed");
                    break;
                case "RegionWeightFormula": {
                    // Ручной расчёт по BirthWeights для дефолтной души (25/5/5/0) и стартового посёлка (food=12<20):
                    // cradle 26+0+17.1-0.3-8=34.8 · garden 20+7+0+0.5=27.5 · narrows 8+2.1+0+3=13.1
                    // vein 5+7+0.9=12.9 · rift 1.5+1.6=3.1
                    var t=new SoulTraits{resilience=25,depth=5,predator=5,memory=0};
                    var w2=ScRegions.BirthWeights(t, 0, w.data);
                    Check(Math.Abs(w2["quiet_cradle"]-34.8)<1e-9, "quiet_cradle weight drifted: "+w2["quiet_cradle"]);
                    Check(Math.Abs(w2["root_garden"]-27.5)<1e-9, "root_garden weight drifted: "+w2["root_garden"]);
                    Check(Math.Abs(w2["bone_narrows"]-13.1)<1e-9, "bone_narrows weight drifted: "+w2["bone_narrows"]);
                    Check(Math.Abs(w2["forgotten_vein"]-12.9)<1e-9, "forgotten_vein weight drifted: "+w2["forgotten_vein"]);
                    Check(Math.Abs(w2["soul_rift"]-3.1)<1e-9, "soul_rift weight drifted: "+w2["soul_rift"]);
                    // Мир реагирует: сытый посёлок убирает штраф колыбели
                    w.data.village.food=40; w.data.village.stability=70;
                    var w3=ScRegions.BirthWeights(t, 0, w.data);
                    Check(Math.Abs(w3["quiet_cradle"]-(26+0+(100-5)*0.18-5*0.06))<1e-9, "Village state ignored by weights");
                    // Глубина ≥50 открывает Разлом бонусом
                    var deep=new SoulTraits{resilience=25,depth=50,predator=5,memory=0};
                    var w4=ScRegions.BirthWeights(deep, 0, w.data);
                    Check(Math.Abs(w4["soul_rift"]-(1.5+50*0.32+0+8))<1e-9, "Deep-soul rift bonus missing");
                    break; }
                case "RegionRollDeterminism": {
                    var t=new SoulTraits{resilience=25,depth=5,predator=5,memory=0};
                    var w2=ScRegions.BirthWeights(t, 0, w.data); // total 91.4 при голодном посёлке
                    Check(ScRegions.RollRegion(w2,0.0)=="quiet_cradle", "roll 0 → not cradle");
                    Check(ScRegions.RollRegion(w2,0.5)=="root_garden", "roll 0.5 → not garden");
                    Check(ScRegions.RollRegion(w2,0.75)=="bone_narrows", "roll 0.75 → not narrows");
                    Check(ScRegions.RollRegion(w2,0.9)=="forgotten_vein", "roll 0.9 → not vein");
                    Check(ScRegions.RollRegion(w2,0.99)=="soul_rift", "roll 0.99 → not rift");
                    var odds=ScRegions.NormalizeWeights(w2);
                    Check(odds.Count==5 && odds[0].pct>=odds[4].pct, "Odds not sorted");
                    int pctSum=0; foreach(var o in odds) pctSum+=o.pct;
                    Check(pctSum>=98 && pctSum<=102, "Odds pct sum drifted: "+pctSum);
                    break; }
                case "JudgmentAppliesOnce": {
                    // Поглощение → убийство → суд: опыт/достижения/черты применяются ровно один раз
                    var aprey=w.ActiveCreatures().Find(c=>c.species=="slug");
                    aprey.x=w.data.player.x+0.3f; aprey.y=w.data.player.y;
                    w.data.creatures.RemoveAll(c=>c.dungeonId=="birth_nest" && c!=aprey);
                    for(int i=0;i<6000 && !aprey.dead;i++){ double dx=aprey.x-w.data.player.x, dy=aprey.y-w.data.player.y, dist=Math.Sqrt(dx*dx+dy*dy); w.SetInput(dist>0.6?dx/dist:0,dist>0.6?dy/dist:0,false); if(dist<1.1) w.PlayerAttack(); w.Update(0.02,"explore"); }
                    w.data.player.x=aprey.x; w.data.player.y=aprey.y;
                    Check(w.AbsorbNearest(), "Cannot absorb for judgment");
                    int karmaEventsBefore=w.data.karmaEvents.Count;
                    Check(karmaEventsBefore>0, "Absorb event missing");
                    w.KillPlayer("killed","тест");
                    Check(w.data.judged==1, "Judgment not applied");
                    var asoul=w.data.soul;
                    int xpAfterJudge=asoul.xp; int lifeAfterJudge=asoul.life;
                    Check(xpAfterJudge>=8, "first_absorb xp missing, got "+xpAfterJudge);
                    Check(w.data.achievements.Find(a=>a.id=="first_absorb").got, "first_absorb not marked");
                    Check(Math.Abs(asoul.traits.predator-5)<24.5 && asoul.traits.predator>=0, "Traits not updated");
                    var j2=w.RunJudgment();
                    Check(j2==w.lastJudgment && asoul.xp==xpAfterJudge && asoul.life==lifeAfterJudge, "Judgment applied twice");
                    // раундtrip с судом не теряет душу
                    w.CaptureState(); var ajson=ScJson.ToJson(w.data);
                    var arestored=ScJson.FromJson<WorldData>(ajson);
                    Check(ScJson.ToJson(arestored)==ajson, "Soul changed on roundtrip after judgment");
                    break; }
                case "JudgmentOldAgeUnlocks": {
                    w.data.player.ageDays=40;
                    w.KillPlayer("old_age","старость");
                    var osoul=w.data.soul;
                    Check(w.data.achievements.Find(a=>a.id=="old_age").got, "old_age achievement missing");
                    Check(osoul.memories.Find(m=>m.id=="m_age").unlocked, "m_age memory locked");
                    Check(Math.Abs(osoul.traits.resilience-30)<1e-9, "Resilience +5 for old age missing, got "+osoul.traits.resilience);
                    Check(osoul.stats.integrity==12, "Integrity +2 for old age missing, got "+osoul.stats.integrity);
                    Check(osoul.xp==25+6, "old_age xp wrong: "+osoul.xp); // достижение 25 + min(6, floor(40/4))
                    Check(osoul.pastLives[0].cause=="угасание", "Past life cause text wrong");
                    var j=w.JudgmentView();
                    Check(j.verdict=="Равновесие" && j.newSoulLevel==1, "Old age verdict wrong");
                    break; }
                case "ReincarnationRegionBirth": {
                    w.KillPlayer("old_age","тест региона");
                    string next=w.data.birthRegionNext;
                    Check(ScRegions.Region(next).id==next, "Rolled region unknown: "+next);
                    double minutesBefore=w.data.minutes;
                    Check(w.Reincarnate(), "Reincarnation refused");
                    Check(w.data.birthRegion==next, "Birth region not applied");
                    Check(w.data.minutes>minutesBefore, "Time skip missing");
                    Check(w.DungeonActive && w.data.activeDungeon=="birth_nest", "New life not in birth nest");
                    Check(w.data.creatures.TrueForAll(c=>!c.dead), "Corpses survived reincarnation");
                    // гарантированная первая пища региона ждёт снаружи
                    var plan=ScRegions.Region(next).spawnPlan;
                    foreach(var row in plan)
                        Check(w.data.creatures.Exists(c=>c.species==row[0]), "Region spawn plan missing "+row[0]+" for "+next);
                    Check(w.data.village.raidCdDays>=0 && w.data.judged==0 && w.data.deathCause==null, "Post-reincarnation flags wrong");
                    break; }
                case "SoulSaveRoundtrip": {
                    w.KillPlayer("killed","тест");
                    w.Reincarnate();
                    // вторая жизнь: поглощение, смерть, суд — потом полный раундtrip
                    var sprey=w.ActiveCreatures().Find(c=>c.species=="slug");
                    if(sprey!=null){ sprey.x=w.data.player.x+0.3f; sprey.y=w.data.player.y;
                        for(int i=0;i<4000 && !sprey.dead;i++){ double dx=sprey.x-w.data.player.x, dy=sprey.y-w.data.player.y, dist=Math.Sqrt(dx*dx+dy*dy); w.SetInput(dist>0.6?dx/dist:0,dist>0.6?dy/dist:0,false); if(dist<1.1) w.PlayerAttack(); w.Update(0.02,"explore"); } }
                    w.KillPlayer("old_age","старость");
                    w.CaptureState(); var sjson=ScJson.ToJson(w.data);
                    var srestored=ScJson.FromJson<WorldData>(sjson);
                    Check(ScJson.ToJson(srestored)==sjson, "Soul world changed on roundtrip");
                    var r2=ScWorld.FromData(srestored);
                    Check(r2.data.soul.xp==w.data.soul.xp && r2.data.soul.life==w.data.soul.life, "Soul lost on reload");
                    Check(r2.data.soul.pastLives.Count==w.data.soul.pastLives.Count, "Past lives lost on reload");
                    Check(r2.data.judged==1 && r2.JudgmentView()!=null, "Judgment view lost on reload mid-death");
                    Check(r2.data.birthRegionNext==w.data.birthRegionNext, "Rolled region lost on reload");
                    break; }
                case "RegionHazards": {
                    // Опасности тикают по реальному времени (Update клампит dt до 0.1): шаг 0.05 × 200 = 10 с.
                    // Регенерация подмешивается — сравниваем с контрольным миром без яда.
                    Func<ScWorld,Poi,Vec> nearIn=(world,poi)=>{ for(int i=0;i<40;i++){ double a=world.rng.Next()*Math.PI*2, r=world.rng.Range(0,1.2); double x=poi.x+Math.Cos(a)*r, y=poi.y+Math.Sin(a)*r; if(world.IsWalkable(x,y)) return Vec.Of(x,y);} return Vec.Of(poi.x,poi.y); };
                    var sick=World(); sick.data.activeDungeon=null; sick.data.birthRegion="root_garden";
                    var grove=sick.data.Poi("mushroom_grove"); var sp=nearIn(sick,grove);
                    sick.data.player.x=sp.x; sick.data.player.y=sp.y;
                    double hp0=sick.data.player.hp;
                    for(int i=0;i<200;i++) sick.Update(0.05,"explore");
                    var calm=World(); calm.data.activeDungeon=null; calm.data.birthRegion="quiet_cradle";
                    for(int i=0;i<200;i++) calm.Update(0.05,"explore");
                    Check(sick.data.player.hp<hp0+ (calm.data.player.hp-calm.data.player.hp) - 0.5, "Poison spores did no damage");
                    var safe=World(); safe.data.activeDungeon=null; safe.data.birthRegion="root_garden";
                    safe.data.player.genome.installed.Add("spore_cloud"); safe.RefreshStats();
                    var g2=safe.data.Poi("mushroom_grove"); var sp2=nearIn(safe,g2);
                    safe.data.player.x=sp2.x; safe.data.player.y=sp2.y;
                    double h0=safe.data.player.hp;
                    for(int i=0;i<200;i++) safe.Update(0.05,"explore");
                    var calm2=World(); calm2.data.activeDungeon=null; calm2.data.birthRegion="quiet_cradle";
                    for(int i=0;i<200;i++) calm2.Update(0.05,"explore");
                    Check(safe.data.player.hp-h0 >= calm2.data.player.hp-calm2.data.player.hp - 0.5, "Protected body poisoned");
                    // Буря Разлома: три пережитые бури ставят флаг глубины души (≈90 с реального времени)
                    var rift=World(); rift.data.activeDungeon=null; rift.data.birthRegion="soul_rift";
                    var rp=rift.data.Poi("rift"); var sp3=nearIn(rift,rp);
                    rift.data.player.x=sp3.x; rift.data.player.y=sp3.y;
                    for(int i=0;i<2400 && rift.data.riftStormSurvived==0;i++) rift.Update(0.05,"explore");
                    Check(rift.data.riftStormSurvived==1, "Storm survival flag missing");
                    Check(rift.data.deathCause==null, "Storm killed a healthy body too fast");
                    break; }
                case "WorldAdvanceAfterDeath": {
                    var adv=w.data;
                    adv.village.food=50; adv.village.stability=50; adv.village.population=10;
                    int foodBefore=(int)adv.village.food; int stabBefore=(int)adv.village.stability;
                    ScSoul.AdvanceWorldAfterDeath(adv, "Свет", "old_age", 48, 100);
                    Check(adv.village.food>foodBefore, "Village food did not grow");
                    Check(Math.Abs(adv.village.stability-(stabBefore+4))<1e-9, "Stability +2 per season wrong");
                    Check(adv.village.raidCdDays>=4, "Light verdict raid respite missing");
                    ScSoul.AdvanceWorldAfterDeath(adv, "Тьма", "killed", 48, 100);
                    Check(Math.Abs(adv.village.stability-(stabBefore+4-4))<1e-9, "Dark verdict stability penalty missing");
                    break; }
                case "OldSaveMigration": {
                    // Сейв v1.1: ключей soul/achievements нет → JSON с null → поля остаются пустыми → миграция
                    w.CaptureState();
                    var stripped=ScJson.FromJson<WorldData>(ScJson.ToJson(w.data));
                    stripped.soul=null; stripped.achievements=null; stripped.karmaTotal=-1; stripped.judged=7;
                    string old=ScJson.ToJson(stripped);
                    Check(!old.Contains("\"soul\":{"), "v1.1 payload still carries soul state");
                    var mloaded=ScJson.FromJson<WorldData>(old);
                    ScSave.Validate(mloaded);
                    ScSoul.EnsureMigration(mloaded);
                    Check(mloaded.soul!=null && mloaded.soul.memories.Count==6 && mloaded.soul.memories[0].unlocked, "Migrated soul missing memories");
                    Check(mloaded.soul.life==Math.Max(1,w.data.lifeNo), "Migrated soul lost life number");
                    Check(mloaded.achievements!=null && mloaded.achievements.Count==16, "Migrated achievements missing");
                    Check(ScJson.ToJson(ScWorld.FromData(mloaded).data)==ScJson.ToJson(ScWorld.FromData(ScJson.FromJson<WorldData>(old)).data), "Migration not deterministic");
                    // пустой каркас WorldData (виртуальный «дистиллят v1.1») тоже мигрирует
                    var bare=new WorldData();
                    ScSoul.EnsureMigration(bare);
                    Check(bare.soul.memories.Count==6 && bare.achievements.Count==16, "Bare WorldData not migrated");
                    break; }
                default: throw new ArgumentException(name);
            }
        }
    }
}
