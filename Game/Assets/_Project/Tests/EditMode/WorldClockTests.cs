using NUnit.Framework;
using ServerClosed.Core.Time;

namespace ServerClosed.Tests.EditMode
{
    /// <summary>
    /// T-01: Проверка календаря и расчёта возраста
    /// </summary>
    public class WorldClockTests
    {
        [Test]
        public void CalculateAge_18Years_ReturnsCorrectAge()
        {
            long birthMinute = 0;
            long currentMinute = 18 * WorldClock.MinutesPerYear;

            double age = WorldClock.CalculateAge(currentMinute, birthMinute);

            Assert.AreEqual(18.0, age, 0.001);
        }

        [Test]
        public void GetYear_FirstMinute_Returns1()
        {
            Assert.AreEqual(1, WorldClock.GetYear(0));
        }

        [Test]
        public void GetDayOfYear_FirstDay_Returns1()
        {
            Assert.AreEqual(1, WorldClock.GetDayOfYear(0));
        }

        [Test]
        public void GetDayOfYear_Day121_Returns1() // Второй год, первый день
        {
            long minute = 120 * WorldClock.MinutesPerDay;
            Assert.AreEqual(1, WorldClock.GetDayOfYear(minute));
        }

        [Test]
        public void GetSeason_FirstDay_Returns1()
        {
            Assert.AreEqual(1, WorldClock.GetSeason(0));
        }

        [Test]
        public void GetSeason_Day31_Returns2() // Второй сезон
        {
            long minute = 30 * WorldClock.MinutesPerDay;
            Assert.AreEqual(2, WorldClock.GetSeason(minute));
        }
    }
}