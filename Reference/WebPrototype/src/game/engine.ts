// Слой Gameplay/Presentation-мост: движок-фасад. Единая машина состояний:
// title → life ⇄ chess → death → judgment → reincarnate → life…
// Держит мир, душу, достижения, текущий бой Magic Chess и сохранения.

import { World, type WorldData } from './world';
import { WorldClock, TIME_SPEED } from './time';
import { newSoul, initAchievements, judgeSoul, reincarnationOptions, advanceWorldAfterDeath, soulLevelOf } from './soul';
import { createRaidChess, chessTick, chessPlace, chessRemove, chessStart, applyChessResult } from './chess';
import { saveGame, loadGame, wipeSaves, hasSave, type EnginePhase, type SaveFile } from './save';
import { mulberry32 } from './rng';
import { GENES } from './genes';
import { DUNGEONS } from './dungeon';
import { ORGAN_INFO, createSensoryState, investInOrgan, setFocus, type SenseOrgan } from './senses';
import { setMode, egestChamber, createDigestive, MODE_INFO, type DigestMode } from './digestion';
import { rollRegion, regionDef, defaultTraits } from './regions';
import { worldRegionById } from './atlas';
import type { RegionId } from './types';
import type {
  SoulState, Achievement, ChessState, JudgmentResult, ReincarnOption, PastLifeRecord,
} from './types';
import type { DungeonId } from './types';

export interface Toast { id: number; text: string; kind: 'info' | 'good' | 'bad' | 'epic' }

export class Engine {
  phase: EnginePhase = 'title';
  world: World | null = null;
  soul: SoulState = newSoul();
  achievements: Achievement[] = initAchievements();
  chess: ChessState | null = null;
  judgment: JudgmentResult | null = null;
  toasts: Toast[] = [];
  rest = false;
  paused = false;

  private listeners = new Set<() => void>();
  private toastSeq = 1;
  private chessTimer = 0;
  private resultHold = 0;
  private saveTimer = 0;
  private lastSavedDay = -1;
  private deathAnnounced = false;
  private dyingT = -1; // >=0 — идёт анимация распада (смерть-растекание) перед экраном смерти
  private rng = mulberry32((Date.now() ^ 0x51ed270b) >>> 0);

  subscribe(fn: () => void): () => void {
    this.listeners.add(fn);
    return () => { this.listeners.delete(fn); };
  }
  notify() { for (const fn of this.listeners) fn(); }

  toast(text: string, kind: Toast['kind'] = 'info') {
    const t = { id: this.toastSeq++, text, kind };
    this.toasts.push(t);
    if (this.toasts.length > 4) this.toasts.shift();
    setTimeout(() => {
      this.toasts = this.toasts.filter((x) => x.id !== t.id);
      this.notify();
    }, 4200);
    this.notify();
  }

  // ===== Жизненный цикл =====
  startNewGame() {
    const seed = this.rng.int(1, 2 ** 31 - 1);
    this.soul = newSoul();
    this.achievements = initAchievements();
    this.world = World.newGame(seed, 1, null, 0);
    this.world.enterBirthNest(); // Дополнение: первая жизнь начинается в родовой норке — мини-вселенной
    this.judgment = null;
    this.chess = null;
    this.phase = 'life';
    this.rest = false;
    this.deathAnnounced = false;
    this.dyingT = -1;
    this.lastSavedDay = this.world.clock.totalDays;
    this.save();
    this.toast('Вы — слизень. Вы слепы: мир познаётся осязанием. Поглощайте жизни.', 'epic');
    this.notify();
  }

  hasSaveGame(): boolean {
    return hasSave();
  }

  continueGame(): boolean {
    const f = loadGame();
    if (!f) return false;
    this.applySave(f);
    return true;
  }

