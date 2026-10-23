# M1.5: Создание сцены Bootstrap вручную

## Шаг 1: Создать новую сцену
1. File → New Scene
2. Выбрать **Built-in** → **2D**
3. Нажать **Create**

---

## Шаг 2: Создать UI элементы

### Canvas:
1. Hierarchy → ПКМ → UI → Canvas
2. Canvas создастся автоматически вместе с EventSystem

### Текст статуса:
1. Hierarchy → ПКМ на Canvas → UI → Legacy → Text
2. Переименовать в `StatusText`
3. Inspector → Rect Transform:
   - Anchor Presets: **Center** (удерживай Alt+Shift при клике = center position)
   - Width: `800`, Height: `400`
4. Inspector → Text компонент:
   - Text: `ServerClosed M1` (первая строка)
   - Font Size: `48`
   - Alignment: **Center Middle** (обе кнопки по центру)
   - Color: **White**

---

## Шаг 3: Создать Bootstrap GameObject

1. Hierarchy → ПКМ → Create Empty
2. Переименовать в `Bootstrap`
3. Inspector → Add Component → вбить `StartScreen` → выбрать **Start Screen (Script)**
4. Inspector → Start Screen → перетащи **StatusText** в поле **Status Text**

---

## Шаг 4: Сохранить сцену

1. File → Save As
2. Путь: `Assets/_Project/Scenes`
3. Имя: `Bootstrap.unity`
4. Save

---

## Шаг 5: Добавить в Build Settings

1. File → Build Settings
2. Нажать **Add Open Scenes**
3. Bootstrap должна появиться со индексом 0

---

## Шаг 6: Запустить PlayMode

1. Нажать **Play** ▶
2. Проверить в Game окне текст
3. Проверить Console:
   - Должен быть лог: `[Bootstrap] ServerClosed New — M1 каркас запущен`
   - Должна быть строка с WorldMinute

---

**После создания напиши "Сцена создана" и покажи скриншот Game окна в Play режиме.**
