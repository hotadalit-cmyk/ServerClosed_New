// SERVERCLOSED — Core: поселение и NPC — диалоги с решениями и последствиями
// (порт world.ts: npcDialogue/npcAction/stealFood/donateFood, §3.2, §15).
// Деревья диалогов — данные; исполнение действий — через публичный API мира.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class DialogueOption { public string id; public string label; }
    public class NpcDialogueData
    {
        public string id, name, role, text;
        public List<DialogueOption> options = new List<DialogueOption>();
    }

    public static class ScNpcs
    {
        /// Дефолтная тройка жителей Опушки (world.ts newGame).
        public static List<Npc> DefaultTrio(WorldData d)
        {
            Poi v = d.Poi("village");
            double vx = v != null ? v.x : 26, vy = v != null ? v.y : 24;
            return new List<Npc>
            {
                new Npc { id = "mirra", name = "Старейшина Мирра", role = "старейшина", x = (float)(vx - 2.4), y = (float)(vy - 1.3), ageDays = 340, lifespan = 480, alive = true, attitude = 50, dialogue = "hello" },
                new Npc { id = "dar", name = "Охотник Дар", role = "охотник", x = (float)(vx + 2.6), y = (float)(vy - 1.0), ageDays = 96, lifespan = 300, alive = true, attitude = 45, dialogue = "hello" },
                new Npc { id = "klumba", name = "Хранительница Клумба", role = "фермер", x = (float)(vx - 4.0), y = (float)(vy + 0.9), ageDays = 150, lifespan = 350, alive = true, attitude = 45, dialogue = "hello" },
            };
        }

        /// Диалог NPC: текст по отношению/состоянию мира + опции (задания, торговля, сюжеты).
        public static NpcDialogueData Dialogue(ScWorld w, string id)
        {
            Npc n = w.Npc(id);
            var res = new NpcDialogueData { id = id, name = n != null ? n.name : id, role = n != null ? n.role : "", text = "" };
            if (n == null) return res;
            var d = w.data;
            var v = d.village;
            var options = res.options;
            double att = n.attitude;
            if (id == "mirra" || id == "ivo")
            {
                var eq = ScQuests.ForGiver(d, "elder");
                var ready = eq.FindAll(x => x.status == QuestStatus.Ready);
                var active = eq.FindAll(x => x.status == QuestStatus.Active);
                var avail = eq.FindAll(x => x.status == QuestStatus.Available);
                res.text = att > 65
                    ? "«Лес шепчет о тебе, странник. Ты не такой, как прочий зверь.»"
                    : att > 35
                        ? "«Слизень, что мыслит... В наши годы чудес не ждали. Но лес полон загадок.»"
                        : "«Уходи, пока не прогнали. Ты пахнешь бедой.»";
                if (active.Count > 0) res.text += " " + active[0].q.hint;
                if (ready.Count > 0)
                {
                    res.text = "«Ты вернулся не с пустыми руками — я чую это. Говори.»";
                    foreach (var r in ready) options.Insert(0, new DialogueOption { id = "turnin_" + r.q.id, label = "Сдать задание: «" + r.q.title + "»" });
                }
                else if (avail.Count > 0)
                {
                    options.Add(new DialogueOption { id = "accept_" + avail[0].q.id, label = "Взять задание: «" + avail[0].q.title + "»" });
                }
                options.Add(new DialogueOption { id = "ask_rift", label = "Спросить о Разломе" });
                if (v.raidPending) options.Add(new DialogueOption { id = "raid_info", label = "Что за шум у ограды?" });
                options.Add(new DialogueOption { id = "leave", label = "Уйти" });
            }
            else if (id == "dar" || id == "rinsa")
            {
                if (v.raidPending)
                {
                    res.text = "«Стая у забора! Возьми ополчение и отгони их — или мы все сдохнем этой ночью.»";
                    options.Add(new DialogueOption { id = "start_raid", label = "Начать оборону (Magic Chess)" });
                    if (d.Flag("alphaSpawned") == 0) options.Add(new DialogueOption { id = "hunt_alpha", label = "Я найду вожака сам — стая уйдёт" });
                    options.Add(new DialogueOption { id = "leave", label = "Уйти" });
                }
                else
                {
                    var hq = ScQuests.ForGiver(d, "hunter");
                    var ready = hq.FindAll(x => x.status == QuestStatus.Ready);
                    var active = hq.FindAll(x => x.status == QuestStatus.Active);
                    var avail = hq.FindAll(x => x.status == QuestStatus.Available);
                    if (ready.Count > 0)
                    {
                        res.text = "«Ну? Говори, что принёс — не томи.»";
                        foreach (var r in ready) options.Insert(0, new DialogueOption { id = "turnin_" + r.q.id, label = "Сдать задание: «" + r.q.title + "»" });
                    }
                    else if (active.Count > 0)
                    {
                        res.text = active[0].q.type == "kill"
                            ? "«Волков ещё ходит: " + w.PopCount("wolf") + ". Не возвращайся с пустыми руками.»"
                            : "«Пока тихо. Но камни на северо-западе звенят — проверь, как там осколки.»";
                    }
                    else if (avail.Count > 0)
                    {
                        res.text = avail[0].q.hint;
                    }
                    else
                    {
                        res.text = "«Лес опустел. Хороший знак... или плохой.»";
                    }
                    if (avail.Count > 0 && ready.Count == 0)
                        options.Add(new DialogueOption { id = "accept_" + avail[0].q.id, label = "Взять задание: «" + avail[0].q.title + "»" });
                    options.Add(new DialogueOption { id = "leave", label = "Уйти" });
                }
            }
            else if (id == "klumba" || id == "prokh")
            {
                var fq = ScQuests.ForGiver(d, "farmer");
                var ready = fq.FindAll(x => x.status == QuestStatus.Ready);
                var avail = fq.FindAll(x => x.status == QuestStatus.Available);
                res.text = v.food < 25
                    ? "«Запрыгали волки — а мы сидим без еды. Если у тебя есть лишняя биомасса...»"
                    : "«Ферма даёт плоды, пока духи леса добры. Иди с миром, слизень.»";
                if (ready.Count > 0)
                {
                    res.text = "«Ты славно потрудился для посёлка. Держи, что обещала.»";
                    foreach (var r in ready) options.Insert(0, new DialogueOption { id = "turnin_" + r.q.id, label = "Сдать задание: «" + r.q.title + "»" });
                }
                else if (avail.Count > 0)
                {
                    res.text = avail[0].q.hint;
                    options.Add(new DialogueOption { id = "accept_" + avail[0].q.id, label = "Взять задание: «" + avail[0].q.title + "»" });
                }
                options.Add(new DialogueOption { id = "donate", label = "Отдать 10 биомассы в запасы (+карма)" });
                options.Add(new DialogueOption { id = "steal", label = "Тихо обчистить склад (−карма)" });
                options.Add(new DialogueOption { id = "ask_spirits", label = "Спросить о духах" });
                options.Add(new DialogueOption { id = "leave", label = "Уйти" });
            }
            return res;
        }

        /// Исполнить выбранную опцию диалога (веб: npcAction).
        public static void Action(ScWorld w, string id, string opt)
        {
            var v = w.data.village;
            switch (opt)
            {
                case "donate":
                    if (w.DonateFood())
                    {
                        Npc n1 = w.Npc(id);
                        if (n1 != null) n1.attitude = ScRng.Clamp(n1.attitude + 6, 0, 100);
                    }
                    break;
                case "steal":
                    w.StealFood();
                    break;
                case "hunt_alpha":
                    w.data.SetFlag("alphaSpawned", 1);
                    Poi burrow = w.data.Poi("burrow");
                    if (burrow != null)
                    {
                        Creature alpha = w.SpawnCreaturePublic("wolf", Vec.Of(burrow.x + 1, burrow.y));
                        if (alpha != null) { alpha.questTag = "alpha"; alpha.name = "Рваное Ухо"; }
                    }
                    Npc n2 = w.Npc(id);
                    if (n2 != null) n2.attitude = ScRng.Clamp(n2.attitude + 4, 0, 100);
                    w.PushLog("Дар: «Вожака зовут Рваное Ухо. Убей его — стая разбредётся. Так честнее.»", "village");
                    break;
                case "start_raid":
                    w.data.SetFlag("startRaid", 1); // движок прочитает флаг и откроет Magic Chess
                    break;
                case "ask_spirits":
                    w.PushLog("Клумба: «Духи собираются у старых камней на северо-востоке. Не тревожь их попусту.»", "world");
                    w.data.SetFlag("showStoneCircle", 1);
                    break;
                case "ask_rift":
                    w.PushLog("«Разлом? Не смей произносить это у детей. Пустошь на юге — не для живых.»", "world");
                    w.data.loreRift++;
                    break;
                case "raid_info":
                    w.PushLog("«Волки. Собирай союзников и отгоняй их — или посёлок обнищает.»", "village");
                    break;
                default:
                    // задания: принять / сдать (§13)
                    if (opt != null && opt.StartsWith("accept_"))
                    {
                        string qid = opt.Substring("accept_".Length);
                        if (w.AcceptQuest(qid))
                        {
                            Npc n3 = w.Npc(id);
                            if (n3 != null) n3.attitude = ScRng.Clamp(n3.attitude + 5, 0, 100);
                        }
                    }
                    else if (opt != null && opt.StartsWith("turnin_"))
                    {
                        string qid2 = opt.Substring("turnin_".Length);
                        if (w.TurnInQuest(qid2))
                        {
                            Npc n4 = w.Npc(id);
                            if (n4 != null) n4.attitude = ScRng.Clamp(n4.attitude + 10, 0, 100);
                        }
                    }
                    break;
            }
        }
    }
}