  private applySave(f: SaveFile) {
    this.phase = f.phase === 'title' ? 'life' : f.phase;
    this.world = World.fromData(f.world);
    this.soul = f.soul;
    // ядро — свойство души: согласуем после миграции старых сейвов (Дополнение к ТЗ)
    if (this.world.data.flags.senseCore === 1 && !this.soul.resonantCore) this.soul.resonantCore = true;
    if (this.soul.spiritMemory == null) this.soul.spiritMemory = 0;
    this.achievements = f.achievements;
    this.chess = f.chess;
    this.judgment = f.judgment as JudgmentResult | null;
    this.rest = false;
    this.deathAnnounced = this.phase === 'death' || this.phase === 'judgment' || this.phase === 'reincarnate';
    this.dyingT = -1;
    // если сохранились посреди смерти без рассчитанного суда — возвращаем к экрану смерти
    if ((this.phase === 'judgment' || this.phase === 'reincarnate') && !this.judgment) this.phase = 'death';
  }

  wipe() {
    wipeSaves();
    this.phase = 'title';
    this.world = null;
    this.soul = newSoul();
    this.achievements = initAchievements();
    this.judgment = null;
    this.chess = null;
    this.notify();
  }

  save() {
    if (!this.world) return;
    saveGame({
      phase: this.phase,
      world: this.world.data,
      soul: this.soul,
      achievements: this.achievements,
      chess: this.chess,
      judgment: this.judgment,
    });
  }

  /** Прогресс анимации распада при смерти 0..1 (0 — не умираем). Для рендера желе. */
  deathDissolve(): number {
    if (this.phase !== 'life' || !this.world || !this.world.data.deathCause || this.dyingT < 0) return 0;
    return Math.min(1, 1 - this.dyingT / 2.0);
  }

  /** Главный шаг: вызывается из canvas-цикла. dtReal — для физики, dtClock — для мировых часов (§7). */
  update(dtReal: number, dtClock = dtReal) {
    if (this.phase === 'life' && this.world) {
      const w = this.world;
      if (w.data.deathCause) {
        // смерть-растекание (референс «Смерть / Распад»): мир замирает, тело распадается на капли,
        // и только после анимации включается экран смерти
        if (!this.deathAnnounced) {
          this.deathAnnounced = true;
          this.dyingT = 2.0;
        } else if (this.dyingT >= 0) {
          this.dyingT -= dtReal;
          if (this.dyingT <= 0) {
            this.dyingT = -1;
            this.phase = 'death';
            this.save();
            this.notify();
          }
        }
        return;
      }
      const speed: keyof typeof TIME_SPEED = this.rest ? 'fast' : (w.dangerNear() ? 'battle' : 'explore');
      w.update(dtClock, speed);
      // реакция времени на контекст (§7): бой замедляет мир, отдых ускоряет
      if (w.data.flags.startRaid === 1) {
        delete w.data.flags.startRaid;
        this.openChess();
        return;
      }
      // Боссы подземелий повержены — эпические тосты (Регион 2/3)
      for (const def of Object.values(DUNGEONS)) {
        if (w.data.flags[`${def.bossFlag}Just`] === 1) {
          delete w.data.flags[`${def.bossFlag}Just`];
          this.toast(`${def.bossName} повержен(а)! Тьма подземелья отступает.`, 'epic');
          this.save();
        }
      }
      // задание выполнено — тост (§13)
      if (w.data.flags.questJustDone === 1) {
        delete w.data.flags.questJustDone;
        this.toast('Задание выполнено. Награда получена.', 'good');
      }
      // эволюция восприятия: тосты пробуждения органов (Дополнение к ТЗ)
      for (const o of ['chemo', 'vibration', 'thermal', 'light', 'vision', 'hearing'] as const) {
        if (w.data.flags[`sense_${o}`] === 1) {
          delete w.data.flags[`sense_${o}`];
          this.toast(`Развилось чувство: ${ORGAN_INFO[o as SenseOrgan].name}.`, 'good');
        }
      }
      // резонансное ядро сформировано: свойство души, переживает смерть (п.9, п.12)
      if (w.data.flags.senseCoreJust === 1) {
        delete w.data.flags.senseCoreJust;
        this.soul.resonantCore = true;
        this.soul.spiritMemory = (this.soul.spiritMemory ?? 0) + 1;
        this.toast('Резонансное ядро сформировано: открыто духовное чутьё.', 'epic');
        this.save();
      }
      if (w.data.flags.senseOverloaded === 1) {
        delete w.data.flags.senseOverloaded;
        this.toast('ПЕРЕГРУЗКА: магическая энергия жжёт тело изнутри!', 'bad');
      }
      // пищеварение (ТЗ §6): открытие режимов и отравление
      for (const m of ['fast', 'gentle'] as const) {
        if (w.data.flags[`dig_${m}_just`] === 1) {
          delete w.data.flags[`dig_${m}_just`];
          this.toast(`Открыт режим пищеварения: ${MODE_INFO[m].name}.`, 'good');
        }
      }
      if (w.data.flags.digToxicJust === 1) {
        delete w.data.flags.digToxicJust;
        this.toast('Отравление: тело темнеет, мембрана судорожно сокращается.', 'bad');
      }
      // «Чаша Отголосов»: открыто новое место мира (только ногами/чутьём — п.6/9)
      const disc = w.consumeRegionDiscovery();
      if (disc) {
        const def = worldRegionById(disc);
        if (def) this.toast(`Открыто место: ${def.name}`, 'good');
      }
      // автосейв: по дню и по таймеру
      this.saveTimer += dtReal;
      if (this.saveTimer > 15 || w.clock.totalDays !== this.lastSavedDay) {
        this.saveTimer = 0;
        this.lastSavedDay = w.clock.totalDays;
        this.save();
      }
      this.notifyThrottled(dtReal);
    } else if (this.phase === 'chess' && this.chess) {
      const st = this.chess;
      if (st.phase === 'battle') {
        this.chessTimer += dtReal;
        while (this.chessTimer >= 0.4 && st.phase === 'battle') {
          this.chessTimer -= 0.4;
          const done = chessTick(st, this.rng);
          if (done) {
            applyChessResult(this.world!, st);
            this.resultHold = 0;
            this.save();
            this.notify();
          }
        }
        this.notifyThrottled(dtReal);
      } else if (st.phase === 'result') {
        this.resultHold += dtReal;
        this.notifyThrottled(dtReal);
      }
    }
  }

