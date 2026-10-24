using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ServerClosed.Core;

namespace ServerClosed.Runtime
{
    // Two independently validated slots. A failed load never starts a new campaign.
    public static class ScSave
    {
        public static string DirectoryOverride;
        public static string LastError { get; private set; }
        public static string SavePath { get { return Path.Combine(AppDataDir(), "sc_save.json"); } }
        public static string AppDataDir()
        {
            if (DirectoryOverride != null) return DirectoryOverride;
#if UNITY_5_3_OR_NEWER
            return Path.Combine(UnityEngine.Application.persistentDataPath, "ServerClosedPort");
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServerClosedPort");
#endif
        }
        [Serializable] public class Slot
        {
            public int version = 2;
            public long sequence;
            public string checksum;
            public string payload;
        }
        static string SlotPath(int n) { return Path.Combine(AppDataDir(), "sc_save_" + n + ".json"); }
        static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        static Slot ReadSlot(string path, out WorldData data)
        {
            data = null;
            if (!File.Exists(path)) return null;
            var slot = ScJson.FromJson<Slot>(File.ReadAllText(path));
            if (slot.version != 2 || slot.sequence < 1 || slot.payload == null || slot.checksum != Hash(slot.payload)) throw new FormatException("Повреждён слот " + Path.GetFileName(path));
            data = ScJson.FromJson<WorldData>(slot.payload);
            Validate(data);
            return slot;
        }
        public static void Validate(WorldData d)
        {
            if (d == null) throw new FormatException("Некорректное состояние сохранения.");
            ScSoul.EnsureMigration(d);   // сейвы v1.1 без души/достижений получают их при первом чтении
            if (d.mapJson == null || d.mapJson.w < 1 || d.mapJson.w > 256 || d.mapJson.h < 1 || d.mapJson.h > 256 || d.mapJson.terrain == null || d.mapJson.terrain.Length != d.mapJson.w * d.mapJson.h) throw new FormatException("Некорректная карта сохранения.");
            if (d.player == null || d.player.genome == null || d.senses == null || d.senses.feed == null || d.senses.feed.Length != 8 || d.senses.unlocked == null || d.digestion == null || d.creatures == null || d.dungeons == null || d.minutes < 0 || double.IsNaN(d.minutes) || double.IsInfinity(d.minutes)) throw new FormatException("Некорректное состояние сохранения.");
            if (d.activeDungeon != null)
            {
                if (!d.dungeons.TryGetValue(d.activeDungeon, out var dun) || dun == null || !dun.active || dun.depth < 1 || dun.depth > dun.floors.Count) throw new FormatException("Некорректный слой подземелья.");
                foreach (var f in dun.floors) if (f.w < 1 || f.h < 1 || f.terrain == null || f.terrain.Length != f.w * f.h) throw new FormatException("Некорректная карта подземелья.");
            }
            if (double.IsNaN(d.player.hp) || double.IsInfinity(d.player.hp) || double.IsNaN(d.player.biomass) || double.IsInfinity(d.player.biomass)) throw new FormatException("Некорректные параметры тела.");
        }
        public static bool Save(WorldData data)
        {
            try
            {
                Validate(data);
                Directory.CreateDirectory(AppDataDir());
                long[] sequence = { 0, 0 };
                for (int i = 0; i < 2; i++) { try { var slot = ReadSlot(SlotPath(i), out _); if (slot != null) sequence[i] = slot.sequence; } catch (Exception) { } }
                int target = sequence[0] <= sequence[1] ? 0 : 1;
                string payload = ScJson.ToJson(data);
                var next = new Slot { sequence = Math.Max(sequence[0], sequence[1]) + 1, payload = payload, checksum = Hash(payload) };
                string path = SlotPath(target), temp = path + ".tmp";
                File.WriteAllText(temp, ScJson.ToJson(next), Encoding.UTF8);
                ReadSlot(temp, out _);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                LastError = null;
                return true;
            }
            catch (Exception e) { LastError = e.Message; return false; }
        }
        public static WorldData Load()
        {
            WorldData best = null; long newest = 0;
            LastError = null;
            for (int i = 0; i < 2; i++)
            {
                try { var slot = ReadSlot(SlotPath(i), out var data); if (slot != null && slot.sequence > newest) { best = data; newest = slot.sequence; } }
                catch (Exception e) { LastError = e.Message; }
            }
            if (best != null) return best;
            // Migrate the original prototype file without overwriting it.
            if (File.Exists(SlotPath(0)) || File.Exists(SlotPath(1))) { LastError = "Оба слота недоступны. " + LastError; return null; }
            string legacy = SavePath;
#if UNITY_5_3_OR_NEWER
            if (DirectoryOverride == null && !File.Exists(legacy)) legacy = Path.Combine(UnityEngine.Application.persistentDataPath, "sc_save.json");
#endif
            try { if (File.Exists(legacy)) { best = ScJson.FromJson<WorldData>(File.ReadAllText(legacy)); Validate(best); return best; } }
            catch (Exception e) { LastError = e.Message; }
            return null;
        }
        public static bool HasSave()
        {
            if (File.Exists(SlotPath(0)) || File.Exists(SlotPath(1)) || File.Exists(SavePath)) return true;
#if UNITY_5_3_OR_NEWER
            if (DirectoryOverride == null) return File.Exists(Path.Combine(UnityEngine.Application.persistentDataPath, "sc_save.json"));
#endif
            return false;
        }
    }
}
