using ServerClosed.Core;
// SERVERCLOSED — Runtime: точка входа Unity (порт engine.ts + page.tsx).
// Повесьте на пустой GameObject в сцене. Создаёт камеру, мир, ввод (WASD/тап/джойстик),
// HUD (IMGUI), автосейв, смерть → суд-заглушка → новое воплощение.
using UnityEngine;

namespace ServerClosed.Runtime
{
    public class ScBootstrap : MonoBehaviour
    {
        ScWorld world;
        ScIsoView view;
        string speedName = "explore";
        Vector2? moveTarget;
        bool showGenome;
        bool showLog;
        string toast; double toastT;
        double autosaveT;
        Vector2 logScroll;
        GUIStyle bigLabel, boxStyle, btnStyle;
        string loadError;
        float UiScale { get { return Mathf.Max(1, Screen.height / 720f); } }
        float UiWidth { get { return Screen.width / UiScale; } }
        float UiHeight { get { return Screen.height / UiScale; } }
        public bool HudWasDrawn { get; private set; }
        public ScWorld World { get { return world; } }
        public bool validationMode;
        void OnApplicationPause(bool paused) { if (paused && world != null && !validationMode) { world.CaptureState(); ScSave.Save(world.data); } }
        void OnApplicationQuit() { if (world != null && !validationMode) { world.CaptureState(); ScSave.Save(world.data); } }

        void Start()
        {
            var camGo = new GameObject("sc_camera");
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = 3.6f;
            cam.backgroundColor = new Color(0.027f, 0.035f, 0.027f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0, 0, -10);

            view = gameObject.AddComponent<ScIsoView>();
            view.cam = cam;

            if (!validationMode && ScSave.HasSave())
            {
                var data = ScSave.Load();
                if (data != null) world = ScWorld.FromData(data);
                else { loadError = ScSave.LastError; return; }
            }
            if (world == null) NewGame();
            view.world = world;
            cam.transform.position = view.PosToWorld(world.data.player.x, world.data.player.y) + new Vector3(0, 0.4f, -10);
            Toast("Вы — слизень. Вы слепы: мир познаётся осязанием. Поглощайте жизни.", 6);
        }

        void NewGame()
        {
            int seed = (int)(System.DateTime.UtcNow.Ticks % 2147483647);
            world = ScWorld.NewGame(seed, 1);
            if (view != null) view.world = world;
            speedName = "explore";
        }

        void SaveNow()
        {
            world.CaptureState();
            if (ScSave.Save(world.data)) Toast("Игра сохранена", 2);
        }

        void Toast(string t, double sec) { toast = t; toastT = sec; }

        void Update()
        {
            if (world == null) return;
            toastT -= Time.deltaTime;

            if (world.data.deathCause != null)
            {
                world.SetInput(0, 0, false);
                moveTarget = null;
                return;
            }

            GatherInput();
            world.Update(Time.deltaTime, speedName);
            ConsumeToasts();

            // камера следует за слизнем
            var p = world.data.player;
            var target = view.PosToWorld(p.x, p.y);
            var cam = view.cam;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target + new Vector3(0, 0.4f, -10), Mathf.Min(1, Time.deltaTime * 7));

            autosaveT += Time.deltaTime;
            if (autosaveT > 60) { autosaveT = 0; world.CaptureState(); ScSave.Save(world.data); }
        }

        void ConsumeToasts()
        {
            if (!string.IsNullOrEmpty(world.data.toast))
            {
                Toast(world.data.toast, 4.5);
                world.data.toast = null;
            }
        }

        // ===== Ввод: WASD/стрелки + тап-движение + виртуальный джойстик =====
        void GatherInput()
        {
            double sx = 0, sy = 0;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null) {
                sx = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                sy = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                if (keyboard.eKey.wasPressedThisFrame) { if (world.nearCorpseId >= 0) world.AbsorbNearest(); else world.ExitBirthNest(); }
                if (keyboard.spaceKey.wasPressedThisFrame) world.PlayerAttack();
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsUiPoint(mouse.position.ReadValue())) moveTarget = view.ScreenToTile(mouse.position.ReadValue());
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null) {
                var t = touch.primaryTouch; var pos = t.position.ReadValue();
                if (t.press.wasPressedThisFrame) { _joyStart = pos; _touchAccepted = !IsUiPoint(pos); }
                if (_touchAccepted && t.press.isPressed && (pos - _joyStart).magnitude > 18) { var dv = pos - _joyStart; sx = dv.x / 110f; sy = dv.y / 110f; moveTarget = null; }
                if (_touchAccepted && t.press.wasReleasedThisFrame) { if ((pos - _joyStart).magnitude <= 18) moveTarget = view.ScreenToTile(pos); _touchAccepted = false; }
            }
            bool sprint = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) sy += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) sy -= 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) sx -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) sx += 1;

            // джойстик/тап (первое касание)
            if (Input.touchCount > 0 && !IsPointerOverUi())
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) _joyStart = t.position;
                if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                {
                    var dv = t.position - _joyStart;
                    if (dv.magnitude > 18) { sx = dv.x / 110f; sy = dv.y / 110f; moveTarget = null; }
                }
                else if (t.phase == TouchPhase.Ended && (t.position - _joyStart).magnitude <= 18)
                {
                    moveTarget = view.ScreenToTile(t.position);
                }
            }
            // мышь: ЛКМ тап — движение
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
                moveTarget = view.ScreenToTile(Input.mousePosition);

            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.E)) { if (world.nearCorpseId >= 0) world.AbsorbNearest(); else world.ExitBirthNest(); }
            if (Input.GetKeyDown(KeyCode.Space)) world.PlayerAttack();