  private notifyAcc = 0;
  private notifyThrottled(dt: number) {
    this.notifyAcc += dt;
    if (this.notifyAcc >= 0.12) { this.notifyAcc = 0; this.notify(); }
  }

  // ===== Смерть → Суд → Реинкарнация (§3.3, §11) =====
  proceedToJudgment() {
    if (!this.world) return;
    this.judgment = judgeSoul(this.soul, this.world.data, this.achievements);
    this.phase = 'judgment';
    this.save();
    this.notify();
  }

  proceedToReincarnation() {
    if (!this.judgment) return;
    this.soul.level = this.judgment.newSoulLevel; // уровень растёт сразу после суда
    this.phase = 'reincarnate';
    this.save();
    this.notify();
  }

  reincarnateOptions(): ReincarnOption[] {
    return reincarnationOptions(this.soul);
  }

  reincarnate(optionId: string) {
    const opt = this.reincarnateOptions().find((o) => o.id === optionId) ?? this.reincarnateOptions()[0];
    const j = this.judgment!;
    const oldWorld = this.world!;

    // 1. Душа принимает итог суда
    this.soul.xp += j.xpGained;
    this.soul.karma = j.karma;
    this.soul.level = j.newSoulLevel;
    const traits = this.soul.traits ?? defaultTraits();
    this.soul.traits = traits;
    const record: PastLifeRecord = {
      life: this.soul.life,
      species: oldWorld.data.player.species,
      ageDays: Math.floor(j.ageDays),
      cause: j.cause === 'old_age' ? 'угасание' : j.cause === 'killed' ? 'гибель' : 'мутация',
      karma: j.karma,
      xpGained: j.xpGained,
      summary: j.summary,
    };
    this.soul.pastLives.push(record);
    this.soul.life++;
    oldWorld.data.decisions = []; // суд прочитал решения — начинаем жизнь с чистой ведомостью

    // 2. Небытие между жизнями (§3.3 + «Резонанс души»): глубокая память души дольше
    // не воплощается — за это время эпохи меняются: цивилизации растут и угасают
    const skipDays = Math.min(600, Math.round((60 + this.soul.life * 30) * (1 + (traits.memory / 100) * 1.5)));
    advanceWorldAfterDeath(oldWorld.data, j, skipDays);
    if (skipDays >= 180) {
      oldWorld.data.flags.eraChanged = 1;
      oldWorld.data.log.push({ day: Math.floor(oldWorld.data.minutes / 1440), year: 1, text: `Душа пребывала в небытии дольше обычного: минуло ${skipDays} дней. Поколения сменились — мир уже не тот, что вы помнили.`, kind: 'world' });
    }

    // 3. Новое тело в том же мире (§11.3)
    const d = oldWorld.data;
    const v = d.pois.village.pos;
    // душа сохраняет ограниченную часть знаний
    const carryCount = Math.max(0, Math.min(2, this.soul.level - 1));
    const carried = oldWorld.rng.shuffle(d.player.knownGenes).slice(0, carryCount);
    d.player = {
      species: 'slug',
      perks: opt.perkGene ? [opt.perkGene] : [],
      pos: { x: v.x - 6.5, y: v.y + 6.5 },
      vel: { x: 0, y: 0 },
      hp: 34, energy: 4,
      ageDays: 0.4,
      lifeNum: this.soul.life,
      biomass: 8 * this.soul.level, // наследие: немного биомассы от уровня души
      genome: { installed: [], capacity: 4, slots: 3, nodes: 0 },
      knownGenes: [...carried],
      discoveredSpecies: [],
      kills: 0, absorbedCount: 0, synDiscovered: [],
      mutations: 0,
      atkCd: 0, hitFlash: 0, regenAcc: 0, energyAcc: 0, attackAnim: 0, facing: 1,
    };
    d.deathCause = null;
    d.deathDay = 0;
    d.creatures = d.creatures.filter((c) => !c.dead);
    // подземелья перерождаются вместе с миром: их обитатели забывают прошлые визиты (§11)
    if (d.dungeons) {
      for (const dun of Object.values(d.dungeons)) {
        if (!dun) continue;
        dun.active = false;
        dun.depth = 1;
        for (const cache of dun.caches) cache.taken = false;
      }
    }
    d.activeDungeon = null;
    d.creatures = d.creatures.filter((c) => c.dungeonFloor == null);
    // душа несёт свой итог в новое тело — путь кармы (§12) считается от неё
    d.flags.soulBaseKarma = this.soul.karma;
    d.flags.lightBlessing = 0;
    d.flags.darkWhisper = 0;
    // Дополнение к ТЗ: тело новое — физические рецепторы развиваются заново (критерий №7);
    // резонансное ядро остаётся свойством души и работает по отдельным правилам
    d.senses = createSensoryState();
    // тело новое — содержимое пищеварения не переходит (критерий: после смерти не остаётся
    // активных процессов предыдущего тела)
    d.digestion = createDigestive();
    d.flags.senseCore = this.soul.resonantCore ? 1 : 0;
    d.flags.spiritMemory = this.soul.spiritMemory ?? 0;
    d.flags.traitDepth = traits.depth; // глубина души ускоряет рецепторы (не даёт зрения — п.6)
    delete d.flags.senseVisionUnlock;
    delete d.flags.senseOverloaded;
    for (const o of ['chemo', 'vibration', 'thermal', 'light', 'vision', 'hearing']) {
      delete d.flags[`sense_${o}`];
    }
    // ведомость поступков начинается с нуля: суд уже прочитал karmaTotal/soulXpLife,
    // оставшийся хвост двойной считался бы в судах следующих жизней (§11.2, §12)
    d.flags.karmaTotal = 0;
    d.flags.soulXpLife = 0;
    // немного живности у деревни, чтобы новая жизнь началась живо;
    // слепому новому телу — первая добыча вплотную (Дополнение к ТЗ, критерий №2)
    const w2 = World.fromData(d);
    for (let i = 0; i < 3; i++) w2.spawnCreature('slug', w2.spawnPosNear(d.pois.village, 3), d.gen);
    w2.spawnCreature('rabbit', w2.spawnPosNear(d.pois.village, 4), d.gen);
    w2.refreshStats();
    d.player.hp = w2.stats.maxHp;

    // 4. Место рождения: вероятностный ролл из судa (детерминирован сидом — §23).
    // Игрок не выбирает: мир сам реагирует на сущность души (Дополнение к ТЗ)
    const weights: Record<string, number> = {};
    for (const o of j.regionOdds ?? []) weights[o.id] = Math.max(1, o.pct);
    const region: RegionId = rollRegion(weights as Record<RegionId, number>, oldWorld.rng());
    w2.applyRegionBirth(region);
    this.soul.birthRegion = region;
    // Дополнение: каждая новая жизнь тоже начинается в родовой норке —
    // слепой комочек растёт в безопасности; выход открывает большой мир с места рождения
    w2.enterBirthNest();

    this.world = w2;
    this.judgment = null;
    this.chess = null;
    this.phase = 'life';
    this.rest = false;
    this.deathAnnounced = false;
    this.dyingT = -1;
    this.lastSavedDay = w2.clock.totalDays;
    this.save();
    const rdef = regionDef(region);
    this.toast(`Вы рождены: ${rdef.name} · ${rdef.difficulty} сложность. Мир жил без вас ${skipDays} дн.`, 'epic');
    this.notify();
  }

