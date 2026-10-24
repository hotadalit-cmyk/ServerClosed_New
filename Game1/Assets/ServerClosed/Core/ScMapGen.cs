// SERVERCLOSED — Core: генерация карты «Лес Первого Дыхания» (порт mapgen.ts, упрощённый атлас).
// Детерминирована по сиду: та же карта, что в веб-версии (ключевые POI и состав совпадают).
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public static class ScMapGen
    {
        public const int MAP_W = 52;
        public const int MAP_H = 52;

        // Ключевые POI (совпадают с веб-версией)
        public static readonly float[] VILLAGE = { 26, 24 };
        public static readonly float[] POND = { 38, 36 };
        public static readonly float[] GROVE = { 9, 40 };
        public static readonly float[] CRYSTAL = { 7, 7 };
        public static readonly float[] STONES = { 42, 8 };
        public static readonly float[] BURROW = { 40, 24 };
        public static readonly float[] RIFT = { 45, 45 };
        public static readonly float[] RUINS = { 14, 20 };
        public static readonly float[] BOGS = { 24, 45 };

        static readonly float[][] RIVER_WAYPOINTS =
        {
            new[] { 25f, 3f }, new[] { 23f, 9f }, new[] { 27f, 15f }, new[] { 22f, 21f },
            new[] { 24f, 28f }, new[] { 21f, 35f }, new[] { 23f, 41f }, new[] { 24f, 46f },
        };

        static readonly float[][] Critical =
        {
            new[] { 19.5f, 30.5f }, new[] { 11.2f, 41.6f }, new[] { 43.6f, 27.4f },
            new[] { 9.6f, 10.0f }, new[] { 42.8f, 42.8f }, new[] { 11.4f, 38.4f },
            new[] { 9.4f, 8.6f }, new[] { 33.8f, 42.2f }, new[] { 26f, 24f },
        };

        public static void Generate(WorldData d)
        {
            var rng = new ScRng((int)((uint)d.seed ^ 0x9e3779b9u));
            int w = MAP_W, h = MAP_H;
            var map = d.mapJson;
            map.w = w; map.h = h;
            map.terrain = new string[w * h];
            map.deco = new List<Deco>();
            var T = map.terrain;
            for (int i = 0; i < T.Length; i++) T[i] = "moss";

            void Put(int x, int y, string t) { if (x >= 0 && y >= 0 && x < w && y < h) T[y * w + x] = t; }

            // POI
            void AddPoi(string kind, double x, double y, double r) { d.pois.Add(new Poi { kind = kind, x = (float)x, y = (float)y, r = (float)r }); }
            AddPoi("village", VILLAGE[0], VILLAGE[1], 7);
            AddPoi("pond", POND[0], POND[1], 5.5);
            AddPoi("mushroom_grove", GROVE[0], GROVE[1], 6);
            AddPoi("crystal_hollow", CRYSTAL[0], CRYSTAL[1], 5);
            AddPoi("stone_circle", STONES[0], STONES[1], 5);
            AddPoi("burrow", BURROW[0], BURROW[1], 4);
            AddPoi("rift", RIFT[0], RIFT[1], 5.5);
            AddPoi("ruins", RUINS[0], RUINS[1], 5);
            AddPoi("bogs", BOGS[0], BOGS[1], 6);

            // Пруд
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double dd = Math.Sqrt((x - POND[0]) * (x - POND[0]) + (y - POND[1]) * 1.15 * (y - POND[1]) * 1.15);
                if (dd < 5.5 * (0.8 + rng.Next() * 0.35)) Put(x, y, "water");
            }
            // Поляна поселения
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (Math.Sqrt((x - VILLAGE[0]) * (x - VILLAGE[0]) + (y - VILLAGE[1]) * (y - VILLAGE[1])) < 8.5 && T[y * w + x] != "water") Put(x, y, "glade");
            // Роща и ложбинка — земля иная
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (Math.Sqrt((x - GROVE[0]) * (x - GROVE[0]) + (y - GROVE[1]) * (y - GROVE[1])) < 6) Put(x, y, "dirt");
                if (Math.Sqrt((x - CRYSTAL[0]) * (x - CRYSTAL[0]) + (y - CRYSTAL[1]) * (y - CRYSTAL[1])) < 5) Put(x, y, "dirt");
            }
            // Круг камней — руинный грунт
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (Math.Sqrt((x - STONES[0]) * (x - STONES[0]) + (y - STONES[1]) * (y - STONES[1])) < 5) Put(x, y, "ruin");

            // Тропы
            void Path(double ax, double ay, double bx, double by)
            {
                int x = (int)ax, y = (int)ay, guard = 0;
                while ((x != (int)bx || y != (int)by) && guard++ < 400)
                {
                    Put(x, y, "path");
                    if (rng.Chance(0.35)) Put(x + 1, y, "path");
                    if (x != (int)bx && (y == (int)by || rng.Chance(0.6))) x += Math.Sign(bx - x);
                    else y += Math.Sign(by - y);
                }
                Put((int)bx, (int)by, "path");
            }
            Path(VILLAGE[0], VILLAGE[1], POND[0], POND[1]);
            Path(VILLAGE[0], VILLAGE[1], GROVE[0], GROVE[1]);
            Path(VILLAGE[0], VILLAGE[1], CRYSTAL[0], CRYSTAL[1]);
            Path(VILLAGE[0], VILLAGE[1], STONES[0], STONES[1]);
            Path(VILLAGE[0], VILLAGE[1], BURROW[0], BURROW[1]);

            // Декорации
            int idc = 1;
            void Add(string kind, double x, double y, double r, int seedv) { map.deco.Add(new Deco { id = idc++, kind = kind, x = (float)x, y = (float)y, r = (float)r, seed = seedv }); }

            // Лес: гуще к краям
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                string t = T[y * w + x];
                if (t == "path" || t == "water" || t == "glade" || t == "ruin") continue;
                double edge = Math.Min(Math.Min(x, y), Math.Min(w - 1 - x, h - 1 - y));
                double density = 0.10 + (1 - Math.Min(1, edge / 12)) * 0.16;
                if (Math.Sqrt((x - VILLAGE[0]) * (x - VILLAGE[0]) + (y - VILLAGE[1]) * (y - VILLAGE[1])) > 10) density += 0.05;
                if (t == "dirt") density *= 0.35;
                if (rng.Chance(density))
                    Add(rng.Chance(0.35) ? "pine" : "tree", x + rng.Range(-0.3, 0.3), y + rng.Range(-0.3, 0.3), 0.55, rng.Int(0, 1000000000));
                else if (rng.Chance(0.02)) Add("bush", x, y, 0.4, rng.Int(0, 1000000000));
                else if (rng.Chance(0.008)) Add("rock", x, y, 0.5, rng.Int(0, 1000000000));
            }
            // Грибы рощи
            for (int i = 0; i < 26; i++)
            {
                double a = rng.Next() * Math.PI * 2, r = rng.Range(0.5, 6);
                Add("mushroom", GROVE[0] + Math.Cos(a) * r, GROVE[1] + Math.Sin(a) * r, 0.35, rng.Int(0, 1000000000));
            }
            // Кристаллы ложбинки
            for (int i = 0; i < 14; i++)
            {
                double a = rng.Next() * Math.PI * 2, r = rng.Range(0.5, 5);
                Add("crystal", CRYSTAL[0] + Math.Cos(a) * r, CRYSTAL[1] + Math.Sin(a) * r, 0.45, rng.Int(0, 1000000000));
            }
            // Менгиры круга + кости
            for (int i = 0; i < 7; i++)
            {
                double a = i / 7.0 * Math.PI * 2;
                Add("stone", STONES[0] + Math.Cos(a) * 3.75, STONES[1] + Math.Sin(a) * 3.75, 0.5, rng.Int(0, 1000000000));
            }
            Add("bones", STONES[0] + 1.2, STONES[1] - 1.2, 0.2, rng.Int(0, 1000000000));

            // Поселение
            double vx = VILLAGE[0], vy = VILLAGE[1];
            Add("hut", vx - 2.4, vy - 2.2, 0.95, 11);
            Add("hut", vx + 2.6, vy - 1.9, 0.95, 22);
            Add("hut", vx - 2.2, vy + 2.4, 0.95, 33);
            Add("hut", vx + 2.7, vy + 2.6, 0.95, 44);
            Add("well", vx + 0.2, vy + 0.2, 0.55, 55);
            Add("campfire", vx - 0.9, vy + 1.4, 0.35, 66);
            Add("farm", vx - 4.6, vy + 0.4, 0.2, 77);
            for (double a2 = 0; a2 < Math.PI * 2; a2 += Math.PI / 22)
            {
                double fx = vx + Math.Cos(a2) * 7.4, fy = vy + Math.Sin(a2) * 7.4 * 0.82;
                if (Math.Abs(Math.Cos(a2) - 1) < 0.22 || Math.Abs(Math.Sin(a2) - 0.75) < 0.2) continue;
                Add("fence", fx, fy, 0.28, 88);
            }

            PaintAtlas(map);
        }

        /// «Чаша Отголосов»: хребты, река, топи, пепел, руины — детерминировано (порт paintAtlas, ядро).
        public static void PaintAtlas(TileMap map)
        {
            int w = map.w, h = map.h;
            var T = map.terrain;
            // идемпотентность по камышу
            foreach (var dd in map.deco) if (dd.kind == "reed") return;

            bool NearCritical(double x, double y, double pad = 2.3)
            {
                foreach (var c in Critical) if (Math.Sqrt((c[0] - x) * (c[0] - x) + (c[1] - y) * (c[1] - y)) < pad) return true;
                if (Math.Sqrt((45 - x) * (45 - x) + (45 - y) * (45 - y)) < 6.6) return true;
                if (Math.Sqrt((38 - x) * (38 - x) + (36 - y) * (36 - y)) < 7.2) return true;
                if (Math.Sqrt((14 - x) * (14 - x) + (20 - y) * (20 - y)) < 5.4) return true;
                if (Math.Sqrt((24 - x) * (24 - x) + (45 - y) * (45 - y)) < 6.8) return true;
                return false;
            }

            void Put(int x, int y, string t) { if (x >= 0 && y >= 0 && x < w && y < h) T[y * w + x] = t; }

            // 1. Северный хребет с рваной кромкой
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                string t = T[y * w + x];
                if (t == "path" || t == "glade" || t == "water") continue;
                double jag = ScRng.H01(x * 131 + 7) * 3.2;
                bool ridge = y <= 2.5 + jag;
                bool spur = Math.Sqrt((x - 37) * (x - 37) + (y - 10.5) * (y - 10.5)) < 1.6 + ScRng.H01(x * 17 + y) * 0.7
                         || Math.Sqrt((x - 47.5) * (x - 47.5) + (y - 11) * (y - 11)) < 1.5 + ScRng.H01(x * 23 + y) * 0.7;
                if ((ridge || spur) && !NearCritical(x, y)) Put(x, y, "mountain");
            }

            // 2. Река Чаши
            for (int i = 0; i < RIVER_WAYPOINTS.Length - 1; i++)
            {
                var a = RIVER_WAYPOINTS[i]; var b = RIVER_WAYPOINTS[i + 1];
                int steps = (int)Math.Ceiling(Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1])) * 2);
                for (int s = 0; s <= steps; s++)
                {
                    double t = (double)s / steps;
                    double cx = a[0] + (b[0] - a[0]) * t + Math.Sin(t * Math.PI * 2 + i) * 0.6;
                    double cy = a[1] + (b[1] - a[1]) * t;
                    int ri = 2;
                    for (int dyy = -ri; dyy <= ri; dyy++) for (int dxx = -ri; dxx <= ri; dxx++)
                    {
                        int x = (int)Math.Round(cx + dxx), y = (int)Math.Round(cy + dyy);
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        double dd = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        if (dd > 0.72 + ScRng.H01(x * 61 + y * 7) * 0.5) continue;
                        string tt = T[y * w + x];
                        if (tt == "path" || tt == "glade") continue;
                        Put(x, y, "water");
                    }
                }
            }

            // 3. Илистые топи
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double dd = Math.Sqrt((x - 24) * (x - 24) + (y - 45) * (y - 45));
                if (dd > 6.4 * (0.82 + ScRng.H01(x * 13 + y * 29) * 0.36)) continue;
                string t = T[y * w + x];
                if (t == "path" || t == "glade" || t == "water" || t == "ruin" || t == "mountain") continue;
                Put(x, y, "marsh");
            }
            // 4. Пепельные поля
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double dd = Math.Sqrt((x - 40) * (x - 40) + (y - 24) * (y - 24));
                if (dd > 6.2 * (0.8 + ScRng.H01(x * 11 + y * 3) * 0.4)) continue;
                string t = T[y * w + x];
                if (t == "path" || t == "glade" || t == "water" || t == "mountain" || t == "marsh") continue;
                Put(x, y, "ash");
            }
            // 5. Затонувшие руины
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double dd = Math.Sqrt((x - 14) * (x - 14) + (y - 20) * (y - 20));
                if (dd > 5.2 * (0.85 + ScRng.H01(x * 19 + y * 41) * 0.3)) continue;
                string t = T[y * w + x];
                if (t == "path" || t == "glade") continue;
                Put(x, y, ScRng.H01(x * 7 + y * 101) < 0.16 ? "water" : "ruin");
            }
            int nextId = 1;
            foreach (var dd in map.deco) nextId = Math.Max(nextId, dd.id + 1);
            for (int i = 0; i < 9; i++)
            {
                double a = i / 9.0 * Math.PI * 2 + ScRng.H01(i * 31) * 0.7;
                double rr = 2.6 + ScRng.H01(i * 57) * 2.1;
                map.deco.Add(new Deco { id = nextId++, kind = "stone", x = (float)(14 + Math.Cos(a) * rr), y = (float)(20 + Math.Sin(a) * rr * 0.85), r = 0.5f, seed = (int)(ScRng.H01(i * 91) * 1e9) });
            }
            map.deco.Add(new Deco { id = nextId++, kind = "bones", x = 14.5f, y = 19.4f, r = 0.2f, seed = 77 });

            // Разлом: кольцо менгиров, кристаллы, кости
            for (int i = 0; i < 7; i++)
            {
                double a = i / 7.0 * Math.PI * 2;
                map.deco.Add(new Deco { id = nextId++, kind = "stone", x = (float)(45 + Math.Cos(a) * 4.4), y = (float)(45 + Math.Sin(a) * 4.4), r = 0.5f, seed = 1000 + i });
            }
            for (int i = 0; i < 4; i++)
            {
                double a = ScRng.H01(i * 17) * Math.PI * 2, rr = 0.8 + ScRng.H01(i * 29) * 3.3;
                map.deco.Add(new Deco { id = nextId++, kind = "crystal", x = (float)(45 + Math.Cos(a) * rr), y = (float)(45 + Math.Sin(a) * rr), r = 0.45f, seed = 2000 + i });
            }
            map.deco.Add(new Deco { id = nextId++, kind = "bones", x = 45, y = 45, r = 0.2f, seed = 3000 });

            // Страховка: критические точки проходимы
            foreach (var c in Critical)
            {
                int xi = (int)c[0], yi = (int)c[1];
                if (xi < 0 || yi < 0 || xi >= w || yi >= h) continue;
                string t = T[yi * w + xi];
                if (t == "water" || t == "mountain") T[yi * w + xi] = "moss";
            }
        }
    }
}
