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
            "RegionHazards", "WorldAdvanceAfterDeath", "OldSaveMigration",
            // v1.3: полная игра
            "QuestAcceptTurnIn", "QuestRequiresChain", "QuestRetroactive", "QuestDonateProgress",
            "SeasonRotationMods", "SeasonVillageFoodDelta", "SeasonMigrationsMove", "SeasonNeutralInDungeon",
            "VillageStageEconomy", "RaidCycleIgnored", "RaidDefenseChess", "ChessRulesSetup",
            "NpcDialogueOptions", "DonateStealFood", "NpcSuccession",
            "DungeonCatacombsPort", "DungeonStairsCache", "AtlasDiscoveryFlow", "CrackSmallBodyOnly",
            "KarmaPathThresholds", "WorldV13Roundtrip", "JudgmentBodyChoice",
            "ChessPlacementBounds", "ChessResultOnce", "ChessSaveContinuation", "ChessSetupDeterminism"
        };
        static ScWorld World() { return ScWorld.NewGame(123456, 1); }
        static ScWorld World(int seed) { return ScWorld.NewGame(seed, 1); }

        /// Вывести мир на поверхность у посёлка (выход из норки по правилам гейта роста).
        static void Surface(ScWorld w)
        {
            w.data.senses.biomassTotal = 8;   // гейт: усвоено ≥8 биомассы
            var f = w.Dun.floors[0];
            w.data.player.x = f.upX; w.data.player.y = f.upY;
            Check(w.ExitBirthNest(), "Surface helper: exit refused");
        }

        static void Warp(ScWorld w, double x, double y)
        {
            var p = w.data.player;
            p.x = (float)x; p.y = (float)y; p.vx = 0; p.vy = 0;
            if (!w.IsWalkable(p.x, p.y)) w.HealPlayerPosition();
        }

        /// Прокрутить один дневной тик (смерть и переход сезона — как в кадрах).
        static void TickDay(ScWorld w) { w.AdvanceDaysForTests(1); }
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
                    // Ручной расчёт по BirthWeights для дефолтной души (25/5/5/0) и стартового посёлка v1.3
                    // (food=40, stability=60 — сытый посёлок, без штрафа −8, как в вебе при newGame):
                    // cradle 26+0+17.1-0.3=42.8 · garden 20+7+0+0.5=27.5 · narrows 8+2.1+0+3=13.1
                    // vein 5+7+0.9=12.9 · rift 1.5+1.6=3.1
                    var t=new SoulTraits{resilience=25,depth=5,predator=5,memory=0};
                    var w2=ScRegions.BirthWeights(t, 0, w.data);
                    Check(Math.Abs(w2["quiet_cradle"]-42.8)<1e-9, "quiet_cradle weight drifted: "+w2["quiet_cradle"]);
                    Check(Math.Abs(w2["root_garden"]-27.5)<1e-9, "root_garden weight drifted: "+w2["root_garden"]);
                    Check(Math.Abs(w2["bone_narrows"]-13.1)<1e-9, "bone_narrows weight drifted: "+w2["bone_narrows"]);
                    Check(Math.Abs(w2["forgotten_vein"]-12.9)<1e-9, "forgotten_vein weight drifted: "+w2["forgotten_vein"]);
                    Check(Math.Abs(w2["soul_rift"]-3.1)<1e-9, "soul_rift weight drifted: "+w2["soul_rift"]);
                    // Мир реагирует: голодный посёлок штрафует колыбель (−8)
                    w.data.village.food=12; w.data.village.stability=70;
                    var w3=ScRegions.BirthWeights(t, 0, w.data);
                    Check(Math.Abs(w3["quiet_cradle"]-(26+0+(100-5)*0.18-5*0.06-8))<1e-9, "Village state ignored by weights");
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
                // ===== v1.3: полная игра =====
                case "QuestAcceptTurnIn": {
                    Surface(w);
                    // задание «Три шкуры»: приём, ретро-прогресс, сдача
                    Check(w.AcceptQuest("wolves"), "wolves quest refused");
                    Check(!w.AcceptQuest("wolves"), "wolves quest accepted twice");
                    var wolfC = w.data.creatures.Find(c => c.species == "wolf" && !c.dead);
                    Check(wolfC != null, "No wolf in world");
                    w.KillCreature(wolfC, "player");
                    w.data.SetFlag("quest_progress_wolves", 3);   // ещё две добычи — прямо в счётчик
                    Check(ScQuests.Status(w.data, ScQuests.ById("wolves")) == QuestStatus.Ready, "wolves not ready after 3 kills");
                    double biomassBefore = w.data.player.biomass;
                    double karmaBefore = w.data.karma;
                    Check(w.TurnInQuest("wolves"), "wolves turn-in refused");
                    Check(Math.Abs(w.data.player.biomass - (biomassBefore + 25)) < 1e-9, "wolves reward biomass wrong");
                    Check(w.data.karma > karmaBefore, "wolves reward karma missing");
                    Check(ScQuests.Done(w.data, "wolves") && w.data.wolfQuestDone == 1, "wolves done flags missing");
                    Check(!w.TurnInQuest("wolves"), "wolves turned in twice");
                    // прогресс пишет именно KillCreature: следующий квест-волк тоже считается
                    Check(ScQuests.CompletedCount(w.data) == 1, "Completed count wrong");
                    break; }
                case "QuestRequiresChain": {
                    Surface(w);
                    Check(!w.AcceptQuest("monarch"), "monarch accepted without descend");
                    Check(!w.AcceptQuest("shards"), "shards accepted without wolves");
                    Check(w.AcceptQuest("descend"), "descend refused");
                    Check(!w.AcceptQuest("monarch"), "monarch accepted before descend done");
                    w.data.SetFlag("quest_progress_descend", 1);   // спуск уже был — мир помнит
                    w.TurnInQuest("descend");
                    Check(w.AcceptQuest("monarch"), "monarch refused after descend");
                    w.data.SetFlag("quest_done_wolves", 1);
                    Check(w.AcceptQuest("shards"), "shards refused after wolves done");
                    break; }
                case "QuestRetroactive": {
                    Surface(w);
                    // мир помнит подвиги: убитый вожак, вошедшее подземелье, убитые волки
                    var wolfC = w.data.creatures.Find(c => c.species == "wolf" && !c.dead);
                    w.KillCreature(wolfC, "player");
                    w.data.flagWolvesKilled = 3;
                    Check(w.AcceptQuest("wolves") && ScQuests.Progress(w.data, "wolves") == 3, "wolf kills not counted retroactively");
                    Check(w.AcceptQuest("descend"), "descend accept failed");
                    w.data.SetFlag("quest_progress_descend", 1);   // спуск уже был
                    w.TurnInQuest("descend");
                    w.data.monarchKilled = 1;   // босс был повержен ДО принятия (и поле, и флаг — как пишет KillCreature)
                    w.data.SetFlag("monarchKilled", 1);
                    Check(w.AcceptQuest("monarch"), "monarch accept failed");
                    Check(ScQuests.Progress(w.data, "monarch") == 1, "boss kill not counted retroactively");
                    break; }
                case "QuestDonateProgress": {
                    Surface(w);
                    w.data.player.biomass = 50;
                    Check(w.AcceptQuest("harvest"), "harvest refused");
                    Check(w.DonateFood() && w.DonateFood(), "donations refused");
                    Check(ScQuests.Progress(w.data, "harvest") == 2, "donate progress wrong");
                    w.data.player.biomass = 10;
                    w.DonateFood();
                    Check(ScQuests.Progress(w.data, "harvest") == 3, "donate progress not complete");
                    Check(w.TurnInQuest("harvest"), "harvest turn-in failed");
                    Check(ScQuests.CompletedCount(w.data) == 1, "harvest not counted");
                    break; }
                case "SeasonRotationMods": {
                    Check(ScSeasons.SeasonOf(0) == "Пробуждение" && ScSeasons.SeasonOf(5.9) == "Пробуждение", "Season 1 wrong");
                    Check(ScSeasons.SeasonOf(6) == "Цветение" && ScSeasons.SeasonOf(12) == "Увядание" && ScSeasons.SeasonOf(18) == "Тьма", "Season order wrong");
                    Check(ScSeasons.SeasonOf(24) == "Пробуждение", "Year wrap wrong");
                    Check(Math.Abs(ScSeasons.Darkness.birthMul - 0.25) < 1e-9 && Math.Abs(ScSeasons.Darkness.aggressionAdd - 0.2) < 1e-9, "Darkness mods wrong");
                    Check(Math.Abs(ScSeasons.Blossom.villageFoodDelta - 2) < 1e-9 && Math.Abs(ScSeasons.Withering.dietMul - 1.3) < 1e-9, "Season economy mods wrong");
                    Check(!string.IsNullOrEmpty(ScSeasons.SeasonLog("Тьма")) && ScSeasons.MigrationsFor("Тьма").Count == 6, "Darkness log/migrations wrong");
                    Check(ScSeasons.MigrationsFor("Цветение").Count == 0, "Blossom should not migrate");
                    Check(Math.Abs(ScSeasons.SeasonalSpawnBoost("mushling", "Увядание") - 2.0) < 1e-9 && Math.Abs(ScSeasons.SeasonalSpawnBoost("wolf", "Цветение") - 1) < 1e-9, "Seasonal spawn boost wrong");
                    break; }
                case "SeasonVillageFoodDelta": {
                    w = World(424242);   // свой сид: редкий бросок базовой смертности не должен ломать экономику теста
                    Surface(w);
                    // ставим мир в Цветение (день 6..11) и Тьму (18..23): формулы еды посёлка
                    w.clock.minutes = 6 * 1440.0; w.data.minutes = w.clock.minutes;   // первый день Цветения
                    w.data.village.food = 40; w.data.village.population = 11;
                    TickDay(w);
                    Check(w.data.deathCause == null, "Player died from baseline mortality in test (seed 424242)");
                    // food = 40 + 3 + stage(11→Деревня=1) − ceil(11*0.4)=5 + Цветение(+2) = 41
                    Check(Math.Abs(w.data.village.food - 41) < 1e-9, "Blossom food delta wrong: " + w.data.village.food);
                    w.clock.minutes = 18 * 1440.0; w.data.minutes = w.clock.minutes;   // Тьма
                    TickDay(w);
                    // 41 + 3 + 1 − 5 − 2 = 38 (Тьма −2)
                    Check(Math.Abs(w.data.village.food - 38) < 1e-9, "Darkness food delta wrong: " + w.data.village.food);
                    break; }
                case "SeasonMigrationsMove": {
                    w = World(424243);   // свой сид против редкой базовой смертности
                    Surface(w);
                    // волки уже есть в мире (стартовый план); смена сезона на Тьму тянет их к посёлку
                    var wolves = w.data.creatures.FindAll(c => c.species == "wolf" && !c.dead);
                    Check(wolves.Count >= 1, "No wolves to migrate");
                    w.data.creatures.RemoveAll(c => c.species != "wolf" || c.dead);   // только волки — чистый эксперимент
                    TickDay(w);   // первый тик лишь фиксирует текущий сезон (как в вебе: lastSeason null)
                    w.clock.minutes = 18 * 1440.0; w.data.minutes = w.clock.minutes;   // наступает Тьма
                    TickDay(w);
                    Check(w.data.deathCause == null, "Player died from baseline mortality in test");
                    var vil = w.data.Poi("village");
                    var migrated = w.data.creatures.FindAll(c => c.species == "wolf" && c.hasHome);
                    Check(migrated.Count >= 1, "Wolves did not get migration homes");
                    foreach (var mw in migrated)
                        Check(ScRng.Dist(mw.homeX, mw.homeY, vil.x, vil.y) < vil.r + 2, "Wolf migrated away from village");
                    Check(w.data.log.Exists(l => l.text.StartsWith("Зверь смещается")), "Migration chronicle missing");
                    break; }
                case "SeasonNeutralInDungeon": {
                    Surface(w);
                    Check(Math.Abs(w.SeasonMods.birthMul - ScSeasons.Mods(w.Season).birthMul) < 1e-9, "Surface mods wrong");
                    w.EnterBirthNest();
                    Check(Math.Abs(w.SeasonMods.birthMul - 1) < 1e-9 && Math.Abs(w.SeasonMods.villageFoodDelta) < 1e-9, "Nest is not season-neutral");
                    var nf = w.Dun.floors[0];
                    w.data.player.x = nf.upX; w.data.player.y = nf.upY;
                    Check(w.ExitBirthNest(), "Second exit from nest refused");
                    w.InvalidateSeasonCache();
                    Check(w.SeasonMods.hint == ScSeasons.Mods(w.Season).hint, "Surface seasons did not resume after nest");
                    break; }
                case "VillageStageEconomy": {
                    string st1; double fb1; ScQuests.VillageStageOf(9, out st1, out fb1);
                    string st2; double fb2; ScQuests.VillageStageOf(11, out st2, out fb2);
                    string st3; double fb3; ScQuests.VillageStageOf(17, out st3, out fb3);
                    Check(st1 == "Хутор" && fb1 == 0 && st2 == "Деревня" && fb2 == 1 && st3 == "Посад" && fb3 == 2, "Village stage thresholds wrong");
                    Check(w.data.village.population == 11 && w.data.village.food == 40 && w.data.village.military == 25, "Village new-game defaults wrong (web parity)");
                    Surface(w);
                    TickDay(w);
                    Check(w.data.deathCause == null, "Player died from baseline mortality in test");
                    Check(w.data.village.stage == "Деревня", "Stage not applied after day tick");
                    break; }
                case "RaidCycleIgnored": {
                    w = World(424244);   // свой сид против редкой базовой смертности
                    Surface(w);
                    // стая у ограды (порог wolves>=4 и шанс 0.4 — формула; здесь — детерминированное расписание)
                    for (int i = 0; i < 4; i++)
                        w.SpawnCreature("wolf", Vec.Of(w.data.Poi("burrow").x + i * 0.5, w.data.Poi("burrow").y), 1);
                    w.data.village.raidPending = true;
                    w.data.village.raidDeadline = w.clock.TotalDays + 1;
                    TickDay(w);   // волки подходят к ограде
                    Check(w.data.village.raidPending, "Raid vanished before deadline");
                    double food0 = w.data.village.food; int pop0 = w.data.village.population; double stab0 = w.data.village.stability;
                    w.data.village.raidDeadline = w.clock.TotalDays;   // срок вышел
                    TickDay(w);
                    Check(w.data.deathCause == null, "Player died from baseline mortality in test");
                    Check(!w.data.village.raidPending, "Raid still pending after deadline");
                    Check(w.data.village.food <= food0 - 16 + 1e-9, "Ignored raid: food penalty missing");
                    Check(w.data.village.population <= pop0 - 2, "Ignored raid: population loss missing");
                    Check(w.data.village.stability < stab0, "Ignored raid: stability penalty missing");
                    Check(w.data.karmaEvents.Exists(k => k.id == "ignored_raid") && Math.Abs(w.data.karmaTotal + 10) < 1e-9, "Ignored raid karma missing");
                    break; }
                case "RaidDefenseChess": {
                    Surface(w);
                    var stChess = ScChess.CreateRaidChess(w);
                    Check(stChess.phase == "setup" && stChess.boardR == 5 && stChess.boardC == 7, "Chess board wrong");
                    Check(stChess.enemies.Count == 3, "Raid 1 wolf pack wrong");   // 2 волка + вожак
                    Check(stChess.reserveLimit == 3, "Reserve limit for military 25 wrong");
                    // победный сценарий: врагам по 1 HP, выставляем полный состав
                    foreach (var e in stChess.enemies) e.hp = 1;
                    Check(ScChess.Place(stChess, "cmdr", 3, 1), "cmdr place refused");
                    Check(ScChess.Place(stChess, "militia", 3, 3), "militia place refused");
                    Check(ScChess.Place(stChess, "archer", 4, 5), "archer place refused");
                    Check(!ScChess.Place(stChess, "militia", 2, 0), "placed into enemy rows");
                    Check(!ScChess.Place(stChess, "ghost", 4, 0), "unknown unit accepted");
                    ScChess.Start(stChess);
                    var brng = new ScRng(777);
                    int guard = 0;
                    while (stChess.phase == "battle" && guard++ < 700) ScChess.Tick(stChess, brng);
                    Check(stChess.phase == "result" && stChess.result == "win", "Rigged battle did not win");
                    Check(stChess.worldMinutesSpent > 0, "World minutes not consumed");
                    int wolves0 = w.data.creatures.FindAll(c => c.species == "wolf" && !c.dead).Count;
                    ScChess.ApplyResult(w, stChess);
                    Check(Math.Abs(w.data.village.military - 33) < 1e-9 && Math.Abs(w.data.village.stability - 68) < 1e-9, "Win effects on village wrong");
                    Check(w.data.village.raidsSurvived == 1 && w.data.raidDefended == 1, "Raid defense flags missing");
                    Check(Math.Abs(w.Npc("dar").attitude - 59) < 1e-9, "Dar attitude +14 missing");
                    Check(w.data.karmaEvents.Exists(k => k.id == "raid_defended"), "Raid defense karma missing");
                    Check(w.data.soul != null && w.data.soul.memories.Exists(mm => mm.id == "m_chess"), "chess memory check needs raid flag"); // m_chess откроется судом при raidDefended
                    break; }
                case "ChessRulesSetup": {
                    var stChess = ScChess.CreateRaidChess(w);
                    Check(ScChess.Place(stChess, "militia", 3, 3), "first place refused");
                    // перестановка существующей фигуры не расходует лимит
                    Check(ScChess.Place(stChess, "militia", 4, 3), "re-place refused");
                    Check(stChess.placed.Count == 1, "Re-place duplicated unit");
                    ScChess.Remove(stChess, stChess.placed[0].uid);
                    Check(stChess.placed.Count == 0, "Remove failed");
                    ScChess.Start(stChess);   // пустой состав — бой не начинается
                    Check(stChess.phase == "setup", "Battle started without units");
                    // проигрышный сценарий: одиночный боец с 1 HP против стаи
                    Check(ScChess.Place(stChess, "militia", 3, 3), "militia place refused");
                    stChess.placed[0].hp = 1;
                    ScChess.Start(stChess);
                    Check(stChess.phase == "battle", "Battle with units did not start");
                    var brng = new ScRng(4242);
                    int guard = 0;
                    while (stChess.phase == "battle" && guard++ < 700) ScChess.Tick(stChess, brng);
                    Check(stChess.phase == "result" && stChess.result == "lose", "Rigged battle did not lose");
                    double food0 = w.data.village.food; int pop0 = w.data.village.population;
                    ScChess.ApplyResult(w, stChess);
                    Check(w.data.village.food <= food0 - 14 + 1e-9 && w.data.village.population <= pop0 - 2, "Lose effects missing");
                    Check(w.data.karmaEvents.Exists(k => k.id == "raid_failed"), "Lose karma missing");
                    break; }
                case "NpcDialogueOptions": {
                    Surface(w);
                    // Мирра (старейшина): опции квестов, Разлом, уход
                    var mirra = ScNpcs.Dialogue(w, "mirra");
                    Check(mirra.options.Exists(o => o.id == "ask_rift") && mirra.options.Exists(o => o.id == "leave"), "Mirra base options wrong");
                    Check(mirra.options.Exists(o => o.id == "accept_descend"), "Mirra does not offer descend");
                    Check(w.AcceptQuest("descend"), "descend accept failed");
                    mirra = ScNpcs.Dialogue(w, "mirra");
                    Check(!mirra.options.Exists(o => o.id == "accept_descend"), "Mirra re-offers active quest");
                    w.data.SetFlag("quest_progress_descend", 1);
                    mirra = ScNpcs.Dialogue(w, "mirra");
                    Check(mirra.options.Exists(o => o.id == "turnin_descend"), "Mirra does not take ready quest");
                    // Дар при набеге: Magic Chess и охота на вожака
                    w.data.village.raidPending = true;
                    var dar = ScNpcs.Dialogue(w, "dar");
                    Check(dar.options.Exists(o => o.id == "start_raid") && dar.options.Exists(o => o.id == "hunt_alpha"), "Dar raid options wrong");
                    ScNpcs.Action(w, "dar", "hunt_alpha");
                    Check(w.data.Flag("alphaSpawned") == 1 && w.data.creatures.Exists(c => c.questTag == "alpha" && c.name == "Рваное Ухо"), "Alpha hunt spawn wrong");
                    ScNpcs.Action(w, "dar", "start_raid");
                    Check(w.ConsumeStartRaid(), "startRaid flag not consumed");
                    w.data.village.raidPending = false;
                    // Клумба: donate/steal/спросить о духах
                    var klumba = ScNpcs.Dialogue(w, "klumba");
                    Check(klumba.options.Exists(o => o.id == "donate") && klumba.options.Exists(o => o.id == "steal") && klumba.options.Exists(o => o.id == "ask_spirits"), "Klumba options wrong");
                    ScNpcs.Action(w, "klumba", "ask_spirits");
                    Check(w.data.Flag("showStoneCircle") == 1, "Spirits flag missing");
                    ScNpcs.Action(w, "mirra", "ask_rift");
                    Check(w.data.loreRift == 1, "Lore rift counter missing");
                    break; }
                case "DonateStealFood": {
                    Surface(w);
                    w.data.player.biomass = 15;
                    double dar0 = w.Npc("dar").attitude; double karma0 = w.data.karmaTotal;
                    Check(w.DonateFood(), "Donate refused");
                    Check(Math.Abs(w.data.village.food - 52) < 1e-9, "Donate food +12 wrong");
                    Check(Math.Abs(w.data.village.stability - 62) < 1e-9, "Donate stability +2 wrong");
                    Check(Math.Abs(w.Npc("dar").attitude - (dar0 + 5)) < 1e-9, "Donate attitude +5 wrong");
                    Check(w.data.donateCount == 1 && w.data.karmaTotal > karma0, "Donate karma/count missing");
                    Check(Math.Abs(w.data.player.biomass - 5) < 1e-9, "Donate biomass cost wrong");
                    Check(!w.DonateFood(), "Donate allowed without biomass");
                    Check(w.StealFood(), "Steal refused");
                    Check(Math.Abs(w.data.village.food - 42) < 1e-9 && Math.Abs(w.data.village.stability - 59) < 1e-9, "Steal economics wrong");
                    Check(w.data.karmaTotal < karma0 + 6, "Steal karma penalty missing");
                    Check(!w.StealFood(), "Steal twice in one day");
                    break; }
                case "NpcSuccession": {
                    Surface(w);
                    var mirra = w.Npc("mirra");
                    mirra.ageDays = 481;   // lifespan 480 — смертность ~13%/день и растёт
                    int guard = 0;
                    while (w.Npc("ivo") == null && guard++ < 400) TickDay(w);
                    Check(!mirra.alive, "Old mirra never died");
                    Check(w.Npc("ivo") != null && w.Npc("ivo").role == "новый старейшина", "Successor ivo missing");
                    Check(w.data.log.Exists(l => l.text.Contains("Иво")), "Succession chronicle missing");
                    // преемники остальных жителей
                    var dar = w.Npc("dar"); dar.ageDays = 999; dar.alive = true;
                    var klumba = w.Npc("klumba"); klumba.ageDays = 999; klumba.alive = true;
                    guard = 0;
                    while ((w.Npc("rinsa") == null || w.Npc("prokh") == null) && guard++ < 400) TickDay(w);
                    Check(w.Npc("rinsa") != null && w.Npc("prokh") != null, "Dar/Klumba successors missing");
                    break; }
                case "DungeonCatacombsPort": {
                    Surface(w);
                    Check(w.AcceptQuest("descend"), "descend accept failed");   // крючок задания до входа
                    var gate = ScDungeon.Catacombs.gatePos(w.data);
                    var pcat = w.data.player;
                    pcat.x = (float)gate.x; pcat.y = (float)gate.y; pcat.vx = 0; pcat.vy = 0;
                    w.UpdateProximity();
                    Check(w.nearGateId == "catacombs", "Gate proximity not detected");
                    Check(w.EnterDungeon("catacombs"), "Enter catacombs refused");
                    Check(w.DungeonActive && w.data.activeDungeon == "catacombs", "Dungeon state wrong");
                    var dun = w.Dun;
                    Check(dun.floors.Count == 3 && dun.floors[0].w == 22 && dun.floors[0].h == 22, "Catacombs floors wrong");
                    int sporelings = 0, mycenoids = 0;
                    foreach (var c in w.ActiveCreatures()) { if (c.species == "sporeling") sporelings++; if (c.species == "mycenoid") mycenoids++; }
                    Check(sporelings == 4 && mycenoids == 1, "Depth 1 spawn plan wrong");
                    Check(w.data.dungeonEntered == 1 && ScQuests.Progress(w.data, "descend") == 1, "Dungeon enter hooks missing");
                    // вниз до третьей глубины — к Монарху (переход по лестницам вниз)
                    var f0 = dun.floors[0];
                    pcat.x = f0.downX + 0.4f; pcat.y = f0.downY + 0.4f;
                    Check(w.DescendStairs() && w.Dun.depth == 2, "Descend to depth 2 refused");
                    var f1 = dun.floors[1];
                    pcat.x = f1.downX + 0.4f; pcat.y = f1.downY + 0.4f;
                    Check(w.DescendStairs() && w.Dun.depth == 3, "Descend to depth 3 refused");
                    var boss = w.ActiveCreatures().Find(c => c.species == "myco_monarch");
                    Check(boss != null && boss.questTag == "monarch" && boss.name == "Грибной Монарх", "Monarch not spawned at depth 3");
                    double biomass0 = w.data.player.biomass;
                    w.KillCreature(boss, "player");
                    Check(w.data.monarchKilled == 1 && w.data.Flag("monarchKilled") == 1, "Monarch kill flags missing");
                    Check(Math.Abs(w.data.player.biomass - (biomass0 + 30)) < 1e-9, "Boss reward biomass wrong");
                    Check(w.data.player.knownGenes.Contains("bioluminescent"), "Boss trophy gene missing");
                    Check(w.data.creatures.Exists(c => c.species == "mycenoid"), "After-boss plan missing");
                    // ретро-прогресс и сдача цепочки заданий
                    w.data.SetFlag("quest_done_descend", 1);
                    Check(w.AcceptQuest("monarch") && ScQuests.Progress(w.data, "monarch") == 1, "Monarch quest not retroactive");
                    Check(w.TurnInQuest("monarch"), "Monarch turn-in failed");
                    // наверх — по лестницам вверх (у каждой — точка up своего этажа)
                    var f2 = dun.floors[2];
                    Check(w.AscendStairs() && w.Dun.depth == 2, "Ascent to depth 2 refused");   // стояли у up глубины 3
                    pcat.x = f1.upX + 0.4f; pcat.y = f1.upY + 0.4f;
                    Check(w.AscendStairs() && w.Dun.depth == 1, "Ascent to depth 1 refused");
                    pcat.x = f0.upX + 0.4f; pcat.y = f0.upY + 0.4f;
                    Check(w.AscendStairs() && !w.DungeonActive && w.data.activeDungeon == null, "Surface exit failed");
                    break; }
                case "DungeonStairsCache": {
                    Surface(w);
                    var gate = ScDungeon.UndergroundChannel.gatePos(w.data);
                    var pch = w.data.player;
                    pch.x = (float)gate.x; pch.y = (float)gate.y; pch.vx = 0; pch.vy = 0;
                    Check(w.EnterDungeon("underground_channel"), "Enter channel refused");
                    var dun = w.Dun;
                    Check(dun.floors.Count == 2, "Channel depths wrong");
                    Check(dun.caches.Count > 0, "No caches placed");
                    // принос: у первого не взятого тайника текущей глубины
                    var cache = dun.caches.Find(cc => cc.depth == dun.depth && !cc.taken);
                    Check(cache != null, "Cache for depth 1 missing");
                    double biomass0 = w.data.player.biomass;
                    pch.x = cache.x + 0.1f; pch.y = cache.y + 0.1f;
                    w.UpdateProximity();
                    Check(w.nearCacheId == cache.id, "Cache proximity missed");
                    Check(w.TakeCache(), "Take cache failed");
                    Check(cache.taken && w.data.player.biomass > biomass0, "Cache reward missing");
                    Check(!w.TakeCache(), "Cache taken twice");
                    // лестница вниз/вверх по координатам этажа
                    var f1 = dun.floors[0];
                    pch.x = f1.downX + 0.4f; pch.y = f1.downY + 0.4f;
                    w.UpdateProximity();
                    Check(w.nearStairsDown, "Stairs-down proximity missed");
                    Check(w.DescendStairs() && w.Dun.depth == 2, "Descend failed");
                    var f2 = dun.floors[1];
                    pch.x = f2.upX + 0.4f; pch.y = f2.upY + 0.4f;
                    w.UpdateProximity();
                    Check(w.nearStairsUp, "Stairs-up proximity missed");
                    Check(w.AscendStairs() && w.Dun.depth == 1, "Ascend failed");
                    pch.x = f1.upX + 0.4f; pch.y = f1.upY + 0.4f;
                    Check(w.AscendStairs() && !w.DungeonActive, "Exit to surface failed");
                    break; }
                case "AtlasDiscoveryFlow": {
                    Check(ScAtlas.RegionAt(26, 24).id == "forest_edge" && ScAtlas.RegionAt(45, 45).id == "soul_rift" && ScAtlas.RegionAt(26, 5).id == "echo_ridge", "RegionAt anchors wrong");
                    Check(ScAtlas.DungeonRegionId("catacombs") == "root_garden" && ScAtlas.DungeonRegionId("crystal_caves") == "forgotten_vein" && ScAtlas.DungeonRegionId("birth_nest") == "quiet_cradle", "DungeonRegionId wrong");
                    Check(ScAtlas.ById("rift_heart").sealedArea && ScAtlas.ById("echo_archive").sealedArea, "Depth regions not sealed");
                    Check(ScAtlas.AllRegions.Length == 18, "World regions count wrong");
                    // норка — подземный слой: её регион открывается раньше поверхности (как в вебе)
                    w.Update(0.02, "explore");
                    Check(w.IsRegionDiscovered("quiet_cradle"), "Nest region not discovered in nest");
                    Surface(w);
                    w.Update(0.02, "explore");
                    Check(w.IsRegionDiscovered("forest_edge"), "Home region not discovered at start");
                    Check(!w.IsRegionDiscovered("sunken_ruins"), "Ruins pre-discovered");
                    Warp(w, 13.5, 17.5);
                    w.Update(0.02, "explore");
                    Check(w.IsRegionDiscovered("sunken_ruins") && w.ConsumeRegionDiscovery() == "sunken_ruins", "Ruins not discovered on visit");
                    Check(w.ConsumeRegionDiscovery() == null, "Discovery consumed twice");
                    // подземелье открывает свой подземный регион
                    var gate = ScDungeon.Catacombs.gatePos(w.data);
                    var pdr = w.data.player;
                    pdr.x = (float)gate.x; pdr.y = (float)gate.y; pdr.vx = 0; pdr.vy = 0;
                    w.EnterDungeon("catacombs");
                    w.Update(0.02, "explore");
                    Check(w.IsRegionDiscovered("root_garden"), "Underground region not discovered in dungeon");
                    break; }
                case "CrackSmallBodyOnly": {
                    Surface(w);
                    // трещина засеяна: декор-вход и добыча в кармане
                    Check(w.data.mapJson.deco.Exists(dc => dc.kind == "crack"), "Crack deco not seeded");
                    Check(w.data.creatures.Exists(c => c.dungeonFloor == 0 && Math.Abs(c.x - 34) < 2 && Math.Abs(c.y - 31) < 2), "Crack pocket prey missing");
                    // малое тело (0 генов) проскальзывает внутрь кармана
                    Warp(w, 34, 31.5);
                    Check(w.IsWalkable(34, 31.5), "Small body blocked by crack");
                    // взрослое тело (2 гена) не пролезает, хроника подсказывает
                    w.data.player.biomass = 100;
                    Check(w.InstallGene("slime_coat") && w.InstallGene("sensors"), "Gene install for crack test failed");
                    int logs0 = w.data.log.Count;
                    Check(!w.IsWalkable(34, 31.5), "Adult body slipped through crack");
                    Check(w.data.log.Count > logs0 && w.data.log[w.data.log.Count - 1].text.Contains("Трещина слишком узка"), "Crack hint log missing");
                    break; }
                case "KarmaPathThresholds": {
                    w.data.karmaTotal = 41;
                    Check(w.KarmaPath() == "Свет", "Karma +41 is not Свет");
                    w.data.karmaTotal = -41;
                    Check(w.KarmaPath() == "Тьма", "Karma −41 is not Тьма");
                    w.data.karmaTotal = 0;
                    Check(w.KarmaPath() == "Равновесие", "Karma 0 is not Равновесие");
                    // Путь Света: ежедневное подношение посёлку и телу
                    Surface(w);
                    w.data.soul.karma = 50;   // итог души + ведомость жизни ≥ 40
                    double food0 = w.data.village.food; double biomass0 = w.data.player.biomass;
                    TickDay(w);
                    Check(w.data.Flag("lightBlessing") == 1, "Light blessing flag missing");
                    Check(w.data.player.biomass > biomass0, "Light blessing biomass missing");
                    Check(w.data.village.food > food0, "Light blessing village food missing");
                    break; }
                case "WorldV13Roundtrip": {
                    Surface(w);
                    w.data.SetFlag("quest_accepted_wolves", 1);
                    w.data.SetFlag("quest_progress_wolves", 2);
                    w.data.worldRegions["sunken_ruins"] = 1;
                    w.data.village.raidPending = true;
                    w.data.village.raidDeadline = 42;
                    w.data.npcs[0].attitude = 77;
                    var stChess = ScChess.CreateRaidChess(w);
                    ScChess.Place(stChess, "cmdr", 3, 2);
                    w.data.chess = stChess;
                    w.CaptureState();
                    string json13 = ScJson.ToJson(w.data);
                    var restored13 = ScJson.FromJson<WorldData>(json13);
                    Check(ScJson.ToJson(restored13) == json13, "v1.3 world changed on roundtrip");
                    Check(restored13.npcs.Count == w.data.npcs.Count && restored13.npcs[0].attitude == 77, "NPCs lost on roundtrip");
                    Check(restored13.Flag("quest_progress_wolves") == 2, "Quest flags lost on roundtrip");
                    Check(restored13.worldRegions.ContainsValue(1) && restored13.worldRegions.ContainsKey("sunken_ruins"), "Atlas discovery lost on roundtrip");
                    Check(restored13.chess != null && restored13.chess.placed.Count == 1 && restored13.chess.placed[0].defId == "cmdr", "Chess state lost on roundtrip");
                    Check(restored13.village.raidPending && restored13.village.raidDeadline == 42, "Raid state lost on roundtrip");
                    ScWorld.FromData(restored13).Update(0.02, "explore");
                    break; }
                case "JudgmentBodyChoice": {
                    w.KillPlayer("old_age", "тест тел");
                    Check(w.data.soul.level == 1, "Soul should be level 1");
                    var opts = ScSoul.ReincarnationOptions(w.data.soul);
                    Check(opts.Count == 1 && opts[0].id == "slug", "Level 1 should offer only base slug");
                    w.data.soul.level = 3;   // душе открыт выбор расширенных тел
                    w.data.soul.stats.will = 3;   // волевая душа заслужила тело волчьей крови
                    opts = ScSoul.ReincarnationOptions(w.data.soul);
                    Check(opts.Exists(o => o.id == "light_slug") && opts.Exists(o => o.id == "wolfblood_slug"), "Reincarnation options missing");
                    Check(w.ReincarnateWith("light_slug"), "ReincarnateWith refused");
                    Check(Math.Abs(w.data.player.bonusMaxHp - 4) < 1e-9 && Math.Abs(w.data.player.bonusPerception - 20) < 1e-9, "light_slug body gifts missing");
                    Check(w.data.player.perks.Contains("spirit_resonance"), "light_slug perk missing");
                    w.KillPlayer("killed", "тест тел 2");
                    w.data.soul.level = 3;
                    w.data.soul.stats.will = 3;   // суд пересчитал уровень — выбор остаётся за душой
                    Check(w.ReincarnateWith("wolfblood_slug"), "Second reincarnation refused");
                    Check(Math.Abs(w.data.player.bonusAtk - 1) < 1e-9 && Math.Abs(w.data.player.bonusSpeed - 0.2) < 1e-9, "wolfblood body gifts missing");
                    Check(w.data.player.perks.Contains("hunter_instinct"), "wolfblood perk missing");
                    break; }
                case "ChessPlacementBounds": {
                    var st=ScChess.CreateRaidChess(w);
                    Check(!ScChess.Place(st,"cmdr",5,0) && !ScChess.Place(st,"cmdr",3,-1) && !ScChess.Place(st,"cmdr",3,7),"Unit placed outside board");
                    break; }
                case "ChessResultOnce": {
                    var st=ScChess.CreateRaidChess(w); st.phase="result"; st.result="win";
                    ScChess.ApplyResult(w,st); string first=ScJson.ToJson(w.data);
                    ScChess.ApplyResult(w,st);
                    Check(first==ScJson.ToJson(w.data),"Raid rewards applied twice");
                    break; }
                case "ChessSaveContinuation": {
                    var st=ScChess.CreateRaidChess(w);
                    ScChess.Place(st,"cmdr",3,2); ScChess.Place(st,"militia",3,3); ScChess.Start(st);
                    var random=new ScRng(124);
                    for(int i=0;i<8 && st.phase=="battle";i++) ScChess.Tick(st,random);
                    var chessRestored=ScJson.FromJson<ChessState>(ScJson.ToJson(st));
                    var resumedRandom=new ScRng(999);
                    for(int i=0;i<20 && st.phase=="battle";i++) { ScChess.Tick(st,random); ScChess.Tick(chessRestored,resumedRandom); }
                    Check(ScJson.ToJson(st)==ScJson.ToJson(chessRestored),"Save/reload changed chess battle outcome or RNG");
                    break; }
                case "ChessSetupDeterminism": {
                    var first=ScChess.CreateRaidChess(w); var chessSecond=ScChess.CreateRaidChess(World());
                    ScChess.Place(first,"cmdr",3,2); ScChess.Place(chessSecond,"cmdr",3,2);
                    Check(ScJson.ToJson(first)==ScJson.ToJson(chessSecond),"Global unit counter breaks deterministic setup");
                    break; }
                default: throw new ArgumentException(name);
            }
        }
    }
}
