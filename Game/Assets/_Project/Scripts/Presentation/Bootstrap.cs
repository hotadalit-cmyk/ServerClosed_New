using UnityEngine;

namespace ServerClosed.Presentation
{
    /// <summary>
    /// Точка входа игры. Инициализирует системы и переключает на главное меню.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Debug.Log("[Bootstrap] ServerClosed New started");
        }

        private void Start()
        {
            // TODO M2: Инициализация Application-слоя
            Debug.Log("[Bootstrap] Ready for M1 smoke test");
        }
    }
}