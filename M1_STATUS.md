# M1 — Промежуточный отчёт

**Статус:** M1.4 завершён (код написан), требуется проверка в Unity

## ✅ Выполнено:

### M1.1: Git-репозиторий ✅
- Путь: `C:\UnityProjects\ServerClosed_New`
- Ветка: `dev/m1-bootstrap`
- Коммиты: 2 (начальный + каркас)

### M1.2: Unity-проект создан ✅
- Папка: `Game1` (переименуем в `Game` после закрытия Unity)
- Версия: 6000.6.5f1
- Шаблон: 2D (URP)
- Вложенный `.git` удалён

### M1.3: Документация ✅
- `CLAUDE.md` — правила нового проекта
- `Docs/Launch/` — 13 файлов пакета

### M1.4: Минимальный каркас (КОД НАПИСАН) ✅
**Сборки:**
- `Core` (noEngineReferences: true) — чистый C#
- `Application` (зависит от Core)
- `Presentation` (зависит от Application)
- `Tests.EditMode` (UNITY_INCLUDE_TESTS)

**Классы:**
- `Core.Time.WorldClock` — единое время (T-01)
  - WorldMinute: long
  - Advance(minutes)
  - GetYear(), GetDayOfYear(), GetAge(birthMinute)
- `Application.Bootstrap` — инициализация систем
- `Presentation.UI.StartScreen` (MonoBehaviour)

**Тесты (6 штук):**
- WorldClock_StartsAtZero
- WorldClock_AdvanceOneDay_ReturnsDay2
- WorldClock_AdvanceOneYear_Returns120Days
- WorldClock_GetAge_CalculatesCorrectly
- WorldClock_NegativeBirthMinute_CalculatesPrehistory (для героя 18 лет)
- WorldClock_AdvanceNegative_ThrowsException

---

## ⏳ Требуется проверка (M1.4 продолжение):

### Шаг 1: Открыть проект в Unity
1. Unity Hub → Open → выбрать `C:\UnityProjects\ServerClosed_New\Game1`
2. Дождаться компиляции (Console должен быть пуст или только warnings Unity)
3. Если ошибки компиляции — сообщить в чат

### Шаг 2: Запустить EditMode-тесты
1. Window → General → Test Runner
2. Вкладка EditMode
3. Run All
4. Убедиться: **все 6 тестов прошли** (зелёные галочки)
5. Если тесты падают — сообщить в чат с текстом ошибки

### Шаг 3: Исправить зависимости Assembly Definitions (если нужно)
Если компиляция жалуется на missing references:
1. Выбрать `Assets/_Project/Scripts/Application/ServerClosed.Application.asmdef`
2. Inspector → Assembly Definition References → добавить `ServerClosed.Core`
3. То же для `ServerClosed.Presentation` → добавить `ServerClosed.Application`
4. То же для `ServerClosed.Tests.EditMode` → добавить `ServerClosed.Core`

### Шаг 4: Сообщить результат
Напиши в чат:
- **"Тесты прошли"** — если все 6 тестов зелёные
- **"Ошибка компиляции: [текст]"** — если Unity не скомпилировал
- **"Тесты упали: [какой тест, текст ошибки]"** — если тесты красные

---

## 🔜 После проверки будет выполнено:

### M1.5: Android-настройка и APK (НЕ ВЫПОЛНЕНО)
- Company Name: NewCompany
- Product Name: ServerClosedNew
- Package Name: `com.newcompany.serverclosednew`
- Scripting Backend: IL2CPP
- Target Architectures: ARM64
- API Level: 26 (Android 8.0)
- Первая сборка APK

### M1.6: Финальный коммит (НЕ ВЫПОЛНЕНО)
- Добавить .meta файлы Unity
- Зафиксировать проверенный каркас
- Закрыть M1 с отчётом

---

**Текущий коммит:** `4ae09cb` "M1: Минимальный каркас Unity 6.6 Android"
