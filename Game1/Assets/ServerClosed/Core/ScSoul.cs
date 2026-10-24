// SERVERCLOSED — Core: душа, карма, Суд души и реинкарнация (порт Reference/WebPrototype/src/game/soul.ts, §11–§12).
// Три сущности данных разделены: тело — worldData.player, душа — worldData.soul (SoulState),
// наследие — pastLives + память мира (мир сохраняется между жизнями).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    // ===== Результат суда (рантайм-объект для UI; мутируемые итоги применяются к душе один раз) =====
    public class JudgmentResult
    {
        public double ageDays;
        public string cause;
        public double karma;
        public string verdict;        // Свет | Равновесие | Тьма
        public int xpGained;
        public List<Achievement> achievements = new List<Achievement>();
        public List<KarmaEvent> decisions = new List<KarmaEvent>();
        public List<MemoryFragment> memoriesUnlocked = new List<MemoryFragment>();
        public string summary;
        public int newSoulLevel;
        public bool levelUp;
        public List<RegionOdds> regionOdds = new List<RegionOdds>();
        public List<string> regionReasons = new List<string>();
    }

    // ===== Вариант тела для нового воплощения (§11.3; рантайм-объект) =====
    public class ReincarnOption
    {
        public string id;
        public string species;
        public string name;
        public string desc;
        public string perkGene;                          // null — без дара
        public double maxHp, atk, def, speed, perception; // модификаторы тела (0 — нет)
    }

    public static class ScSoul
    {
        // xp-пороги уровней души 1..5 (soul.ts SOUL_LEVELS)
        public static readonly int[] SOUL_LEVELS = { 0, 60, 150, 300, 520 };

        public static int SoulLevelOf(int xp)
        {
            int lvl = 1;
            for (int i = 0; i < SOUL_LEVELS.Length; i++) if (xp >= SOUL_LEVELS[i]) lvl = i + 1;
            return lvl;
        }

        public static SoulState NewSoul()
        {
            var s = new SoulState
            {
                life = 1,
                xp = 0,
                level = 1,
                karma = 0,
                stats = new SoulStats { memory = 1, will = 1, adaptation = 1, perception = 1, resonance = 1, integrity = 10 },
                pastLives = new List<PastLifeRecord>(),
                memories = new List<MemoryFragment>(),
                resonantCore = false,
                spiritMemory = 0,
                traits = ScRegions.DefaultTraits(),   // нейтральная душа первой жизни
                birthRegion = "quiet_cradle"          // первая жизнь — сравнительно благоприятная среда
            };
            s.memories.Add(new MemoryFragment { id = "m_first", title = "Первая память", text = "Ты помнишь тепло — не солнца, а чего-то большего. Мир был цел, а потом что-то треснуло над горизонтом. Разлом?", unlocked = true });
            s.memories.Add(new MemoryFragment { id = "m_rift", title = "Шёпот духа", text = "«Разлом дышит, — пело нечто древнее. — Он ждёт того, кто соберёт себя из чужих жизней.»", unlocked = false });
            s.memories.Add(new MemoryFragment { id = "m_light", title = "Свет помнит тебя", text = "На мгновение после смерти ты видел сад из стекла и листвы. Он ждал того, кто не сломает чужую волю.", unlocked = false });
            s.memories.Add(new MemoryFragment { id = "m_dark", title = "Тьма узнала твой вкус", text = "Что-то под Разломом причмокнуло, когда твоя душа прошла мимо. Ему понравилось.", unlocked = false });
            s.memories.Add(new MemoryFragment { id = "m_age", title = "Предсмертная ясность", text = "Дожив до глубокой старости, ты вспомнил(а) поле из твоего самого первого сна. Оно существует.", unlocked = false });
            s.memories.Add(new MemoryFragment { id = "m_chess", title = "Поле сражений", text = "Память о другом ты: ты стоял над доской мира и двигал существа, как фигуры.", unlocked = false });
            return s;
        }

        public static List<Achievement> InitAchievements()
        {
            var a = new List<Achievement>();
            a.Add(new Achievement { id = "first_absorb", title = "Первое поглощение", desc = "Впустить чужую жизнь в свою.", soulXp = 8, got = false });
            a.Add(new Achievement { id = "five_species", title = "Исследователь плоти", desc = "Изучить строение пяти видов.", soulXp = 15, got = false });
            a.Add(new Achievement { id = "syn1", title = "Химия жизни", desc = "Открыть первую синергию генов.", soulXp = 15, got = false });
            a.Add(new Achievement { id = "syn3", title = "Композитор генома", desc = "Открыть три синергии.", soulXp = 25, got = false });
            a.Add(new Achievement { id = "defend_village", title = "Щит Опушки", desc = "Отбить набег волков на поселение.", soulXp = 20, got = false });
            a.Add(new Achievement { id = "merciful", title = "Милосердный хищник", desc = "Изгнать стаю, убив лишь вожака.", soulXp = 20, got = false });
            a.Add(new Achievement { id = "old_age", title = "Полный круг", desc = "Умереть от старости, прожив долгую жизнь.", soulXp = 25, got = false });
            a.Add(new Achievement { id = "spirit_touch", title = "Коснуться духа", desc = "Поглотить лесного духа.", soulXp = 18, got = false });
            a.Add(new Achievement { id = "apex", title = "Вершина пищевой цепи", desc = "Поглотить волка, будучи слизнем.", soulXp = 20, got = false });
            a.Add(new Achievement { id = "crystal", title = "Минеральная жизнь", desc = "Поглотить кристального слизня.", soulXp = 15, got = false });
            a.Add(new Achievement { id = "descender", title = "Вниз по грибнице", desc = "Спуститься в Грибные Катакомбы.", soulXp = 15, got = false });
            a.Add(new Achievement { id = "monarch", title = "Трон из спор", desc = "Повергнуть Грибного Монарха на третьей глубине.", soulXp = 30, got = false });
            a.Add(new Achievement { id = "titan", title = "Гора, что ходила", desc = "Повергнуть Кристального Титана в Кристальных Пещерах.", soulXp = 30, got = false });
            a.Add(new Achievement { id = "quests3", title = "Друг Опушки", desc = "Выполнить три задания поселян.", soulXp = 22, got = false });
            a.Add(new Achievement { id = "senses_awake", title = "Мир обрёл краски", desc = "Развить зрение из одного лишь осязания.", soulXp = 18, got = false });
            a.Add(new Achievement { id = "core_formed", title = "Резонансное ядро", desc = "Сплесть ядро из магической руды и открыть духовное чутьё.", soulXp = 25, got = false });
            return a;
        }

        // ===== Изученные виды этой жизни: из ведомости поглощений (в вебе — player.discoveredSpecies) =====

        public static List<string> DiscoveredSpecies(WorldData d)
        {
            var found = new List<string>();
            foreach (KarmaEvent e in d.karmaEvents)
            {
                if (e.id == null || !e.id.StartsWith("absorb_")) continue;
                string sp = e.id.Substring(7);
                if (!found.Contains(sp)) found.Add(sp);
            }
            return found;
        }

        public static int DiscoveredCount(WorldData d) { return DiscoveredSpecies(d).Count; }

        // ===== Суд души (§11.2): достижения, решения, карма — НЕ продолжительность жизни =====
        // Мутирует душу (черты, характеристики, воспоминания) и применяет итоги (xp, карма, уровень,
        // запись в pastLives, номер воплощения) РОВНО ОДИН РАЗ за смерть — safe к перезагрузкам:
        // в вебе итог применялся на кнопке перерождения и терялся при сохранении между фазами.

        public static JudgmentResult Judge(SoulState soul, WorldData d, List<Achievement> achievements)
        {
            string cause = d.deathCauseCode != null ? d.deathCauseCode : "killed";
            double ageDays = d.player.ageDays;
            double karmaDelta = d.karmaTotal;

            // достижения этой жизни
            var unlockedNow = new List<string>();
            List<string> discovered = DiscoveredSpecies(d);
            int syn = d.synDiscovered.Count;
            if (d.player.absorbedCount > 0) unlockedNow.Add("first_absorb");
            if (discovered.Count >= 5) unlockedNow.Add("five_species");
            if (syn >= 1) unlockedNow.Add("syn1");
            if (syn >= 3) unlockedNow.Add("syn3");
            if (d.raidDefended == 1) unlockedNow.Add("defend_village");
            if (d.alphaKilled == 1 && d.wolfQuestDone == 0) unlockedNow.Add("merciful");
            if (cause == "old_age") unlockedNow.Add("old_age");
            if (discovered.Contains("spirit")) unlockedNow.Add("spirit_touch");
            if (discovered.Contains("wolf")) unlockedNow.Add("apex");   // игрок-слизень — базовая форма порта
            if (discovered.Contains("crystalslug")) unlockedNow.Add("crystal");
            if (d.dungeonEntered == 1) unlockedNow.Add("descender");
            if (d.monarchKilled == 1) unlockedNow.Add("monarch");
            if (d.titanKilled == 1) unlockedNow.Add("titan");
            // quests3 — задания посёлка ещё не перенесены (v1.2), условия никогда не возникают
            if (Array.IndexOf(d.senses.unlocked, "vision") >= 0) unlockedNow.Add("senses_awake");
            if (d.senseCore == 1) unlockedNow.Add("core_formed");

            var gotNow = new List<Achievement>();
            foreach (Achievement a in achievements)
            {
                if (!a.got && unlockedNow.Contains(a.id)) { a.got = true; gotNow.Add(a); }
            }

            // карма: поступки важнее прожитых дней (§11.2 — не награждаем за долголетие)
            double karma = Math.Max(-100, Math.Min(100, soul.karma + karmaDelta));
            string verdict = karma >= 40 ? "Свет" : karma <= -40 ? "Тьма" : "Равновесие";

            // духовный опыт: достижения + чуть-чуть за сам опыт жизни
            int xpGained = 0;
            foreach (Achievement a in gotNow) xpGained += a.soulXp;
            xpGained += Math.Min(6, (int)Math.Floor(ageDays / 4));
            int oldLevel = soul.level;
            int newLevel = SoulLevelOf(soul.xp + xpGained);

            // воспоминания
            var memUnlocked = new List<MemoryFragment>();
            foreach (MemoryFragment m in soul.memories)
            {
                if (m.unlocked) continue;
                bool open = false;
                if (m.id == "m_rift" && (d.spiritAbsorbed == 1 || d.loreRift >= 2)) open = true;
                if (m.id == "m_light" && verdict == "Свет") open = true;
                if (m.id == "m_dark" && verdict == "Тьма") open = true;
                if (m.id == "m_age" && cause == "old_age") open = true;
                if (m.id == "m_chess" && d.raidDefended == 1) open = true;
                if (open) { m.unlocked = true; memUnlocked.Add(m); }
            }

            // характеристики души растут от образа жизни (§11.1)
            SoulStats st = soul.stats;
            if (discovered.Count / 3 > 0) st.adaptation += 1;
            if (gotNow.Exists(a => a.id == "defend_village")) st.will += 1;
            if (discovered.Count >= 5) st.perception += 1;
            if (verdict == "Свет") st.resonance += 1;
            st.integrity = Math.Min(20, st.integrity + (cause == "old_age" ? 2 : 1));
            if (memUnlocked.Count > 0) st.memory += 1;

            // сводка жизни
            string speciesName = "Лесной слизень";   // тело порта — слизень (виды тел придут с опциями перерождения в UI)
            string causeText = cause == "old_age" ? "угас от старости" : cause == "killed" ? "погиб в схватке" : cause == "mutation" ? "разрушен нестабильным геномом" : "погиб";
            string summary = "Воплощение №" + soul.life + ": " + speciesName + " — " + causeText + " на " + Math.Floor(ageDays) + "-й день жизни. Изучено видов: " + discovered.Count + ". Поглощений: " + d.player.absorbedCount + ". Карма жизни: " + (karmaDelta > 0 ? "+" : "") + Math.Round(karmaDelta) + ".";

            // ===== Резонанс души: черты растут только от реальных действий =====
            SoulTraits prevTraits = soul.traits != null ? soul.traits : ScRegions.DefaultTraits();
            SoulTraits newTraits = ScRegions.UpdateSoulTraits(prevTraits, d, karmaDelta, memUnlocked.Count, cause);
            soul.traits = newTraits;

            // ядро — свойство души: жившая с ядром жизнь делает его постоянным (дух. память растёт)
            if (d.senseCore == 1) { soul.resonantCore = true; soul.spiritMemory++; }

            var odds = ScRegions.NormalizeWeights(ScRegions.BirthWeights(newTraits, karma, d));
            var reasons = ScRegions.ExplainBirth(newTraits, karma, d);

            // ===== Применение итога к душе (в вебе — на кнопке; здесь — сразу и один раз) =====
            soul.xp += xpGained;
            soul.karma = karma;
            soul.level = newLevel;
            soul.pastLives.Add(new PastLifeRecord
            {
                life = soul.life,
                species = "slug",
                ageDays = Math.Floor(ageDays),
                cause = cause == "old_age" ? "угасание" : cause == "killed" ? "гибель" : "мутация",
                karma = karma,
                xpGained = xpGained,
                summary = summary
            });
            soul.life++;

            return new JudgmentResult
            {
                ageDays = ageDays,
                cause = cause,
                karma = karma,
                verdict = verdict,
                xpGained = xpGained,
                achievements = gotNow,
                decisions = new List<KarmaEvent>(d.karmaEvents),
                memoriesUnlocked = memUnlocked,
                summary = summary,
                newSoulLevel = newLevel,
                levelUp = newLevel > oldLevel,
                regionOdds = odds,
                regionReasons = reasons
            };
        }

        // ===== Варианты тел для нового воплощения (§11.3: слизень — базовая универсальная форма) =====

        public static List<ReincarnOption> ReincarnationOptions(SoulState soul)
        {
            var opts = new List<ReincarnOption>();
            opts.Add(new ReincarnOption
            {
                id = "slug", species = "slug", name = "Лесной слизень",
                desc = "Базовая форма. Слабая, пластичная, благодарная генетическим экспериментам."
            });
            if (soul.level >= 2 || soul.stats.resonance >= 2)
            {
                opts.Add(new ReincarnOption
                {
                    id = "light_slug", species = "slug", name = "Светлый слизень",
                    desc = "Тело, помнящее резонанс духов: чуть крепче и зорче, родится с Духовным резонансом.",
                    perkGene = "spirit_resonance", maxHp = 4, perception = 20
                });
            }
            if (soul.stats.will >= 2 || soul.xp >= SOUL_LEVELS[1])
            {
                opts.Add(new ReincarnOption
                {
                    id = "wolfblood_slug", species = "slug", name = "Слизень волчьей крови",
                    desc = "Память хищника вплетена в душу: агрессивнее и быстрее, родится с Охотничьим инстинктом.",
                    perkGene = "hunter_instinct", atk = 1, speed = 0.2
                });
            }
            return opts;
        }

        /// <summary>
        /// Переход мира после смерти (§3.3): посёлок переживает годы без игрока,
        /// трупы прошлой жизни исчезают. Мир НЕ сбрасывается.
        /// Вызывается ПОСЛЕ скачка часов; dayAfter — номер дня в мире на момент записи.
        /// </summary>
        public static void AdvanceWorldAfterDeath(WorldData d, string verdict, string cause, int days, int dayAfter)
        {
            var v = d.village;
            int seasons = Math.Max(1, (int)Math.Round(days / 24.0));
            for (int i = 0; i < seasons; i++)
            {
                v.food = Math.Max(0, Math.Min(80, v.food + (v.stability > 45 ? 8 : -4)));
                v.population = Math.Max(3, Math.Min(30, v.population + (v.food > 30 ? 1 : v.food < 12 ? -1 : 0)));
                v.stability = Math.Max(5, Math.Min(90, v.stability + (verdict == "Свет" ? 2 : verdict == "Тьма" ? -2 : 0)));
            }
            if (verdict == "Свет") v.raidCdDays = Math.Max(v.raidCdDays, 4);
            // трупы прошлой жизни исчезают — мир чистится статистически (уровень C, §6)
            d.creatures.RemoveAll(c => c.dead);
            // наследие: отметка для истории
            d.log.Add(new HistoryEntry { day = dayAfter, year = dayAfter / 24 + 1, text = "Воплощение завершилось (" + (cause == "old_age" ? "угасание" : "гибель") + "). Мир продолжается.", kind = "world" });
        }

        /// <summary>Миграция сейвов v1.1 → v1.2: у старых миров нет души и достижений.</summary>
        public static void EnsureMigration(WorldData d)
        {
            if (d.soul == null || d.soul.memories == null || d.soul.memories.Count == 0 || d.soul.stats == null)
            {
                SoulState fresh = NewSoul();
                if (d.soul != null) fresh.life = Math.Max(1, d.lifeNo);
                else fresh.life = Math.Max(1, d.lifeNo);
                d.soul = fresh;
            }
            if (d.soul.traits == null) d.soul.traits = ScRegions.DefaultTraits();
            if (d.soul.pastLives == null) d.soul.pastLives = new List<PastLifeRecord>();
            if (d.achievements == null || d.achievements.Count == 0) d.achievements = InitAchievements();
        }
    }
}