#endif
            // Convert screen-axis movement into the isometric tile basis.
            if (sx != 0 || sy != 0) { moveTarget = null; double tx = sx - sy * 2; sy = -sx - sy * 2; sx = tx; }
            // движение к цели тапа
            if (sx == 0 && sy == 0 && moveTarget != null)
            {
                var p = world.data.player;
                double dx = moveTarget.Value.x - p.x, dy = moveTarget.Value.y - p.y;
                double d = System.Math.Sqrt(dx * dx + dy * dy);
                if (d < 0.18) moveTarget = null;
                else { sx = dx / d; sy = dy / d; }
            }

            double len = System.Math.Sqrt(sx * sx + sy * sy);
            if (len > 0.02) world.SetInput(sx / System.Math.Max(1, len), sy / System.Math.Max(1, len), sprint);
            else world.SetInput(0, 0, false);
        }

        Vector2 _joyStart;
        bool _touchAccepted;
        bool IsUiPoint(Vector2 point)
        {
            var pos = new Vector2(point.x / UiScale, (Screen.height - point.y) / UiScale);
            if (new Rect(8, 8, 320, 150).Contains(pos) || new Rect(UiWidth - 130, 8, 122, 210).Contains(pos)) return true;
            if (pos.y >= UiHeight - 108) return true;
            if (showGenome && new Rect(8, 170, Mathf.Min(360, UiWidth - 20), UiHeight - 240).Contains(pos)) return true;
            if (showLog && new Rect(UiWidth - Mathf.Min(380, UiWidth - 20) - 8, 230, Mathf.Min(380, UiWidth - 20), UiHeight - 320).Contains(pos)) return true;
            return world != null && world.data.deathCause != null;
        }
        bool IsPointerOverUi() { return GUIUtility.hotControl != 0; }

        // ===== Суд/смерть: продолжение жизни =====
        public void Reincarnate()
        {
            if (!world.Reincarnate()) return;
            moveTarget = null; speedName = "explore";
            SaveNow();
            Toast("Воплощение №" + world.data.lifeNo + ". Душа помнит прошлую жизнь.", 5);
        }

        // ===== HUD (IMGUI — просто и надёжно для прототипа) =====
        void OnGUI()
        {
            HudWasDrawn = true;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1));
            if (bigLabel == null)
            {
                bigLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
                boxStyle = new GUIStyle(GUI.skin.box);
                btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            }
            if (world == null) { if (loadError != null) GUI.Box(new Rect(20, 20, UiWidth - 40, 160), "Не удалось загрузить сохранение. Файлы сохранены.\n" + loadError); return; }
            var d = world.data;
            var p = d.player;

            // левая верхняя панель
            GUILayout.BeginArea(new Rect(8, 8, 320, 150), boxStyle);
            GUILayout.Label(world.clock.Label() + (world.DungeonActive ? "  ·  Родовая норка" : ""), bigLabel);
            GUILayout.Label(string.Format("HP {0}/{1}   ЭН {2}/{3}", System.Math.Round(p.hp), System.Math.Round(world.stats.maxHp),
                System.Math.Round(p.energy), System.Math.Round(world.stats.energyMax)), bigLabel);
            GUILayout.Label(string.Format("Биомасса: {0}  ·  Руда: {1}  ·  Возраст: {2} дн.",
                System.Math.Floor(p.biomass), System.Math.Floor(d.senses.ore), System.Math.Floor(p.ageDays)), bigLabel);
            string organs = "Чувства: ";
            foreach (var o in d.senses.unlocked) organs += ShortOrgan(o) + " ";
            double cur, need; string organ;
            ScSenses.NextGoal(d.senses, null, out organ, out cur, out need);
            if (organ != null) organs += "— " + ShortOrgan(organ) + " " + cur + "/" + need;
            GUILayout.Label(organs, bigLabel);
            GUILayout.EndArea();

            // кнопки справа сверху
            GUILayout.BeginArea(new Rect(UiWidth - 130, 8, 122, 210));
            if (GUILayout.Button("Геном", btnStyle)) showGenome = !showGenome;
            if (GUILayout.Button("Журнал", btnStyle)) showLog = !showLog;
            if (GUILayout.Button(speedName == "fast" ? "Идти" : "Отдых", btnStyle))
                speedName = speedName == "fast" ? "explore" : "fast";
            if (GUILayout.Button("Сохранить", btnStyle)) SaveNow();
            GUILayout.EndArea();

            // геном
            if (showGenome) DrawGenome();

            // журнал
            if (showLog) DrawLog();

            // нижние действия
            float by = UiHeight - 54;
            if (world.nearCorpseId >= 0 && GUI.Button(new Rect(12, by - 44, 170, 40), "Поглотить [E]"))
                world.AbsorbNearest();
            if (GUI.Button(new Rect(UiWidth - 160, by, 150, 40), "Атака")) world.PlayerAttack();

            // выход из норки (у лаза)
            if (world.DungeonActive && d.activeDungeon == "birth_nest")
            {
                var f = world.Dun.floors[world.Dun.depth - 1];
                double dd = System.Math.Sqrt((f.upX - p.x) * (f.upX - p.x) + (f.upY - p.y) * (f.upY - p.y));
                if (dd < 1.45 && GUI.Button(new Rect(UiWidth / 2 - 80, by - 30, 160, 36), "В большой мир [E]"))
                    world.ExitBirthNest();
            }

            // тост
            if (toastT > 0 && !string.IsNullOrEmpty(toast))
            {
                var old = GUI.skin.box.fontSize;
                GUI.skin.box.fontSize = 14;
                var w2 = Mathf.Min(560, UiWidth - 40);
                GUI.Box(new Rect((UiWidth - w2) / 2, 64, w2, 44), toast);
                GUI.skin.box.fontSize = old;
            }

            // смерть → суд (заглушка §12)
            if (d.deathCause != null)
            {
                GUI.Box(new Rect(0, 0, UiWidth, UiHeight), "");
                var w3 = Mathf.Min(620, UiWidth - 40);
                GUILayout.BeginArea(new Rect((UiWidth - w3) / 2, UiHeight * 0.25f, w3, 320), boxStyle);
                var h = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("СУД ДУШИ", h);
                GUILayout.Label(world.JudgmentSummary(), bigLabel);
                GUILayout.Label("Путь: " + world.KarmaPath() + "  ·  Карма: " + System.Math.Round(d.karma), bigLabel);
                if (GUILayout.Button("Новое воплощение", btnStyle, GUILayout.Height(38))) Reincarnate();
                GUILayout.EndArea();
            }

            // подсказка управления
            GUI.Label(new Rect(8, UiHeight - 26, UiWidth - 16, 22),
                "WASD/стрелки или тап — движение · тап по существу/Атака — удар · Поглотить [E] · Отдых — ускорить время", bigLabel);
        }

        string ShortOrgan(string o)
        {
            switch (o)
            {
                case "touch": return "Осяз";
                case "chemo": return "Хим";
                case "vibration": return "Вибр";
                case "thermal": return "Тепл";
                case "light": return "Свет";
                case "vision": return "Зрен";
                case "hearing": return "Слух";
                case "spirit": return "Дух";
            }
            return o;
        }

        void DrawGenome()
        {
            var d = world.data;
            float w = Mathf.Min(360, UiWidth - 20);
            GUILayout.BeginArea(new Rect(8, 170, w, UiHeight - 240), boxStyle);
            GUILayout.Label("ГЕНОМ: слотов " + d.player.genome.installed.Count + "/" + d.player.genome.slots +
                " · энергии " + world.stats.energyUsed + "/" + System.Math.Round(d.player.genome.capacity) +
                " · стабильность " + System.Math.Round(world.stats.stability), bigLabel);
            GUILayout.Label("Установлено:", bigLabel);
            foreach (var id in d.player.genome.installed.ToArray())
            {
                GeneDef g;
                if (!Genes.All.TryGetValue(id, out g)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(g.name, bigLabel);
                if (GUILayout.Button("Снять", GUILayout.Width(64))) world.RemoveGene(id);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            GUILayout.Label("Генофонд (раскрытые гены):", bigLabel);
            foreach (var id in d.player.knownGenes.ToArray())
            {
                if (d.player.genome.installed.Contains(id)) continue;
                GeneDef g;
                if (!Genes.All.TryGetValue(id, out g)) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(g.name + " (" + g.cost + " биомассы)", bigLabel);
                if (GUILayout.Button("Вплести", GUILayout.Width(76))) world.InstallGene(id);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            double? nc = NodePlan.NextCost(d.player.genome.nodes);
            if (nc != null && GUILayout.Button("Узел эволюции (" + nc.Value + " биомассы): +1 слот", btnStyle))
                world.BuyNode();
            GUILayout.EndArea();
        }

        void DrawLog()
        {
            float w = Mathf.Min(380, UiWidth - 20);
            var area = new Rect(UiWidth - w - 8, 230, w, UiHeight - 320);
            GUILayout.BeginArea(area, boxStyle);
            logScroll = GUILayout.BeginScrollView(logScroll);
            var log = world.data.log;
            for (int i = log.Count - 1; i >= 0 && i > log.Count - 40; i--)
                GUILayout.Label("Д" + log[i].day + ": " + log[i].text, bigLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
