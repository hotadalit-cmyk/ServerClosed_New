using ServerClosed.Core;
// SERVERCLOSED — Runtime: точка входа Unity (порт engine.ts + page.tsx; v1.3 — полная игра).
// Машина состояний: жизнь ⇄ Magic Chess → смерть → Суд души → Реинкарнация (выбор тела) → жизнь…
// HUD (IMGUI): сезоны и мир «Чаши Отголосов», посёлок и задания, диалоги NPC,
// подземелья с лестницами и приносами, суд с долями регионов, Magic Chess.
using UnityEngine;

namespace ServerClosed.Runtime
{
    public class ScBootstrap : MonoBehaviour
    {
        ScWorld world;
        ScIsoView view;
        string speedName = "explore";
        Vector2? moveTarget;
        bool showGenome, showLog, showQuests, showVillage;
        string toast; double toastT;
        double autosaveT;
        Vector2 logScroll, questScroll, jugScroll, reScroll, dlgScroll, chessScroll, rosterScroll;
        GUIStyle bigLabel, smallLabel, boxStyle, btnStyle, titleStyle;
        string loadError;
        // Magic Chess: тик автобоя 0.4 с (порт engine.ts chessTimer); мир на расстановке стоит (§7)
        double chessTimer;
        ScRng chessRng;
        bool inChess;
        // открытый диалог NPC (порт engine.dialogModal)
        string dialogNpc;
        // экраны смерти: 0 — Суд души, 1 — Реинкарнация (выбор тела §11.3)
        int deathStage;
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

            if (inChess) { TickChess(); return; }   // бой: мировое время остановлено (§7)

            GatherInput();
            world.Update(Time.deltaTime, speedName);
            ConsumeEvents();

            // камера следует за слизнем
            var p = world.data.player;
            var target = view.PosToWorld(p.x, p.y);
            var cam = view.cam;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target + new Vector3(0, 0.4f, -10), Mathf.Min(1, Time.deltaTime * 7));

