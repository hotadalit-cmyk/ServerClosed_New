// SERVERCLOSED — Core: восприятие (Дополнение к ТЗ), пищеварение (§5–§7), производные статы (§9).
// Порт senses.ts + digestion.ts + computeDerived.
using System;
using System.Collections.Generic;

namespace ServerClosed.Core
{
    public static class ScSenses
    {
        /// Усвоение пищи питает рецепторы ПО ДИЕТЕ. Возвращает список ВНОВЬ пробуждённых органов.
        public static List<string> GrantBiomass(SensoryState s, double amount, Dictionary<string, double> thrMul, double[] dietIdx)
        {
            s.biomassTotal += amount;
            var newly = new List<string>();
            for (int oi = 1; oi < SensesData.ORDER.Length; oi++)   // touch (0) врождённое
            {
                string organ = SensesData.ORDER[oi];
                double gain = SensesData.UNIVERSAL_TRICKLE * amount;
                if (dietIdx != null)
                {
                    for (int k = 0; k + 1 < dietIdx.Length; k += 2)
                        if ((int)dietIdx[k] == oi) gain += dietIdx[k + 1] * amount;
                }
                if (s.focus == organ) gain *= SensesData.FOCUS_MUL;
                s.feed[oi] += gain;
                double thr = 1;
                if (thrMul != null && thrMul.TryGetValue(organ, out var multiplier)) thr = multiplier;
                bool unlockedAlready = Array.IndexOf(s.unlocked, organ) >= 0;
                if (!unlockedAlready && s.feed[oi] >= SensesData.Thresholds[organ] * thr)
                {
                    var list = new List<string>(s.unlocked); list.Add(organ);
                    s.unlocked = list.ToArray();
                    newly.Add(organ);
                }
            }
            return newly;
        }

        /// Выбор игрока: вложить биомассу напрямую в орган (выбор развития).
        public static List<string> InvestInOrgan(SensoryState s, string organ, double amount, Dictionary<string, double> thrMul)
        {
            var res = new List<string>();
            int oi = SensesData.OrganIndex(organ);
            if (oi <= 0 || amount <= 0) return res;    // touch/spirit нельзя
            s.feed[oi] += amount;
            double thr = 1;
            if (thrMul != null && thrMul.TryGetValue(organ, out var multiplier)) thr = multiplier;
            if (Array.IndexOf(s.unlocked, organ) < 0 && s.feed[oi] >= SensesData.Thresholds[organ] * thr)
            {
                var list = new List<string>(s.unlocked); list.Add(organ);
                s.unlocked = list.ToArray();
                res.Add(organ);
            }
            return res;
        }

        public static void SetFocus(SensoryState s, string organ) { s.focus = organ; }

        /// Прогресс органа для HUD.
        public static void OrganProgress(SensoryState s, string organ, Dictionary<string, double> thrMul, out double cur, out double need)
        {
            int oi = SensesData.OrganIndex(organ);
            double thr = 1;
            if (thrMul != null && thrMul.TryGetValue(organ, out var multiplier)) thr = multiplier;
            cur = Math.Floor(s.feed[oi] * 10) / 10;
            need = Math.Round(SensesData.Thresholds[organ] * thr);
        }

        /// Следующая цель развития (для подсказки HUD).
        public static void NextGoal(SensoryState s, Dictionary<string, double> thrMul, out string organ, out double cur, out double need)
        {
            organ = null; cur = 0; need = 0;
            double bestFrac = -1;
            for (int oi = 1; oi < SensesData.ORDER.Length; oi++)
            {
                string o = SensesData.ORDER[oi];
                if (Array.IndexOf(s.unlocked, o) >= 0) continue;
                double thr = 1;
                if (thrMul != null && thrMul.TryGetValue(o, out var multiplier)) thr = multiplier;
                double nd = Math.Max(1, Math.Round(SensesData.Thresholds[o] * thr));
                double frac = s.feed[oi] / nd;
                if (frac > bestFrac) { bestFrac = frac; organ = o; cur = Math.Floor(s.feed[oi] * 10) / 10; need = nd; }
            }
        }

