// SERVERCLOSED — Core: главный шаг симуляции (порт world.ts, вертикальный срез).
// FSM AI (wander/flee/chase), коллизии, бой, поглощение → пищеварение → биомасса →
// органы чувств → гены; карма (§12), родовая норка (мини-вселенная), дневные тики (§6),
// старение (§10), самолечение позиции. Магический Chess/NPC-диалоги — заглушки (см. README).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class ScWorld
    {
        public WorldData data;
        public ScRng rng;
        public WorldClock clock;
        public DerivedStats stats;

        // Рантайм (не сохраняются)
        public int nearCorpseId = -1;
        public string nearNpcId;                 // NPC рядом (диалог по [E]/кнопке)
        public string nearGateId;                // врата подземелья рядом
        public bool nearStairsUp, nearStairsDown;
        public int nearCacheId = -1;
        public double shakeT;
        public double senseTime;
        public bool coreActive;
        public readonly List<KarmaEvent> pendingJudgment = new List<KarmaEvent>();
        public JudgmentResult lastJudgment;      // кэш экрана Суда; итоги применены к душе один раз
        // опасности региона (regions.ts updateRegion — рантайм)
        double poisonAcc; bool poisonLogged;
        double stormT, stormHit; double stormCd = 22; int stormSurvived;
        // сезоны (Этап 2): кеш модификаторов + отслеживание смены сезона (миграции/хроника)
        string lastSeason;                       // null после загрузки — первый тик без хроники (как в вебе)
        SeasonMods seasonModsCache;
        double crackHintT;                       // троттлинг подсказки про узкую трещину

        // ===== Создание мира =====
        public static ScWorld NewGame(int seed, int lifeNo)
        {
            var d = new WorldData { seed = seed, lifeNo = lifeNo, minutes = 7 * 60 };
            ScMapGen.Generate(d);
            var w = new ScWorld { data = d };
            w.rng = new ScRng(seed ^ (d.dayTicks * 265443576));
            w.clock = new WorldClock(d.minutes) { dayTickCounter = d.dayTicks };
            w.stats = w.RecomputeStats();

            // Тихая колыбель — стартовое место (посёлок −6.5, +6.5) — точка рождения quiet_cradle
            var spawn = ScRegions.SpawnPoint("quiet_cradle", d);
            d.player.x = spawn.x; d.player.y = spawn.y;
            d.player.hp = w.stats.maxHp;
            d.soul = ScSoul.NewSoul();
            d.achievements = ScSoul.InitAchievements();

            w.SpawnInitialPops();
            w.SpawnGuards();
            w.SeedCrack();
            w.EnsureV13();
            w.EnterBirthNest();
            w.PushLog("Родовая норка. Вы — бесформенный комочек слизи. Поглощайте живность: лаза наружу ещё не достать.", "world");
            return w;
        }

        public static ScWorld FromData(WorldData d)
        {
            ScSoul.EnsureMigration(d);   // сейвы v1.1 без души получают пустую душу с номером жизни
            var w = new ScWorld { data = d };
            w.rng = new ScRng((int)((uint)d.seed ^ (uint)(d.dayTicks * 265443576)));
            w.clock = new WorldClock(d.minutes) { dayTickCounter = d.dayTicks };
            w.stats = w.RecomputeStats();
            if (d.rngState >= 0) w.rng.State = (uint)d.rngState;
            w.coreActive = d.senseCore == 1;
            w.SeedCrack();
            w.EnsureV13();               // миграция v1.2 → v1.3: NPC, флаги, атлас, посёлок
            w.HealPlayerPosition();
            return w;
        }

        // ===== Миграция v1.2 → v1.3 (идемпотентна): NPC, флаги, атлас, посёлок =====
        public void EnsureV13()
        {
            var d = data;
            if (d.flags == null) d.flags = new Dictionary<string, double>();
            if (d.worldRegions == null) d.worldRegions = new Dictionary<string, int>();
            if (d.npcs == null) d.npcs = new List<Npc>();
            if (d.npcs.Count == 0) d.npcs = ScNpcs.DefaultTrio(d);   // посёлок живёт и в старых мирах
            if (d.player != null && d.player.discoveredSpecies == null) d.player.discoveredSpecies = new List<string>();
            if (d.village != null)
            {
                if (d.village.military <= 0) d.village.military = 25;
                if (d.village.raidCdDays <= 0 && !d.village.raidPending) d.village.raidCdDays = 2;
            }
            // «Чаша Отголосов»: игрок стоит в каком-то месте мира — оно открыто (дальше только ногами, п.9)
            string here = DungeonActive || d.activeDungeon != null
                ? ScAtlas.DungeonRegionId(d.activeDungeon)
                : ScAtlas.RegionAt(d.player.x, d.player.y).id;
            if (!d.worldRegions.ContainsKey(here)) d.worldRegions[here] = 1;
        }

        /// Тайная трещина: декор-вход + добыча в кармане для малого тела (веб: crackSeeded).
        public void SeedCrack()
        {
            var d = data;
            if (d.Flag("crackSeeded") == 1) return;
            d.SetFlag("crackSeeded", 1);
            var m = d.mapJson;
            foreach (var spot in ScAtlas.CrackSpots)
            {
                m.deco.Add(new Deco { id = d.nextId++, kind = "crack", x = (float)spot.entranceX, y = (float)spot.entranceY, r = (float)spot.rOut, seed = d.seed });
                for (int i = 0; i < 3; i++)
                {
                    double a = i / 3.0 * Math.PI * 2 + 0.5;
                    Creature c = SpawnCreaturePublic("slug", Vec.Of(spot.pocketX + Math.Cos(a) * 0.8, spot.pocketY + Math.Sin(a) * 0.8));
                    if (c != null) { c.hp = 10; c.ageDays = 1; }
                }
                Creature cr = SpawnCreaturePublic("crystalslug", Vec.Of(spot.pocketX, spot.pocketY + 0.9));
                if (cr != null) { cr.hp = 14; cr.ageDays = 1; }
            }
        }

        // ===== Производные характеристики (§9 + §10) =====
        public DerivedStats RecomputeStats()
        {
            var p = data.player;
            var d = ScGenome.ComputeDerived(p.genome);
            double hpMul, atkMul, speedMul, xpMul;
            ScAging.AgeModifiers(p.ageDays, d.lifeDays, out hpMul, out atkMul, out speedMul, out xpMul);
            d.maxHp = Math.Max(8, Math.Round(d.maxHp * hpMul - p.mutations * 2));
            d.atk = Math.Max(1, d.atk * atkMul);
            d.speed = d.speed * speedMul;
            foreach (string perk in p.perks)
            {
                GeneDef g;
                if (Genes.All.TryGetValue(perk, out g))
                {
                    d.maxHp += g.maxHp * 0.6; d.atk += g.atk * 0.6; d.def += g.def * 0.6;
                }
            }
            // дары тела от варианта перерождения (§11.3)
            d.maxHp += p.bonusMaxHp; d.atk += p.bonusAtk; d.def += p.bonusDef;
            d.speed += p.bonusSpeed; d.perception += p.bonusPerception;
            return d;
        }

        public void RefreshStats() { stats = RecomputeStats(); }

        public void CaptureState() { data.rngState = rng.State; }

        /// Быстрая прокрутка дневных тиков для проверки/смоука: те же DailyTick(1), что и при переходе дней кадрами.
        public void AdvanceDaysForTests(int days)
        {
            for (int i = 0; i < days && data.deathCause == null; i++) DailyTick(1);
            clock.Advance(days * 1440.0);
            data.minutes = clock.minutes;
            data.dayTicks = clock.dayTickCounter;
        }

        // A new body in the existing world; world history, clock and population survive.
        // Поток перерождения — порт engine.ts reincarnate: суд уже применён к душе (RunJudgment
        // при смерти), здесь — небытие (скачок времени), новое тело, ролл региона (ролл сделан при суде).
        public bool Reincarnate() { return ReincarnateWith(null); }

        /// Перерождение с выбранным в UI вариантом тела (§11.3); null — как в v1.2 (последний вариант).
        public bool ReincarnateWith(string optionId)
        {
            if (data.deathCause == null || data.soul == null) return false;
            if (data.judged == 0) RunJudgment();      // страховка: суд обязателен до перерождения
            var soul = data.soul;

            // 1) Небытие между жизнями (§3.3): глубокая память души дольше не воплощается
            int skipDays = Math.Min(600, (int)Math.Round((60 + soul.life * 30) * (1 + soul.traits.memory / 100.0 * 1.5)));
            data.lastSkipDays = skipDays;
            clock.Advance(skipDays * 1440.0);
            data.minutes = clock.minutes;
            data.dayTicks = clock.dayTickCounter;
            ScSoul.AdvanceWorldAfterDeath(data, lastJudgment != null ? lastJudgment.verdict : "Равновесие",
                data.deathCauseCode != null ? data.deathCauseCode : "killed", skipDays, clock.TotalDays);

            // 2) Новое тело: душа сохраняет ограниченную часть знаний (§11.3)
            int carryCount = Math.Max(0, Math.Min(2, soul.level - 1));
            var pool = new List<string>(data.player.knownGenes);
            rng.Shuffle(pool);
            var carried = new List<string>();
            for (int i = 0; i < pool.Count && carried.Count < carryCount; i++) carried.Add(pool[i]);
            // вариант тела: игрок выбирает на экране Реинкарнации (§11.3); null — авто (последний)
            var opts = ScSoul.ReincarnationOptions(soul);
            ReincarnOption opt = null;
            if (!string.IsNullOrEmpty(optionId))
                foreach (var o in opts) if (o.id == optionId) { opt = o; break; }
            if (opt == null) opt = opts[opts.Count - 1];

            data.lifeNo++;
            data.player = new PlayerState
            {
                knownGenes = carried,
                perks = opt.perkGene != null ? new List<string> { opt.perkGene } : new List<string>(),
                ageDays = 0.4,
                biomass = 8 * soul.level,   // наследие: немного биомассы от уровня души
                bonusMaxHp = opt.maxHp, bonusAtk = opt.atk, bonusDef = opt.def,
                bonusSpeed = opt.speed, bonusPerception = opt.perception
            };
            data.senses = new SensoryState();
            data.digestion = new DigestiveState();
            data.karma = 0;
            data.karmaTotal = 0;
            data.deathCause = null;
            data.deathCauseCode = null;
            data.judged = 0;
            lastJudgment = null;
            data.karmaEvents.Clear();            // суд прочитал ведомость — жизнь начинается с чистым листом
            data.senseCore = soul.resonantCore ? 1 : 0;   // ядро — свойство души, а не тела
            data.nestExited = data.inNest = data.nestReadyShown = 0;
            data.activeDungeon = null;
            // флаги жизни (§11.3): путь кармы от души, блаженства сбрасываются, рецепторы тела новые
            data.SetFlag("soulBaseKarma", soul.karma);
            data.SetFlag("lightBlessing", 0);
            data.SetFlag("darkWhisper", 0);
            data.SetFlag("soulXpLife", 0);
            data.SetFlag("traitDepth", soul.traits != null ? soul.traits.depth : 0);
            data.chess = null;                   // бой не переживает перерождение
            InvalidateSeasonCache();
            lastSeason = null;
            crackHintT = 0;
            // подземелья перерождаются вместе с миром: обитатели забывают прошлые визиты (§11)
            foreach (var kv in data.dungeons)
            {
                kv.Value.active = false; kv.Value.depth = 1;
                foreach (DungeonCache cache in kv.Value.caches) cache.taken = false;
            }
            data.dungeons.Remove("birth_nest");
            data.creatures.RemoveAll(c => c.dead || c.dungeonId != null);
            // немного живности у деревни, чтобы новая жизнь началась живой (первая добыча вплотную)
            var vil = data.Poi("village");
            Vec vp = vil != null ? Vec.Of(vil.x, vil.y) : Vec.Of(26, 24);
            for (int i = 0; i < 3; i++) SpawnNearPos("slug", vp, 1.0, 3.0);
            SpawnNearPos("rabbit", vp, 1.5, 4.0);

            // 3) Место рождения: ролл сделан при суде из долей регионаOdds (§23 — детерминирован сидом).
            string region = data.birthRegionNext != null ? data.birthRegionNext : "quiet_cradle";
            ApplyRegionBirth(region);
            soul.birthRegion = region;

            HealPlayerPosition();
            RefreshStats();
            data.player.hp = stats.maxHp;
            senseMap.Clear(); pendingJudgment.Clear();
            EnterBirthNest();
            var rdef = ScRegions.Region(region);
            PushLog("Резонанс души привёл вас: " + rdef.name + " (" + rdef.difficulty + " сложность). Мир жил без вас " + skipDays + " дн.", "life");
            data.PushToast("Вы рождены: " + rdef.name + " · " + rdef.difficulty + " сложность. Мир жил без вас " + skipDays + " дн.", "epic");
            CaptureState();
            return true;
        }

        // ===== Место рождения (regions.ts applyRegionBirth): позиция + гарантированная первая пища =====
        void ApplyRegionBirth(string regionId)
        {
            RegionDef def = ScRegions.Region(regionId);
            data.birthRegion = regionId;
            Vec pt = ScRegions.SpawnPoint(regionId, data);
            if (!IsWalkable(pt.x, pt.y))
            {
                for (int i = 0; i < 28; i++)
                {
                    double a = rng.Next() * Math.PI * 2;
                    double r = rng.Range(0.8, 3.2);
                    double x = pt.x + Math.Cos(a) * r, y = pt.y + Math.Sin(a) * r;
                    if (IsWalkable(x, y)) { pt = Vec.Of(x, y); break; }
                }
            }
            var p = data.player;
            p.x = pt.x; p.y = pt.y; p.vx = 0; p.vy = 0; p.facing = 1;
            foreach (string[] plan in def.spawnPlan)
            {
                int n;
                if (!int.TryParse(plan[1], out n)) n = 1;
                bool first = plan[0] == "slug";
                for (int i = 0; i < n; i++) SpawnNearPos(plan[0], pt, first ? 1.0 : 1.2, first ? 2.0 : 3.4);
            }
            senseMap.Clear();
        }

        /// Существо рядом с точкой (world.ts spawnNearPos): до 12 попыток найти проходимое место.
        void SpawnNearPos(string species, Vec pt, double rMin, double rMax)
        {
            for (int i = 0; i < 12; i++)
            {
                double a = rng.Next() * Math.PI * 2;
                double r = rng.Range(rMin, rMax);
                double x = pt.x + Math.Cos(a) * r, y = pt.y + Math.Sin(a) * r;
                if (IsWalkable(x, y)) { SpawnCreature(species, Vec.Of(x, y), 1); return; }
            }
            SpawnCreature(species, pt, 1); // последняя попытка — в самой точке
        }

        // ===== Суд души (§11.2): применяется к душе РОВНО ОДИН РАЗ на смерть (safe к перезагрузкам) =====
        public JudgmentResult RunJudgment()
        {
            if (data.deathCause == null || data.soul == null) return null;
            if (data.judged != 0) return lastJudgment;
            var j = ScSoul.Judge(data.soul, data, data.achievements);
            data.judged = 1;
            lastJudgment = j;
            // доли региона на момент суда + детерминированный ролл места рождения (§23)
            data.judgmentOdds = j.regionOdds;
            var weights = new Dictionary<string, double>();
            foreach (RegionOdds o in j.regionOdds) weights[o.id] = Math.Max(1, o.pct);
            data.birthRegionNext = ScRegions.RollRegion(weights, rng.Next());
            PushLog("Суд души: " + j.verdict + ". " + j.summary, "life");
            return j;
        }

        /// Экран суда после перезагрузки: итоги уже в душе — восстанавливаем картину из сохранённых данных.
        public JudgmentResult JudgmentView()
        {
            if (lastJudgment != null) return lastJudgment;
            if (data.deathCause == null || data.soul == null || data.judged == 0) return null;
            var j = new JudgmentResult
            {
                ageDays = 0,
                cause = data.deathCauseCode != null ? data.deathCauseCode : "killed",
                karma = data.soul.karma,
                verdict = data.soul.karma >= 40 ? "Свет" : data.soul.karma <= -40 ? "Тьма" : "Равновесие",
                xpGained = 0,
                summary = "Воплощение №" + Math.Max(1, data.soul.life - 1) + " завершено. Итоги суда уже в душе.",
                newSoulLevel = data.soul.level,
                levelUp = false,
                regionOdds = data.judgmentOdds != null && data.judgmentOdds.Count > 0
                    ? data.judgmentOdds
                    : ScRegions.NormalizeWeights(ScRegions.BirthWeights(data.soul.traits, data.soul.karma, data)),
                regionReasons = ScRegions.ExplainBirth(data.soul.traits, data.soul.karma, data)
            };
            return j;
        }

        // ===== Опасности региона (regions.ts updateRegion + world.ts hazard): надземье, живое тело =====
        void UpdateRegionHazards(double dt)
        {
            if (DungeonActive) return;   // в норке/подземелье региональная погода не достаёт
            RegionDef def = ScRegions.Region(data.birthRegion);
            var p = data.player;
            if (def.hazard == "poison_spores")
            {
                bool protectedBy = p.genome.installed.Contains("spore_veil") || p.genome.installed.Contains("spore_cloud");
                Poi grove = data.Poi("mushroom_grove");
                bool inGrove = grove != null && ScRng.Dist(p.x, p.y, grove.x, grove.y) < grove.r + 1.5;
                if (!protectedBy && inGrove)
                {
                    poisonAcc += dt;
                    if (!poisonLogged)
                    {
                        poisonLogged = true;
                        PushLog("Ядовитые споры Сада жалят незащищённое тело (споровая защита спасла бы).", "life");
                    }
                    if (poisonAcc >= 8)
                    {
                        poisonAcc = 0;
                        p.hp -= 1; p.hitFlash = 1;
                        PushFx("dmg", p.x, p.y, "-1", "150,220,120");
                        if (p.hp <= 0) KillPlayer("killed", "Ядовитые споры Сада убили тело.");
                    }
                }
                else { poisonAcc = 0; poisonLogged = false; }
            }
            if (def.hazard == "rift_storm")
            {
                Poi rift = data.Poi("rift");
                bool nearRift = rift != null && ScRng.Dist(p.x, p.y, rift.x, rift.y) < 12;
                if (stormT > 0)
                {
                    stormT = Math.Max(0, stormT - dt);
                    if (nearRift)
                    {
                        stormHit += dt;
                        if (stormHit >= 2)
                        {
                            stormHit = 0;
                            p.hp -= 1; p.hitFlash = 1;
                            PushFx("dmg", p.x, p.y, "-1", "190,120,235");
                            if (p.hp <= 0) KillPlayer("killed", "Буря Разлома выжгла тело.");
                        }
                    }
                    if (stormT == 0)
                    {
                        stormSurvived++;
                        if (stormSurvived >= 3) data.riftStormSurvived = 1;   // глубина души в суде
                        PushLog("Буря Разлома стихла. Воздух звенит остаточной магией.", "world");
                    }
                }
                else
                {
                    stormCd -= dt;
                    if (stormCd <= 0)
                    {
                        stormCd = 25; stormT = 5; stormHit = 0;
                        PushLog("БУРЯ РАЗЛОМА: реальность трещит, магические потоки выжигают всё живое.", "world");
                    }
                }
            }
        }

        // ===== Проходимость =====
        public bool IsWalkable(double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)) return false;
            if (DungeonActive)
            {
                var f = Dun.floors[Dun.depth - 1];
                int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
                if (xi < 0 || yi < 0 || xi >= f.w || yi >= f.h) return false;
                return f.terrain[yi * f.w + xi] != "rock";
            }
            return IsSurfaceWalkable(x, y);
        }

        private bool IsSurfaceWalkable(double x, double y)
        {
            var m = data.mapJson;
            int tx = (int)Math.Floor(x), ty = (int)Math.Floor(y);
            if (tx < 0 || ty < 0 || tx >= m.w || ty >= m.h) return false;
            string t = m.terrain[ty * m.w + tx];
            if (t == "water" || t == "mountain") return false;
            foreach (var deco in m.deco)
            {
                if (deco.r <= 0) continue;
                if (deco.kind == "crack" && deco.r >= 0.5)
                {
                    // тайная трещина: узкий проход только для малого тела (п.3 карты мира —
                    // маленький слизень пролезает туда, куда не пролезет эволюционировавшая форма)
                    if (ScRng.Dist(deco.x, deco.y, x, y) < deco.r * 0.8)
                    {
                        if (data.player.genome.installed.Count > ScAtlas.CRACK_MAX_ORGANS)
                        {
                            if (crackHintT <= 0)
                            {
                                crackHintT = 6;
                                PushLog("Трещина слишком узка для вашего взрослого тела. Малый слизень прошёл бы… (нужно не больше 1 установленного гена)", "life");
                            }
                            return false;
                        }
                        continue; // малое тело проскальзывает сквозь трещину
                    }
                    continue;
                }
                if (ScRng.Dist(deco.x, deco.y, x, y) < deco.r * 0.8) return false;
            }
            return true;
        }

        public bool WalkableRadius(double x, double y, double r)
        {
            if (!IsWalkable(x, y)) return false;
            return IsWalkable(x + r, y) && IsWalkable(x - r, y) && IsWalkable(x, y + r) && IsWalkable(x, y - r);
        }

        private void MoveWithCollision(Creature c, double dx, double dy, double r = 0.28)
        {
            float px = c.x, py = c.y;
            double tryX = px + dx;
            if (WalkableRadius(tryX, py, r)) c.x = (float)tryX;
            double tryY = c.y + dy;
            if (WalkableRadius(c.x, tryY, r)) c.y = (float)tryY;
        }

        private void MovePlayerWithCollision(double dx, double dy, double r = 0.28)
        {
            var p = data.player;
            double tryX = p.x + dx;
            if (WalkableRadius(tryX, p.y, r)) p.x = (float)tryX;
            double tryY = p.y + dy;
            if (WalkableRadius(p.x, tryY, r)) p.y = (float)tryY;
        }

        // ===== Существа =====
        public List<Creature> ActiveCreatures()
        {
            var res = new List<Creature>();
            string dunId = DungeonActive ? data.activeDungeon : null;
            int depth = DungeonActive ? Dun.depth : 0;
            foreach (var c in data.creatures)
            {
                if (c.dungeonFloor != depth) continue;
                if (depth > 0 && c.dungeonId != dunId) continue;
                res.Add(c);
            }
            return res;
        }

        public Creature SpawnCreature(string species, Vec pos, int gen, string dungeonId = null, int dungeonFloor = 0)
        {
            var def = Species.Get(species);
            if (def == null) return null;
            var c = new Creature
            {
                id = data.nextId++,
                species = species,
                x = pos.x, y = pos.y,
                hp = def.maxHp,
                ageDays = rng.Range(def.lifespan * 0.1, def.lifespan * 0.6),
                ai = "wander",
                gen = gen,
                dungeonId = dungeonId,
                dungeonFloor = dungeonFloor,
            };
            data.creatures.Add(c);
            return c;
        }

        /// Публичный спавн для систем над миром (NPC-сюжеты, трещина, подземелья).
        public Creature SpawnCreaturePublic(string species, Vec pos)
        {
            return SpawnCreature(species, pos, data.dayTicks / 6 + 1);
        }

        /// Ополчение: два стража держат строй у ограды (world.ts spawnGuards).
        void SpawnGuards()
        {
            Poi v = data.Poi("village");
            if (v == null) return;
            for (int i = 0; i < 2; i++)
            {
                double gx = v.x + (i == 1 ? 1.5 : -1.5), gy = v.y + 2.8;
                Creature g = SpawnCreature("guard", Vec.Of(gx, gy), 1);
                if (g != null)
                {
                    g.ai = "guard";
                    g.homeX = (float)gx; g.homeY = (float)gy; g.hasHome = true;
                }
            }
        }

        /// Численность вида в надземном слое (web popCount; §6 уровень C).
        public int PopCount(string species)
        {
            int n = 0;
            foreach (var c in data.creatures)
                if (c.species == species && !c.dead && c.dungeonFloor == 0) n++;
            return n;
        }

        // ===== NPC посёлка (§15) =====
        public Npc Npc(string id)
        {
            foreach (var n in data.npcs) if (n.id == id) return n;
            return null;
        }

        public Vec SpawnPosNear(Poi poi, double rMin = 0.5, double? rMax = null)
        {
            double rMaxE = rMax ?? poi.r;
            for (int i = 0; i < 30; i++)
            {
                double a = rng.Next() * Math.PI * 2;
                double r = rng.Range(rMin, rMaxE);
                double x = poi.x + Math.Cos(a) * r, y = poi.y + Math.Sin(a) * r * 0.85;
                if (IsSurfaceWalkable(x, y)) return Vec.Of(x, y);
            }
            return Vec.Of(poi.x, poi.y);
        }

        private void SpawnInitialPops()
        {
            var plan = new List<KeyValuePair<string, string>>
            {
                KVP("slug", "village"), KVP("slug", "village"), KVP("slug", "pond"), KVP("slug", "pond"),
                KVP("slug", "mushroom_grove"), KVP("slug", "mushroom_grove"), KVP("slug", "burrow"),
                KVP("beetle", "village"), KVP("beetle", "mushroom_grove"), KVP("beetle", "burrow"),
                KVP("firefly", "pond"), KVP("firefly", "stone_circle"), KVP("firefly", "mushroom_grove"),
                KVP("rabbit", "village"), KVP("rabbit", "burrow"), KVP("rabbit", "mushroom_grove"),
                KVP("deer", "burrow"), KVP("wolf", "burrow"), KVP("toad", "pond"), KVP("toad", "pond"),
                KVP("spider", "mushroom_grove"), KVP("spider", "burrow"),
                KVP("mushling", "mushroom_grove"), KVP("mushling", "mushroom_grove"), KVP("mushling", "mushroom_grove"),
                KVP("crystalslug", "crystal_hollow"), KVP("crystalslug", "crystal_hollow"),
                KVP("spirit", "stone_circle"),
            };
            foreach (var kv in plan)
                SpawnCreature(kv.Key, SpawnPosNear(data.Poi(kv.Value)), 1);
        }

        static KeyValuePair<string, string> KVP(string a, string b) { return new KeyValuePair<string, string>(a, b); }

        // ===== Главный шаг (§7) =====
        public void Update(double dtReal, string speedName)
        {
            if (data.deathCause != null || speedName == "pause" || dtReal <= 0 || double.IsNaN(dtReal) || double.IsInfinity(dtReal)) return;
            dtReal = Math.Min(dtReal, 0.1);
            HealPlayerPosition();
            double speed = ScTime.SpeedOf(speedName);
            bool battle = IsInBattle();
            if (battle && speedName != "fast") speed = ScTime.SPEED_BATTLE;   // бой течёт медленнее (§7)
            double dtSim = Math.Min(dtReal, 0.05);
            shakeT = Math.Max(0, shakeT - dtReal);
            int crossedDays = clock.Advance(dtReal * speed);
            data.minutes = clock.minutes;
            data.dayTicks = clock.dayTickCounter;
            for (int day = 0; day < crossedDays && data.deathCause == null; day++) DailyTick(1);
            if (data.deathCause != null) return;

            var p = data.player;

            if (speedName != "fast")
            {
                UpdatePlayer(dtSim);
                foreach (var c in ActiveCreatures()) UpdateCreature(c, dtSim);
            }
            else
            {
                p.vx = 0; p.vy = 0;
                UpdatePlayer(dtSim);
                p.hp = Math.Min(stats.maxHp, p.hp + stats.hpRegen * dtReal * 5 + 2 * dtReal);
                p.energy = Math.Min(stats.energyMax, p.energy + dtReal * 2);
                foreach (var c in ActiveCreatures()) UpdateCreature(c, dtSim * 0.5);
            }

            TickDigestion(dtSim);
            crackHintT = Math.Max(0, crackHintT - dtReal);
            UpdateProximity();
            UpdateSenses(dtReal);
            ScSenses.TickSenses(data.senses, dtReal);
            UpdateRegionHazards(dtReal);   // погода региона течёт в реальном времени (как в вебе)
            UpdateAtlasDiscovery();        // «Чаша Отголосов»: регион открывается ногами/чутьём (п.9)
        }

        /// Флаг startRaid — движок открывает Magic Chess и сбрасывает флаг (engine.ts update).
        public bool ConsumeStartRaid()
        {
            if (data.Flag("startRaid") != 1) return false;
            data.flags.Remove("startRaid");
            return true;
        }

        /// Движок забирает событие открытия места для тоста (consume).
        public string ConsumeRegionDiscovery()
        {
            string id = data.lastDiscoveredRegion;
            data.lastDiscoveredRegion = null;
            return id;
        }

        // ===== Сезоны года (Этап 2 карты): экология дышит по календарю =====
        /// Текущий сезон. Норка/подземелья — мини-вселенная: туда сезоны не доходят.
        public string Season { get { return ScSeasons.SeasonOf(clock.TotalDays); } }
        /// Модификаторы сезона; в подземельях — нейтральные (свой микроклимат).
        public SeasonMods SeasonMods
        {
            get
            {
                if (seasonModsCache != null) return seasonModsCache;
                seasonModsCache = DungeonActive || data.activeDungeon != null ? ScSeasons.Neutral : ScSeasons.Mods(Season);
                return seasonModsCache;
            }
        }
        public void InvalidateSeasonCache() { seasonModsCache = null; }

        // ===== «Чаша Отголосов»: открытие мировых регионов (п.6/9) =====
        public bool IsRegionDiscovered(string id)
        {
            int v;
            return data.worldRegions != null && data.worldRegions.TryGetValue(id, out v) && v == 1;
        }

        /// Текущий мировой регион: подземный по слою или поверхностный по точке.
        public string CurrentWorldRegionId()
        {
            if (DungeonActive && data.activeDungeon != null) return ScAtlas.DungeonRegionId(data.activeDungeon);
            return ScAtlas.RegionAt(data.player.x, data.player.y).id;
        }

        public string CurrentWorldRegionName()
        {
            var def = ScAtlas.ById(CurrentWorldRegionId());
            return def != null ? def.name : "Неизвестные земли";
        }

        void DiscoverRegion(string id)
        {
            if (IsRegionDiscovered(id)) return;
            var def = ScAtlas.ById(id);
            if (def == null || def.sealedArea) return; // запечатанные Глубины не открываются (Этап 4)
            data.worldRegions[id] = 1;
            data.lastDiscoveredRegion = id;
            PushLog("Вы открыли место: " + def.name + ". " + def.ecology, "world");
        }

        void UpdateAtlasDiscovery() { DiscoverRegion(CurrentWorldRegionId()); }

        private bool IsInBattle()
        {
            var p = data.player;
            foreach (var c in ActiveCreatures())
            {
                if (c.dead || Species.Get(c.species).diet != "carnivore") continue;
                if (ScRng.Dist(c.x, c.y, p.x, p.y) < 5.5) return true;
            }
            return false;
        }

        // ===== Игрок =====
        private void UpdatePlayer(double dt)
        {
            var p = data.player;
            p.atkCd = Math.Max(0, p.atkCd - dt);
            p.attackAnim = Math.Max(0, p.attackAnim - dt * 2.6);
            p.hitFlash = Math.Max(0, p.hitFlash - dt * 3);

            double spd = Math.Sqrt(p.vx * p.vx + p.vy * p.vy);
            if (spd > 0.001)
            {
                double step = spd * dt;
                MovePlayerWithCollision(p.vx / spd * step, p.vy / spd * step);
            }
            // энергия и регенерация
            bool moving = spd > 0.2;
            if (!moving) p.energy = Math.Min(stats.energyMax, p.energy + dt * 0.9);
            else p.energy = Math.Max(0, p.energy - dt * 0.14);
            p.hp = Math.Min(stats.maxHp, p.hp + stats.hpRegen * dt);
            // открытие режимов пищеварения (§6.6)
            var opened = ScDigestion.UnlockModes(data.digestion, data.senses.biomassTotal);
            foreach (var m in opened)
                PushLog("Режим пищеварения открыт: «" + (m == "fast" ? "Ускоренный" : "Бережный") + "».", "life");
        }

        public void SetInput(double nx, double ny, bool sprint)
        {
            var p = data.player;
            if (data.deathCause != null) { p.vx = p.vy = 0; return; }
            if (double.IsNaN(nx) || double.IsInfinity(nx) || double.IsNaN(ny) || double.IsInfinity(ny)) { nx = ny = 0; }
            double len = Math.Sqrt(nx * nx + ny * ny);
            if (len > 1) { nx /= len; ny /= len; }
            double sp = stats.speed * (sprint && p.energy > 0 ? 1.42 : 1.0);
            p.vx = (float)(nx * sp); p.vy = (float)(ny * sp);
        }

        // ===== Существа: FSM AI =====
        private void UpdateCreature(Creature c, double dt)
        {
            var def = Species.Get(c.species);
            if (c.dead)
            {
                c.corpseT -= dt;
                return;
            }
            c.ageDays += 0;   // старение существ — только в DailyTick (§6, игровые дни)
            c.atkCd = Math.Max(0, c.atkCd - dt);
            c.hitFlash = Math.Max(0, c.hitFlash - dt * 3);
            c.stunT = Math.Max(0, c.stunT - dt);
            c.slowT = Math.Max(0, c.slowT - dt);
            c.stateT -= dt;
            if (c.stateT <= 0 && c.ai != "guard")
            {
                c.ai = rng.Chance(0.5) ? "wander" : "idle";
                c.stateT = rng.Range(1.5, 4);
                c.hasWanderTo = false;
            }

            var p = data.player;
            double d = ScRng.Dist(c.x, c.y, p.x, p.y);
            double speedMul = c.slowT > 0 ? 0.5 : 1;
            double spd = def.speed * speedMul;

            if (c.stunT > 0) { c.vx = 0; c.vy = 0; return; }

            // агро хищников и защита
            // путь Тьмы (§12): родные хищники не трогают своего; сезонный голод (Этап 2) дерзит зимой
            bool kin = KarmaPath() == "Тьма" && !c.aggroByPlayer
                       && (c.species == "wolf" || c.species == "spider" || c.species == "toad");
            SeasonMods sm = SeasonMods;
            double seasonAggro = 1 + Math.Max(0, sm.aggressionAdd) * 1.5 + Math.Min(0, sm.aggressionAdd) * 0.6;
            double aggroR = 4.2 * ScRegions.Region(data.birthRegion).mods.aggressionMul * seasonAggro;
            if (!c.dead && !kin && def.diet == "carnivore" && d < aggroR && (c.aggroByPlayer || rng.Chance(def.aggression * dt * 0.6 * seasonAggro)))
            { c.ai = "chase"; c.stateT = 6; }

            double nx = 0, ny = 0;
            switch (c.ai)
            {
                case "wander":
                    if (!c.hasWanderTo)
                    {
                        c.wx = c.x + (float)rng.Range(-3, 3); c.wy = c.y + (float)rng.Range(-3, 3);
                        c.hasWanderTo = true;
                    }
                    if (StepToward(c, c.wx, c.wy, spd, dt)) c.hasWanderTo = false;
                    break;
                case "flee":
                    if (d > 0.01) { nx = (c.x - p.x) / d; ny = (c.y - p.y) / d; }
                    MoveWithCollision(c, nx * spd * dt, ny * spd * dt);
                    break;
                case "chase":
                    if (d > 1.0) { StepToward(c, p.x, p.y, spd, dt); }
                    else if (c.atkCd <= 0)
                    {
                        c.atkCd = 1.1;
                        double dmg = Math.Max(1, def.atk * rng.Range(0.85, 1.15) - stats.def * 0.5);
                        data.player.hp -= dmg;
                        data.player.hitFlash = 1;
                        shakeT = 0.28;
                        PushFx("hit", p.x, p.y, null, "220,90,90");
                        if (data.player.hp <= 0) KillPlayer("killed", "Вас разорвал " + def.name + ".");
                    }
                    break;
                case "guard":
                    if (c.hasHome)
                    {
                        double dh = ScRng.Dist(c.x, c.y, c.homeX, c.homeY);
                        if (dh > 2.2) StepToward(c, c.homeX, c.homeY, spd, dt);
                    }
                    break;
                default:
                    c.vx = 0; c.vy = 0;
                    break;
            }
        }

        private bool StepToward(Creature c, double tx, double ty, double spd, double dt)
        {
            double dx = tx - c.x, dy = ty - c.y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.2) { c.vx = 0; c.vy = 0; return true; }
            double step = Math.Min(spd * dt, d);
            MoveWithCollision(c, dx / d * step, dy / d * step);
            c.vx = (float)(dx / d * spd); c.vy = (float)(dy / d * spd);
            return false;
        }

        // ===== Бой игрока =====
        public void PlayerAttack()
        {
            var p = data.player;
            if (data.deathCause != null || p.atkCd > 0 || p.energy < 1) return;
            p.atkCd = 0.65;
            p.energy = Math.Max(0, p.energy - 1);
            p.attackAnim = 1;
            Creature target = null;
            double bd = 1.35;
            foreach (var c in ActiveCreatures())
            {
                if (c.dead) continue;
                double dd = ScRng.Dist(c.x, c.y, p.x, p.y);
                if (dd < bd) { bd = dd; target = c; }
            }
            if (target == null) return;
            var def = Species.Get(target.species);
            double dmg = Math.Max(1, stats.atk * rng.Range(0.85, 1.15) - def.def * 0.5);
            if (KarmaPath() == "Тьма" && p.hp < stats.maxHp * 0.5) dmg *= 1.2;
            target.hp -= dmg;
            target.hitFlash = 1;
            PushFx("dmg", target.x, target.y, "-" + Math.Round(dmg), "235,210,90");
            target.aggroByPlayer = true;
            if (def.diet == "herbivore" || def.diet == "detritivore") { target.ai = "flee"; target.stateT = 4; }
            else { target.ai = "chase"; target.stateT = 8; }
            // электрическая ловушка (синергия)
            if (stats.synergies.Exists(s => s.special == "stun") && rng.Chance(0.18)) target.stunT = 1.4;
            if (target.hp <= 0) KillCreature(target, "player");
        }

        public void KillCreature(Creature c, string by)
        {
            if (c.dead) return;
            c.dead = true;
            c.corpseT = 90;
            c.ai = "dead";
            var def = Species.Get(c.species);
            if (by == "player")
            {
                data.player.kills++;
                PushFx("kill", c.x, c.y, null, def.sentient ? "201,160,220" : "150,190,110");
                if (def.sentient) Karma("kill_sentient", "Убийство разумного: " + def.name, -20, "bad");
                // вожак стаи (§12 пощада + посёлок): стая уходит, оборона решена без шахмат
                if (c.questTag == "alpha" && data.Flag("alphaKilled") == 0)
                {
                    data.SetFlag("alphaKilled", 1);
                    data.alphaKilled = 1;
                    OnAlphaKilled();
                }
                if (c.species == "wolf") data.flagWolvesKilled++;   // ретроактивный прогресс задания «Три шкуры»
                // мир помнит павших владык подземелий (веса рождения + достижения души)
                if (c.species == "myco_monarch") data.monarchKilled = 1;
                if (c.species == "crystal_titan") data.titanKilled = 1;
                // боссы подземелий (Регион 2/3): фирменный ген — гарантированная добыча
                foreach (var ddef in ScDungeon.Defs)
                {
                    if (c.questTag == ddef.bossTag && !string.IsNullOrEmpty(ddef.bossFlag) && data.Flag(ddef.bossFlag) == 0)
                    {
                        if (ddef.bossFlag == "monarchKilled") data.monarchKilled = 1;
                        if (ddef.bossFlag == "titanKilled") data.titanKilled = 1;
                        data.SetFlag(ddef.bossFlag, 1);
                        data.player.biomass += ddef.bossReward;
                        if (!string.IsNullOrEmpty(ddef.bossGene) && !data.player.knownGenes.Contains(ddef.bossGene))
                        {
                            data.player.knownGenes.Add(ddef.bossGene);
                            GeneDef bg;
                            if (Genes.All.TryGetValue(ddef.bossGene, out bg))
                                PushLog("Из праха павшего вы извлекли «" + bg.name + "».", "life");
                        }
                        PushLog(ddef.bossName + " рассыпается мерцающей пылью. Подземелье выдыхает.", "world");
                        data.PushToast(ddef.bossName + " повержен(а)! Тьма подземелья отступает.", "epic");
                    }
                }
                // прогресс заданий: убийства и боссы (§13)
                TrackQuestKill(c.species, c.questTag);
            }
        }

        /// Вожак стаи пал: стая уходит из региона (пощада, §12), посёлок вздыхает свободнее.
        void OnAlphaKilled()
        {
            var wolves = data.creatures.FindAll(cc => cc.species == "wolf" && !cc.dead);
            int keep = Math.Min(2, wolves.Count);
            foreach (var w in data.creatures)
            {
                if (w.species != "wolf" || w.dead) continue;
                if (wolves.IndexOf(w) < keep) continue;
                w.dead = true; w.absorbed = true; w.corpseT = 0;   // уходят за край леса
            }
            data.player.biomass += 40;
            Npc dar = Npc("dar");
            if (dar != null) dar.attitude = ScRng.Clamp(dar.attitude + 10, 0, 100);
            data.village.raidCdDays = 5;
            if (data.village.raidPending)
            {
                data.village.raidPending = false;
                data.village.raidsSurvived++;
            }
            PushLog("Вожак стаи пал. Остальные волки ушли в глубину леса.", "world");
        }

        /// Смерть тела: code — машиночитаемая причина (old_age|killed|mutation), text — для журнала/экрана.
        public void KillPlayer(string code, string text)
        {
            if (data.deathCause != null) return;   // смерть наступает один раз (§18 идемпотентность)
            data.player.hp = 0;
            data.deathCauseCode = code;
            data.deathCause = text;
            PushLog(text, "life");
            RunJudgment();   // суд применяется сразу и один раз — итоги переживают перезагрузку
        }

        // ===== Поглощение (§5) → пищеварение (§6) =====
        public bool AbsorbNearest()
        {
            if (data.deathCause != null) return false;
            Creature best = null;
            double bd = 1.2;
            foreach (var c in ActiveCreatures())
            {
                if (!c.dead || c.absorbed || c.corpseT <= 0) continue;
                double dd = ScRng.Dist(c.x, c.y, data.player.x, data.player.y);
                if (dd < bd) { bd = dd; best = c; }
            }
            if (best == null) return false;
            var def = Species.Get(best.species);
            RegionMods rm = ScRegions.Region(data.birthRegion).mods;
            double mass = rng.Range(def.bioMin, Math.Max(def.bioMin, def.bioMax)) * rm.biomassMul;
            string reason;
            if (!ScDigestion.CanAbsorb(data.digestion, AbsorbCapacity(), mass, out reason))
            {
                data.PushToast(reason, "warn");
                return false;
            }

            double[] comp;
            if (!Food.Prey.TryGetValue(best.species, out comp)) comp = new[] { 0.0, 1.0, 0.0 };
            double[] diet;
            if (!SensesData.DietFeed.TryGetValue(best.species, out diet)) diet = new double[0];
            double ore;
            double oreYield = SensesData.OreYield.TryGetValue(best.species, out ore) ? ore : 0;
            if (oreYield > 0) oreYield += rm.oreBonus;   // Забытая жила богаче рудой (п.7)

            var item = new StoredItem
            {
                kind = (int)comp[0],
                name = "Останки: " + def.name,
                species = best.species,
                mass = mass,
                hardness = comp[1],
                diet = diet,
                oreYield = oreYield,
                genePool = def.geneDrop,
                geneChance = def.geneChance * rm.geneChanceMul,   // Костяные теснины щедрее на гены (§ п.7)
                toxicity = comp[2],
                progress = 0,
                seed = rng.Int(0, 999999),
            };
            ScDigestion.PutItem(data.digestion, item);
            best.absorbed = true;
            data.player.absorbedCount++;
            // события захвата: открытия, карма, задания — в момент захвата (§5.4, §11, §13)
            if (!data.player.discoveredSpecies.Contains(best.species))
            {
                data.player.discoveredSpecies.Add(best.species);
                PushLog("Строение существа изучено: " + def.name + ".", "life");
                if (def.sentient) Karma("absorb_sentient", "Поглощение разумного: " + def.name, -15, "bad");
            }
            if (best.species == "spirit") data.spiritAbsorbed = 1;   // воспоминание m_rift + глубина души
            Karma("absorb_" + best.species, "Поглощение: " + def.name, def.sentient ? -8 : 0, def.sentient ? "bad" : "neutral");
            PushFx("absorb", best.x, best.y, "+" + Math.Round(mass), "154,200,96");
            // прогресс заданий: поглощения (§13)
            TrackQuestAbsorb(best.species);
            return true;
        }

        public double AbsorbCapacity() { return ScDigestion.CapacityOf(data.player.biomass, data.player.genome.slots); }
        public double DigestivePressure() { return ScDigestion.PressureOf(data.digestion, AbsorbCapacity()); }

        // ===== Переваривание (§6) и его плоды (§7) =====
        private void TickDigestion(double dt)
        {
            var dig = data.digestion;
            var completed = new List<StoredItem>();
            double enzyme = ScDigestion.EnzymeStrength(data.player.genome.installed.Count, data.senses.biomassTotal);
            double load = 1 + DigestivePressure() * 0.5;
            ScDigestion.TickDigestive(dig, dt, enzyme, load, completed);
            // ускоренный режим жжёт энергию
            double drain = ScDigestion.ModeEnergyDrain(dig.mode);
            data.player.energy = Math.Max(0, data.player.energy - drain * dt * 10);
            foreach (var it in completed) CompleteDigestion(it);
        }

        private void CompleteDigestion(StoredItem it)
        {
            // биомасса тела (§7.1)
            data.player.biomass += it.mass;
            // нерастворимый остаток (§7)
            double[] ki;
            double residue = Food.KindInfo.TryGetValue(Food.KindName(it.kind), out ki) ? ki[1] : 0.06;
            data.digestion.insoluble += it.mass * residue;
            // питание рецепторов (Дополнение: диета); сезонная питательность пищи (Этап 2 — Увядание 1.3, Тьма 0.8)
            double dietMul = SeasonMods.dietMul;
            double[] effDiet = it.diet;
            if (dietMul != 1 && it.diet != null)
            {
                effDiet = new double[it.diet.Length];
                for (int i = 0; i < it.diet.Length; i++) effDiet[i] = i % 2 == 1 ? it.diet[i] * dietMul : it.diet[i];
            }
            var newly = ScSenses.GrantBiomass(data.senses, it.mass, SenseThrMul(), effDiet);
            foreach (var organ in newly)
            {
                PushLog("Пробуждение: " + OrganName(organ) + ". Мир стал немного другим.", "life");
                data.PushToast("Новое чувство: " + OrganName(organ), "epic");
            }
            // руда (п.8)
            if (it.oreYield > 0) GainOre(it.oreYield);
            // риск отравления
            if (it.toxicity > 0 && rng.Chance(it.toxicity * ScDigestion.ModeToxMul(data.digestion.mode) * 0.5))
            {
                data.player.hp = Math.Max(1, data.player.hp - 3);
                PushLog("Токсин обжёг изнутри. Тело дрожит.", "life");
            }
            // раскрытие гена (§7.2 генетическая память)
            if (it.genePool != null && it.genePool.Length > 0 && rng.Chance(it.geneChance * ScDigestion.ModeGeneMul(data.digestion.mode)))
            {
                string gene = it.genePool[rng.Int(0, it.genePool.Length - 1)];
                if (!data.player.knownGenes.Contains(gene))
                {
                    data.player.knownGenes.Add(gene);
                    GeneDef g;
                    if (Genes.All.TryGetValue(gene, out g))
                        PushLog("Генетическая память: раскрыт ген «" + g.name + "» (Геном).", "life");
                }
            }
            // синергии
            var d2 = ScGenome.ComputeDerived(data.player.genome);
            foreach (var s in d2.synergies)
                if (!data.synDiscovered.Contains(s.id)) { data.synDiscovered.Add(s.id); PushLog("Синергия: «" + s.name + "». " + s.desc, "life"); }
            // старение чуть-чуть замедляется от еды
            if (data.player.hp <= 0) KillPlayer("mutation", "Тело не выдержало.");
        }

        private Dictionary<string, double> SenseThrMul()
        {
            var m = new Dictionary<string, double>();
            // «Резонанс души» (п.6): глубина души ускоряет рецепторы, но не даёт зрения
            double depth = data.soul != null && data.soul.traits != null ? data.soul.traits.depth : 0;
            double thrBoost = Math.Min(0.25, depth * 0.0025);
            if (thrBoost > 0)
                foreach (var o in new[] { "chemo", "vibration", "thermal", "light", "vision", "hearing" }) m[o] = 1 - thrBoost;
            return m;
        }

        public static string OrganName(string organ)
        {
            switch (organ)
            {
                case "touch": return "Осязание";
                case "chemo": return "Химическое восприятие";
                case "vibration": return "Вибрационное восприятие";
                case "thermal": return "Тепловое восприятие";
                case "light": return "Светочувствительность";
                case "vision": return "Зрение";
                case "hearing": return "Слух";
                case "spirit": return "Духовное чутьё";
            }
            return organ;
        }

        // ===== Руда → резонансное ядро (п.8) =====
        private void GainOre(double n)
        {
            var sn = data.senses;
            ScSenses.GrantOre(sn, n, coreActive);
            PushLog("Усвоена магическая руда: +" + n + " (всего " + Math.Floor(sn.ore) + ").", "life");
            if (!coreActive && sn.ore >= SensesData.ORE_CORE_THRESHOLD)
            {
                data.senseCore = 1;
                coreActive = true;
                PushLog("Из руды в теле сплетается резонансное ядро — духовное чутьё пробуждается.", "life");
                data.PushToast("Резонансное ядро сплетено", "epic");
            }
        }

        // ===== Геном: установка/снятие/узлы =====
        public bool InstallGene(string id)
        {
            string reason;
            if (!ScGenome.CanInstall(data.player.genome, id, out reason)) { data.PushToast(reason, "warn"); return false; }
            var g = Genes.All[id];
            if (data.player.biomass < g.cost) { data.PushToast("Не хватает биомассы: нужно " + g.cost, "warn"); return false; }
            data.player.biomass -= g.cost;
            data.player.genome.installed.Add(id);
            RefreshStats();
            if (data.player.hp > stats.maxHp) data.player.hp = stats.maxHp;
            PushLog("Ген «" + g.name + "» вплетён в тело.", "life");
            return true;
        }

        public bool RemoveGene(string id)
        {
            var p = data.player;
            if (!p.genome.installed.Contains(id)) return false;
            p.genome.installed.Remove(id);
            data.player.mutations++;
            RefreshStats();
            p.hp = Math.Min(p.hp, stats.maxHp);
            PushLog("Ген выплеснут: тело перестроилось (мутация).", "life");
            return true;
        }

        public bool BuyNode()
        {
            double? cost = NodePlan.NextCost(data.player.genome.nodes);
            if (cost == null) return false;
            if (data.player.biomass < cost.Value) { data.PushToast("Не хватает биомассы на узел эволюции", "warn"); return false; }
            data.player.biomass -= cost.Value;
            data.player.genome.nodes++;
            data.player.genome.slots += 1;
            data.player.genome.capacity += 1;
            RefreshStats();
            PushLog("Эволюционный узел: +1 слот, +1 энергии генома.", "life");
            return true;
        }

        // ===== Восприятие: скан осязания, отпечатки, разведка =====
        private Dictionary<int, Tuple<HashSet<string>, double>> senseMap = new Dictionary<int, Tuple<HashSet<string>, double>>();

        private void UpdateSenses(double dt)
        {
            senseTime += dt;
            var p = data.player;
            double tR = TouchRadius();
            foreach (var c in ActiveCreatures())
            {
                double d = ScRng.Dist(c.x, c.y, p.x, p.y);
                Tuple<HashSet<string>, double> e;
                if (!senseMap.TryGetValue(c.id, out e)) { e = Tuple.Create(new HashSet<string>(), -99.0); senseMap[c.id] = e; }
                var organs = new HashSet<string>();
                bool Has(string organ) { return Array.IndexOf(data.senses.unlocked, organ) >= 0; }
                bool moving = c.vx * c.vx + c.vy * c.vy > 0.12 * 0.12;
                bool warm = c.species == "rabbit" || c.species == "deer" || c.species == "wolf" || c.species == "guard";
                bool glowy = c.species == "firefly" || c.species == "spirit" || c.species == "myco_monarch";
                if (d <= tR + 0.15) organs.Add("touch");
                if (Has("chemo") && d <= 3.6 * (c.dead ? 1.5 : 1)) organs.Add("chemo");
                if (Has("vibration") && !c.dead && moving && d <= 4.6) organs.Add("vibration");
                if (Has("thermal") && d <= 4.0 * (warm ? 1 : 0.55)) organs.Add("thermal");
                if (Has("light") && d <= 5.5 * (glowy ? 1 : 0.5)) organs.Add("light");
                if (Has("vision") && d <= VisionRadius()) organs.Add("vision");
                if (Has("hearing") && !c.dead && d <= 6.0 && (moving || Species.Get(c.species).diet == "carnivore")) organs.Add("hearing");
                if (coreActive && d <= 7.5) organs.Add("spirit");
                if (organs.Count > 0) senseMap[c.id] = Tuple.Create(organs, senseTime);
                else if (senseTime - e.Item2 > 8) senseMap.Remove(c.id);
            }
            // разведка тайлов вокруг (радиус осязания)
            MarkExploredAround(p.x, p.y, Math.Max(tR + 0.6, VisionRadius()));
        }

        public double TouchRadius()
        {
            double r = 1.75;
            if (data.senses.unlocked.Length >= 3) r += 0.15;
            return r;
        }

        public double VisionRadius()
        {
            if (Array.IndexOf(data.senses.unlocked, "vision") < 0) return 0;
            double r = 6.5;
            if (data.player.genome.installed.Contains("bioluminescent")) r *= 1.15;
            return r;
        }

        public bool IsEntitySensed(int id)
        {
            Tuple<HashSet<string>, double> e;
            return senseMap.TryGetValue(id, out e) && e.Item1.Count > 0 && senseTime - e.Item2 <= 8;
        }

        public bool IsEntityTouchedNow(int id)
        {
            Tuple<HashSet<string>, double> e;
            if (!senseMap.TryGetValue(id, out e)) return false;
            return e.Item1.Contains("touch") && senseTime - e.Item2 < 1.2;
        }

        // ===== Разведанные тайлы =====
        public bool IsTileExplored(int tx, int ty)
        {
            if (DungeonActive)
            {
                var f = Dun.floors[Dun.depth - 1];
                if (tx < 0 || ty < 0 || tx >= f.w || ty >= f.h) return false;
                if (Dun.depth - 1 >= Dun.explored.Count) return false;
                var ex = Dun.explored[Dun.depth - 1];
                return ex != null && ex.Contains(ty * f.w + tx);
            }
            return data.senses.explored.Contains(ty * data.mapJson.w + tx);
        }

        private void MarkExploredAround(double px, double py, double r)
        {
            if (DungeonActive)
            {
                var f = Dun.floors[Dun.depth - 1];
                while (Dun.explored.Count < Dun.depth) Dun.explored.Add(new List<int>());
                var ex = Dun.explored[Dun.depth - 1];
                if (ex == null) { ex = new List<int>(); Dun.explored[Dun.depth - 1] = ex; }
                int g0x = Math.Max(0, (int)Math.Floor(px - r)), g1x = Math.Min(f.w - 1, (int)Math.Floor(px + r));
                int g0y = Math.Max(0, (int)Math.Floor(py - r)), g1y = Math.Min(f.h - 1, (int)Math.Floor(py + r));
                for (int ty = g0y; ty <= g1y; ty++)
                    for (int tx = g0x; tx <= g1x; tx++)
                    {
                        double ddx = tx + 0.5 - px, ddy = ty + 0.5 - py;
                        if (Math.Sqrt(ddx * ddx + ddy * ddy) > r) continue;
                        int i = ty * f.w + tx;
                        if (!ex.Contains(i)) ex.Add(i);
                    }
                return;
            }
            var m = data.mapJson;
            int a0x = Math.Max(0, (int)Math.Floor(px - r)), a1x = Math.Min(m.w - 1, (int)Math.Floor(px + r));
            int a0y = Math.Max(0, (int)Math.Floor(py - r)), a1y = Math.Min(m.h - 1, (int)Math.Floor(py + r));
            for (int ty = a0y; ty <= a1y; ty++)
                for (int tx = a0x; tx <= a1x; tx++)
                {
                    double dx = tx + 0.5 - px, dy = ty + 0.5 - py;
                    if (Math.Sqrt(dx * dx + dy * dy) > r) continue;
                    int i = ty * m.w + tx;
                    if (!data.senses.explored.Contains(i)) data.senses.explored.Add(i);
                }
        }

        // ===== Близость: трупы для [E], NPC, врата подземелий, лестницы, приносы =====
        public void UpdateProximity()
        {
            var p = data.player;
            nearCorpseId = -1;
            double best = 1.2;
            foreach (var c in ActiveCreatures())
            {
                if (c.dead && !c.absorbed && c.corpseT > 0)
                {
                    double d = ScRng.Dist(c.x, c.y, p.x, p.y);
                    if (d < best) { best = d; nearCorpseId = c.id; }
                }
            }
            nearNpcId = null; nearGateId = null;
            nearStairsUp = false; nearStairsDown = false;
            nearCacheId = -1;
            if (DungeonActive)
            {
                DungeonState dun = Dun;
                if (dun != null && dun.depth - 1 < dun.floors.Count)
                {
                    var f = dun.floors[dun.depth - 1];
                    if (dun.depth < dun.floors.Count && ScRng.Dist(f.downX, f.downY, p.x, p.y) < 1.25) nearStairsDown = true;
                    if (ScRng.Dist(f.upX, f.upY, p.x, p.y) < 1.25) nearStairsUp = true;
                }
                double bestC = 1.2;
                foreach (var cache in dun.caches)
                {
                    if (cache.taken || cache.depth != dun.depth) continue;
                    double d = ScRng.Dist(cache.x, cache.y, p.x, p.y);
                    if (d < bestC) { bestC = d; nearCacheId = cache.id; }
                }
            }
            else
            {
                foreach (var def in ScDungeon.Defs)
                {
                    Vec gate = def.gatePos(data);
                    if (ScRng.Dist(gate.x, gate.y, p.x, p.y) < 1.5) { nearGateId = def.id; break; }
                }
                double bestN = 1.3;
                foreach (var n in data.npcs)
                {
                    if (!n.alive) continue;
                    double d = ScRng.Dist(n.x, n.y, p.x, p.y);
                    if (d < bestN) { bestN = d; nearNpcId = n.id; }
                }
            }
        }

        // ===== Родовая норка: мини-вселенная (Дополнение) =====
        public DungeonState Dun
        {
            get
            {
                DungeonState s;
                return data.activeDungeon != null && data.dungeons.TryGetValue(data.activeDungeon, out s) ? s : null;
            }
        }

        public bool DungeonActive
        {
            get
            {
                if (data.activeDungeon == null) return false;
                DungeonState s;
                return data.dungeons.TryGetValue(data.activeDungeon, out s) && s.active;
            }
        }

        public bool EnterBirthNest()
        {
            if (data.activeDungeon == "birth_nest") return false;
            DungeonState dun;
            if (!data.dungeons.TryGetValue("birth_nest", out dun) || dun == null)
                dun = CreateNest(data);
            dun.active = true;
            dun.depth = 1;
            if (dun.explored.Count == 0) dun.explored.Add(new List<int>());
            dun.returnX = data.player.x; dun.returnY = data.player.y;
            data.activeDungeon = "birth_nest";
            InvalidateSeasonCache();   // мини-вселенная: свой микроклимат
            data.creatures.RemoveAll(c => c.dungeonId == "birth_nest");
            SpawnNestFloor(dun, data);
            var f0 = dun.floors[0];
            data.player.x = f0.downX + 0.5f; data.player.y = f0.downY + 0.9f;
            data.player.vx = 0; data.player.vy = 0;
            data.inNest = 1;
            return true;
        }

        public static DungeonState CreateNest(WorldData d)
        {
            var dun = new DungeonState { id = "birth_nest" };
            dun.explored.Add(new List<int>());
            d.dungeons["birth_nest"] = dun;
            return dun;
        }

        private void SpawnNestFloor(DungeonState dun, WorldData d)
        {
            if (dun.floors.Count > 0) return;
            // норка 18×13: стены-rock по периметру, гнездовая кладка, лаза-выход (up)
            const int w = 18, h = 13;
            var f = new DungeonFloor { depth = 1, w = w, h = h };
            f.terrain = new string[w * h];
            for (int i = 0; i < f.terrain.Length; i++) f.terrain[i] = "floor";
            for (int x = 0; x < w; x++) { f.terrain[x] = "rock"; f.terrain[(h - 1) * w + x] = "rock"; }
            for (int y = 0; y < h; y++) { f.terrain[y * w] = "rock"; f.terrain[y * w + w - 1] = "rock"; }
            f.downX = 9; f.downY = 2;   // кладка (старт комочка)
            f.upX = 9; f.upY = 10;      // лаза наружу
            f.terrain[(int)f.upY * w + (int)f.upX] = "floor";
            // немного «свечения» для атмосферы
            f.terrain[4 * w + 4] = "glow"; f.terrain[7 * w + 13] = "glow"; f.terrain[3 * w + 12] = "glow";
            dun.floors.Add(f);
            // живность норки: безопасная мелочь + принос
            for (int i = 0; i < 4; i++) SpawnCreature("slug", NestFreePos(f), 1, "birth_nest", 1);
            for (int i = 0; i < 2; i++) SpawnCreature("beetle", NestFreePos(f), 1, "birth_nest", 1);
            for (int i = 0; i < 2; i++) SpawnCreature("mushling", NestFreePos(f), 1, "birth_nest", 1);
            for (int i = 0; i < 2; i++) SpawnCreature("firefly", NestFreePos(f), 1, "birth_nest", 1);
        }

        private Vec NestFreePos(DungeonFloor f)
        {
            for (int i = 0; i < 40; i++)
            {
                double x = 1.5 + rng.Range(0, f.w - 3), y = 1.5 + rng.Range(0, f.h - 3);
                if (IsWalkable(x, y)) return Vec.Of(x, y);
            }
            return Vec.Of(f.w / 2.0, f.h / 2.0);
        }

        /// Готовность к выходу: ≥8 биомассы ИЛИ ≥2 органа помимо осязания.
        public void NestReady(out bool ready, out double cur, out double need)
        {
            int extra = 0;
            foreach (var o in data.senses.unlocked) if (o != "touch") extra++;
            if (extra >= 2) { ready = true; cur = 2; need = 2; }
            else { cur = Math.Min(8, Math.Floor(data.senses.biomassTotal)); need = 8; ready = cur >= need; }
        }

        public bool ExitBirthNest()
        {
            if (!DungeonActive || data.activeDungeon != "birth_nest") return false;
            var dun = Dun;
            var f = dun.floors[dun.depth - 1];
            var p = data.player;
            if (ScRng.Dist(f.upX, f.upY, p.x, p.y) > 1.45) return false;
            bool ready; double cur, need;
            NestReady(out ready, out cur, out need);
            if (!ready)
            {
                PushLog("Проход ещё тесен: усвойте 8 биомассы (" + Math.Floor(cur) + "/" + need + ") или откройте 2 чувства.", "life");
                return false;
            }
            dun.active = false;
            data.activeDungeon = null;
            InvalidateSeasonCache();   // вернулись в большой мир — сезоны снова действуют
            p.x = dun.returnX; p.y = dun.returnY;
            p.vx = 0; p.vy = 0;
            data.creatures.RemoveAll(c => c.dungeonId != null && c.dead);
            data.nestExited = 1;
            data.inNest = 0;
            PushLog("Комочек протекает сквозь узкий лаз... большой мир обрушивается ощущениями.", "world");
            return true;
        }

        // ===== Подземелья-инстансы (Регион 2/3 + Подземное русло, §6) — порт dungeon-блока world.ts =====
        public DungeonDef DungeonDefOf
        {
            get { return DungeonActive && data.activeDungeon != null ? ScDungeon.Def(data.activeDungeon) : null; }
        }

        public bool EnterDungeon(string id)
        {
            if (DungeonActive || data.deathCause != null) return false;
            DungeonDef def = ScDungeon.Def(id);
            if (def == null) return false;
            Vec gate = def.gatePos(data);
            var p = data.player;
            if (ScRng.Dist(gate.x, gate.y, p.x, p.y) > 1.7) return false;
            DungeonState dun;
            if (!data.dungeons.TryGetValue(id, out dun) || dun == null)
            {
                dun = ScDungeon.CreateState(data.seed, id, gate.x + 0.9, gate.y + 1.5);
                data.dungeons[id] = dun;
            }
            dun.active = true;
            dun.depth = 1;
            dun.returnX = (float)(gate.x + 0.9);
            dun.returnY = (float)(gate.y + 1.5);
            data.activeDungeon = id;
            InvalidateSeasonCache();   // подземелье: нейтральный микроклимат
            // подземелье переросло прошлый визит: убираем всё, что осталось, и зарождаем заново
            data.creatures.RemoveAll(c => c.dungeonId == id);
            SpawnDungeonFloor(id, 1);
            data.dungeonEntered = 1;               // достижение descender + мир помнит смелость
            AdvanceQuestForType("dungeon_enter", id);
            // игрок переносится ко входу первого этажа (сцена меняется целиком)
            DungeonFloor f0 = dun.floors[0];
            p.x = f0.upX + 0.5f; p.y = f0.upY + 0.5f;
            p.vx = 0; p.vy = 0; p.facing = 1;
            senseMap.Clear();
            data.PushToast(def.name + ". Глубина 1 из " + def.depths + ". Ищите лестницу вниз.", "epic");
            PushLog(def.enterLog, "world");
            return true;
        }

        public bool DescendStairs()
        {
            DungeonState dun = Dun;
            string id = data.activeDungeon;
            if (!DungeonActive || dun == null || id == null || dun.depth >= dun.floors.Count) return false;
            DungeonFloor f = dun.floors[dun.depth - 1];
            var p = data.player;
            if (ScRng.Dist(f.downX, f.downY, p.x, p.y) > 1.45) return false;
            dun.depth++;
            data.creatures.RemoveAll(c => c.dungeonId != null && c.dead);
            SpawnDungeonFloor(id, dun.depth);
            DungeonFloor nf = dun.floors[dun.depth - 1];
            p.x = nf.upX + 0.5f; p.y = nf.upY + 0.5f;
            p.vx = 0; p.vy = 0;
            senseMap.Clear();
            PushLog("Глубина " + dun.depth + ". Давление камня растёт.", "world");
            return true;
        }

        public bool AscendStairs()
        {
            DungeonState dun = Dun;
            string id = data.activeDungeon;
            if (!DungeonActive || dun == null || id == null) return false;
            DungeonFloor f = dun.floors[dun.depth - 1];
            var p = data.player;
            if (ScRng.Dist(f.upX, f.upY, p.x, p.y) > 1.45) return false;
            if (dun.depth <= 1)
            {
                dun.active = false;
                data.activeDungeon = null;
                InvalidateSeasonCache();   // поверхность: сезоны действуют
                p.x = dun.returnX; p.y = dun.returnY;
                p.vx = 0; p.vy = 0;
                data.creatures.RemoveAll(c => c.dungeonId != null && c.dead);
                senseMap.Clear();
                DungeonDef def = ScDungeon.Def(id);
                if (def != null) PushLog(def.exitLog, "world");
                return true;
            }
            dun.depth--;
            data.creatures.RemoveAll(c => c.dungeonId != null && c.dead);
            SpawnDungeonFloor(id, dun.depth);
            DungeonFloor nf = dun.floors[dun.depth - 1];
            p.x = nf.downX + 0.5f; p.y = nf.downY + 0.5f;
            p.vx = 0; p.vy = 0;
            senseMap.Clear();
            PushLog("Вы поднимаетесь на глубину " + dun.depth + ".", "world");
            return true;
        }

        public bool TakeCache()
        {
            DungeonState dun = Dun;
            if (!DungeonActive || dun == null || nearCacheId < 0) return false;
            DungeonCache cache = dun.caches.Find(c => c.id == nearCacheId);
            if (cache == null || cache.taken) return false;
            DungeonDef def = DungeonDefOf;
            if (def == null) return false;
            cache.taken = true;
            double amount = 8 + Math.Round(rng.Range(0, 6)) + cache.depth * 2;
            data.player.biomass += amount;
            // диета: принос тоже питает рецепторы по своему составу (сезонная питательность — Этап 2)
            double[] cacheDiet;
            if (!SensesData.DietFeed.TryGetValue("cache_" + def.cacheKind, out cacheDiet)) cacheDiet = null;
            double dietMul = SeasonMods.dietMul;
            if (cacheDiet != null && dietMul != 1)
            {
                var scaled = new double[cacheDiet.Length];
                for (int i = 0; i < cacheDiet.Length; i++) scaled[i] = i % 2 == 1 ? cacheDiet[i] * dietMul : cacheDiet[i];
                cacheDiet = scaled;
            }
            var newly = ScSenses.GrantBiomass(data.senses, amount, SenseThrMul(), cacheDiet);
            foreach (var organ in newly)
            {
                PushLog("Пробуждение: " + OrganName(organ) + ". Мир стал немного другим.", "life");
                data.PushToast("Новое чувство: " + OrganName(organ), "epic");
            }
            if (def.cacheKind == "crystal") GainOre(2 + ScRegions.Region(data.birthRegion).mods.oreBonus); // жила — источник руды (п.7)
            PushLog((def.cacheKind == "crystal" ? "Кристальная жила выломана" : "Запас грибницы собран") + ": +" + amount + " биомассы.", "life");
            return true;
        }

        void SpawnDungeonFloor(string id, int depth)
        {
            DungeonDef def = ScDungeon.Def(id);
            DungeonState dun;
            if (def == null || !data.dungeons.TryGetValue(id, out dun) || dun == null) return;
            bool bossAlive = !string.IsNullOrEmpty(def.bossFlag) && data.Flag(def.bossFlag) == 0;
            string[][] plan = def.spawnPlan(depth, bossAlive);
            DungeonFloor f = dun.floors[depth - 1];
            foreach (string[] pair in plan)
            {
                int count;
                if (!int.TryParse(pair[1], out count)) count = 1;
                for (int i = 0; i < count; i++)
                {
                    Creature c = SpawnCreature(pair[0], DungeonSpawnPos(f), data.dayTicks / 6 + 1, id, depth);
                    if (c != null && pair[0] == def.bossSpecies && !string.IsNullOrEmpty(def.bossTag))
                    {
                        c.questTag = def.bossTag;
                        c.name = def.bossName;
                    }
                }
            }
        }

        Vec DungeonSpawnPos(DungeonFloor f)
        {
            for (int i = 0; i < 60; i++)
            {
                int x = rng.Int(1, f.w - 2), y = rng.Int(1, f.h - 2);
                if (f.terrain[y * f.w + x] == "rock") continue;
                if (Math.Sqrt((x - f.upX) * (x - f.upX) + (y - f.upY) * (y - f.upY)) < 3) continue; // не у самого входа
                return Vec.Of(x + 0.5, y + 0.5);
            }
            return Vec.Of(f.upX + 2, f.upY);
        }

        // ===== Задания посёлка (§13) — порт quest-хуков world.ts =====
        void AdvanceQuest(string qid, double add)
        {
            var q = ScQuests.ById(qid);
            if (q == null || !ScQuests.Accepted(data, qid) || ScQuests.Done(data, qid)) return;
            double cur = ScQuests.Progress(data, qid);
            if (cur >= q.need) return;
            double nv = Math.Min(q.need, cur + add);
            data.SetFlag("quest_progress_" + qid, nv);
            if (nv >= q.need)
                PushLog("Задание выполнено: «" + q.title + "». Награда ждёт у давшего его жителя посёлка.", "village");
        }

        /// Прогресс по типу задания (вход в подземелье по id).
        void AdvanceQuestForType(string type, string target)
        {
            foreach (var q in ScQuests.Quests)
                if (q.type == type && q.target == target) AdvanceQuest(q.id, 1);
        }

        void TrackQuestKill(string species, string questTag)
        {
            foreach (var q in ScQuests.Quests)
            {
                if (q.type == "kill" && q.target == species) AdvanceQuest(q.id, 1);
                if (q.type == "boss" && q.target == questTag) AdvanceQuest(q.id, 1);
            }
        }

        void TrackQuestAbsorb(string species)
        {
            foreach (var q in ScQuests.Quests)
                if (q.type == "absorb" && q.target == species) AdvanceQuest(q.id, 1);
        }

        /// Принять задание у NPC (§13); ретроактивный прогресс — мир помнит подвиги.
        public bool AcceptQuest(string qid)
        {
            var q = ScQuests.ById(qid);
            if (q == null || ScQuests.Accepted(data, qid) || ScQuests.Done(data, qid)) return false;
            if (!string.IsNullOrEmpty(q.requires) && !ScQuests.Done(data, q.requires)) return false;
            data.SetFlag("quest_accepted_" + qid, 1);
            // ретроактивный прогресс: убитые до принятия волки засчитываются
            data.SetFlag("quest_progress_" + qid, qid == "wolves" ? Math.Min(3, data.flagWolvesKilled) : 0);
            // боссы и подземелья тоже засчитываются задним числом — мир помнит подвиги (§13)
            if (q.type == "boss" && !string.IsNullOrEmpty(q.target))
            {
                foreach (var ddef in ScDungeon.Defs)
                    if (ddef.bossTag == q.target && data.Flag(ddef.bossFlag) == 1) data.SetFlag("quest_progress_" + qid, 1);
            }
            if (q.type == "dungeon_enter" && !string.IsNullOrEmpty(q.target) && data.dungeons.ContainsKey(q.target))
                data.SetFlag("quest_progress_" + qid, 1);
            if (qid == "wolves") data.SetFlag("wolfQuestAccepted", 1); // совместимость: маркер логова вожака
            PushLog("Принято задание: «" + q.title + "».", "village");
            return true;
        }

        /// Сдать задание: награда, карма, запись в хронику (§13).
        public bool TurnInQuest(string qid)
        {
            var q = ScQuests.ById(qid);
            if (q == null || !ScQuests.Accepted(data, qid) || ScQuests.Done(data, qid) || ScQuests.Progress(data, qid) < q.need) return false;
            data.SetFlag("quest_done_" + qid, 1);
            data.SetFlag("questJustDone", 1);   // UI покажет тост
            if (qid == "wolves") data.wolfQuestDone = 1;   // совместимость с милосердием черт души
            var parts = new List<string>();
            if (q.biomass > 0) { data.player.biomass += q.biomass; parts.Add("+" + q.biomass + " биомассы"); }
            Karma("quest_" + qid, "Задание выполнено: «" + q.title + "»", q.karma, "good", q.soulXp);
            PushLog("Награда за «" + q.title + "»: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "благодарность посёлка") + ".", "village");
            return true;
        }

        // ===== Поселение: торговля и репутация (§15, §3.2) =====
        /// Отдать 10 биомассы в запасы посёлка (+карма, +прогресс задания «Голодный год»).
        public bool DonateFood()
        {
            var p = data.player;
            if (p.biomass < 10) return false;
            p.biomass -= 10;
            data.village.food += 12;
            data.village.stability = ScRng.Clamp(data.village.stability + 2, 0, 100);
            foreach (var n in data.npcs) n.attitude = ScRng.Clamp(n.attitude + 5, 0, 100);
            Karma("donate_food", "Передача биомассы в запасы поселения", 6, "good", 3);
            data.donateCount++;   // хищная природа видит щедрость
            // прогресс заданий: пожертвования (§13)
            foreach (var q in ScQuests.Quests)
                if (q.type == "donate") AdvanceQuest(q.id, 1);
            return true;
        }

        /// Тихо обчистить склад (−карма; раз в день).
        public bool StealFood()
        {
            var p = data.player;
            if (data.flagStealCd > clock.TotalDays) return false;
            data.flagStealCd = clock.TotalDays + 1;
            p.biomass += 12;
            data.village.food = Math.Max(0, data.village.food - 10);
            data.village.stability = ScRng.Clamp(data.village.stability - 3, 0, 100);
            foreach (var n in data.npcs) n.attitude = ScRng.Clamp(n.attitude - 12, 0, 100);
            Karma("steal_food", "Тайная кража еды со склада поселения", -8, "bad");
            return true;
        }

        /// Рядом хищник — отдых небезопасен (движок ставит скорость «battle»).
        public bool DangerNear()
        {
            var p = data.player;
            foreach (var c in ActiveCreatures())
            {
                if (c.dead || c.species == "guard") continue;
                var def = Species.Get(c.species);
                if (def.diet != "carnivore") continue;
                if (ScRng.Dist(c.x, c.y, p.x, p.y) < 5.5) return true;
            }
            return false;
        }

        /// Миграции при смене сезона (Этап 2 карты): часть популяций стягивается к пищевым узлам.
        /// Мягкая сила: переназначаем дом и подтягиваем часть особей шагом (без телепортов).
        void MigrateForSeason(string season)
        {
            var poiNames = new Dictionary<string, string>
            {
                { "village", "людским очагам" }, { "pond", "пруду" }, { "mushroom_grove", "грибной роще" },
                { "crystal_hollow", "кристальной нише" }, { "stone_circle", "каменному кругу" },
                { "burrow", "норе у корней" }, { "rift", "разлому" },
            };
            var plan = ScSeasons.MigrationsFor(season);
            if (plan.Count == 0) return;
            // агрегируем по точке приюта: одна запись в хронику на направление
            var byPoi = new Dictionary<string, List<string>>();
            foreach (var kv in plan)
            {
                Poi poi = data.Poi(kv.Value);
                if (poi == null) continue;
                var crowd = data.creatures.FindAll(c => c.species == kv.Key && !c.dead && c.dungeonFloor == 0);
                // мигрирует до половины стаи — мир меняется постепенно, стая не исчезает целиком
                int movers = (int)Math.Ceiling(crowd.Count * 0.5);
                for (int i = 0; i < movers && i < crowd.Count; i++)
                {
                    var c = crowd[i];
                    double a = rng.Next() * Math.PI * 2;
                    double r = 1 + rng.Next() * poi.r * 0.7;
                    double hx = poi.x + Math.Cos(a) * r, hy = poi.y + Math.Sin(a) * r * 0.7;
                    c.homeX = (float)hx; c.homeY = (float)hy; c.hasHome = true;
                    if (c.ai == "idle" || c.ai == "wander")
                    {
                        c.ai = "wander";
                        c.wx = (float)hx; c.wy = (float)hy; c.hasWanderTo = true;
                        c.stateT = 6 + rng.Next() * 8;
                    }
                }
                if (movers > 0)
                {
                    List<string> names;
                    if (!byPoi.TryGetValue(kv.Value, out names)) { names = new List<string>(); byPoi[kv.Value] = names; }
                    SpeciesDef sd = Species.Get(kv.Key);
                    names.Add(sd != null ? sd.name.ToLower() : kv.Key);
                }
            }
            foreach (var kv in byPoi)
            {
                string label;
                if (!poiNames.TryGetValue(kv.Key, out label)) label = kv.Key;
                PushLog("Зверь смещается: " + string.Join(", ", kv.Value.ToArray()) + " — к " + label + ".", "world");
            }
        }

        // ===== Самолечение позиции (страховка «пропавшего героя») =====
        public bool HealPlayerPosition()
        {
            var p = data.player;
            if (IsWalkable(p.x, p.y)) return false;
            if (DungeonActive)
            {
                var f = Dun.floors[Dun.depth - 1];
                Vec safe = data.activeDungeon == "birth_nest"
                    ? Vec.Of(f.downX + 0.5, f.downY + 0.9)
                    : Vec.Of(f.upX + 0.5, f.upY + 0.5);
                p.x = safe.x; p.y = safe.y;
            }
            else
            {
                int cx = float.IsNaN(p.x) || float.IsInfinity(p.x) ? 0 : (int)Math.Floor(p.x);
                int cy = float.IsNaN(p.y) || float.IsInfinity(p.y) ? 0 : (int)Math.Floor(p.y);
                Vec? found = null;
                for (int r = 0; r < 14 && found == null; r++)
                    for (int dy = -r; dy <= r && found == null; dy++)
                        for (int dx = -r; dx <= r && found == null; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            int tx = cx + dx, ty = cy + dy;
                            if (tx < 0 || ty < 0 || tx >= data.mapJson.w || ty >= data.mapJson.h) continue;
                            if (IsWalkable(tx + 0.5, ty + 0.5)) found = Vec.Of(tx + 0.5, ty + 0.5);
                        }
                if (found == null)
                {
                    var v = data.Poi("village");
                    if (v != null && IsWalkable(v.x, v.y)) found = Vec.Of(v.x, v.y);
                    if (found == null)
                        for (int y = 0; y < data.mapJson.h && found == null; y++)
                            for (int x = 0; x < data.mapJson.w && found == null; x++)
                                if (IsWalkable(x + 0.5, y + 0.5)) found = Vec.Of(x + 0.5, y + 0.5);
                    if (found == null) throw new InvalidOperationException("Карта не содержит проходимых клеток.");
                }
                p.x = found.Value.x; p.y = found.Value.y;
            }
            p.vx = 0; p.vy = 0;
            PushLog("Поток вытолкнул вас на твёрдую поверхность — тело снова на месте.", "life");
            return true;
        }

        // ===== Дневные тики (§6 уровень C): сезоны, старение, смертность, NPC, популяции, посёлок, набеги =====
        private void DailyTick(int days)
        {
            var p = data.player;
            var d = data;

            // 0. СЕЗОНЫ (Этап 2 карты): смена сезона → хроника + миграции. Норка — вне сезонов.
            bool inNestLike = d.activeDungeon != null;   // подземелья и норка — свой микроклимат
            string curSeason = ScSeasons.SeasonOf(clock.TotalDays);
            bool seasonChanged = lastSeason != null && lastSeason != curSeason;
            lastSeason = curSeason;
            InvalidateSeasonCache();
            if (seasonChanged && !inNestLike)
            {
                PushLog(ScSeasons.SeasonLog(curSeason), "world");
                MigrateForSeason(curSeason);
            }

            // 0. Родовая норка: добыча восполняется медленно — сидеть в безопасности невыгодно
            if (d.activeDungeon == "birth_nest" && d.dungeons.ContainsKey("birth_nest"))
            {
                int alive = 0;
                foreach (var c in d.creatures) if (c.dungeonId == "birth_nest" && !c.dead) alive++;
                if (alive < 9 && rng.Chance(0.45))
                {
                    string[] pool = { "slug", "beetle", "mushling", "firefly", "slug" };
                    Creature c2 = SpawnCreature(pool[rng.Int(0, pool.Length - 1)], NestFreePos(d.dungeons["birth_nest"].floors[0]), d.dayTicks / 6 + 1, "birth_nest", 1);
                }
            }

            // 1. Старение игрока (§10)
            p.ageDays += days;
            RefreshStats();
            if (p.hp > stats.maxHp) p.hp = stats.maxHp;
            string newStage = ScAging.LifeStageOf(p.ageDays, stats.lifeDays);
            if (d.Flag("lastStage") != StageNum(newStage))
            {
                d.SetFlag("lastStage", StageNum(newStage));
                if (newStage == "Старение") PushLog("Ваше тело вступает в стадию Старения. Силы уже не те, зато опыт глубже.", "life");
                if (newStage == "Угасание") PushLog("Угасание. Дни сочатся медленно, и вы чувствуете, как душа готовится к пути.", "life");
            }

            // 2. Проверка естественной смерти — по игровому дню, не по кадру (§10)
            for (int i = 0; i < days; i++)
            {
                if (rng.Chance(ScAging.DailyMortality(p.ageDays, stats.lifeDays)))
                {
                    KillPlayer("old_age", "Тело состарилось и растеклось.");
                    return;
                }
            }

            // 3. Мутации от нестабильного генома (§9.2)
            if (stats.stability < 40 && rng.Chance(0.22))
            {
                p.mutations++;
                p.hp = Math.Min(p.hp, stats.maxHp);
                PushLog("Геном дрожит: клеточная мутация ослабила тело (−2 к максимуму здоровья). Стабилизируйте геном.", "life");
                if (stats.stability < 15 && rng.Chance(0.3))
                {
                    p.hp -= 4;
                    if (p.hp <= 0) { KillPlayer("mutation", "Тело не выдержало."); return; }
                }
            }

            // 4. Старение NPC и смена поколений (§15: правители стареют и умирают)
            for (int ni = d.npcs.Count - 1; ni >= 0; ni--)
            {
                var n = d.npcs[ni];
                if (!n.alive) continue;
                n.ageDays += days;
                if (n.ageDays > n.lifespan && rng.Chance(ScAging.DailyMortality(n.ageDays, n.lifespan)))
                {
                    n.alive = false;
                    d.village.stability = ScRng.Clamp(d.village.stability - 8, 0, 100);
                    PushLog(n.name + " умер(ла) от старости. Поселение скорбит.", "village");
                    // смена власти / преемник
                    if (n.id == "mirra")
                    {
                        d.npcs.Add(new Npc { id = "ivo", name = "Старейшина Иво", role = "новый старейшина", x = n.x, y = n.y, ageDays = 40, lifespan = 400, alive = true, attitude = 40, dialogue = "hello_successor" });
                        PushLog("Молодой Иво становится старейшиной. Его помнят только ваши рассказы.", "village");
                    }
                    if (n.id == "dar")
                        d.npcs.Add(new Npc { id = "rinsa", name = "Охотница Ринса", role = "новый охотник", x = n.x, y = n.y, ageDays = 30, lifespan = 320, alive = true, attitude = 35, dialogue = "hello_successor" });
                    if (n.id == "klumba")
                        d.npcs.Add(new Npc { id = "prokh", name = "Огородник Прох", role = "фермер", x = n.x, y = n.y, ageDays = 36, lifespan = 340, alive = true, attitude = 40, dialogue = "hello_successor" });
                }
            }

            // 5. Популяции: рождаемость и поколения (§6 уровень C) — сезоны дышат (Этап 2)
            d.dayTicks += 0;   // поколение считается из дня мира
            int gen = d.dayTicks / 6 + 1;
            SeasonMods sm = SeasonMods;
            string[] speciesSeen = { "slug", "beetle", "firefly", "rabbit", "deer", "wolf", "toad", "spider", "mushling", "crystalslug", "shardling", "spirit" };
            foreach (string sp in speciesSeen)
            {
                SpeciesDef def = Species.Get(sp);
                if (def == null || def.popCap <= 0) continue;
                int pop = PopCount(sp);
                if (pop >= def.popCap) continue;
                double rate = sp == "spirit" ? 0.05 : sp == "crystalslug" || sp == "shardling" ? 0.08 : sp == "wolf" ? 0.22 : 0.35;
                double seasonal = rate * sm.birthMul * ScSeasons.SeasonalSpawnBoost(sp, curSeason);
                if (rng.Chance(seasonal * days))
                {
                    // духи возрождаются и у Разлома (регион предельной сложности — п.3)
                    string poiKey;
                    if (sp == "wolf") poiKey = "burrow";
                    else if (sp == "toad" || sp == "firefly") poiKey = "pond";
                    else if (sp == "spider" || sp == "mushling") poiKey = "mushroom_grove";
                    else if (sp == "crystalslug" || sp == "shardling") poiKey = "crystal_hollow";
                    else if (sp == "spirit") poiKey = d.Poi("rift") != null && rng.Chance(0.4) ? "rift" : "stone_circle";
                    else poiKey = "village";
                    Poi poi = d.Poi(poiKey);
                    if (poi != null) SpawnCreature(sp, SpawnPosNear(poi), gen);
                }
            }

            // 6. Поселение: экономика дня (§15) + стадия роста
            var v = d.village;
            string stageName; double stageFoodBonus;
            ScQuests.VillageStageOf(v.population, out stageName, out stageFoodBonus);
            if (v.stage != stageName)
            {
                if (stageName == "Деревня") PushLog("Опушка разрослась: теперь это Деревня. Тропы стали шире.", "village");
                if (stageName == "Посад") PushLog("Опушка стала Посадом — слава о спокойном месте тянет людей.", "village");
                v.stage = stageName;
            }
            v.food = v.food + 3 + stageFoodBonus - Math.Ceiling(v.population * 0.4) + sm.villageFoodDelta;
            if (v.food < 0)
            {
                v.food = 0;
                if (rng.Chance(0.5))
                {
                    v.population = Math.Max(3, v.population - 1);
                    v.stability = ScRng.Clamp(v.stability - 4, 0, 100);
                    PushLog("В Опушке голод: запасы иссякли, кто-то покинул поселение.", "village");
                }
            }
            else if (v.food > 60 && rng.Chance(0.3))
            {
                v.population += 1;
                PushLog("В Опушке родился ребёнок. Поселение растёт.", "village");
            }
            v.stability += v.stability < 50 ? 1 : v.stability > 70 ? -1 : 0;
            if (v.raidCdDays > 0) v.raidCdDays -= days;

            // 6.5 Пути кармы (§12): мир реагирует на то, кем стала душа
            string path = KarmaPath();
            if (path == "Свет")
            {
                v.food += 2;
                p.biomass += 1;
                if (d.Flag("lightBlessing") == 0)
                {
                    d.SetFlag("lightBlessing", 1);
                    PushLog("Путь Света: посёлок оставляет вам подношение у тропы (+2 еды посёлку, +1 биомассы вам каждый день).", "karma");
                }
            }
            else if (path == "Тьма" && d.Flag("darkWhisper") == 0)
            {
                d.SetFlag("darkWhisper", 1);
                PushLog("Путь Тьмы: волки больше не видят в вас добычу, а голод внутри стал сильнее.", "karma");
            }

            // 7. Набег волков
            if (!v.raidPending && v.raidCdDays <= 0)
            {
                int wolves = PopCount("wolf");
                if (wolves >= 4 && rng.Chance(0.4))
                {
                    v.raidPending = true;
                    v.raidDeadline = clock.TotalDays + 1;
                    PushLog("ВОЛКИ У ОГРАДЫ! Стая кружит у поселения. Дар зовёт к обороне.", "village");
                }
            }
            if (v.raidPending)
            {
                if (clock.TotalDays >= v.raidDeadline)
                {
                    // игрок проигнорировал — поселение платит
                    v.raidPending = false;
                    v.raidCdDays = 3;
                    v.food = Math.Max(0, v.food - 16);
                    v.population = Math.Max(3, v.population - 2);
                    v.stability = ScRng.Clamp(v.stability - 12, 0, 100);
                    PushLog("Стая прорвалась в посёлок: запасы разграблены, люди погибли. Вы не пришли.", "village");
                    Karma("ignored_raid", "Игнорирование набега: поселение пострадало", -10, "bad");
                    // волки уходят сытыми
                    Poi burrow = d.Poi("burrow");
                    if (burrow != null)
                        foreach (var w in d.creatures)
                            if (w.species == "wolf" && !w.dead)
                            {
                                w.x = (float)(burrow.x + rng.Range(-2, 2));
                                w.y = (float)(burrow.y + rng.Range(-2, 2));
                            }
                }
                else
                {
                    // волки подошли к деревне
                    Poi vil = d.Poi("village");
                    if (vil != null)
                    {
                        int wi = 0;
                        foreach (var w in d.creatures)
                        {
                            if (w.species != "wolf" || w.dead || wi >= 5) continue;
                            double a = wi / 5.0 * Math.PI * 2;
                            double tx = vil.x + Math.Cos(a) * (vil.r + 2.5);
                            double ty = vil.y + Math.Sin(a) * (vil.r + 2.5) * 0.82;
                            StepToward(w, tx, ty, Species.Get("wolf").speed, 0.016);
                            wi++;
                        }
                    }
                }
            }
        }

        static double StageNum(string stage)
        {
            string[] stages = { "Зарождение", "Молодость", "Расцвет", "Старение", "Угасание" };
            int i = Array.IndexOf(stages, stage);
            return i >= 0 ? i : 0;
        }

        // ===== Карма (§12): текущая жизнь — кламп для HUD, безклампная сумма — для суда и черт; путь — от души =====
        public string KarmaPath()
        {
            double total = (data.soul != null ? data.soul.karma : 0) + data.karmaTotal;
            return ScQuests.KarmaPathOf(total);   // пороги ±40 — как в вебе (karmaPathOf)
        }

        private void Karma(string id, string title, double delta, string tag) { Karma(id, title, delta, tag, 0); }

        private void Karma(string id, string title, double delta, string tag, double xp)
        {
            data.karma = ScRng.Clamp(data.karma + delta, -100, 100);
            data.karmaTotal += delta;   // суду нужна сумма без клампа (как flags.karmaTotal в вебе)
            if (xp > 0) data.SetFlag("soulXpLife", data.Flag("soulXpLife") + xp);
            data.karmaEvents.Add(new KarmaEvent { day = clock.TotalDays, year = clock.Year, id = id, title = title, karma = delta, tag = tag });
            if (delta != 0) PushLog(title + " [" + (delta > 0 ? "+" : "") + delta + " кармы]", "karma");
        }

        /// Публичная карма для систем над миром (шахматы — §14/§20).
        public void KarmaPublic(string id, string title, double delta, string tag, double xp)
        {
            Karma(id, title, delta, tag, xp);
        }

        private string PoiKeyFor(string species)
        {
            switch (species)
            {
                case "crystalslug": return "crystal_hollow";
                case "mushling": case "spider": return "mushroom_grove";
                case "toad": case "firefly": return "pond";
                case "deer": case "wolf": case "rabbit": return "burrow";
                case "spirit": return "stone_circle";
                default: return "village";
            }
        }

        // ===== Журнал и FX =====
        public void PushLog(string text, string kind)
        {
            data.log.Add(new HistoryEntry { day = clock.TotalDays, year = clock.Year, text = text, kind = kind });
            if (data.log.Count > 120) data.log.RemoveRange(0, data.log.Count - 120);
        }

        public void PushFx(string kind, double x, double y, string text, string color)
        {
            data.fx.Add(new FxEvent { kind = kind, x = (float)x, y = (float)y, text = text, color = color });
            if (data.fx.Count > 24) data.fx.RemoveRange(0, data.fx.Count - 24);
        }

        // ===== Суд души (экран смерти §12): сводка из уже применённых итогов суда =====
        public string JudgmentSummary()
        {
            var j = JudgmentView();
            if (j == null)
            {
                return "Смерть: " + data.deathCause + ". Путь души: " + KarmaPath() +
                       ". Карма: " + Math.Round(data.karma) + ". Убито: " + data.player.kills +
                       ". Поглощено: " + data.player.absorbedCount + ". Воплощение №" + data.lifeNo + " завершено.";
            }
            string odds = "";
            int shown = 0;
            foreach (RegionOdds o in j.regionOdds)
            {
                if (shown >= 2) break;
                if (shown > 0) odds += " · ";
                odds += o.name + " " + o.pct + "%";
                shown++;
            }
            return "Смерть: " + data.deathCause + " Вердикт: " + j.verdict +
                   ". Душа: ур." + j.newSoulLevel + " (" + data.soul.xp + " XP). Карма: " + Math.Round(j.karma) +
                   ". Куда зовёт душа: " + odds + ". " + j.summary;
        }
    }
}
