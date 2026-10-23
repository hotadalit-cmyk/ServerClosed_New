using UnityEngine;
using UnityEngine.UI;

namespace ServerClosed.Presentation.UI
{
    /// <summary>
    /// Стартовый экран. Инициализирует Bootstrap.
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        [SerializeField] private Text _statusText;

        private Application.Bootstrap _bootstrap;

        private void Start()
        {
            _bootstrap = new Application.Bootstrap();
            _bootstrap.Initialize();

            if (_statusText != null)
            {
                var clock = _bootstrap.GetWorldClock();
                _statusText.text = $"ServerClosed M1\n" +
                                  $"Год {clock.GetYear()}, День {clock.GetDayOfYear()}\n" +
                                  $"WorldMinute: {clock.WorldMinute}";
            }
        }
    }
}
