namespace ServerClosed.Core.Time
{
    /// <summary>
    /// Единственный источник игрового времени. T-01.
    /// WorldMinute — целое 64-битное число. 120 дней/год, 24 часа/день, 60 мин/час.
    /// </summary>
    public static class WorldClock
    {
        public const int MinutesPerHour = 60;
        public const int HoursPerDay = 24;
        public const int MinutesPerDay = MinutesPerHour * HoursPerDay; // 1440
        public const int DaysPerSeason = 30;
        public const int SeasonsPerYear = 4;
        public const int DaysPerYear = DaysPerSeason * SeasonsPerYear; // 120
        public const int MinutesPerYear = MinutesPerDay * DaysPerYear; // 172800

        public static double CalculateAge(long worldMinute, long birthMinute)
        {
            return (worldMinute - birthMinute) / (double)MinutesPerYear;
        }

        public static int GetYear(long worldMinute)
        {
            return (int)(worldMinute / MinutesPerYear) + 1;
        }

        public static int GetDayOfYear(long worldMinute)
        {
            long minutesInCurrentYear = worldMinute % MinutesPerYear;
            return (int)(minutesInCurrentYear / MinutesPerDay) + 1;
        }

        public static int GetSeason(long worldMinute)
        {
            int dayOfYear = GetDayOfYear(worldMinute) - 1; // 0-based
            return (dayOfYear / DaysPerSeason) + 1; // 1-4
        }
    }
}