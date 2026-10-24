// SERVERCLOSED — Core: регионы рождения и черты души (порт Reference/WebPrototype/src/game/regions.ts, §17 — переносим без изменений).
// 5 регионов рождения с разной экологией; вероятностный выбор места рождения из черт души
// + состояния мира; многомерная модель души: черты растут только от реальных событий жизни.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    // ===== Модификаторы региона (regions.ts RegionMods). Статичные данные, не сохраняются. =====
    public class RegionMods
    {
        public double biomassMul;       // богатство добычи этой жизни
        public double geneChanceMul;    // щедрость генофонда
        public double aggressionMul;    // злость хищников
        public double oreBonus;         // доп. руда
        public double spiritRmul;       // радиус духовного чутья
        public double depthMul;         // скорость роста духовной глубины за жизнь (в вебе пока без потребителя — держим данные)
    }

    public class RegionDef
    {
        public string id;
        public string name;
        public string difficulty;
        public string desc;
        public string perk;
        public string minus;
        public string hazard;          // none | poison_spores | rift_storm
        public RegionMods mods;
        public string[][] spawnPlan;   // [вид, количество] — гарантированная первая пища региона

        public RegionDef(string id, string name, string difficulty, string desc, string perk, string minus,
            string hazard, RegionMods mods, string[][] spawnPlan)
        {
            this.id = id; this.name = name; this.difficulty = difficulty;
            this.desc = desc; this.perk = perk; this.minus = minus;
            this.hazard = hazard; this.mods = mods; this.spawnPlan = spawnPlan;
        }
    }

    public static class ScRegions
    {
        // Фиксированный порядок регионов = порядок ключей REGIONS в regions.ts (детерминизм ролла, §23).
        public static readonly string[] REGION_IDS = {
            "quiet_cradle", "root_garden", "bone_narrows", "forgotten_vein", "soul_rift"
        };

        public static readonly RegionDef QuietCradle = new RegionDef(
            "quiet_cradle", "Тихая колыбель", "низкая",
            "Мягкая почва у поселения, много слабых организмов, минимум хищников. Хорошее место для развития первых органов чувств.",
            "Спокойный старт: хищники замечают вас позже.",
            "Мало редких генов и магических минералов.",
            "none",
            new RegionMods { biomassMul = 1.0, geneChanceMul = 0.85, aggressionMul = 0.7, oreBonus = 0, spiritRmul = 1.0, depthMul = 1.0 },
            new[] { new[] { "slug", "2" }, new[] { "rabbit", "1" }, new[] { "firefly", "1" } });

        public static readonly RegionDef RootGarden = new RegionDef(
            "root_garden", "Сад глубинных корней", "умеренная",
            "Богатая экосистема грибной рощи: конкуренты за пищу, ядовитые грибы, небольшие хищники.",
            "Быстрый рост: +20% биомассы с добычи.",
            "Ядовитые споры жалят тело без споровой защиты.",
            "poison_spores",
            new RegionMods { biomassMul = 1.2, geneChanceMul = 1.0, aggressionMul = 1.0, oreBonus = 0, spiritRmul = 1.0, depthMul = 1.0 },
            new[] { new[] { "slug", "2" }, new[] { "mushling", "2" }, new[] { "beetle", "1" } });

        public static readonly RegionDef BoneNarrows = new RegionDef(
            "bone_narrows", "Костяные теснины", "высокая",
            "Логова у волчьей норы: агрессивные существа, дефицит пищи, чужая территория. Приходится прятаться и доедать остатки чужой добычи.",
            "Сильные защитные и хищные гены: шанс добыть ген +30%.",
            "Добыча тощая (−20% биомассы), хищники злее и зорче.",
            "none",
            new RegionMods { biomassMul = 0.8, geneChanceMul = 1.3, aggressionMul = 1.35, oreBonus = 0, spiritRmul = 1.0, depthMul = 1.0 },
            new[] { new[] { "slug", "2" }, new[] { "beetle", "2" }, new[] { "wolf", "1" } });

        public static readonly RegionDef ForgottenVein = new RegionDef(
            "forgotten_vein", "Забытая жила", "экстремальная",
            "Кристальная ложбинка: недостаток органической пищи, насыщенные магией минералы, нестабильная среда.",
            "Магическая руда рядом: чутьё души растёт, руды больше.",
            "Органическая пища бедна (−25% биомассы).",
            "none",
            new RegionMods { biomassMul = 0.75, geneChanceMul = 1.15, aggressionMul = 1.0, oreBonus = 1, spiritRmul = 1.15, depthMul = 1.5 },
            new[] { new[] { "slug", "2" }, new[] { "crystalslug", "1" }, new[] { "shardling", "1" } });

        public static readonly RegionDef SoulRift = new RegionDef(
            "soul_rift", "Разлом забытых душ", "предельная",
            "Искажённая реальность у Разлома: древние сущности, опасные магические потоки. Обычные способы выживания могут оказаться бесполезными.",
            "Уникальные духовные эволюции: бури обостряют чутьё, глубина души растёт в полтора раза быстрее.",
            "Магический шторм выжигает тело рядом с Разломом.",
            "rift_storm",
            new RegionMods { biomassMul = 0.9, geneChanceMul = 1.15, aggressionMul = 1.1, oreBonus = 0, spiritRmul = 1.1, depthMul = 1.5 },
            new[] { new[] { "slug", "2" }, new[] { "spirit", "1" } });

        public static RegionDef Region(string id)
        {
            if (id == QuietCradle.id) return QuietCradle;
            if (id == RootGarden.id) return RootGarden;
            if (id == BoneNarrows.id) return BoneNarrows;
            if (id == ForgottenVein.id) return ForgottenVein;
            if (id == SoulRift.id) return SoulRift;
            return QuietCradle; // regionDef(): незнакомый id → тихая колыбель
        }

        /// Точка рождения региона (regions.ts spawnPoint): смещение от ориентира на карте.
        /// Ориентир-POI отсутствует —fallback на посёлок, затем на центр карты.
        public static Vec SpawnPoint(string id, WorldData d)
        {
            Vec fallback = Vec.Of(26, 24);
            Poi poi = null;
            switch (id)
            {
                case "root_garden": poi = d.Poi("mushroom_grove"); break;
                case "bone_narrows": poi = d.Poi("burrow"); break;
                case "forgotten_vein": poi = d.Poi("crystal_hollow"); break;
                case "soul_rift": poi = d.Poi("rift"); break;
                default: poi = d.Poi("village"); break;
            }
            if (poi == null) poi = d.Poi("village");
            if (poi == null) return fallback;
            double dx = 0, dy = 0;
            switch (id)
            {
                case "quiet_cradle": dx = -6.5; dy = 6.5; break;
                case "root_garden": dx = 2.2; dy = 1.6; break;
                case "bone_narrows": dx = 3.6; dy = 3.4; break;
                case "forgotten_vein": dx = 2.6; dy = 3.0; break;
                case "soul_rift": dx = -2.2; dy = -2.2; break;
            }
            return Vec.Of(poi.x + dx, poi.y + dy);
        }

        // ===== Многомерная модель души: рост от реальных событий жизни =====

        public static SoulTraits DefaultTraits()
        {
            return new SoulTraits { resilience = 25, depth = 5, predator = 5, memory = 0 };
        }

        /// <summary>
        /// Пересчёт черт души по итогам прожитой жизни (regions.ts updateSoulTraits).
        /// Только реальные события: старость, боссы, шторма, духи, воспоминания, убийства, милосердие.
        /// Прирост за одну жизнь ограничен — душа меняется постепенно.
        /// </summary>
        public static SoulTraits UpdateSoulTraits(SoulTraits prev, WorldData d, double karmaTotal, int memUnlocked, string cause)
        {
            // Зло учитывается в хищной природе; добро — только в весах рождения (BirthWeights), как в regions.ts.
            double evil = Math.Max(0, -karmaTotal);
            // Задания посёлка (v1.3 перенесены): завершённые задания растят память души.
            // Веб-формула считала устаревший список id (wolves/hides/myco/shards/famine/titan);
            // в порту честно считаются все 6 реальных заданий quests.ts.
            int questsDone = ScQuests.CompletedCount(d);
            // Милосердие: вожак убит вне задания (wolfQuestDone ставит сдача «Трёх шкур»)
            int mercy = (d.alphaKilled == 1 && d.wolfQuestDone == 0) ? 1 : 0;

            var t = new SoulTraits
            {
                resilience = prev.resilience,
                depth = prev.depth,
                predator = prev.predator,
                memory = prev.memory
            };
            int discovered = ScSoul.DiscoveredCount(d);
            // Устойчивость: старость, боссы, оборона, глубины, бури, мутации
            t.resilience += ScRng.Clamp(
                (cause == "old_age" ? 5 : 0) + (d.monarchKilled == 1 ? 4 : 0) + (d.titanKilled == 1 ? 5 : 0)
                + (d.raidDefended == 1 ? 4 : 0) + (d.dungeonEntered == 1 ? 2 : 0) + d.player.mutations * 0.5,
                0, 24);
            // Духовная глубина: ядро, духи, воспоминания, Разлом
            t.depth += ScRng.Clamp(
                (d.senseCore == 1 ? 10 : 0) + (d.spiritAbsorbed == 1 ? 6 : 0) + memUnlocked * 4
                + d.loreRift * 1.5 + (d.riftStormSurvived == 1 ? 3 : 0),
                0, 22);
            // Хищная природа: убийства и зло против милосердия и щедрости
            t.predator += ScRng.Clamp(
                Math.Min(18, d.player.kills * 1.1) + evil * 0.10 - Math.Min(9, d.donateCount * 1.5) - mercy * 3,
                -20, 24);
            // Память души: воспоминания, изученный мир, задания
            t.memory += ScRng.Clamp(
                memUnlocked * 5 + Math.Min(8, discovered * 0.6) + questsDone * 1.5, 0, 20);
            t.resilience = Math.Round(ScRng.Clamp(t.resilience, 0, 100));
            t.depth = Math.Round(ScRng.Clamp(t.depth, 0, 100));
            t.predator = Math.Round(ScRng.Clamp(t.predator, 0, 100));
            t.memory = Math.Round(ScRng.Clamp(t.memory, 0, 100));
            return t;
        }

        // ===== Вероятности рождения: черты души + состояние живого мира =====

        /// Вес региона. Добрая душа НЕ гарантированно получает лёгкую жизнь — только склонность.
        /// Мир помнит и реагирует: голодный мир хуже колыбели, побеждённые владыки открывают магию.
        public static Dictionary<string, double> BirthWeights(SoulTraits t, double karma, WorldData d)
        {
            double good = Math.Max(0, karma);
            double evil = Math.Max(0, -karma);
            double eq = Math.Max(0, 20 - Math.Abs(karma)); // души равновесия
            var w = new Dictionary<string, double>();
            w["quiet_cradle"] = 26 + good * 0.30 + (100 - t.predator) * 0.18 - t.depth * 0.06;
            w["root_garden"] = 20 + eq * 0.35 + t.memory * 0.08 + t.depth * 0.10;
            w["bone_narrows"] = 8 + t.predator * 0.42 + evil * 0.12 + t.resilience * 0.12;
            w["forgotten_vein"] = 5 + t.resilience * 0.28 + t.depth * 0.18;
            w["soul_rift"] = 1.5 + t.depth * 0.32 + t.memory * 0.16 + (t.depth >= 50 ? 8 : 0);
            VillageState v = d.village;
            if (v.food < 20 || v.stability < 35) w["quiet_cradle"] -= 8;
            if (d.monarchKilled == 1) w["forgotten_vein"] += 6;
            if (d.titanKilled == 1) w["soul_rift"] += 5;
            if (v.raidsSurvived >= 2) w["bone_narrows"] += 5;
            foreach (string k in REGION_IDS) w[k] = Math.Max(1, w[k]); // невозможных регионов нет
            return w;
        }

        /// Доли в процентах, сортировка по убыванию (regions.ts normalizeWeights; ничьи — по порядку REGION_IDS).
        public static List<RegionOdds> NormalizeWeights(Dictionary<string, double> w)
        {
            double total = 0;
            foreach (string k in REGION_IDS) total += w[k];
            if (total <= 0) total = 1;
            var list = new List<RegionOdds>();
            for (int i = 0; i < REGION_IDS.Length; i++)
            {
                string id = REGION_IDS[i];
                list.Add(new RegionOdds
                {
                    id = id,
                    name = Region(id).name,
                    difficulty = Region(id).difficulty,
                    pct = (int)Math.Round(w[id] / total * 100),
                    order = i
                });
            }
            list.Sort((a, b) => a.pct != b.pct ? b.pct.CompareTo(a.pct) : a.order.CompareTo(b.order));
            return list;
        }

        /// Детерминированный ролл места рождения по накопленным весам (§23 воспроизводимость).
        public static string RollRegion(Dictionary<string, double> w, double roll)
        {
            if (roll < 0 || roll >= 1) roll = ScRng.Clamp(roll, 0, 0.999999);
            double total = 0;
            foreach (string k in REGION_IDS) total += w[k];
            double acc = roll * total;
            foreach (string id in REGION_IDS)
            {
                acc -= w[id];
                if (acc <= 0) return id;
            }
            return "quiet_cradle";
        }

        /// Объяснение вероятностей для экрана Суда: какие качества что повысили (не более 4 строк).
        public static List<string> ExplainBirth(SoulTraits t, double karma, WorldData d)
        {
            var lines = new List<string>();
            if (t.predator >= 40) lines.Add("Хищная природа " + Math.Round(t.predator) + " — тянет к территориям постоянной борьбы (Костяные теснины).");
            if (t.depth >= 35) lines.Add("Духовная глубина " + Math.Round(t.depth) + " — открывает магические области (Забытая жила, Разлом забытых душ).");
            if (t.resilience >= 45) lines.Add("Устойчивость " + Math.Round(t.resilience) + " — выживете там, где слабые души гибнут.");
            if (t.memory >= 35) lines.Add("Память души " + Math.Round(t.memory) + " — дольше пребывания в небытии между жизнями.");
            if (karma >= 40) lines.Add("Карма +" + Math.Round(karma) + " — мир склонен дать вам передышку, но не гарантирует её.");
            if (karma <= -40) lines.Add("Карма " + Math.Round(karma) + " — тьма в душе зовёт туда, где сила решает всё.");
            if (Math.Abs(karma) < 40 && t.predator < 30 && lines.Count == 0)
                lines.Add("Равновесная душа без резких склонностей — мир предлагает самые разнообразные пути.");
            VillageState v = d.village;
            if (v.food < 20 || v.stability < 35) lines.Add("Мир истощён прошлой жизнью — Тихая колыбель уже не так тиха.");
            if (d.titanKilled == 1 || d.monarchKilled == 1) lines.Add("Павшие владыки подземелий — магия ищет новые русла.");
            if (lines.Count > 4) lines.RemoveRange(4, lines.Count - 4);
            return lines;
        }
    }
}
