// SERVERCLOSED — Core: задания посёлка (§13 — RPG-цикл, §15 — жители)
// (порт Reference/WebPrototype/src/game/quests.ts + villageStageOf/karmaPathOf из dungeon.ts).
// Данные квестов — чистые определения; состояние — во флагах мира (quest_accepted_/quest_done_/quest_progress_),
// поэтому задания сохраняются вместе с миром и переживают смерти (§11, §18).
// Прогресс пишут хуки мира (убийства/поглощения/вход в подземелье/пожертвования), сдача — через диалог NPC.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class QuestDef
    {
        public string id;
        public string giver;         // elder | hunter | farmer
        public string title, desc, hint;
        public string type;          // kill | absorb | dungeon_enter | boss | donate
        public string target;        // species id | DungeonId | тег босса (null — не нужен)
        public int need;
        public double biomass, karma;
        public int soulXp;
        public string requires;      // id квеста, который нужно завершить раньше (null — нет)
    }

    public enum QuestStatus { Available, Active, Ready, Done }

    public class QuestView
    {
        public QuestDef q;
        public QuestStatus status;
        public double progress;
    }

    public static class ScQuests
    {
        public static readonly QuestDef[] Quests =
        {
            new QuestDef {
                id = "wolves", giver = "hunter",
                title = "Три шкуры",
                desc = "Убить трёх лесных волков — посёлок боится зимы.",
                hint = "«Волки наглеют с каждым днём. Убей трёх — и я поверю, что лес не съест нас зимой.»",
                type = "kill", target = "wolf", need = 3,
                biomass = 25, karma = 3, soulXp = 2,
            },
            new QuestDef {
                id = "descend", giver = "elder",
                title = "Свет во тьме",
                desc = "Спуститься в Грибные Катакомбы у грибной рощи.",
                hint = "«Под рощей лежат старые катакомбы. Грибница там поёт по ночам. Посмотри, что гниёт под нами — и расскажи.»",
                type = "dungeon_enter", target = "catacombs", need = 1,
                biomass = 20, karma = 4, soulXp = 3,
            },
            new QuestDef {
                id = "monarch", giver = "elder",
                title = "Сердце грибницы",
                desc = "Повергнуть Грибного Монарха на третьей глубине Катакомб.",
                hint = "«Грибница видит сон — и в этом сне есть король. Сорви с него корону, и катакомбы выдохнут.»",
                type = "boss", target = "monarch", need = 1, requires = "descend",
                biomass = 40, karma = 6, soulXp = 5,
            },
            new QuestDef {
                id = "shards", giver = "hunter",
                title = "Хрустальные осколки",
                desc = "Поглотить трёх Осколышей из Кристальных Пещер у каменной ложбинки.",
                hint = "«На северо-западе камни звенят, будто в них спрятаны колокольчики. Принеси мне три осколочные туши — дознайся, что это.»",
                type = "absorb", target = "shardling", need = 3, requires = "wolves",
                biomass = 30, karma = 3, soulXp = 4,
            },
            new QuestDef {
                id = "harvest", giver = "farmer",
                title = "Голодный год",
                desc = "Трижды передать биомассу в запасы посёлка (по 10 за раз).",
                hint = "«Зима будет тощая. Три раза поделись своей диковинной биомассой — и я отпою тебе у костра.»",
                type = "donate", need = 3,
                biomass = 0, karma = 6, soulXp = 4,
            },
            new QuestDef {
                id = "titan", giver = "elder",
                title = "Падший титан",
                desc = "Повергнуть Кристального Титана на третьей глубине Кристальных Пещер.",
                hint = "«Сны о каменном исполине приходят теперь даже детям. Если грибница пала — пади и ты, титан пещер.»",
                type = "boss", target = "titan", need = 1, requires = "monarch",
                biomass = 60, karma = 5, soulXp = 8,
            },
        };

        public static QuestDef ById(string id)
        {
            foreach (var q in Quests) if (q.id == id) return q;
            return null;
        }

        public static bool Accepted(WorldData f, string id) { return f.Flag("quest_accepted_" + id) == 1; }
        public static bool Done(WorldData f, string id) { return f.Flag("quest_done_" + id) == 1; }
        public static double Progress(WorldData f, string id) { return f.Flag("quest_progress_" + id); }

        public static QuestStatus Status(WorldData f, QuestDef q)
        {
            if (Done(f, q.id)) return QuestStatus.Done;
            if (!Accepted(f, q.id)) return QuestStatus.Available;
            return Progress(f, q.id) >= q.need ? QuestStatus.Ready : QuestStatus.Active;
        }

        /// Список квестов в порядке получения со статусами (для журнала).
        public static List<QuestView> Views(WorldData f)
        {
            var res = new List<QuestView>();
            foreach (var q in Quests) res.Add(new QuestView { q = q, status = Status(f, q), progress = Progress(f, q.id) });
            return res;
        }

        /// Квесты, которые этот NPC может выдать или принять (не сданные и с выполненным требованием).
        public static List<QuestView> ForGiver(WorldData f, string giver)
        {
            var res = new List<QuestView>();
            foreach (var q in Quests)
            {
                if (q.giver != giver) continue;
                var st = Status(f, q);
                if (st == QuestStatus.Done) continue;
                if (!string.IsNullOrEmpty(q.requires) && !Done(f, q.requires)) continue;
                res.Add(new QuestView { q = q, status = st, progress = Progress(f, q.id) });
            }
            return res;
        }

        /// Сколько заданий всего завершено (для достижения quests3).
        public static int CompletedCount(WorldData f)
        {
            int n = 0;
            foreach (var q in Quests) if (Done(f, q.id)) n++;
            return n;
        }

        public static string GiverLabel(string giver)
        {
            return giver == "elder" ? "Старейшина" : giver == "hunter" ? "Охотник" : "Фермер";
        }

        // ===== Стадии посёлка (§15: цивилизация растёт) — dungeon.ts villageStageOf =====
        public static void VillageStageOf(int population, out string name, out double foodBonus)
        {
            if (population >= 16) { name = "Посад"; foodBonus = 2; return; }
            if (population >= 10) { name = "Деревня"; foodBonus = 1; return; }
            name = "Хутор"; foodBonus = 0;
        }

        // ===== Пути кармы (§12) — dungeon.ts karmaPathOf =====
        public static string KarmaPathOf(double karma)
        {
            if (karma >= 40) return "Свет";
            if (karma <= -40) return "Тьма";
            return "Равновесие";
        }
    }
}
