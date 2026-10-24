// SERVERCLOSED — Core: Magic Chess — стратегический автобой
// (порт Reference/WebPrototype/src/game/chess.ts, ТЗ §14, §20).
// - Расстановка: мировое время ОСТАНОВЛЕНО (§7).
// - Бой: автобой тиками; по завершении в мир уходит фиксированный небольшой интервал времени.
// - Временные боевые копии (не постоянные существа мира) — возраст не убивает юнитов посреди матча (§14).
// - Синергии видов/элементов, роли юнитов, командир.
// - Последствия боя меняют поселение и фракции (§14, §20).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public static class ScChess
    {
        public const int BOARD_R = 5;
        public const int BOARD_C = 7;

        static ChessUnitDef D(string id, string name, string role, string species, double hp, double atk,
            double def, int range, int atkCdMs, string trait, string[] synergies)
        {
            return new ChessUnitDef {
                id = id, name = name, role = role, species = species,
                hp = hp, atk = atk, def = def, range = range, atkCdMs = atkCdMs,
                trait = trait, synergies = new List<string>(synergies),
            };
        }

        /// Состав строится из того, что игрок открыл и от чего посёлок может выставить (§14).
        public static List<ChessUnitDef> BuildRoster(ScWorld world)
        {
            var d = world.data;
            var roster = new List<ChessUnitDef>();
            roster.Add(D("cmdr", "Командир-слизень", "Командир", "slug",
                Math.Max(60, Math.Round(world.stats.maxHp * 1.6)),
                Math.Max(6, Math.Round(world.stats.atk * 1.15)),
                Math.Max(2, Math.Round(world.stats.def)),
                1, 900, "Аура командира: соседние союзники бьют на 20% сильнее.", new[] { "Лес" }));
            roster.Add(D("militia", "Ополченец", "Защитник", "guard",
                100, 7, 7, 1, 1000, "Крепкая ограда: держит строй перед стрелками.", new[] { "Стража", "Лес" }));
            roster.Add(D("archer", "Лучник Дара", "Стрелок", "guard",
                50, 10, 1, 3, 1300, "Стреляет через полполя, но хрупок.", new[] { "Стража" }));
            List<string> disc = d.player != null && d.player.discoveredSpecies != null
                ? d.player.discoveredSpecies
                : ScSoul.DiscoveredSpecies(d);
            if (disc.Contains("wolf"))
                roster.Add(D("ally_wolf", "Волк-изгой", "Боец", "wolf",
                    55, 10, 2, 1, 850, "Изгнанный из стаи — злее сородичей.", new[] { "Хищник", "Лес" }));
            if (disc.Contains("spirit"))
                roster.Add(D("grove_spirit", "Дух рощи", "Маг", "spirit",
                    36, 11, 2, 2, 1600, "Лечит союзников на 2 HP за ход (аура Духа).", new[] { "Дух", "Лес" }));
            if (disc.Contains("crystalslug"))
                roster.Add(D("crystal_guard", "Кристальный страж", "Защитник", "crystalslug",
                    90, 5, 9, 1, 1200, "Живая скала. Медленный, но почти несокрушимый.", new[] { "Лес" }));
            if (disc.Contains("golem"))
                roster.Add(D("rune_golem", "Рунный голем", "Защитник", "golem",
                    130, 9, 11, 1, 1400, "Оживший камень пещер: медленный валун с тяжёлыми кулаками.", new[] { "Стража", "Лес" }));
            if (disc.Contains("mushling"))
                roster.Add(D("spore_choir", "Хор спор", "Поддержка", "mushling",
                    46, 4, 3, 2, 1500, "Каждый ход лечит самого раненого союзника на 5 HP.", new[] { "Лес" }));
            return roster;
        }

        public static ChessState CreateRaidChess(ScWorld world)
        {
            var roster = BuildRoster(world);
            var v = world.data.village;
            int limit = (int)ScRng.Clamp(3 + Math.Floor(v.military / 30.0), 3, 6);
            int raidNo = v.raidsSurvived + 1;
            var st = new ChessState {
                phase = "setup",
                round = raidNo,
                boardR = BOARD_R,
                boardC = BOARD_C,
                playerRoster = roster,
                placed = new List<ChessUnit>(),
                enemies = new List<ChessUnit>(),
                reserveLimit = limit,
                battleLog = new List<string> { "Стая подходит к ограде. Расставьте оборону — время остановилось." },
                worldMinutesSpent = 0,
                scenario = "Набег №" + raidNo + ": «Волчья стая у Опушки»",
                stake = "На кону: запасы еды, жизни поселян и покой леса.",
            };
            // волчья стая: сила растёт с каждым набегом; первый набег — 2 волка и вожак,
            // далее стая полнеет (кривая сложности); вожак всегда в центре (0,3), никто не пересекается
            var rng = new ScRng(unchecked((int)((uint)world.data.seed ^ (uint)(raidNo * 7919))));
            double wolfHp = 40 + raidNo * 4;
            st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 0, 1, wolfHp, rng));
            st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 0, 5, wolfHp, rng));
            if (raidNo >= 2) { st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 1, 2, wolfHp, rng)); st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 1, 4, wolfHp, rng)); }
            if (raidNo >= 3) st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 1, 3, wolfHp, rng));
            if (raidNo >= 4) st.enemies.Add(Enemy(100 + st.enemies.Count, "e_wolf", 0, 2, wolfHp, rng)); // стая ширит фланги
            st.enemies.Add(Enemy(100 + st.enemies.Count, "e_alpha", 0, 3, 66 + raidNo * 4, rng, true)); // вожак в центре
            return st;
        }

        static ChessUnit Enemy(int uid, string defId, int r, int c, double hp, ScRng rng, bool alpha = false)
        {
            return new ChessUnit {
                uid = uid, defId = alpha ? "e_alpha" : defId, side = "enemy", r = r, c = c,
                hp = hp, cd = rng.Range(0.3, 1.2), alive = true,
            };
        }



        public static bool Place(ChessState st, string defId, int r, int c)
        {
            if (st.phase != "setup") return false;
            if (r < 3 || r >= st.boardR || c < 0 || c >= st.boardC) return false; // свои ряды — нижние два
            if (st.placed.Exists(u => u.alive && u.r == r && u.c == c)) return false;
            ChessUnit existing = st.placed.Find(u => u.defId == defId);
            if (existing != null) { existing.r = r; existing.c = c; return true; } // переставить
            if (st.placed.Count >= st.reserveLimit) return false;
            ChessUnitDef def = st.playerRoster.Find(d => d.id == defId);
            if (def == null) return false; // неизвестный юнит (не в ростере) — тихо отклоняем
            foreach (var unit in st.placed) st.nextUnitId = Math.Max(st.nextUnitId, unit.uid);
            st.placed.Add(new ChessUnit {
                uid = ++st.nextUnitId, defId = defId, side = "player", r = r, c = c,
                hp = def.hp, cd = 0.4, alive = true,
            });
            return true;
        }

        public static void Remove(ChessState st, int uid)
        {
            if (st.phase == "setup") st.placed.RemoveAll(u => u.uid == uid);
        }

        public static void Start(ChessState st)
        {
            if (st.phase != "setup" || st.placed.Count == 0) return;
            st.phase = "battle";
            st.battleLog.Add("Рог ополчения трубит! Время снова пошло.");
        }

        // ===== Данные врагов =====
        class EnemyStats { public string name; public double atk, def, range, cd; }
        static readonly Dictionary<string, EnemyStats> EnemyTable = new Dictionary<string, EnemyStats>
        {
            { "e_wolf", new EnemyStats { name = "Лесной волк", atk = 8, def = 2, range = 1, cd = 0.95 } },
            { "e_alpha", new EnemyStats { name = "Рваное Ухо, вожак", atk = 11, def = 3, range = 1, cd = 0.85 } },
        };

        /// Данные врагов для UI (короткое имя, полное имя, базовый HP — для полосок здоровья).
        public static string EnemyShort(string defId) { return defId == "e_alpha" ? "Вожак" : "Волк"; }
        public static string EnemyName(string defId) { return defId == "e_alpha" ? "Рваное Ухо, вожак стаи" : "Лесной волк"; }
        public static double EnemyBaseHp(string defId) { return defId == "e_alpha" ? 70 : 44; }

        static IEnumerable<ChessUnit> AllUnits(ChessState st)
        {
            foreach (var u in st.placed) yield return u;
            foreach (var u in st.enemies) yield return u;
        }

        /// Один тик автобоя (~400 мс). Возвращает true, если бой завершён.
        public static bool Tick(ChessState st, ScRng rng)
        {
            if (st.battleRngState >= 0) rng.State = (uint)st.battleRngState;
            try
            {
            if (st.phase != "battle") return true;
            st.worldMinutesSpent += 3.3; // бой съедает небольшой фиксированный интервал мирового времени (§7)

            // синергии игрока
            var tags = new List<string>();
            foreach (var u in st.placed)
            {
                if (!u.alive) continue;
                ChessUnitDef d = st.playerRoster.Find(x => x.id == u.defId);
                if (d != null) tags.AddRange(d.synergies);
            }
            int tagCount(string t) { int n = 0; foreach (var x in tags) if (x == t) n++; return n; }
            double hpMul = 1 + (tagCount("Лес") >= 3 ? 0.15 : tagCount("Лес") >= 2 ? 0.08 : 0);
            double atkMul = 1 + (tagCount("Хищник") >= 1 ? 0.12 : 0) + (tagCount("Лес") >= 3 ? 0.1 : 0);
            double defMul = 1 + (tagCount("Стража") >= 2 ? 0.15 : 0);
            bool spiritHeal = tagCount("Дух") >= 1;

            foreach (var u in AllUnits(st))
            {
                if (!u.alive) continue;
                if (u.stunT > 0) { u.stunT = Math.Max(0, u.stunT - 0.4); continue; }
                u.cd -= 0.4;
                bool isPlayer = u.side == "player";
                ChessUnitDef def = isPlayer ? st.playerRoster.Find(d => d.id == u.defId) : null;
                EnemyStats estats = !isPlayer && EnemyTable.ContainsKey(u.defId) ? EnemyTable[u.defId] : null;
                if (isPlayer && def == null) continue;
                if (!isPlayer && estats == null) continue;
                double myAtkBase = isPlayer ? def.atk : estats.atk;
                double myDef = isPlayer ? def.def : estats.def;
                double myRange = isPlayer ? def.range : estats.range;
                double myCd = isPlayer ? def.atkCdMs / 1000.0 : estats.cd;

                // цели
                var foes = new List<ChessUnit>();
                foreach (var f in (isPlayer ? st.enemies : st.placed)) if (f.alive) foes.Add(f);
                if (foes.Count == 0) break;
                ChessUnit target = foes[0];
                double bd = Math.Abs(foes[0].r - u.r) + Math.Abs(foes[0].c - u.c);
                foreach (var f in foes)
                {
                    double d = Math.Abs(f.r - u.r) + Math.Abs(f.c - u.c);
                    if (d < bd) { bd = d; target = f; }
                }

                // командирская аура
                double aura = 1;
                if (isPlayer)
                {
                    ChessUnit cmdr = st.placed.Find(x => x.alive && x.defId == "cmdr");
                    if (cmdr != null && u.uid != cmdr.uid && Math.Abs(cmdr.r - u.r) + Math.Abs(cmdr.c - u.c) == 1) aura = 1.2;
                }

                if (bd <= myRange)
                {
                    if (u.cd <= 0)
                    {
                        u.cd = myCd;
                        ChessUnit foe = target;
                        double foeDef;
                        if (isPlayer)
                        {
                            EnemyStats ed;
                            foeDef = EnemyTable.TryGetValue(foe.defId, out ed) ? ed.def : 2;
                        }
                        else
                        {
                            ChessUnitDef pd = st.playerRoster.Find(d => d.id == foe.defId);
                            foeDef = defMul * (pd != null ? pd.def : 1);
                        }
                        bool isSupport = isPlayer && def.role == "Поддержка";
                        double dmg = Math.Max(1, myAtkBase * (isPlayer ? atkMul * aura : 1) * rng.Range(0.85, 1.15) - foeDef * 0.5);
                        if (!isSupport)
                        {
                            foe.hp -= dmg;
                            if (isPlayer && def.role == "Маг")
                            {
                                // маг бьёт по площади: 50% урона соседям цели
                                foreach (var f in foes)
                                    if (f != foe && Math.Abs(f.r - foe.r) + Math.Abs(f.c - foe.c) == 1) f.hp -= dmg * 0.5;
                            }
                        }
                        if (isPlayer && def.role == "Поддержка")
                        {
                            // поддержка лечит самого раненого союзника вместо урона
                            ChessUnit wounded = null;
                            double wRatio = double.MaxValue;
                            foreach (var x in st.placed)
                            {
                                if (!x.alive) continue;
                                ChessUnitDef xd = st.playerRoster.Find(d => d.id == x.defId);
                                if (xd == null || xd.hp <= 0) continue;
                                double ratio = x.hp / xd.hp;
                                if (ratio < wRatio) { wRatio = ratio; wounded = x; }
                            }
                            if (wounded != null)
                            {
                                ChessUnitDef wd = st.playerRoster.Find(d => d.id == wounded.defId);
                                wounded.hp = Math.Min(wd.hp * hpMul, wounded.hp + 5);
                                st.battleLog.Add(def.name + " исцеляет союзника (+5).");
                            }
                        }
                    }
                }
                else
                {
                    // шаг к цели
                    double dr = Math.Sign(target.r - u.r);
                    double dc = Math.Sign(target.c - u.c);
                    bool canR = dr != 0 && !IsOccupied(st, u, (int)(u.r + dr), u.c);
                    bool canC = dc != 0 && !IsOccupied(st, u, u.r, (int)(u.c + dc));
                    if (canR && (Math.Abs(target.r - u.r) >= Math.Abs(target.c - u.c) || !canC)) u.r += (int)dr;
                    else if (canC) u.c += (int)dc;
                }
            }

            // аура Духа лечит всех игроков
            if (spiritHeal)
            {
                foreach (var u in st.placed)
                {
                    if (!u.alive) continue;
                    ChessUnitDef d = st.playerRoster.Find(x => x.id == u.defId);
                    if (d == null) continue;
                    double max = d.hp * hpMul;
                    u.hp = Math.Min(max, u.hp + 1);
                }
            }

            // смерти
            foreach (var u in st.placed)
            {
                if (!u.alive) continue;
                ChessUnitDef d = st.playerRoster.Find(x => x.id == u.defId);
                if (d == null) continue;
                double max = d.hp * hpMul;
                if (u.hp <= 0) { u.alive = false; st.battleLog.Add(d.name + " пал."); }
                else u.hp = Math.Min(u.hp, max);
            }
            foreach (var u in st.enemies)
            {
                if (u.alive && u.hp <= 0)
                {
                    u.alive = false;
                    st.battleLog.Add(EnemyTable[u.defId].name + " убит.");
                }
            }

            bool playersAlive = st.placed.Exists(u => u.alive);
            bool enemiesAlive = st.enemies.Exists(u => u.alive);
            if (!enemiesAlive)
            {
                st.phase = "result";
                st.result = "win";
                st.battleLog.Add("Стая разбита! Ограда устояла.");
                return true;
            }
            if (!playersAlive)
            {
                st.phase = "result";
                st.result = "lose";
                st.battleLog.Add("Ополчение разбито... Волки рвутся в посёлок.");
                return true;
            }
            if (st.worldMinutesSpent > 240)
            {
                st.phase = "result";
                st.result = "lose";
                st.battleLog.Add("Стая измотала защитников и ушла с добычей.");
                return true;
            }
            return false;

            }
            finally { st.battleRngState = rng.State; }
        }

        static bool IsOccupied(ChessState st, ChessUnit self, int r, int c)
        {
            if (r < 0 || c < 0 || r >= st.boardR || c >= st.boardC) return true;
            foreach (var u in AllUnits(st))
                if (u.alive && u != self && u.r == r && u.c == c) return true;
            return false;
        }

        /// Применить результат боя к миру (последствия §14/§20).
        public static void ApplyResult(ScWorld world, ChessState st)
        {
            if (st.phase != "result" || st.resultApplied || (st.result != "win" && st.result != "lose")) return;
            st.resultApplied = true;
            var v = world.data.village;
            v.raidPending = false;
            v.raidCdDays = 4;
            if (st.result == "win")
            {
                v.food = Math.Max(0, v.food - 5);
                v.military = ScRng.Clamp(v.military + 8, 0, 100);
                v.stability = ScRng.Clamp(v.stability + 8, 0, 100);
                v.raidsSurvived++;
                world.data.raidDefended = 1;
                Npc dar = world.Npc("dar");
                if (dar != null) dar.attitude = ScRng.Clamp(dar.attitude + 14, 0, 100);
                world.KarmaPublic("raid_defended", "Оборона Опушки: стая отброшена от поселения", 8, "good", 6);
                // волки обескровлены: половина стаи гибнет
                var wolves = world.data.creatures.FindAll(c => c.species == "wolf" && !c.dead);
                int half = (int)Math.Ceiling(wolves.Count / 2.0);
                for (int i = 0; i < half && i < wolves.Count; i++)
                {
                    wolves[i].dead = true; wolves[i].absorbed = true; wolves[i].corpseT = 0;
                }
                world.PushLog("Вы вели ополчение. Стая разбита, посёлок спасён. Ополченцы шепчутся о «командире-слизне».", "chess");
            }
            else
            {
                v.food = Math.Max(0, v.food - 14);
                v.population = Math.Max(3, v.population - 2);
                v.stability = ScRng.Clamp(v.stability - 10, 0, 100);
                Npc dar = world.Npc("dar");
                if (dar != null) dar.attitude = ScRng.Clamp(dar.attitude - 6, 0, 100);
                world.PushLog("Оборона провалилась. Волки ушли с добычей, посёлок оплакивает погибших.", "chess");
                world.KarmaPublic("raid_failed", "Поражение при обороне Опушки", 0, "neutral", 2);
            }
        }
    }
}
