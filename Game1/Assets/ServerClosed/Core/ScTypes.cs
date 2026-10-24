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

    // ===== Поселение (§15, упрощённо) =====
    [Serializable]
    public class VillageState
    {
        public string name = "Опушка";
        public string stage = "Хутор";
        public int population = 6;
        public double food = 12;
        public double stability = 70;
        public int raidsSurvived;
    }

    // ===== История/карма =====
    [Serializable]
    public class HistoryEntry { public int day, year; public string text; public string kind; }
    [Serializable]
    public class KarmaEvent { public int day, year; public string id, title; public double karma; public string tag; }

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
        public List<HistoryEntry> log = new List<HistoryEntry>();
        public List<KarmaEvent> karmaEvents = new List<KarmaEvent>();
        public double karma = 0;                 // -100..100 текущей жизни (§12)
        public List<string> synDiscovered = new List<string>();
        public Dictionary<string, DungeonState> dungeons = new Dictionary<string, DungeonState>();
        public string activeDungeon;             // null — надземье
        public string deathCause;                // null — жив
        public string birthRegion = "quiet_cradle";
        public int lifeNo = 1;
        // флаги-события
        public int alphaKilled, senseCore, nestExited, inNest, nestReadyShown;
        // рантайм (не сохраняются значимо, но пусть лежат здесь для простоты)
        public List<FxEvent> fx = new List<FxEvent>();
        public string toast;                     // одноразовый тост для UI
        public string toastKind;

        public Poi Poi(string key)
        {
            foreach (var p in pois) if (p.kind == key) return p;
            return null;
        }
        public void PushToast(string text, string kind) { toast = text; toastKind = kind; }
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
