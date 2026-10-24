// SERVERCLOSED — Core: управляемый RNG (ТЗ §23 воспроизводимость).
// Точный порт src/game/rng.ts: mulberry32 + FNV-1a. Совпадает с веб-версией по сидам.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public class ScRng
    {
        private uint _a;
        private readonly uint _seed;

        public ScRng(int seed) { unchecked { _seed = (uint)seed; _a = (uint)seed; } }

        /// [0..1) — детерминированный поток, совпадает с JS mulberry32.
        public double Next()
        {
            unchecked
            {
                _a = _a + 0x6d2b79f5u;
                uint t = (_a ^ (_a >> 15)) * (1u | _a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return ((t ^ (t >> 14))) / 4294967296.0;
            }
        }

        public int Int(int min, int max) { return (int)Math.Floor(Next() * (max - min + 1)) + min; }
        public double Range(double min, double max) { return Next() * (max - min) + min; }
        public bool Chance(double p) { return Next() < p; }
        public uint State { get { return _a; } set { _a = value; } }
        public uint SeedValue { get { return _seed; } }

        public T Pick<T>(IList<T> arr) { return arr[(int)Math.Floor(Next() * arr.Count)]; }

        public void Shuffle<T>(IList<T> arr)
        {
            for (int i = arr.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(Next() * (i + 1));
                T tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
            }
        }

        /// FNV-1a: строка → 32-битный сид.
        public static uint HashSeed(string s)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 0x01000193u; }
                return h;
            }
        }

        /// h01 из mapgen.ts — детерминированный хеш для географии (без rng).
        public static double H01(int n)
        {
            unchecked
            {
                uint x = (uint)n ^ 0x9e3779b9u;
                x *= 0x85ebca6bu;
                x ^= x >> 13;
                x *= 0xc2b2ae35u;
                x ^= x >> 16;
                return x / 4294967296.0;
            }
        }

        public static double Dist(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
    }

    /// Точка/вектор в тайловых координатах (аналог Vec из types.ts).
    [Serializable]
    public struct Vec
    {
        public float x; public float y;
        public Vec(float px, float py) { x = px; y = py; }
        public static Vec Of(double px, double py) { return new Vec((float)px, (float)py); }
    }
}
