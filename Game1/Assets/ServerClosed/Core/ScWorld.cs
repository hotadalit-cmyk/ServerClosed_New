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
        public double shakeT;
        public double senseTime;
        public bool coreActive;
        public readonly List<KarmaEvent> pendingJudgment = new List<KarmaEvent>();
        public JudgmentResult lastJudgment;      // кэш экрана Суда; итоги применены к душе один раз
        // опасности региона (regions.ts updateRegion — рантайм)
        double poisonAcc; bool poisonLogged;
        double stormT, stormHit; double stormCd = 22; int stormSurvived;

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
            w.HealPlayerPosition();
            return w;
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

        // A new body in the existing world; world history, clock and population survive.
        // Поток перерождения — порт engine.ts reincarnate: суд уже применён к душе (RunJudgment
        // при смерти), здесь — небытие (скачок времени), новое тело, ролл региона (ролл сделан при суде).
        public bool Reincarnate()
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
            // вариант тела: лучший из доступных душе (выбор в UI — следующий шаг; в вебе выбирает игрок)
            var opts = ScSoul.ReincarnationOptions(soul);
            ReincarnOption opt = opts[opts.Count - 1];

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

        public void SpawnCreature(string species, Vec pos, int gen, string dungeonId = null, int dungeonFloor = 0)
        {
            var def = Species.Get(species);
            if (def == null) return;
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
            UpdateProximity();
            UpdateSenses(dtReal);
            ScSenses.TickSenses(data.senses, dtReal);
            UpdateRegionHazards(dtReal);   // погода региона течёт в реальном времени (как в вебе)
        }

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
            if (!c.dead && def.diet == "carnivore" && d < 4.2 && (c.aggroByPlayer || rng.Chance(def.aggression * dt * 0.6)))
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
                // мир помнит павших владык подземелий (веса рождения + достижения души)
                if (c.species == "myco_monarch") data.monarchKilled = 1;
                if (c.species == "crystal_titan") data.titanKilled = 1;
            }
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
            if (best.species == "spirit") data.spiritAbsorbed = 1;   // воспоминание m_rift + глубина души
            Karma("absorb_" + best.species, "Поглощение: " + def.name, def.sentient ? -8 : 0, def.sentient ? "bad" : "neutral");
            PushFx("absorb", best.x, best.y, "+" + Math.Round(mass), "154,200,96");
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
            // питание рецепторов (Дополнение: диета)
            var newly = ScSenses.GrantBiomass(data.senses, it.mass, SenseThrMul(), it.diet);
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
            double depthBoost = 0; // глубина души — заглушка v1 (см. README: soul traits)
            if (depthBoost > 0)
                foreach (var o in new[] { "chemo", "vibration", "thermal", "light", "vision", "hearing" }) m[o] = 1 - depthBoost;
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

        // ===== Близость: трупы для [E] =====
        private void UpdateProximity()
        {
            nearCorpseId = -1;
            double best = 1.2;
            foreach (var c in ActiveCreatures())
            {
                if (c.dead && !c.absorbed && c.corpseT > 0)
                {
                    double d = ScRng.Dist(c.x, c.y, data.player.x, data.player.y);
                    if (d < best) { best = d; nearCorpseId = c.id; }
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
            p.x = dun.returnX; p.y = dun.returnY;
            p.vx = 0; p.vy = 0;
            data.creatures.RemoveAll(c => c.dungeonId != null && c.dead);
            data.nestExited = 1;
            data.inNest = 0;
            PushLog("Комочек протекает сквозь узкий лаз... большой мир обрушивается ощущениями.", "world");
            return true;
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

        // ===== Дневные тики (§6 уровень C): старение, смертность, популяции =====
        private void DailyTick(int days)
        {
            var p = data.player;
            p.ageDays += days;
            RefreshStats();
            // смертность игрока (Гомпертц §10)
            double mort = ScAging.DailyMortality(p.ageDays, stats.lifeDays);
            if (rng.Chance(mort)) { KillPlayer("old_age", "Тело состарилось и растеклось."); return; }

            // старение и смерть существ, восстановление популяций
            for (int i = data.creatures.Count - 1; i >= 0; i--)
            {
                var c = data.creatures[i];
                if (c.dungeonId != null) continue;
                var def = Species.Get(c.species);
                c.ageDays += days;
                if (c.dead && (c.corpseT -= days * 60) <= -60) { data.creatures.RemoveAt(i); continue; }
                if (!c.dead && rng.Chance(ScAging.DailyMortality(c.ageDays, def.lifespan))) { data.creatures.RemoveAt(i); continue; }
            }
            foreach (var kv in Species.All)
            {
                var def = kv.Value;
                if (def.popCap <= 0) continue;
                int count = 0;
                foreach (var c in data.creatures)
                    if (c.species == kv.Key && !c.dead && c.dungeonFloor == 0) count++;
                if (count < def.popCap && rng.Chance(0.4))
                {
                    Poi poi = data.Poi(PoiKeyFor(kv.Key));
                    if (poi != null) SpawnCreature(kv.Key, SpawnPosNear(poi), data.dayTicks / 6 + 1);
                }
            }
            // дневной прирост энергии
            data.player.energy = Math.Min(stats.energyMax, data.player.energy + 2);
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

        // ===== Карма (§12): текущая жизнь — кламп для HUD, безклампная сумма — для суда и черт; путь — от души =====
        public string KarmaPath()
        {
            double total = (data.soul != null ? data.soul.karma : 0) + data.karmaTotal;
            if (total >= 25) return "Свет";
            if (total <= -25) return "Тьма";
            return "Равновесие";
        }

        private void Karma(string id, string title, double delta, string tag)
        {
            data.karma = ScRng.Clamp(data.karma + delta, -100, 100);
            data.karmaTotal += delta;   // суду нужна сумма без клампа (как flags.karmaTotal в вебе)
            data.karmaEvents.Add(new KarmaEvent { day = clock.TotalDays, year = clock.Year, id = id, title = title, karma = delta, tag = tag });
            if (delta != 0) PushLog(title + " [" + (delta > 0 ? "+" : "") + delta + " кармы]", "karma");
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