            autosaveT += Time.deltaTime;
            if (autosaveT > 60) { autosaveT = 0; world.CaptureState(); ScSave.Save(world.data); }
        }

        /// Одноразовые события мира → тосты/фазы (порт блока engine.update).
        void ConsumeEvents()
        {
            if (world.ConsumeStartRaid()) { OpenChess(); return; }
            string disc = world.ConsumeRegionDiscovery();
            if (!string.IsNullOrEmpty(disc))
            {
                var def = ScAtlas.ById(disc);
                if (def != null) Toast("Открыто место: " + def.name, 4);
            }
            if (world.data.Flag("questJustDone") == 1)
            {
                world.data.flags.Remove("questJustDone");
                Toast("Задание выполнено. Награда получена.", 4);
            }
            if (!string.IsNullOrEmpty(world.data.toast))
            {
                Toast(world.data.toast, 4.5);
                world.data.toast = null;
            }
        }

        // ===== Magic Chess (§14): расстановка → автобой → последствия =====
        void OpenChess()
        {
            world.data.chess = ScChess.CreateRaidChess(world);
            chessRng = new ScRng(world.data.seed ^ 0x51ed270b);
            chessTimer = 0;
            inChess = true;
            moveTarget = null;
            world.SetInput(0, 0, false);
            Toast("Время остановилось. Расставьте оборону.", 5);
        }

        void CloseChess()
        {
            world.data.chess = null;
            inChess = false;
            SaveNow();
        }

        void TickChess()
        {
            var st = world.data.chess;
            if (st == null) { inChess = false; return; }
            if (st.phase != "battle") return;
            chessTimer += Time.deltaTime;
            while (chessTimer >= 0.4 && st.phase == "battle")
            {
                chessTimer -= 0.4;
                bool done = ScChess.Tick(st, chessRng);
                if (done) { ScChess.ApplyResult(world, st); SaveNow(); }
            }
        }

        // ===== Ввод: WASD/стрелки + тап-движение + виртуальный джойстик =====
        bool _ePressed;
        void GatherInput()
        {
            // диалог открыт: мир живёт, но тело слушает собеседника
            if (dialogNpc != null) { world.SetInput(0, 0, false); moveTarget = null; return; }

            double sx = 0, sy = 0;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null) {
                sx = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                sy = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
                _ePressed = keyboard.eKey.wasPressedThisFrame;
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
            _ePressed = Input.GetKeyDown(KeyCode.E);
            if (Input.GetKeyDown(KeyCode.Space)) world.PlayerAttack();
#endif
            // [E]: диалог → подземелья/лестницы/приносы → поглощение (порт engine.interact)
            if (_ePressed)
            {
                _ePressed = false;
                if (world.nearNpcId != null) dialogNpc = world.nearNpcId;
                else if (world.DungeonActive)
                {
                    if (world.data.activeDungeon == "birth_nest" && world.nearStairsUp) world.ExitBirthNest();
                    else if (world.nearStairsUp) world.AscendStairs();
                    else if (world.nearStairsDown) world.DescendStairs();
                    else if (world.nearCacheId >= 0) world.TakeCache();
                    else if (world.nearCorpseId >= 0) world.AbsorbNearest();
                }
                else if (world.nearGateId != null) world.EnterDungeon(world.nearGateId);
                else if (world.nearCorpseId >= 0) world.AbsorbNearest();
            }

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
            if (inChess || dialogNpc != null || world.data.deathCause != null) return true;
            var pos = new Vector2(point.x / UiScale, (Screen.height - point.y) / UiScale);
            if (new Rect(8, 8, 340, 190).Contains(pos) || new Rect(UiWidth - 130, 8, 122, 260).Contains(pos)) return true;
            if (pos.y >= UiHeight - 108) return true;
            if (showGenome && new Rect(8, 210, Mathf.Min(360, UiWidth - 20), UiHeight - 280).Contains(pos)) return true;
            if (showLog && new Rect(UiWidth - Mathf.Min(380, UiWidth - 20) - 8, 270, Mathf.Min(380, UiWidth - 20), UiHeight - 340).Contains(pos)) return true;
            if (showQuests && new Rect(8, 210, Mathf.Min(360, UiWidth - 20), UiHeight - 280).Contains(pos)) return true;
            if (showVillage && new Rect(UiWidth - Mathf.Min(360, UiWidth - 20) - 8, 270, Mathf.Min(360, UiWidth - 20), 240).Contains(pos)) return true;
            return false;
        }
        bool IsPointerOverUi() { return GUIUtility.hotControl != 0; }

        // ===== Суд/реинкарнация: продолжение жизни =====
        public void Reincarnate()
        {
            if (!world.Reincarnate()) return;
            deathStage = 0;
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
                smallLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                boxStyle = new GUIStyle(GUI.skin.box);
                btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            }
            if (world == null) { if (loadError != null) GUI.Box(new Rect(20, 20, UiWidth - 40, 160), "Не удалось загрузить сохранение. Файлы сохранены.\n" + loadError); return; }
            var d = world.data;
            var p = d.player;

            if (inChess) { DrawChess(); return; }
            if (d.deathCause != null) { if (deathStage == 0) DrawJudgment(); else DrawReincarnation(); return; }

            // левая верхняя панель: часы, сезон, тело, мир, посёлок
            GUILayout.BeginArea(new Rect(8, 8, 340, 190), boxStyle);
            GUILayout.Label(world.clock.Label() + (world.DungeonActive ? "  ·  " + (d.activeDungeon == "birth_nest" ? "Родовая норка" : DungeonName(d.activeDungeon)) : ""), bigLabel);
            GUILayout.Label("Сезон: " + world.Season + " — " + world.SeasonMods.hint, smallLabel);
            GUILayout.Label(string.Format("HP {0}/{1}   ЭН {2}/{3}   Возраст: {4} дн.", System.Math.Round(p.hp), System.Math.Round(world.stats.maxHp),
                System.Math.Round(p.energy), System.Math.Round(world.stats.energyMax), System.Math.Floor(p.ageDays)), bigLabel);
            GUILayout.Label(string.Format("Биомасса: {0}  ·  Руда: {1}  ·  {2}", System.Math.Floor(p.biomass), System.Math.Floor(d.senses.ore), world.CurrentWorldRegionName()), bigLabel);
            var v = d.village;
            string raid = v.raidPending ? "  ·  ВОЛКИ У ОГРАДЫ!" : "";
            GUILayout.Label(string.Format("Посёлок: {0} · еда {1} · люди {2} · стража {3}{4}", v.stage, System.Math.Round(v.food), v.population, System.Math.Round(v.military), raid), bigLabel);
            GUILayout.EndArea();

            // кнопки справа сверху
            GUILayout.BeginArea(new Rect(UiWidth - 130, 8, 122, 260));
            if (GUILayout.Button("Геном", btnStyle)) { showGenome = !showGenome; showQuests = false; }
            if (GUILayout.Button("Задания", btnStyle)) { showQuests = !showQuests; showGenome = false; }
            if (GUILayout.Button("Журнал", btnStyle)) showLog = !showLog;
            if (GUILayout.Button("Посёлок", btnStyle)) showVillage = !showVillage;
            if (GUILayout.Button(speedName == "fast" ? "Идти" : "Отдых", btnStyle))
            {
                if (speedName != "fast" && world.DangerNear()) Toast("Рядом хищники — не до отдыха.", 2.5);
                else speedName = speedName == "fast" ? "explore" : "fast";
            }
            if (GUILayout.Button("Сохранить", btnStyle)) SaveNow();
            GUILayout.EndArea();

            if (showGenome) DrawGenome();
            if (showLog) DrawLog();
            if (showQuests) DrawQuests();
            if (showVillage) DrawVillage();
            if (dialogNpc != null) DrawDialogue();

            // нижние действия по близости
            float by = UiHeight - 54;
            if (world.nearCorpseId >= 0 && GUI.Button(new Rect(12, by - 44, 170, 40), "Поглотить [E]"))
                world.AbsorbNearest();
            if (GUI.Button(new Rect(UiWidth - 160, by, 150, 40), "Атака")) world.PlayerAttack();
            float cy = by - 88;
            if (world.nearNpcId != null)
            {
                var npc = world.Npc(world.nearNpcId);
                if (npc != null && GUI.Button(new Rect(12, cy, 210, 40), "Поговорить: " + npc.name + " [E]")) dialogNpc = npc.id;
                cy -= 46;
            }
            if (!world.DungeonActive && world.nearGateId != null)
            {
                if (GUI.Button(new Rect(12, cy, 250, 40), "Войти: " + DungeonName(world.nearGateId) + " [E]"))
                    world.EnterDungeon(world.nearGateId);
                cy -= 46;
            }
            if (world.DungeonActive)
            {
                if (world.nearStairsDown && GUI.Button(new Rect(12, cy, 190, 40), "Лестница вниз [E]"))
                    world.DescendStairs();
                if (world.nearStairsUp && d.activeDungeon != "birth_nest" && GUI.Button(new Rect(12, cy, 190, 40), "Лестница вверх [E]"))
                    world.AscendStairs();
                if (world.nearCacheId >= 0 && GUI.Button(new Rect(12, cy, 190, 40), CacheLabel() + " [E]"))
                    world.TakeCache();
                if (d.activeDungeon == "birth_nest" && world.nearStairsUp && GUI.Button(new Rect(UiWidth / 2 - 80, by - 30, 160, 36), "В большой мир [E]"))
                    world.ExitBirthNest();
            }

            // тост
            if (toastT > 0 && !string.IsNullOrEmpty(toast))
            {
                var old = GUI.skin.box.fontSize;
                GUI.skin.box.fontSize = 14;
                var w2 = Mathf.Min(560, UiWidth - 40);
                GUI.Box(new Rect((UiWidth - w2) / 2, 210, w2, 44), toast);
                GUI.skin.box.fontSize = old;
            }

            // подсказка управления
            GUI.Label(new Rect(8, UiHeight - 26, UiWidth - 16, 22),
                "WASD/тап — движение · Пробел — удар · E — говорить/вход/поглотить · Отдых — ускорить время", bigLabel);
        }

        string DungeonName(string id)
        {
            var def = ScDungeon.Def(id);
            return def != null ? def.name : id;
        }

        string CacheLabel()
        {
            var def = world.DungeonDefOf;
            return def != null ? def.cacheLabel : "Взять принос";
        }

        // ===== Панель заданий (§13) =====
        void DrawQuests()
        {
            float w = Mathf.Min(360, UiWidth - 20);
            GUILayout.BeginArea(new Rect(8, 210, w, UiHeight - 280), boxStyle);
            GUILayout.Label("ЗАДАНИЯ ПОСЁЛКА", bigLabel);
            questScroll = GUILayout.BeginScrollView(questScroll);
            foreach (var qv in ScQuests.Views(world.data))
            {
                string status = qv.status == QuestStatus.Done ? "выполнено"
                    : qv.status == QuestStatus.Ready ? "ГОТОВО к сдаче"
                    : qv.status == QuestStatus.Active ? System.Math.Floor(qv.progress) + "/" + qv.q.need
                    : (qv.q.requires == null || ScQuests.Done(world.data, qv.q.requires)) ? "доступно" : "позже";
                GUILayout.Label(qv.q.title + " — " + status + "\n" + qv.q.desc, smallLabel);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ===== Панель посёлка (§15) =====
        void DrawVillage()
        {
            float w = Mathf.Min(360, UiWidth - 20);
            var area = new Rect(UiWidth - w - 8, 270, w, 240);
            GUILayout.BeginArea(area, boxStyle);
            var v = world.data.village;
            GUILayout.Label("ПОСЁЛОК «" + v.name + "» — " + v.stage, bigLabel);
            GUILayout.Label("Еда: " + System.Math.Round(v.food) + " · Люди: " + v.population +
                "\nСтабильность: " + System.Math.Round(v.stability) + " · Стража: " + System.Math.Round(v.military) +
                "\nНабегов отбито: " + v.raidsSurvived +
                (v.raidPending ? "\nСТАЯ У ОГРАДЫ! Дар зовёт к обороне." : "\nПередышка: " + System.Math.Max(0, System.Math.Round(v.raidCdDays)) + " дн."), smallLabel);
            GUILayout.EndArea();
        }

        // ===== Диалог NPC (§3.2, §13) =====
        void DrawDialogue()
        {
            var dlg = ScNpcs.Dialogue(world, dialogNpc);
            float w = Mathf.Min(460, UiWidth - 40);
            float h = Mathf.Min(360, UiHeight - 200);
            GUILayout.BeginArea(new Rect((UiWidth - w) / 2, UiHeight * 0.22f, w, h), boxStyle);
            GUILayout.Label(dlg.name + " · " + dlg.role, bigLabel);
            dlgScroll = GUILayout.BeginScrollView(dlgScroll, GUILayout.Height(Mathf.Max(60f, Mathf.Min(110f, h - 190f))));
            GUILayout.Label(dlg.text, bigLabel);
            GUILayout.EndScrollView();
            foreach (var opt in dlg.options.ToArray())
            {
                if (opt.id == "leave") continue;
                if (GUILayout.Button(opt.label, btnStyle))
                {
                    ScNpcs.Action(world, dialogNpc, opt.id);
                    if (world.ConsumeStartRaid()) { dialogNpc = null; OpenChess(); return; }   // кадр завершён — дальше рисуется шахматная доска
                }
            }
            if (GUILayout.Button("Уйти", btnStyle)) dialogNpc = null;
            GUILayout.EndArea();
        }

        // ===== Экран Magic Chess (§14) =====
        void DrawChess()
        {
            var st = world.data.chess;
            if (st == null) { inChess = false; return; }
            GUI.Box(new Rect(0, 0, UiWidth, UiHeight), "");
            GUILayout.BeginArea(new Rect(16, 12, UiWidth - 32, 70), boxStyle);
            GUILayout.Label(st.scenario, bigLabel);
            GUILayout.Label(st.stake + "  ·  Юнитов: " + st.placed.Count + "/" + st.reserveLimit +
                "  ·  время мира: " + (st.phase == "setup" ? "остановлено" : "+" + System.Math.Round(st.worldMinutesSpent) + " мин"), smallLabel);
            GUILayout.EndArea();

            // поле 7×5 (на узких экранах панель состава уходит под доску)
            float rw = Mathf.Min(330, UiWidth - 40);
            bool sidePanel = UiWidth >= 780;
            float cell = sidePanel ? Mathf.Min(58, (UiWidth - rw - 48) / st.boardC) : Mathf.Min(58, (UiWidth - 40) / st.boardC);
            float bx = sidePanel ? 24 : (UiWidth - cell * st.boardC) / 2;
            float by2 = 96;
            for (int r = 0; r < st.boardR; r++)
                for (int c = 0; c < st.boardC; c++)
                {
                    var rect = new Rect(bx + c * cell, by2 + r * cell, cell - 3, cell - 3);
                    ChessUnit unit = st.placed.Find(u => u.alive && u.r == r && u.c == c);
                    if (unit == null) unit = st.enemies.Find(u => u.alive && u.r == r && u.c == c);
                    bool playerZone = r >= 3;
                    GUI.backgroundColor = unit != null
                        ? (unit.side == "player" ? new Color(0.45f, 0.6f, 0.32f) : new Color(0.72f, 0.3f, 0.24f))
                        : playerZone ? new Color(0.24f, 0.42f, 0.24f) : new Color(0.22f, 0.22f, 0.22f);
                    string label = "";
                    if (unit != null)
                    {
                        string uname = unit.side == "player"
                            ? (st.playerRoster.Find(dd => dd.id == unit.defId) != null ? st.playerRoster.Find(dd => dd.id == unit.defId).name : "?")
                            : ScChess.EnemyShort(unit.defId);
                        ChessUnitDef udef = unit.side == "player" ? st.playerRoster.Find(dd => dd.id == unit.defId) : null;
                        double maxHp = unit.side == "player" ? (udef != null ? udef.hp : 1) : ScChess.EnemyBaseHp(unit.defId);
                        label = uname + "\n" + System.Math.Max(0, System.Math.Round(unit.hp)) + "/" + System.Math.Round(maxHp);
                    }
                    bool clicked = st.phase == "setup" && playerZone && GUI.Button(rect, label);
                    GUI.backgroundColor = Color.white;
                    if (clicked)
                    {
                        var def = st.playerRoster.Find(dd => !st.placed.Exists(u => u.defId == dd.id));
                        if (def != null && st.placed.Count < st.reserveLimit) ScChess.Place(st, def.id, r, c);
                    }
                }

            // правая колонка (или низ): состав/журнал/итог
            var panelRect = sidePanel
                ? new Rect(UiWidth - rw - 16, 96, rw, UiHeight - 130)
                : new Rect(16, by2 + cell * st.boardR + 8, UiWidth - 32, Mathf.Max(150, UiHeight - by2 - cell * st.boardR - 24));
            GUILayout.BeginArea(panelRect, boxStyle);
            if (st.phase == "setup")
            {
                GUILayout.Label("Рота обороны — коснитесь зелёной клетки (ваши ряды — нижние два)", smallLabel);
                rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(Mathf.Max(80, panelRect.height - 150)));
                foreach (var def in st.playerRoster.ToArray())
                {
                    bool placed = st.placed.Exists(u => u.defId == def.id);
                    GUILayout.Label(def.name + " · " + def.role + " · HP " + def.hp + " · атк " + def.atk + " · брн " + def.def + " · дист " + def.range + " · " + string.Join(", ", def.synergies.ToArray()) + "\n" + def.trait, smallLabel);
                    if (placed && GUILayout.Button("убрать с поля", GUILayout.Width(130)))
                        ScChess.Remove(st, st.placed.Find(u => u.defId == def.id).uid);
                }
                GUILayout.EndScrollView();
                GUI.enabled = st.placed.Count > 0;
                if (GUILayout.Button("Трубить сбор (начать бой)", GUILayout.Height(40))) ScChess.Start(st);
                GUI.enabled = true;
            }
            else if (st.phase == "battle")
            {
                GUILayout.Label("Ход сражения", bigLabel);
                chessScroll = GUILayout.BeginScrollView(chessScroll);
                for (int i = st.battleLog.Count - 1; i >= 0 && i > st.battleLog.Count - 12; i--)
                    GUILayout.Label(st.battleLog[i], smallLabel);
                GUILayout.EndScrollView();
            }
            else
            {
                bool win = st.result == "win";
                GUILayout.Label(win ? "Стая отбита!" : "Оборона пала…", titleStyle);
                GUILayout.Label(win
                    ? "Посёлок спасён. Ополченцы запомнят командира-слизня. Волчьи ряды поредели."
                    : "Волки ушли с добычей. Посёлок осиротел, но жив. Ваши союзники пали.", smallLabel);
                GUILayout.Label("В мире прошло " + System.Math.Round(st.worldMinutesSpent) + " минут.", smallLabel);
                if (GUILayout.Button("Вернуться в мир", GUILayout.Height(40))) CloseChess();
            }
            GUILayout.EndArea();
        }

        // ===== Экран Суда души (§11.2) =====
        void DrawJudgment()
        {
            GUI.Box(new Rect(0, 0, UiWidth, UiHeight), "");
            float w = Mathf.Min(620, UiWidth - 40);
            var j = world.JudgmentView();
            GUILayout.BeginArea(new Rect((UiWidth - w) / 2, 20, w, UiHeight - 40), boxStyle);
            jugScroll = GUILayout.BeginScrollView(jugScroll);
            GUILayout.Label("СУД ДУШИ", titleStyle);
            if (j != null)
            {
                GUILayout.Label("Воплощение №" + world.data.soul.life + " · вердикт: " + j.verdict, bigLabel);
                GUILayout.Label(j.summary, bigLabel);
                GUILayout.Label("Прожито: " + System.Math.Floor(j.ageDays) + " дн. · Карма: " + (j.karma > 0 ? "+" : "") + System.Math.Round(j.karma) +
                    " · Опыт: +" + j.xpGained + " · Уровень души: " + j.newSoulLevel + (j.levelUp ? " ↑" : ""), bigLabel);
                if (j.achievements.Count > 0)
                {
                    GUILayout.Label("Заслуги перед вечностью", bigLabel);
                    foreach (var a in j.achievements.ToArray())
                        GUILayout.Label(a.title + " — " + a.desc + " (+" + a.soulXp + " опыта)", smallLabel);
                }
                if (j.regionOdds.Count > 0)
                {
                    GUILayout.Label("Куда тянет вашу душу — вероятности рождения", bigLabel);
                    foreach (var o in j.regionOdds.ToArray())
                        GUILayout.Label(o.name + " — " + o.pct + "% · " + o.difficulty + " сложность", smallLabel);
                    foreach (var reason in j.regionReasons.ToArray())
                        GUILayout.Label("• " + reason, smallLabel);
                }
                if (j.memoriesUnlocked.Count > 0)
                {
                    GUILayout.Label("Приоткрытые воспоминания", bigLabel);
                    foreach (var m in j.memoriesUnlocked.ToArray())
                        GUILayout.Label(m.title + ": " + m.text, smallLabel);
                }
            }
            else
            {
                GUILayout.Label(world.JudgmentSummary(), bigLabel);
            }
            GUILayout.Label("Путь: " + world.KarmaPath() + "  ·  Карма: " + System.Math.Round(world.data.soul.karma), bigLabel);
            if (GUILayout.Button("К новому воплощению", btnStyle, GUILayout.Height(44))) deathStage = 1;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ===== Экран Реинкарнации с выбором тела (§11.3) =====
        void DrawReincarnation()
        {
            GUI.Box(new Rect(0, 0, UiWidth, UiHeight), "");
            float w = Mathf.Min(620, UiWidth - 40);
            var soul = world.data.soul;
            GUILayout.BeginArea(new Rect((UiWidth - w) / 2, 20, w, UiHeight - 40), boxStyle);
            reScroll = GUILayout.BeginScrollView(reScroll);
            GUILayout.Label("РЕИНКАРНАЦИЯ", titleStyle);
            GUILayout.Label("Душа уровня " + soul.level + " · карма " + (soul.karma > 0 ? "+" : "") + System.Math.Round(soul.karma) +
                " · опыт " + soul.xp + "\nМир жил без вас " + world.data.lastSkipDays + " дн.: посёлок менялся, зверь плодился, старики уходили.", bigLabel);
            GUILayout.Label("Выберите тело:", bigLabel);
            foreach (var opt in ScSoul.ReincarnationOptions(soul).ToArray())
            {
                string gifts = "";
                if (opt.maxHp != 0) gifts += " +HP" + opt.maxHp;
                if (opt.atk != 0) gifts += " +атк" + opt.atk;
                if (opt.def != 0) gifts += " +брн" + opt.def;
                if (opt.speed != 0) gifts += " +скор" + opt.speed;
                if (opt.perception != 0) gifts += " +воспр" + opt.perception;
                if (GUILayout.Button(opt.name + (gifts != "" ? "  [" + gifts + " ]" : "") + "\n" + opt.desc, btnStyle, GUILayout.Height(64)))
                    ReincarnateWith(opt.id);
            }
            if (soul.pastLives.Count > 0)
            {
                GUILayout.Label("Нить жизней", bigLabel);
                for (int i = soul.pastLives.Count - 1; i >= 0; i--)
                {
                    var pl = soul.pastLives[i];
                    GUILayout.Label("№" + pl.life + " · " + pl.ageDays + " дн. · " + pl.cause + " · карма " + System.Math.Round(pl.karma) + " · +" + pl.xpGained + " опыта", smallLabel);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void ReincarnateWith(string optionId)
        {
            if (!world.ReincarnateWith(optionId)) return;
            deathStage = 0;
            moveTarget = null; speedName = "explore";
            SaveNow();
            Toast("Воплощение №" + world.data.lifeNo + ". Душа помнит прошлую жизнь.", 5);
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
            GUILayout.BeginArea(new Rect(8, 210, w, UiHeight - 280), boxStyle);
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
            var area = new Rect(UiWidth - w - 8, 270, w, UiHeight - 340);
            GUILayout.BeginArea(area, boxStyle);
            logScroll = GUILayout.BeginScrollView(logScroll);
            var log = world.data.log;
            for (int i = log.Count - 1; i >= 0 && i > log.Count - 40; i--)
                GUILayout.Label("Д" + log[i].day + ": " + log[i].text, smallLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
