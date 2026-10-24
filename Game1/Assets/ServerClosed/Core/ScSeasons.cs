// SERVERCLOSED — Core: СЕЗОНЫ — год живёт по календарю, экология дышит
// (порт Reference/WebPrototype/src/game/seasons.ts, ТЗ §7 календарь).
// - 4 сезона × 6 дней: Пробуждение, Цветение, Увядание, Тьма.
// - Сезоны меняют: рождаемость популяций, голод хищников, урожай посёлка,
//   питательность пищи для органов восприятия (продолжение диеты — Дополнение от игрока).
// - Миграции: при смене сезона часть популяций смещается к пищевым узлам
//   (Тьма — теплокровные жмутся к посёлку и пруду, хищники идут за ними).
// - Родовая норка — мини-вселенная: тепло кладки, сезоны туда не доходят (лор Задачи 20).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class SeasonMods
    {
        public double birthMul;          // множитель рождаемости популяций (dailyTick §6)
        public double aggressionAdd;     // прибавка к радиусу/шансу агрессии хищников (голод)
        public double dietMul;           // питательность пищи для органов (диета → перцепция)
        public double villageFoodDelta;  // ежедневная прибавка/убыль еды посёлка (урожай)
        public double fireflyMul;        // яркость светляков/света (визуал)
        public string hint;              // HUD-подсказка «что сезон делает»
    }

    public static class ScSeasons
    {
        public static readonly string[] SeasonNames = { "Пробуждение", "Цветение", "Увядание", "Тьма" };

        public static readonly SeasonMods Awakening = new SeasonMods {
            birthMul = 1.5, aggressionAdd = 0, dietMul = 1.0, villageFoodDelta = 1, fireflyMul = 1,
            hint = "Молодая поросль: зверь выводит потомство, лес полон жизни",
        };
        public static readonly SeasonMods Blossom = new SeasonMods {
            birthMul = 1.7, aggressionAdd = -0.05, dietMul = 1.05, villageFoodDelta = 2, fireflyMul = 1.15,
            hint = "Пик изобилия: добычи много, хищники сыты и спокойны",
        };
        public static readonly SeasonMods Withering = new SeasonMods {
            birthMul = 0.9, aggressionAdd = 0.05, dietMul = 1.3, villageFoodDelta = 0, fireflyMul = 0.95,
            hint = "Грибы плодоносят: пища жирнее, химическое восприятие растёт быстрее",
        };
        public static readonly SeasonMods Darkness = new SeasonMods {
            birthMul = 0.25, aggressionAdd = 0.2, dietMul = 0.8, villageFoodDelta = -2, fireflyMul = 0.8,
            hint = "Голод и стужа: добыча редеет, хищники дерзки, зверь жмётся к людям",
        };

        public static readonly SeasonMods Neutral = new SeasonMods {
            birthMul = 1, aggressionAdd = 0, dietMul = 1, villageFoodDelta = 0, fireflyMul = 1,
            hint = "Микроклимат подземелья: сезонам сюда не дотянуться",
        };

        public static SeasonMods Mods(string season)
        {
            if (season == "Пробуждение") return Awakening;
            if (season == "Цветение") return Blossom;
            if (season == "Увядание") return Withering;
            if (season == "Тьма") return Darkness;
            return Neutral;
        }

        /// Сезон по дню мира (6 дней в сезоне — календарь §7).
        public static string SeasonOf(double totalDays)
        {
            int idx = (int)Math.Floor(totalDays / 6.0) % 4;
            return SeasonNames[idx];
        }

        /// Атмосферная запись в хронику при наступлении сезона.
        public static string SeasonLog(string season)
        {
            if (season == "Пробуждение") return "Пробуждение. Талые воды шепчут, зверь выводит потомство — лес полон лёгкой добычи.";
            if (season == "Цветение") return "Цветение. Тёплый свет, нектар и споры в воздухе: пик изобилия. Хищники сыты и ленивы.";
            if (season == "Увядание") return "Увядание. Листва рыжеет, грибы плодоносят в полную силу — время химического восприятия.";
            return "Тьма. Стужа сжимает мир: добыча редеет, хищники дерзкие, зверь жмётся к людским очагам.";
        }

        /// Виды, чьи спавны сезонно усиливаются (осень — грибы, лето — светляки).
        public static double SeasonalSpawnBoost(string species, string season)
        {
            if (season == "Увядание" && (species == "mushling" || species == "sporeling" || species == "mycenoid")) return 2.0;
            if (season == "Цветение" && (species == "firefly" || species == "toad")) return 1.6;
            if (season == "Тьма" && (species == "wolf" || species == "spider")) return 1.4; // голодные хищники плодятся на падали
            return 1;
        }

        /// Кто куда тянется в какой сезон (миграции): вид → POI-ключ приюта. null — сезона нет в плане.
        public static List<KeyValuePair<string, string>> MigrationsFor(string season)
        {
            var res = new List<KeyValuePair<string, string>>();
            if (season == "Тьма")
            {
                // Тьма: теплокровные жмутся к посёлку (там подкармливают) и пруду (не замерзает у ключей);
                // хищники стягиваются за добычей — волки к посёлку, пауки к грибной роще.
                res.Add(KV("rabbit", "village")); res.Add(KV("deer", "village")); res.Add(KV("wolf", "village"));
                res.Add(KV("toad", "pond")); res.Add(KV("spider", "mushroom_grove")); res.Add(KV("firefly", "stone_circle"));
            }
            else if (season == "Увядание")
            {
                // Увядание: грибы плодоносят — грибная роща кормит слизней и жуков
                res.Add(KV("mushling", "mushroom_grove")); res.Add(KV("slug", "mushroom_grove")); res.Add(KV("beetle", "mushroom_grove"));
            }
            else if (season == "Пробуждение")
            {
                // Пробуждение: рассредоточение по летним угодьям
                res.Add(KV("deer", "pond")); res.Add(KV("rabbit", "pond")); res.Add(KV("wolf", "burrow"));
            }
            return res;
        }

        static KeyValuePair<string, string> KV(string a, string b) { return new KeyValuePair<string, string>(a, b); }
    }
}
