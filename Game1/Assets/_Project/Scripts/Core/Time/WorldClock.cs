namespace ServerClosed.Core.Time
{
    /// <summary>
    /// Единственный источник игрового времени. T-01.
    /// WorldMinute — целое неотрицательное 64-битное число.
    /// Год = 120 дней, день = 1440 минут.
    /// </summary>
    public class WorldClock
    {
        public long WorldMinute { get; private set; }

        public WorldClock()
        {
            WorldMinute = 0;
        }

        public void Advance(long minutes)
        {
            if (minutes < 0)
                throw new System.ArgumentException("Cannot advance by negative time", nameof(minutes));

            WorldMinute += minutes;
        }

        public int GetYear()
        {
            // 1 год = 120 дней × 1440 минут = 172800 минут
            return (int)(WorldMinute / 172800) + 1;
        }

        public int GetDayOfYear()
        {
            // День отображается с единицы
            long minutesInYear = WorldMinute % 172800;
            return (int)(minutesInYear / 1440) + 1;
        }

        public double GetAge(long birthMinute)
        {
            return (WorldMinute - birthMinute) / 172800.0;
        }
    }
}
