using NUnit.Framework;
using ServerClosed.Core.Time;

namespace ServerClosed.Tests.EditMode
{
    /// <summary>
    /// Тесты WorldClock согласно TIME-01, TIME-02 из 07_TESTS_ACCEPTANCE.md
    /// </summary>
    public class WorldClockTests
    {
        [Test]
        public void WorldClock_StartsAtZero()
        {
            // Arrange & Act
            var clock = new WorldClock();

            // Assert
            Assert.AreEqual(0, clock.WorldMinute, "WorldMinute должен стартовать с 0");
            Assert.AreEqual(1, clock.GetYear(), "Год отображается с 1");
            Assert.AreEqual(1, clock.GetDayOfYear(), "День отображается с 1");
        }

        [Test]
        public void WorldClock_AdvanceOneDay_ReturnsDay2()
        {
            // Arrange
            var clock = new WorldClock();

            // Act
            clock.Advance(1440); // 1 день = 1440 минут

            // Assert
            Assert.AreEqual(1440, clock.WorldMinute);
            Assert.AreEqual(1, clock.GetYear(), "Год 1");
            Assert.AreEqual(2, clock.GetDayOfYear(), "День 2");
        }

        [Test]
        public void WorldClock_AdvanceOneYear_Returns120Days()
        {
            // Arrange
            var clock = new WorldClock();

            // Act
            clock.Advance(172800); // 1 год = 120 дней × 1440 минут

            // Assert
            Assert.AreEqual(172800, clock.WorldMinute);
            Assert.AreEqual(2, clock.GetYear(), "Переход на год 2");
            Assert.AreEqual(1, clock.GetDayOfYear(), "День 1 нового года");
        }

        [Test]
        public void WorldClock_GetAge_CalculatesCorrectly()
        {
            // Arrange
            var clock = new WorldClock();
            long birthMinute = 0;

            // Act
            clock.Advance(172800); // 1 год
            double age = clock.GetAge(birthMinute);

            // Assert
            Assert.AreEqual(1.0, age, 0.0001, "Возраст должен быть ровно 1 год");
        }

        [Test]
        public void WorldClock_NegativeBirthMinute_CalculatesPrehistory()
        {
            // Arrange
            var clock = new WorldClock();
            long birthMinute = -3110400; // -18 лет для первого героя

            // Act
            // WorldMinute = 0, герой родился 18 лет назад
            double age = clock.GetAge(birthMinute);

            // Assert
            Assert.AreEqual(18.0, age, 0.0001, "Герой должен быть 18 лет при minute=0");
        }

        [Test]
        public void WorldClock_AdvanceNegative_ThrowsException()
        {
            // Arrange
            var clock = new WorldClock();

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() => clock.Advance(-1));
        }
    }
}
