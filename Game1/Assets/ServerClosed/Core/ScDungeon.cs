// SERVERCLOSED — Core: подземелья-инстансы (ТЗ §6 — Регион 2/3 + Подземное русло)
// (порт Reference/WebPrototype/src/game/dungeon.ts).
// «Грибные Катакомбы» и «Кристальные Пещеры» построены на одном реестре DungeonDef:
// генерация, план спавна, врата, босс. Карта детерминирована сидом мира (§23);
// время в подземелье — то же мировое (§7). Родовая норка живёт в ScWorld (v1.1, проверено).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class DungeonDef
    {
        public string id, name;
        public int depths;
        public Func<WorldData, Vec> gatePos;
        public string enterLog, exitLog, cacheLabel;
        public string cacheKind;        // mushroom | crystal | river
        public string bossName;         // пусто — подземелье без владыки (транзитная артерия)
        public string bossTag;          // Creature.questTag босса
        public string bossSpecies;
        public string bossFlag;         // флаг мира = 1, когда босс повержен
        public double bossReward;       // биомасса за босса
        public string bossGene;         // гарантированный ген-трофей
        public Func<int, bool, string[][]> spawnPlan;  // (depth, bossAlive) → [вид, кол-во][]
    }

    public static class ScDungeon
    {
        public const int DUNGEON_W = 22;
        public const int DUNGEON_H = 22;

        public static readonly DungeonDef Catacombs = new DungeonDef
        {
            id = "catacombs",
            name = "Грибные Катакомбы",
            depths = 3,
            gatePos = pois =>
            {
                Poi g = pois.Poi("mushroom_grove");
                return Vec.Of(g.x + 2.4, g.y - 1.6);
            },
            enterLog = "Вы спускаетесь в Грибные Катакомбы. Споры светятся, и темнота дышит.",
            exitLog = "Вы поднимаетесь к свету. Лес встречает вас запахом дождя.",
            cacheLabel = "Взять запасы",
            cacheKind = "mushroom",
            bossName = "Грибной Монарх",
            bossTag = "monarch",
            bossSpecies = "myco_monarch",
            bossFlag = "monarchKilled",
            bossReward = 30,
            bossGene = "bioluminescent",
            spawnPlan = (depth, bossAlive) =>
            {
                switch (depth)
                {
                    case 1: return Plan("sporeling", 4, "mycenoid", 1);
                    case 2: return Plan("sporeling", 3, "mycenoid", 3);
                    case 3: return bossAlive
                        ? Plan("myco_monarch", 1, "mycenoid", 1, "sporeling", 1)
                        : Plan("mycenoid", 2, "sporeling", 3);
                    default: return new string[0][];
                }
            },
        };

        public static readonly DungeonDef CrystalCaves = new DungeonDef
        {
            id = "crystal_caves",
            name = "Кристальные Пещеры",
            depths = 3,
            gatePos = pois =>
            {
                Poi g = pois.Poi("crystal_hollow");
                return Vec.Of(g.x + 2.4, g.y + 1.6);
            },
            enterLog = "Вы входите в Кристальные Пещеры. Воздух звенит, и стены поют на грани слуха.",
            exitLog = "Вы выбираетесь наружу. Каменная звенящая тишина остаётся за спиной.",
            cacheLabel = "Выломать жилу",
            cacheKind = "crystal",
            bossName = "Кристальный Титан",
            bossTag = "titan",
            bossSpecies = "crystal_titan",
            bossFlag = "titanKilled",
            bossReward = 45,
            bossGene = "prismatic_core",
            spawnPlan = (depth, bossAlive) =>
            {
                switch (depth)
                {
                    case 1: return Plan("shardling", 4, "golem", 1);
                    case 2: return Plan("shardling", 3, "golem", 2);
                    case 3: return bossAlive
                        ? Plan("crystal_titan", 1, "golem", 1, "shardling", 1)
                        : Plan("golem", 2, "shardling", 3);
                    default: return new string[0][];
                }
            },
        };

        // «Чаша Отголосов»: Подземное русло — центральная транспортная артерия пещерного
        // мира. Реки приносят пищу из разных биомов, течения разносят споры. Владыки нет:
        // это дорога мира, а не тронный зал (Этап 1 — играбельный участок).
        public static readonly DungeonDef UndergroundChannel = new DungeonDef
        {
            id = "underground_channel",
            name = "Подземное русло",
            depths = 2,
            gatePos = pois => Vec.Of(33.8, 42.2), // затопленный вход у юго-западного берега пруда
            enterLog = "Вы проскальзываете в Подземное русло. Холодное течение несёт запахи чужих земель.",
            exitLog = "Течение выносит вас наружу. Вода Чаши всегда возвращается к топям.",
            cacheLabel = "Подобрать принос",
            cacheKind = "river",
            bossName = "",
            bossTag = "",
            bossSpecies = "toad",
            bossFlag = "",
            bossReward = 0,
            bossGene = "",
            spawnPlan = (depth, bossAlive) =>
            {
                switch (depth)
                {
                    case 1: return Plan("slug", 2, "toad", 2, "mycenoid", 1); // течения принесли споры Катакомб
                    case 2: return Plan("toad", 2, "spider", 2, "slug", 1);
                    default: return new string[0][];
                }
            },
        };

        public static readonly DungeonDef[] Defs = { Catacombs, CrystalCaves, UndergroundChannel };

        public static DungeonDef Def(string id)
        {
            foreach (var d in Defs) if (d.id == id) return d;
            return null;
        }

        static string[][] Plan(params object[] pairs)
        {
            var res = new string[pairs.Length / 2][];
            for (int i = 0; i < pairs.Length / 2; i++)
                res[i] = new[] { (string)pairs[i * 2], pairs[i * 2 + 1].ToString() };
            return res;
        }

        /// Хеш строки в число (для смешивания id подземелья в сид генерации).
        public static uint HashStr(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char ch in s)
            {
                h ^= ch;
                h *= 0x01000193;
            }
            return h;
        }

        /// Генерация одного этажа: комнаты + коридоры, детерминированно по сиду подземелья и глубине.
        public static DungeonFloor GenerateFloor(int seed, int depth, string dungeonId)
        {
            var rng = new ScRng(unchecked((int)((uint)seed ^ (uint)(depth * 0x5f356495L) ^ HashStr(dungeonId))));
            const int w = DUNGEON_W, h = DUNGEON_H;
            var terrain = new string[w * h];
            for (int i = 0; i < terrain.Length; i++) terrain[i] = "rock";

            // комнаты
            var rooms = new List<int[]>();
            int roomCount = 5 + (depth >= 2 ? 1 : 0);
            int guard = 0;
            while (rooms.Count < roomCount && guard++ < 200)
            {
                int rw = rng.Int(3, 6), rh = rng.Int(3, 5);
                int rx = rng.Int(1, w - rw - 2), ry = rng.Int(1, h - rh - 2);
                bool overlaps = false;
                foreach (var r in rooms)
                    if (rx < r[0] + r[2] + 1 && rx + rw + 1 > r[0] && ry < r[1] + r[3] + 1 && ry + rh + 1 > r[1]) { overlaps = true; break; }
                if (overlaps) continue;
                rooms.Add(new[] { rx, ry, rw, rh });
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                        if (x > 0 && y > 0 && x < w - 1 && y < h - 1) terrain[y * w + x] = "floor";
            }

            // коридоры между последовательными комнатами (L-образные)
            for (int i = 1; i < rooms.Count; i++)
            {
                int[] a = rooms[i - 1], b = rooms[i];
                int ax = a[0] + a[2] / 2, ay = a[1] + a[3] / 2;
                int bx = b[0] + b[2] / 2, by = b[1] + b[3] / 2;
                int x = ax, y = ay;
                while (x != bx) { Carve(terrain, w, h, x, y); x += Math.Sign(bx - x); }
                while (y != by) { Carve(terrain, w, h, x, y); y += Math.Sign(by - y); }
                Carve(terrain, w, h, bx, by);
            }

            // светящийся мох / кристаллы и лужи на полу
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (terrain[y * w + x] != "floor") continue;
                    double r = rng.Next();
                    if (r < 0.07 + depth * 0.02) Carve(terrain, w, h, x, y, "glow");
                    else if (r < 0.095) Carve(terrain, w, h, x, y, "puddle");
                }

            int[] first = rooms.Count > 0 ? rooms[0] : null;
            int[] last = rooms.Count > 1 ? rooms[rooms.Count - 1] : null;
            int upX = first != null ? first[0] + first[2] / 2 : 2;
            int upY = first != null ? first[1] + first[3] / 2 : 2;
            int downX = last != null ? last[0] + last[2] / 2 : w - 3;
            int downY = last != null ? last[1] + last[3] / 2 : h - 3;
            if (terrain[upY * w + upX] == "rock") Carve(terrain, w, h, upX, upY);
            if (terrain[downY * w + downX] == "rock") Carve(terrain, w, h, downX, downY);

            return new DungeonFloor { depth = depth, w = w, h = h, terrain = terrain, upX = upX, upY = upY, downX = downX, downY = downY };
        }

        static void Carve(string[] terrain, int w, int h, int x, int y, string t = "floor")
        {
            if (x > 0 && y > 0 && x < w - 1 && y < h - 1) terrain[y * w + x] = t;
        }

        /// Полное состояние подземелья при первом входе в этой жизни.
        public static DungeonState CreateState(int seed, string dungeonId, double returnX, double returnY)
        {
            DungeonDef def = Def(dungeonId);
            var rng = new ScRng(unchecked((int)((uint)seed ^ 0xd00ca7eu ^ HashStr(dungeonId))));
            var floors = new List<DungeonFloor>();
            if (def != null)
            {
                for (int d = 1; d <= def.depths; d++) floors.Add(GenerateFloor(seed, d, dungeonId));
            }
            var caches = new List<DungeonCache>();
            int cid = 1;
            foreach (var f in floors)
            {
                int count = f.depth < def.depths ? 2 : 1;
                int guard = 0, placed = 0;
                while (placed < count && guard++ < 300)
                {
                    int x = rng.Int(1, f.w - 2), y = rng.Int(1, f.h - 2);
                    if (f.terrain[y * f.w + x] != "floor") continue;
                    if (Math.Sqrt((x - f.upX) * (x - f.upX) + (y - f.upY) * (y - f.upY)) < 2.5) continue;
                    if (f.depth < def.depths && Math.Sqrt((x - f.downX) * (x - f.downX) + (y - f.downY) * (y - f.downY)) < 2.5) continue;
                    caches.Add(new DungeonCache { id = cid++, depth = f.depth, x = x, y = y, taken = false });
                    placed++;
                }
            }
            var st = new DungeonState { id = dungeonId, active = false, depth = 1, returnX = (float)returnX, returnY = (float)returnY, floors = floors, caches = caches };
            st.explored = new List<List<int>>();
            foreach (var f in floors) st.explored.Add(new List<int>());
            return st;
        }

        // Диета приносов подземелий — ключи "cache_mushroom"/"cache_crystal"/"cache_river"
        // уже лежат в SensesData.DietFeed (ScData.cs), формат пар [индекс органа, питание].
    }
}
