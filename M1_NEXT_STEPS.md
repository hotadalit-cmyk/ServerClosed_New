# M1.5–M1.7: Финальные шаги

## M1.5: Создание сцены Bootstrap и проверка PlayMode

### Шаг 1: Создать сцену через меню
1. Unity → Меню **ServerClosed → M1 → Create Bootstrap Scene**
2. Дождись сообщения в Console: "Bootstrap сцена создана"
3. File → Save Scene As → `Assets/_Project/Scenes/Bootstrap.unity`

### Шаг 2: Добавить в Build Settings
1. File → Build Settings
2. Нажать **Add Open Scenes**
3. Bootstrap должна появиться в списке со индексом 0

### Шаг 3: Запустить PlayMode
1. Нажать **Play** (▶)
2. Проверить в Game окне:
   - Текст "ServerClosed M1"
   - Строка "Год X, День Y"
   - Строка "WorldMinute: 0"
3. Проверить Console:
   - Должен быть лог: `[Bootstrap] ServerClosed New — M1 каркас запущен. WorldMinute=0`
4. Остановить Play

**Если текст не появился или Console пустая — сделай скриншот и сообщи.**

---

## M1.6: Android настройка

### Шаг 1: Переключить платформу
1. File → Build Settings
2. Выбрать **Android** → Switch Platform
3. Дождись окончания (может занять 2-5 минут)

### Шаг 2: Player Settings
1. Build Settings → Player Settings
2. **Company Name:** `NewCompany`
3. **Product Name:** `ServerClosed New`
4. Вкладка **Android** (иконка Android):
   - **Package Name:** `com.newcompany.serverclosednew`
   - **Version:** `0.1.0`
   - **Minimum API Level:** `26` (Android 8.0 Oreo)
   - **Target API Level:** Automatic (highest installed)

### Шаг 3: Other Settings (прокрутить вниз)
1. **Scripting Backend:** `IL2CPP`
2. **Target Architectures:** только **ARM64** (убрать галочку ARMv7)
3. File → Save Project

---

## M1.7: Первая сборка APK (опционально)

### Если Android SDK настроен:
1. Build Settings → Build
2. Сохранить как: `C:\UnityProjects\ServerClosed_New\Artifacts\M1_FirstBuild.apk`
3. Дождись окончания сборки
4. Проверь размер файла (должен быть 30-50 MB для IL2CPP ARM64)

### Если сборка не удалась:
- Скопируй текст ошибки и сообщи
- **Не блокирует M1** — можно завершить без APK

### Если APK собрался и есть телефон:
```bash
adb devices
adb install -r C:\UnityProjects\ServerClosed_New\Artifacts\M1_FirstBuild.apk
```

---

## M1.8: Финальный коммит

После успешного PlayMode и Android-настройки:

**Напиши "M1 готов"** — я сделаю финальный коммит с результатами проверок.

---

## Текущий статус:
- ✅ M1.1: Git-репозиторий
- ✅ M1.2: Unity-проект
- ✅ M1.3: Документация
- ✅ M1.4: Код и тесты (скомпилировано)
- ⏳ M1.5: Сцена Bootstrap + PlayMode
- ⏳ M1.6: Android настройка
- ⏳ M1.7: APK (опционально)
- ⏳ M1.8: Финальный коммит
