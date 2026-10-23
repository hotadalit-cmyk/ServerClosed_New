using UnityEngine;

namespace ServerClosed.Application
{
    /// <summary>
    /// Точка входа приложения. Инициализирует системы.
    /// </summary>
    public class Bootstrap
    {
        private Core.Time.WorldClock _worldClock;

        public void Initialize()
        {
            _worldClock = new Core.Time.WorldClock();
            Debug.Log($"[Bootstrap] ServerClosed New — M1 каркас запущен. WorldMinute={_worldClock.WorldMinute}");
        }

        public Core.Time.WorldClock GetWorldClock() => _worldClock;
    }
}
