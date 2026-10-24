// SERVERCLOSED — Core: модели данных мира (порт types.ts). Только [Serializable]-POCO:
// сохраняются через JsonUtility (ScSave.cs). Переносимо в C# без Unity-зависимостей.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    // ===== Карта =====
    [Serializable]
    public class Deco
    {
        public int id;
        public string kind;        // tree|pine|bush|rock|mushroom|stone|hut|fence|campfire|farm|well|crystal|bones|crack|reed
        public float x, y;
        public float r;            // радиус коллизии (0 — проходимо)
        public int seed;
    }

    [Serializable]
    public class Poi { public string kind; public float x, y, r; }

    [Serializable]
    public class TileMap
    {
        public int w, h;
        public string[] terrain;   // w*h — moss|dirt|path|water|glade|ruin|mountain|marsh|ash
        public List<Deco> deco = new List<Deco>();
    }

    // ===== Подземелья (Регион 2/3 + родовая норка) =====
    [Serializable]
    public class DungeonFloor
    {
        public int depth, w, h;
        public string[] terrain;   // rock|floor|glow|puddle
        public float upX, upY, downX, downY;
    }

    [Serializable]
    public class DungeonCache { public int id, depth; public float x, y; public bool taken; }

    [Serializable]
    public class DungeonState
    {
        public string id;          // birth_nest | catacombs | crystal_caves | underground_channel
        public bool active;
        public int depth = 1;
        public float returnX, returnY;
        public List<DungeonFloor> floors = new List<DungeonFloor>();
        public List<DungeonCache> caches = new List<DungeonCache>();
        public List<List<int>> explored = new List<List<int>>();   // разведанные тайлы по глубинам
    }

    // ===== Существа =====
    [Serializable]
    public class Creature
    {
        public int id;
        public string species;
        public string name;            // имена особых существ (боссы, Рваное Ухо)
        public float x, y, vx, vy;
        public double hp;
        public double ageDays;
        public string ai = "wander";   // idle|wander|flee|chase|attack|guard|dead
        public int targetId = -1;
        public float wx, wy;           // wanderTo
        public bool hasWanderTo;
        public double stateT;
        public double atkCd, hitFlash;
        public bool dead;
        public double corpseT;
        public bool absorbed;
        public bool aggroByPlayer;
        public double stunT, slowT;
        public string questTag;
        public int gen = 1;
        public float homeX, homeY; public bool hasHome;
        public int dungeonFloor;       // 0 — надземное
        public string dungeonId;       // null — надземное
    }

    // ===== Тело игрока =====
    [Serializable]
    public class Genome
    {
        public List<string> installed = new List<string>();
        public double capacity = 4;
        public int slots = 3;
        public int nodes = 0;
    }

    [Serializable]
    public class PlayerState
    {
        public float x, y, vx, vy;
        public int facing = 1;
        public double hp = 34;
        public double energy = 4;
        public double ageDays = 0;
        public double biomass = 0;
        public int kills, absorbedCount, mutations;
        public double atkCd, hitFlash, attackAnim;
        public double restT;
        public Genome genome = new Genome();
        public List<string> knownGenes = new List<string>();
        public List<string> perks = new List<string>();
        // дары тела от варианта перерождения (§11.3 ReincarnOption.mods)
        public double bonusMaxHp, bonusAtk, bonusDef, bonusSpeed, bonusPerception;
        // изученные виды этой жизни (веб: player.discoveredSpecies — в момент поглощения)
        public List<string> discoveredSpecies = new List<string>();
    }

    // ===== Восприятие (Дополнение к ТЗ) =====
    [Serializable]
    public class SensoryState
    {
        public double biomassTotal;
        // питание органов по SensesData.ORDER (touch..hearing) + [7] = spirit
        public double[] feed = new double[8];
        public string focus;           // null — нет фокуса
        public double ore;
        public double magicEnergy;
        public double overloadT;
        public string[] unlocked = { "touch" };
        public List<int> explored = new List<int>();
    }

    // ===== Пищеварение (§5–§7) =====
    [Serializable]
    public class StoredItem
    {
        public int id;
        public int kind;               // 0 organic 1 mineral 2 magic 3 toxic
        public string name;
        public string species;
        public double mass, hardness;
        public double[] diet;          // питание рецепторов (индексы SensesData.ORDER)
        public double oreYield;
        public string[] genePool;
        public double geneChance;
        public double toxicity;
        public double progress;
        public int seed;
        public int chamber;
        public double elapsed;
    }

    [Serializable]
    public class DigestiveState
    {
        public StoredItem c0, c1;      // две полости (§6.5)
        public string mode = "balanced";
        public string[] modeUnlocked = { "balanced" };
        public double insoluble;
        public int seq = 1;

        public StoredItem Get(int i) { return i == 0 ? c0 : c1; }
        public void Set(int i, StoredItem it) { if (i == 0) c0 = it; else c1 = it; }
        public IEnumerable<StoredItem> Items()
        {
            if (c0 != null) yield return c0;
            if (c1 != null) yield return c1;
        }
    }

    // ===== Поселение (§15) и NPC посёлка =====
    [Serializable]
    public class VillageState
    {
        public string name = "Опушка";
        public string stage = "Хутор";
        public int population = 11;
        public double food = 40;
        public double stability = 60;
        public double military = 25;
        public int raidsSurvived;
        public double raidCdDays = 2;            // передышка после набега (Свет продлевает)
        public bool raidPending;                 // стая у ограды — ждёт обороны (Magic Chess)
        public double raidDeadline;              // день, когда игнор обернётся разорением
    }

    [Serializable]
    public class Npc
    {
        public string id, name, role;
        public float x, y;
        public double ageDays;
        public double lifespan;
        public bool alive = true;
        public double attitude = 50;
        public string dialogue = "hello";        // hello | hello_successor
    }

    // ===== История/карма =====
    [Serializable]
    public class HistoryEntry { public int day, year; public string text; public string kind; }
    [Serializable]
    public class KarmaEvent { public int day, year; public string id, title; public double karma; public string tag; }

    // ===== Душа (порт soul.ts + «Резонанс души» regions.ts; §11–§12) =====
    [Serializable]
    public class SoulTraits
    {
        public double resilience;   // 0..100 — устойчивость: переносит экстремальные условия
        public double depth;        // 0..100 — духовная глубина: чувствительность к магии и душам
        public double predator;     // 0..100 — хищная природа: склонность к насилию и поглощению
        public double memory;       // 0..100 — память души: сила унаследованных впечатлений
    }

    [Serializable]
    public class SoulStats
    {
        public int memory;          // Память
        public int will;            // Воля
        public int adaptation;      // Адаптация
        public int perception;      // Восприятие
        public int resonance;       // Резонанс
        public int integrity;       // Целостность
    }

    [Serializable]
    public class MemoryFragment { public string id, title, text; public bool unlocked; }

    [Serializable]
    public class Achievement { public string id, title, desc; public int soulXp; public bool got; }

    [Serializable]
    public class PastLifeRecord
    {
        public int life;
        public string species;
        public double ageDays;
        public string cause;
        public double karma;
        public int xpGained;
        public string summary;
    }

    /// Душа — отдельный от тела объект: реинкарнация заменяет тело, но не душу.
    [Serializable]
    public class SoulState
    {
        public int life = 1;                // номер текущего воплощения
        public int xp;
        public int level = 1;               // уровень души 1..5
        public double karma;                // -100..100, живёт между жизнями
        public SoulStats stats = new SoulStats();
        public List<PastLifeRecord> pastLives = new List<PastLifeRecord>();
        public List<MemoryFragment> memories = new List<MemoryFragment>();
        public bool resonantCore;           // резонансное ядро сплетено — переходит между жизнями
        public int spiritMemory;            // сколько жизней душа жила с ядром
        public SoulTraits traits = new SoulTraits();
        public string birthRegion = "quiet_cradle"; // регион последнего рождения
    }

    /// Доля региона в вероятности рождения (для экрана Суда; сохраняется вместе с итогом суда).
    [Serializable]
    public class RegionOdds { public string id; public string name; public string difficulty; public int pct; public int order; }

    // ===== Magic Chess (§14, §20): временные боевые копии — не существа мира =====
    [Serializable]
    public class ChessUnitDef
    {
        public string id, name, role, species;
        public double hp, atk, def;
        public int range, atkCdMs;
        public string trait;
        public List<string> synergies = new List<string>();
    }

    [Serializable]
    public class ChessUnit
    {
        public int uid;
        public string defId, side;               // player | enemy
        public int r, c;
        public double hp, cd;
        public bool alive = true;
        public double stunT, slowT, poisonT;
    }

    [Serializable]
    public class ChessState
    {
        public string phase = "setup";           // setup | battle | result
        public int round;
        public int boardR, boardC;
        public List<ChessUnitDef> playerRoster = new List<ChessUnitDef>();
        public List<ChessUnit> placed = new List<ChessUnit>();
        public List<ChessUnit> enemies = new List<ChessUnit>();
        public int reserveLimit;
        public List<string> battleLog = new List<string>();
        public double worldMinutesSpent;
        public long battleRngState = -1;
        public int nextUnitId = 1000;
        public bool resultApplied;
        public string scenario, stake, result;   // result: win | lose (только в фазе result)
    }

    // ===== Мир целиком (единственный сейв-корень, §18) =====
    [Serializable]
    public class WorldData
    {
        public int seed;
        public long rngState = -1;
        public double minutes;
        public int dayTicks;
        public int nextId = 1;
        public TileMap mapJson = new TileMap();
        public List<Poi> pois = new List<Poi>();
        public PlayerState player = new PlayerState();
        public List<Creature> creatures = new List<Creature>();
        public SensoryState senses = new SensoryState();
        public DigestiveState digestion = new DigestiveState();
        public VillageState village = new VillageState();
        public List<Npc> npcs = new List<Npc>();
        public List<HistoryEntry> log = new List<HistoryEntry>();
        public List<KarmaEvent> karmaEvents = new List<KarmaEvent>();
        public double karma = 0;                 // -100..100 текущей жизни (§12)
        public List<string> synDiscovered = new List<string>();
        public Dictionary<string, DungeonState> dungeons = new Dictionary<string, DungeonState>();
        public string activeDungeon;             // null — надземье
        public string deathCause;                // null — жив; человекочитаемая причина
        public string deathCauseCode;            // null — жив; old_age | killed | mutation | starved
        public string birthRegion = "quiet_cradle";
        public int lifeNo = 1;
        // флаги-события
        public int alphaKilled, senseCore, nestExited, inNest, nestReadyShown;
        // флаги-события «мира, который помнит» (не сбрасываются перерождением — мир помнит)
        public int monarchKilled, titanKilled, raidDefended, dungeonEntered, spiritAbsorbed, riftStormSurvived, wolfQuestDone;
        public int loreRift;                     // счётчик преданий Разлома (источник появится с лором)
        public int donateCount;                  // пожертвования посёлку (источник появится с экономикой)
        // ===== Душа и достижения (v1.2, §11–§12) =====
        public SoulState soul = new SoulState();
        public List<Achievement> achievements = new List<Achievement>();
        public double karmaTotal = 0;            // сумма поступков жизни без клампа (для суда и черт)
        public int judged;                       // суд этой смерти уже применён
        public string birthRegionNext;           // регион, выпавший по итогам суда (ролл при суде)
        public List<RegionOdds> judgmentOdds = new List<RegionOdds>(); // доли на момент суда
        public int lastSkipDays;                 // сколько дней мир жил без игрока (для тоста)
        // ===== Полная игра (v1.3) =====
        public ChessState chess;                 // текущий бой Magic Chess (null — нет боя)
        public Dictionary<string, double> flags = new Dictionary<string, double>(); // флаги-события (веб: Record<string,number>)
        public Dictionary<string, int> worldRegions = new Dictionary<string, int>(); // «Чаша Отголосов»: открытые места (п.9)
        public double flagStealCd;               // кража со склада: не раньше этого дня
        public double flagWolvesKilled;          // счётчик волков (ретроактивный прогресс задания)
        // рантайм (не сохраняются: очередь событий эффектов живёт один кадр)
        [NonSerialized] public List<FxEvent> fx = new List<FxEvent>();
        [NonSerialized] public string lastDiscoveredRegion;   // движок забирает для тоста (consume)
        public string toast;                     // одноразовый тост для UI
        public string toastKind;

        public Poi Poi(string key)
        {
            foreach (var p in pois) if (p.kind == key) return p;
            return null;
        }
        public void PushToast(string text, string kind) { toast = text; toastKind = kind; }
        /// Значение флага (веб: flags[key] ?? 0).
        public double Flag(string key)
        {
            double v;
            return flags.TryGetValue(key, out v) ? v : 0;
        }
        public void SetFlag(string key, double v) { flags[key] = v; }
    }

    [Serializable]
    public class FxEvent { public string kind; public float x, y; public string text; public string color; }

    // ===== Производные характеристики (§9: computeDerived) =====
    public class DerivedStats
    {
        public double maxHp = 34, atk = 4, def = 1, speed = 2.0, perception = 130;
        public double energyMax = 4, hpRegen = 0.25, lifeDays = 14;
        public double stability = 100, energyUsed = 0;
        public List<SynergyDef> synergies = new List<SynergyDef>();
        public HashSet<string> visuals = new HashSet<string> { "slug" };
    }
}