        /// Усвоение руды (п.8): до ядра — счётчик, после — магическая энергия.
        public static void GrantOre(SensoryState s, double n, bool coreActive)
        {
            s.ore += n;
            if (coreActive) s.magicEnergy = Math.Min(SensesData.MAGIC_ENERGY_MAX, s.magicEnergy + 2 * n);
        }

        public static void TickSenses(SensoryState s, double dt)
        {
            if (s.magicEnergy > 0) s.magicEnergy = Math.Max(0, s.magicEnergy - dt * 0.05);
        }
    }

    public static class ScDigestion
    {
        public const double CAPACITY_BASE = 12;

        /// AbsorptionCapacity (§5.5).
        public static double CapacityOf(double biomass, int slots) { return CAPACITY_BASE + biomass * 0.3 + slots * 5; }

        public static double StoredMassOf(DigestiveState d)
        {
            double m = 0;
            foreach (var it in d.Items()) m += it.mass;
            return m;
        }

        public static double PressureOf(DigestiveState d, double cap)
        {
            return cap > 0 ? Math.Min(1, StoredMassOf(d) / cap) : 0;
        }

        /// Сила ферментов (§6.4).
        public static double EnzymeStrength(int installedGenes, double biomassTotal)
        {
            return 1 + installedGenes * 0.12 + Math.Min(1.2, biomassTotal / 55);
        }

        public static double ModeSpeedMul(string mode)
        {
            if (mode == "fast") return 1.8;
            if (mode == "gentle") return 0.62;
            return 1.0;
        }

        public static double ModeGeneMul(string mode) { return mode == "fast" ? 0.8 : mode == "gentle" ? 1.65 : 1.0; }
        public static double ModeToxMul(string mode) { return mode == "fast" ? 1.6 : mode == "gentle" ? 0.6 : 1.0; }
        public static double ModeEnergyDrain(string mode) { return mode == "fast" ? 0.22 : mode == "gentle" ? 0.06 : 0.03; }

        /// Секунд на переваривание предмета (§6.4).
        public static double DigestSeconds(StoredItem it, double enzyme, string mode, double load)
        {
            const double baseSec = 7;
            double compat = Food.KindInfo.ContainsKey(Food.KindName(it.kind)) ? Food.KindInfo[Food.KindName(it.kind)][0] : 1.0;
            double t = (baseSec * it.hardness) / (enzyme * compat) * load / ModeSpeedMul(mode);
            return Math.Max(1.5, t);
        }

        public static int FreeChamber(DigestiveState d) { return d.c0 == null ? 0 : d.c1 == null ? 1 : -1; }

        public static bool CanAbsorb(DigestiveState d, double cap, double mass, out string reason)
        {
            if (FreeChamber(d) < 0)
            {
                reason = "Свободной пищеварительной полости нет — содержимое сначала нужно переварить.";
                return false;
            }
            if (StoredMassOf(d) + mass > cap)
            {
                reason = "Тело не вместит столько: внутреннее давление опасно.";
                return false;
            }
            reason = null;
            return true;
        }

        public static StoredItem PutItem(DigestiveState d, StoredItem proto)
        {
            int idx = FreeChamber(d);
            if (idx < 0) return null;
            proto.id = d.seq++; proto.chamber = idx; proto.elapsed = 0;
            d.Set(idx, proto);
            return proto;
        }

        /// Номер визуальной фазы 1..12 (§6.3).
        public static int PhaseOf(double progress) { return Math.Min(12, 1 + (int)Math.Floor(progress * 12)); }

        /// Посекундный тик полостей: прогресс — только в данных. Завершённые предметы → out.
        public static void TickDigestive(DigestiveState d, double dt, double enzyme, double load, List<StoredItem> completed)
        {
            for (int i = 0; i < 2; i++)
            {
                var it = d.Get(i);
                if (it == null) continue;
                it.elapsed += dt;
                double total = DigestSeconds(it, enzyme, d.mode, load);
                it.progress = Math.Min(1, it.progress + dt / total);
                if (it.progress >= 1) { completed.Add(it); d.Set(i, null); }
            }
        }

