// SERVERCLOSED — Core: мировое время (ТЗ §7) и старение (§10). Порт src/game/time.ts.
using System;

namespace ServerClosed.Core
{
    public static class ScTime
    {
        public const int MINUTES_IN_HOUR = 60;
        public const int HOURS_IN_DAY = 24;
        public const int MINUTES_IN_DAY = HOURS_IN_DAY * MINUTES_IN_HOUR; // 1440
        public const int DAYS_IN_SEASON = 6;
        public static readonly string[] SEASONS = { "Пробуждение", "Цветение", "Увядание", "Тьма" };

        // Скорости: игровых минут за реальную секунду (§7)
        public const double SPEED_PAUSE = 0;
        public const double SPEED_EXPLORE = 3;   // день ≈ 8 реальных минут
        public const double SPEED_FAST = 36;     // отдых: день ≈ 40 секунд
        public const double SPEED_BATTLE = 1.5;  // бой течёт медленнее

        public static double SpeedOf(string name)
        {
            if (name == "pause") return SPEED_PAUSE;
            if (name == "fast") return SPEED_FAST;
            if (name == "battle") return SPEED_BATTLE;
            return SPEED_EXPLORE;
        }
    }

    [Serializable]
    public class WorldClock
    {
        public double minutes = 0;   // всего игровых минут с сотворения мира
        public int dayTickCounter = 0;

        public WorldClock() { }
        public WorldClock(double m) { minutes = m; }

        /// Продвинуть время; возвращает число наступивших НОВЫХ дней.
        public int Advance(double gameMinutes)
        {
            if (double.IsNaN(gameMinutes) || double.IsInfinity(gameMinutes)) return 0;
            int before = (int)Math.Floor(minutes / ScTime.MINUTES_IN_DAY);
            minutes += Math.Max(0, gameMinutes);
            if (double.IsNaN(minutes) || double.IsInfinity(minutes)) minutes = before * ScTime.MINUTES_IN_DAY;
            int after = (int)Math.Floor(minutes / ScTime.MINUTES_IN_DAY);
            int crossed = after - before;
            dayTickCounter += crossed;
            return crossed;
        }

        public int TotalDays { get { return (int)Math.Floor(minutes / ScTime.MINUTES_IN_DAY); } }
        public int Year { get { return TotalDays / (ScTime.DAYS_IN_SEASON * 4) + 1; } }
        public string Season { get { return ScTime.SEASONS[(TotalDays / ScTime.DAYS_IN_SEASON) % 4]; } }
        public int DayOfSeason { get { return TotalDays % ScTime.DAYS_IN_SEASON + 1; } }
        public int Hour { get { return (int)Math.Floor(minutes % ScTime.MINUTES_IN_DAY / ScTime.MINUTES_IN_HOUR); } }
        public int Minute { get { return (int)Math.Floor(minutes % ScTime.MINUTES_IN_HOUR); } }
        public double HourF { get { return minutes % ScTime.MINUTES_IN_DAY / ScTime.MINUTES_IN_HOUR; } }
        public bool IsNight { get { return HourF < 5 || HourF >= 21; } }
        public bool IsDusk { get { return (HourF >= 19 && HourF < 21) || (HourF >= 5 && HourF < 7); } }

        public string Label()
        {
            return string.Format("Год {0}, {1}, день {2} — {3:D2}:{4:D2}",
                Year, Season, DayOfSeason, Hour, Minute);
        }
    }

    // ===== Старение (§10) =====
    public static class ScAging
    {
        public static string LifeStageOf(double ageDays, double lifeDays)
        {
            double f = ageDays / Math.Max(1, lifeDays);
            if (f < 0.15) return "Зарождение";
            if (f < 0.45) return "Молодость";
            if (f < 0.70) return "Расцвет";
            if (f < 0.85) return "Старение";
            return "Угасание";
        }

        /// Вероятность естественной смерти за игровой день (закон Гомпертца, §10).
        public static double DailyMortality(double ageDays, double lifeDays, double onsetF = 0.7)
        {
            if (lifeDays <= 0) return 0;
            if (ageDays <= lifeDays * onsetF) return 0.0005;
            const double k = 0.85;
            double x = (ageDays - lifeDays * onsetF) / Math.Max(1, lifeDays * (1 - onsetF));
            return Math.Min(0.9, 0.02 * Math.Exp(k * 2.2 * x));
        }

        public static void AgeModifiers(double ageDays, double lifeDays,
            out double hpMul, out double atkMul, out double speedMul, out double xpMul)
        {
            double f = ageDays / Math.Max(1, lifeDays);
            if (f < 0.15) { hpMul = 0.8; atkMul = 0.7; speedMul = 0.9; xpMul = 1.1; }
            else if (f < 0.45) { hpMul = 1.0; atkMul = 0.9; speedMul = 1.0; xpMul = 1.1; }
            else if (f < 0.70) { hpMul = 1.1; atkMul = 1.0; speedMul = 1.0; xpMul = 1.0; }
            else if (f < 0.85) { hpMul = 1.0; atkMul = 0.95; speedMul = 0.92; xpMul = 1.2; }
            else { hpMul = 0.85; atkMul = 0.85; speedMul = 0.82; xpMul = 1.3; }
        }
    }
}