  soulLevelOfXp(): number { return soulLevelOf(this.soul.xp); }

  // ===== Magic Chess (§14) =====
  openChess() {
    if (!this.world) return;
    this.chess = createRaidChess(this.world);
    this.phase = 'chess'; // мировое время остановлено на время расстановки (§7)
    this.save();
    this.toast('Время остановилось. Расставьте оборону.', 'epic');
    this.notify();
  }

  placeChessUnit(defId: string, r: number, c: number) {
    if (!this.chess) return;
    if (chessPlace(this.chess, defId, r, c)) this.notify();
  }

  removeChessUnit(uid: number) {
    if (!this.chess) return;
    chessRemove(this.chess, uid);
    this.notify();
  }

  startChessBattle() {
    if (!this.chess) return;
    chessStart(this.chess);
    this.notify();
  }

  closeChess() {
    this.chess = null;
    this.phase = 'life';
    this.save();
    this.notify();
  }

  // ===== Ввод и действия игрока =====
  setInput(dx: number, dy: number, sprint = false) {
    if (this.world) {
      this.world.input.dx = dx;
      this.world.input.dy = dy;
      this.world.input.sprint = sprint;
    }
  }

  queueAttack() {
    if (this.world) this.world.input.attackQueued = true;
  }

  tryAbsorb(): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const ok = this.world.absorbNearest();
    if (ok) { this.save(); this.notify(); }
    else if (this.world.lastAbsorbReject) {
      // §5.2: причина отказа должна быть понятна игроку — тост
      this.toast(this.world.lastAbsorbReject, 'bad');
      this.world.lastAbsorbReject = null;
      this.notify();
    }
    return ok;
  }

  // ===== Развитие органов: выбор игрока (Дополнение — «что сейчас развивать») =====
  /** Вложить 5 биомассы тела напрямую в выбранный орган (конкурирует с генами). */
  investOrgan(organ: SenseOrgan): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const p = this.world.data.player;
    const cost = 5;
    if (p.biomass < cost) return false;
    p.biomass -= cost;
    const unlocked = investInOrgan(this.world.data.senses!, organ, cost, this.world.senseModsNow().thr);
    for (const o of unlocked) {
      this.world.data.flags[`sense_${o}`] = 1;
      this.world.pushLog(`Развилось чувство: ${ORGAN_INFO[o].name}.`, 'life');
      if (o === 'vision') this.world.data.flags.senseVisionUnlock = 1;
    }
    this.save();
    this.notify();
    return true;
  }

  /** Фокус развития: выбранный орган получает усиленный приток от любой пищи. */
  focusOrgan(organ: SenseOrgan | null) {
    if (!this.world) return;
    setFocus(this.world.data.senses!, organ);
    this.save();
    this.notify();
  }

  // ===== Пищеварение: режимы и выведение (ТЗ §6.5–§6.6) =====
  setDigestMode(mode: DigestMode): boolean {
    if (!this.world) return false;
    const ok = setMode(this.world.data.digestion!, mode);
    if (ok) { this.save(); this.notify(); }
    return ok;
  }

  /** Выведение: выбросить содержимое полости, потеряв часть массы (§6.6). */
  egestChamberAt(idx: number): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const dig = this.world.data.digestion!;
    const item = egestChamber(dig, idx);
    if (!item) return false;
    const p = this.world.data.player;
    p.energy = Math.max(0, p.energy - 1.5); // выведение стоит энергии
    dig.insoluble += item.mass * 0.05;
    this.world.pushLog(`Содержимое извергнуто: ${item.name}. Часть массы потеряна.`, 'life');
    this.save();
    this.notify();
    return true;
  }

  /** Нерастворимый остаток (§7): выбросить накопленное. */
  clearInsoluble(): boolean {
    if (!this.world) return false;
    const dig = this.world.data.digestion!;
    if (dig.insoluble <= 0) return false;
    dig.insoluble = 0;
    this.world.pushLog('Нерастворимый остаток выброшен наружу.', 'life');
    this.save();
    this.notify();
    return true;
  }

  // ===== Единое взаимодействие [E] / тап по объекту =====
  interact(): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const w = this.world;
    if (w.dungeonActive) {
      if (w.nearStairsUp) return this.ascendStairs();
      if (w.nearStairsDown) return this.descendStairs();
      if (w.nearCacheId != null) return this.takeCache();
      return this.tryAbsorb();
    }
    if (w.nearGateId) return this.enterDungeon(w.nearGateId);
    return this.tryAbsorb();
  }

  // ===== Подземелья-инстансы (Регион 2/3, §6) =====
  enterDungeon(id: DungeonId): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const def = DUNGEONS[id];
    const ok = this.world.enterDungeon(id);
    if (ok) {
      this.toast(`${def.name}. Глубина 1 из ${def.depths}. Ищите лестницу вниз.`, 'epic');
      this.save();
      this.notify();
    }
    return ok;
  }

  descendStairs(): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const ok = this.world.descendStairs();
    if (ok) {
      this.toast(`Глубина ${this.world.dun!.depth} из ${this.world.dungeonDef!.depths}.`, 'info');
      this.save();
      this.notify();
    }
    return ok;
  }

  ascendStairs(): boolean {
    if (!this.world || this.phase !== 'life') return false;
    // родовая норка: выход в большой мир — только через «созревание» (гейт роста)
    if (this.world.data.activeDungeon === 'birth_nest') {
      const ok = this.world.exitBirthNest();
      if (ok) {
        this.toast('Вы вышли из родовой норки в большой мир.', 'epic');
        this.save();
        this.notify();
      } else if (this.world.nearStairsUp) {
        const r = this.world.nestReady();
        this.toast(`Проход ещё тесен: нужно 8 биомассы или 2 чувства (${r.cur}/${r.need}).`, 'info');
      }
      return ok;
    }
    const wasActive = this.world.dungeonActive;
    const ok = this.world.ascendStairs();
    if (ok) {
      this.toast(wasActive && !this.world.dungeonActive ? 'Вы вышли на поверхность.' : 'Вы поднялись.', 'info');
      this.save();
      this.notify();
    }
    return ok;
  }

  takeCache(): boolean {
    if (!this.world || this.phase !== 'life') return false;
    const ok = this.world.takeCache();
    if (ok) { this.save(); this.notify(); }
    return ok;
  }

  setRest(on: boolean) {
    if (!this.world) return;
    if (on && this.world.dangerNear()) {
      this.toast('Рядом хищники — не до отдыха.', 'bad');
      this.rest = false;
      this.notify();
      return;
    }
    this.rest = on;
    this.notify();
  }

  clockLabel(): string {
    return this.world ? this.world.clock.label() : '';
  }

  clock(): WorldClock | null {
    return this.world?.clock ?? null;
  }
}

export { GENES };