        /// Открытие режимов (§6.6). Возвращает новые режимы.
        public static List<string> UnlockModes(DigestiveState d, double biomassTotal)
        {
            var opened = new List<string>();
            if (Array.IndexOf(d.modeUnlocked, "fast") < 0 && biomassTotal >= Food.FAST_UNLOCK_AT)
            { d.modeUnlocked = Append(d.modeUnlocked, "fast"); opened.Add("fast"); }
            if (Array.IndexOf(d.modeUnlocked, "gentle") < 0 && biomassTotal >= Food.GENTLE_UNLOCK_AT)
            { d.modeUnlocked = Append(d.modeUnlocked, "gentle"); opened.Add("gentle"); }
            return opened;
        }

        public static bool SetMode(DigestiveState d, string m)
        {
            if (m != "balanced" && Array.IndexOf(d.modeUnlocked, m) < 0) return false;
            d.mode = m; return true;
        }

        public static string[] Append(string[] arr, string v)
        {
            var l = new List<string>(arr); l.Add(v); return l.ToArray();
        }
    }

    // ===== Производные характеристики (§9: computeDerived) =====
    public static class ScGenome
    {
        public static DerivedStats ComputeDerived(Genome genome)
        {
            var d = new DerivedStats();
            foreach (string id in genome.installed)
            {
                GeneDef g;
                if (!Genes.All.TryGetValue(id, out g)) continue;
                d.energyUsed += g.energy;
                d.stability -= g.instability;
                d.maxHp += g.maxHp; d.atk += g.atk; d.def += g.def; d.speed += g.speed;
                d.perception += g.perception; d.energyMax += g.energyMax;
                d.hpRegen += g.hpRegen; d.lifeDays += g.lifeDays;
                d.visuals.Add(g.category);
            }
            foreach (var s in Synergies.All)
            {
                if (genome.installed.Contains(s.geneA) && genome.installed.Contains(s.geneB))
                {
                    d.synergies.Add(s);
                    d.visuals.Add(s.id);
                    d.maxHp += s.maxHp; d.atk += s.atk; d.def += s.def; d.speed += s.speed;
                    d.perception += s.perception; d.energyMax += s.energyMax;
                    d.hpRegen += s.hpRegen; d.lifeDays += s.lifeDays;
                }
            }
            for (int i = 0; i < Synergies.Conflicts.GetLength(0); i++)
            {
                if (genome.installed.Contains(Synergies.Conflicts[i, 0]) && genome.installed.Contains(Synergies.Conflicts[i, 1]))
                    d.stability -= 15;
            }
            d.stability = ScRng.Clamp(d.stability, 0, 100);
            return d;
        }

        public static bool CanInstall(Genome genome, string geneId, out string reason)
        {
            GeneDef g;
            if (!Genes.All.TryGetValue(geneId, out g)) { reason = "Неизвестный ген"; return false; }
            if (genome.installed.Contains(geneId)) { reason = "Уже установлен"; return false; }
            for (int i = 0; i < Synergies.Conflicts.GetLength(0); i++)
            {
                string a = Synergies.Conflicts[i, 0], b = Synergies.Conflicts[i, 1];
                if (a == geneId && genome.installed.Contains(b)) { reason = "Конфликт с геном: " + Genes.All[b].name; return false; }
                if (b == geneId && genome.installed.Contains(a)) { reason = "Конфликт с геном: " + Genes.All[a].name; return false; }
            }
            if (genome.installed.Count >= genome.slots) { reason = "Нет свободных слотов органов"; return false; }
            var d = ComputeDerived(genome);
            if (d.energyUsed + g.energy > genome.capacity) { reason = "Недостаточно энергии генома"; return false; }
            reason = null; return true;
        }
    }
}
