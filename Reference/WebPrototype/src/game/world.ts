// Слой Simulation: живой мир (ТЗ §6, §8, §10, §15).
// Три уровня симуляции в прототипе сведены к одному активному локационному уровню (A)
// плюс дневные статистические проверки (элементы уровня C) — по §20 прототип не грузит
// CPU постоянной симуляцией тысяч объектов.

import { mulberry32, clamp, dist, type Rng } from './rng';
import {
  WorldClock, TIME_SPEED, lifeStageOf, dailyMortality, ageModifiers,
} from './time';
import { SPECIES } from './creatures';
import { computeDerived, canInstall, GENES, nodeNextCost, NODE_PLAN, type DerivedStats } from './genes';
import { generateWorldMap, paintAtlas, type Poi } from './mapgen';
import {
  DUNGEONS, createDungeonState, karmaPathOf, villageStageOf,
  type DungeonDef,
} from './dungeon';
import type {
  Vec, SpeciesId, Creature, Npc, VillageState, KarmaEvent, HistoryEntry,
  Genome, DeathCause, DungeonId, DungeonState, FxEvent, WorldTrace,
} from './types';
import {
  createSensoryState, grantBiomass, grantOre, tickSenses, senseMods, ORGAN_INFO, DIET_FEED,
  ORE_CORE_THRESHOLD, ORE_OVERLOAD, IMPRINT_LIFE, ORE_YIELD,
  type SensoryState, type SenseMods, type SenseOrgan,
} from './senses';
import {
  QUESTS, questAccepted, questDone, questProgress, questsForGiver, giverLabel,
} from './quests';
import { REGIONS, regionDef, ensureRiftWorldData, type RegionDef } from './regions';
import type { RegionId } from './types';
import {
  regionAt, dungeonRegionId, worldRegionById, CRACK_SPOTS, CRACK_MAX_ORGANS,
} from './atlas';
import {
  seasonOf, SEASON_MODS, SEASON_LOG, SEASON_MIGRATIONS, seasonalSpawnBoost,
  type Season, type SeasonMods,
} from './seasons';
import {
  createDigestive, canAbsorb, putItem, tickDigestive, unlockModes,
  pressureOf, enzymeStrength, capacityOf, FOOD_KINDS, PREY_COMPOSITION, PREY_DEFAULT,
  MODE_INFO,
  type DigestiveState, type StoredItem, type DigestMode,
} from './digestion';

const ORE_YIELD_BY_SPECIES = ORE_YIELD;

/**
 * «Чаша Отголосов»: достроить географию на карте мира (новой или старой).
 * Идемпотентна: flags.atlasPainted после первой раскраски; POI/трещины добавляются при отсутствии.
 * Вызывается из конструктора World — покрывает и миграцию v4→v5, и новые игры.
 */
export function ensureAtlasWorldData(data: WorldData): void {
  if (!data.worldRegions) data.worldRegions = {};
  // игрок стоит в каком-то месте мира — оно открыто (дальше только ногами, п.9)
  const here = data.activeDungeon
    ? dungeonRegionId(data.activeDungeon)
    : regionAt(data.player.pos.x, data.player.pos.y).id;
  data.worldRegions[here] = 1;
  if (!data.flags.atlasPainted) {
    paintAtlas(data.mapJson, data.pois);
    data.flags.atlasPainted = 1;
  }
  // добыча в тайном кармане трещины: маленькие слизни получают раннее преимущество
  if (!data.flags.crackSeeded) {
    data.flags.crackSeeded = 1;
    for (const spot of CRACK_SPOTS) {
      for (let i = 0; i < 3; i++) {
        const a = (i / 3) * Math.PI * 2 + 0.5;
        data.creatures.push({
          id: data.nextId++, species: 'slug', pos: { x: spot.pocket.x + Math.cos(a) * 0.8, y: spot.pocket.y + Math.sin(a) * 0.8 },
          vel: { x: 0, y: 0 }, hp: 10, ageDays: 1, ai: 'wander', stateT: 0, atkCd: 0, hitFlash: 0, dead: false, gen: data.gen,
        });
      }
      data.creatures.push({
        id: data.nextId++, species: 'crystalslug', pos: { x: spot.pocket.x, y: spot.pocket.y + 0.9 },
        vel: { x: 0, y: 0 }, hp: 14, ageDays: 1, ai: 'wander', stateT: 0, atkCd: 0, hitFlash: 0, dead: false, gen: data.gen,
      });
    }
  }
  // страховка миграции: игрок не должен остаться стоять в скале/воде
  const p = data.player;
  const t = (x: number, y: number): string => {
    const { w, h, terrain } = data.mapJson;
    const xi = Math.floor(x), yi = Math.floor(y);
    if (xi < 0 || yi < 0 || xi >= w || yi >= h) return 'moss';
    return terrain[yi * w + xi];
  };
  if (t(p.pos.x, p.pos.y) === 'water' || t(p.pos.x, p.pos.y) === 'mountain') {
    outer:
    for (let r = 1; r <= 6; r++) {
      for (let a = 0; a < 12; a++) {
        const ang = (a / 12) * Math.PI * 2;
        const x = p.pos.x + Math.cos(ang) * r, y = p.pos.y + Math.sin(ang) * r;
        const tt = t(x, y);
        if (tt !== 'water' && tt !== 'mountain') { p.pos = { x, y }; break outer; }
      }
    }
  }
}

export interface PlayerState {
  species: SpeciesId;
  perks: string[];            // наследие души (гены, с которыми родились)
  pos: Vec; vel: Vec;
  hp: number; energy: number;
  ageDays: number; lifeNum: number;
  biomass: number;
  genome: Genome;
  knownGenes: string[];
  discoveredSpecies: SpeciesId[];
  kills: number; absorbedCount: number; synDiscovered: string[];
  mutations: number;
  atkCd: number; hitFlash: number; regenAcc: number; energyAcc: number;
  attackAnim: number;
  facing: number;             // -1 влево / 1 вправо (для отрисовки)
  flowT?: number;             // слизь течёт вдоль стены (обтекание — анимация канваса)
}

export interface WorldData {
  seed: number;
  minutes: number;
  dayTicks: number;
  mapJson: { w: number; h: number; terrain: string[]; deco: { id: number; kind: string; pos: Vec; r: number; seed: number }[] };
  pois: Record<string, Poi>;
  creatures: Creature[];
  nextId: number;
  npcs: Npc[];
  village: VillageState;
  player: PlayerState;
  log: HistoryEntry[];
  decisions: KarmaEvent[];
  flags: Record<string, number>;
  gen: number;
  deathCause: DeathCause | null;
  deathDay: number;
  dungeons?: Partial<Record<DungeonId, DungeonState>>; // инстансы подземелий (Регион 2/3, §6)
  activeDungeon?: DungeonId | null;                    // в каком подземелье сейчас игрок
  senses?: SensoryState;      // эволюция восприятия тела (Дополнение к ТЗ)
  traces?: WorldTrace[];      // отпечатки событий для духовного чутья (не много — кап 40)
  birthRegion?: RegionId;     // «Резонанс души»: регион рождения текущего тела
  worldRegions?: Record<string, number>; // «Чаша Отголосов»: открытые места (только ощущённые — п.9)
  digestion?: DigestiveState; // ТЗ §5–§7: полости/содержимое/режимы — внутренняя система тела
}

export interface PlayerStats extends DerivedStats {
  stage: string;
  mortality: number;          // риск смерти за день
}

const N = (x: number) => x; // no-op

export class World {
  data: WorldData;
  rng: Rng;
  clock: WorldClock;
  stats: PlayerStats;
  input = { dx: 0, dy: 0, sprint: false, attackQueued: false };
  nearCorpseId: number | null = null;
  nearNpcId: string | null = null;
  nearGateId: DungeonId | null = null; // у врат одного из подземелий (надземье)
  nearStairsUp = false;       // у лестницы вверх (в подземелье)
  nearStairsDown = false;     // у лестницы вниз (в подземелье)
  nearCacheId: number | null = null; // рядом запас грибницы / кристальная жила
  uiDirty = true;
  private lastSeason: Season | null = null;   // для обнаружения смены сезона (Этап 2)
  private seasonModsCache: SeasonMods | null = null;
  fx: FxEvent[] = [];         // визуальные события для канваса (не сохраняются, §17/§18)
  shakeT = 0;                 // остаток тряски экрана от полученного урона, сек

  // ===== Дополнение к ТЗ: эволюция восприятия — рантайм (не сохраняется, §18) =====
  senseTime = 0;              // часы восприятия (для угасания отпечатков/памяти)
  private senseAcc = 0;       // троттлинг сканирования
  imprints = new Map<string, { x: number; y: number; age: number; kind: 'solid' | 'entity'; diet?: string }>();
  senseMap = new Map<number, { organs: Set<SenseOrgan>; t: number; x: number; y: number }>();
  npcSense = new Map<string, { organs: Set<SenseOrgan>; t: number; x: number; y: number }>();
  private expS = new Set<number>();
  private expDunS = new Set<number>();
  // опасности регионов (рантайм, §18): сад — яд, разлом — бури
  private poisonAcc = 0;
  private poisonLogged = false;
  private stormT = 0;
  private stormCd = 22;
  private stormHit = 0;
  private stormSurvived = 0;

  /** Поставить визуальное событие канвасу (очередь ограничена — живучесть §18). */
  fxPush(e: FxEvent) {
    if (this.fx.length > 64) this.fx.shift();
    this.fx.push(e);
  }

  constructor(data: WorldData) {
    this.data = data;
    // Миграция старых сейвов v1: одиночное поле dungeon → реестр dungeons (Регион 2/3)
    if (data.dungeons == null) {
      const legacy = (data as unknown as { dungeon?: DungeonState | null }).dungeon ?? null;
      data.dungeons = legacy ? { catacombs: legacy } : {};
    }
    if (data.activeDungeon === undefined) data.activeDungeon = null;
    // существа старых сейвов не знали о принадлежности к подземелью — катакомбные
    for (const c of data.creatures) {
      if (c.dungeonFloor != null && !c.dungeonId) c.dungeonId = 'catacombs';
    }
    // миграция старой волчьей охоты в систему заданий (§13)
    if (data.flags.wolfQuestDone && !questDone(data.flags, 'wolves')) {
      data.flags.quest_accepted_wolves = 1;
      data.flags.quest_done_wolves = 1;
    } else if (data.flags.wolfQuestAccepted && !questAccepted(data.flags, 'wolves')) {
      data.flags.quest_accepted_wolves = 1;
      data.flags.quest_progress_wolves = Math.min(3, data.flags.wolvesKilled ?? 0);
    }
    if (!data.village.stage) data.village.stage = 'Хутор'; // старые сейвы: стадии не было (§15)
    // Дополнение к ТЗ: нормализация восприятия и следов (старые сейвы v1/v2)
    if (!data.senses) data.senses = createSensoryState();
    if (!data.traces) data.traces = [];
    // ТЗ §5–§7: система пищеварения — миграция старых сейвов: тело без содержимого
    if (!data.digestion) data.digestion = createDigestive();
    if (!data.birthRegion) data.birthRegion = 'quiet_cradle'; // старые сейвы рождались у посёлка
    // «Чаша Отголосов»: география мира + открытые места (миграция v4→v5 и страховка)
    ensureAtlasWorldData(data);
    for (const dun of Object.values(data.dungeons ?? {})) {
      if (dun && !dun.explored) dun.explored = dun.floors.map(() => [] as number[]);
    }
    this.rebuildExplored();
    this.rng = mulberry32(data.seed ^ (data.dayTicks * 2654435761));
    this.clock = new WorldClock(data.minutes);
    this.stats = this.recomputeStats();
    // отравленный сейв: герой вне проходимой зоны — лечим сразу при загрузке
    this.healPlayerPosition();
  }

  // ===== Резонанс души: регион рождения (Дополнение к ТЗ) =====
  get regionDef(): RegionDef {
    return regionDef(this.data.birthRegion);
  }

  // ===== «Чаша Отголосов»: открытые места мира (карта-восприятие) =====
  lastDiscoveredRegion: string | null = null; // рантайм: для тоста движка (не сохраняется, §18)
  private crackHintT = 0;                     // троттлинг подсказки про узкую трещину

  /** Открыт ли мировой регион (п.9: ничего не раскрывается до первого ощущения). */
  isRegionDiscovered(id: string): boolean {
    return this.data.worldRegions?.[id] === 1;
  }

  /** Текущий мировой регион игрока: поверхностный по точке или подземный по слою. */
  currentWorldRegionId(): string {
    if (this.dungeonActive && this.dungeonId) return dungeonRegionId(this.dungeonId);
    return regionAt(this.data.player.pos.x, this.data.player.pos.y).id;
  }

  currentWorldRegionName(): string {
    return worldRegionById(this.currentWorldRegionId())?.name ?? 'Неизвестные земли';
  }

  /** Открыть регион (ногами/чутьём — никаких утечек через UI): лог + тост-флаг. */
  private discoverRegion(id: string): void {
    if (this.isRegionDiscovered(id)) return;
    const def = worldRegionById(id);
    if (!def || def.sealed) return; // запечатанные Глубины не открываются (Этап 4)
    if (!this.data.worldRegions) this.data.worldRegions = {};
    this.data.worldRegions[id] = 1;
    this.lastDiscoveredRegion = id;
    this.pushLog(`Вы открыли место: ${def.name}. ${def.ecology}`, 'world');
  }

  /** Посекундная проверка: где мы и что открывается (обновление карты-восприятия). */
  private updateAtlasDiscovery(): void {
    this.discoverRegion(this.currentWorldRegionId());
  }

  // ===== Пищеварение (ТЗ §5–§7) — параметры тела =====
  /** AbsorptionCapacity — максимальная масса содержимого (§5.5). */
  absorbCapacity(): number {
    const p = this.data.player;
    return capacityOf(p.biomass, p.genome.slots);
  }
  /** Сила ферментов: эволюция и геном (§6.4). */
  digestiveEnzyme(): number {
    return enzymeStrength(this.data.player.genome.installed.length, this.data.senses?.biomassTotal ?? 0);
  }
  /** InternalPressure 0..1 — для HUD и анимации мембраны (§5.5). */
  digestivePressure(): number {
    return pressureOf(this.data.digestion!, this.absorbCapacity());
  }
  digestMode(): DigestMode {
    return this.data.digestion!.mode;
  }
  /** Причина последнего отказа в захвате — движок покажет тост (рантайм, §18). */
  lastAbsorbReject: string | null = null;

  /** Движок забирает событие открытия для тоста (consume). */
  consumeRegionDiscovery(): string | null {
    const id = this.lastDiscoveredRegion;
    this.lastDiscoveredRegion = null;
    return id;
  }

  /** Применить условия рождения: точка, гарантированная первая пища (п.8), спавн-план региона. */
  applyRegionBirth(regionId: RegionId): boolean {
    const def = REGIONS[regionId];
    if (!def) return false;
    this.data.birthRegion = regionId;
    const pois = this.data.pois;
    if (regionId === 'soul_rift' && !pois.rift) {
      ensureRiftWorldData(this.data); // страховка старых карт без Разлома — детерминированная достройка
    }
    let pt = def.spawnPoint(pois);
    if (!this.isWalkable(pt.x, pt.y)) {
      for (let i = 0; i < 28; i++) {
        const a = this.rng() * Math.PI * 2;
        const r = this.rng.range(0.8, 3.2);
        const x = pt.x + Math.cos(a) * r, y = pt.y + Math.sin(a) * r;
        if (this.isWalkable(x, y)) { pt = { x, y }; break; }
      }
    }
    const p = this.data.player;
    p.pos = { ...pt };
    p.vel = { x: 0, y: 0 };
    p.facing = 1;
    for (const [sp, n] of def.spawnPlan) {
      for (let i = 0; i < n; i++) {
        this.spawnNearPos(pt, sp, i === 0 && sp === 'slug' ? 1.0 : 1.2, i === 0 && sp === 'slug' ? 2.0 : 3.4);
      }
    }
    // смена слоя рождения: отпечатки и память чувств недействительны
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.pushLog(`Резонанс души привёл вас: ${def.name} (${def.difficulty} сложность).`, 'life');
    return true;
  }

  /** Посекундный тик опасностей региона (п.7): ядовитые споры Сада, бури Разлома. */
  private updateRegion(dt: number) {
    const def = this.regionDef;
    const p = this.data.player;
    if (def.hazard === 'poison_spores') {
      const grove = this.data.pois.mushroom_grove;
      const protectedBy = p.genome.installed.includes('spore_veil') || p.genome.installed.includes('spore_cloud');
      const inGrove = dist(p.pos.x, p.pos.y, grove.pos.x, grove.pos.y) < grove.r + 1.5;
      if (!protectedBy && inGrove) {
        this.poisonAcc += dt;
        if (!this.poisonLogged) {
          this.poisonLogged = true;
          this.pushLog('Ядовитые споры Сада жалят незащищённое тело (споровая защита спасла бы).', 'life');
        }
        if (this.poisonAcc >= 8) {
          this.poisonAcc = 0;
          p.hp -= 1;
          p.hitFlash = 1;
          this.fxPush({ kind: 'dmg', x: p.pos.x, y: p.pos.y, text: '-1', color: '150,220,120' });
          if (p.hp <= 0) this.onPlayerDeath('killed');
        }
      } else {
        this.poisonAcc = 0;
        this.poisonLogged = false;
      }
    }
    if (def.hazard === 'rift_storm') {
      const rift = this.data.pois.rift;
      const nearRift = rift ? dist(p.pos.x, p.pos.y, rift.pos.x, rift.pos.y) < 12 : false;
      if (this.stormT > 0) {
        this.stormT = Math.max(0, this.stormT - dt);
        this.data.flags.riftStorm = 1; // канвас рисует бурю
        if (nearRift) {
          this.stormHit += dt;
          if (this.stormHit >= 2) {
            this.stormHit = 0;
            p.hp -= 1;
            p.hitFlash = 1;
            this.fxPush({ kind: 'dmg', x: p.pos.x, y: p.pos.y, text: '-1', color: '190,120,235' });
            if (p.hp <= 0) this.onPlayerDeath('killed');
          }
        }
        if (this.stormT === 0) {
          delete this.data.flags.riftStorm;
          this.stormSurvived++;
          if (this.stormSurvived >= 3) this.data.flags.riftStormSurvived = 1; // глубина души в суде
          this.pushLog('Буря Разлома стихла. Воздух звенит остаточной магией.', 'world');
        }
      } else {
        this.stormCd -= dt;
        if (this.stormCd <= 0) {
          this.stormCd = 25;
          this.stormT = 5;
          this.stormHit = 0;
          this.pushLog('БУРЯ РАЗЛОМА: реальность трещит, магические потоки выжигают всё живое.', 'world');
        }
      }
    }
  }

  // ===== Фабрики =====
  static newGame(seed: number, lifeNum: number, perkGene: string | null, soulKarma: number, lifeDaysBonus = 0, statMods: Partial<Record<'maxHp' | 'atk' | 'def' | 'speed' | 'perception', number>> = {}): World {
    const { map, pois } = generateWorldMap(seed);
    const v = pois.village.pos;
    const data: WorldData = {
      seed,
      minutes: 7 * 60, // рассвет первого дня
      dayTicks: 0,
      mapJson: map,
      pois,
      creatures: [],
      nextId: 1,
      npcs: [
        { id: 'mirra', name: 'Старейшина Мирра', role: 'старейшина', pos: { x: v.x - 2.4, y: v.y - 1.3 }, ageDays: 340, lifespan: 480, alive: true, attitude: 50, dialogue: 'hello' },
        { id: 'dar', name: 'Охотник Дар', role: 'охотник', pos: { x: v.x + 2.6, y: v.y - 1.0 }, ageDays: 96, lifespan: 300, alive: true, attitude: 45, dialogue: 'hello' },
        { id: 'klumba', name: 'Хранительница Клумба', role: 'фермер', pos: { x: v.x - 4.0, y: v.y + 0.9 }, ageDays: 150, lifespan: 350, alive: true, attitude: 45, dialogue: 'hello' },
      ],
      village: { name: 'Опушка', stage: 'Хутор', population: 11, food: 40, stability: 60, military: 25, raidCdDays: 2, raidsSurvived: 0, ruinChanceT: 0 },
      player: {
        species: 'slug', perks: [],
        pos: { x: v.x - 6.5, y: v.y + 6.5 }, vel: { x: 0, y: 0 },
        hp: 34, energy: 4,
        ageDays: 0.4, lifeNum,
        biomass: 0,
        genome: { installed: [], capacity: 4, slots: 3, nodes: 0 },
        knownGenes: [],
        discoveredSpecies: [],
        kills: 0, absorbedCount: 0, synDiscovered: [],
        mutations: 0,
        atkCd: 0, hitFlash: 0, regenAcc: 0, energyAcc: 0, attackAnim: 0, facing: 1,
      },
      log: [{ day: 0, year: 1, text: 'Вы просыпаетесь слизнем у Леса Первого Дыхания. Мир уже был здесь до вас.', kind: 'life' }],
      decisions: [],
      flags: { soulBaseKarma: soulKarma }, // путь кармы учитывает прошлое души (§12)
      gen: 1,
      deathCause: null,
      deathDay: 0,
    };
    const w = new World(data);
    if (perkGene && GENES[perkGene]) {
      w.data.player.perks.push(perkGene);
      if (!w.data.player.knownGenes.includes(perkGene)) w.data.player.knownGenes.push(perkGene);
    }
    w.stats.lifeDays += lifeDaysBonus;
    for (const [k, val] of Object.entries(statMods)) {
      (w.stats as unknown as Record<string, number>)[k] += val as number;
    }
    w.data.player.hp = w.stats.maxHp;
    w.data.player.energy = w.stats.energyMax;
    w.spawnInitialPops();
    w.spawnGuards();
    // «Резонанс души»: первая жизнь — нейтральная душа в благоприятной среде (п.5 баланса);
    // точка старта + гарантированная первая пища вплотную (слепой старт, критерий №2)
    w.applyRegionBirth('quiet_cradle');
    return w;
  }

  static fromData(data: WorldData): World {
    return new World(data);
  }

  // ===== Производные характеристики игрока =====
  recomputeStats(): PlayerStats {
    const p = this.data.player;
    const d = computeDerived(p.genome);
    const am = ageModifiers(p.ageDays, d.lifeDays);
    const stage = lifeStageOf(p.ageDays, d.lifeDays);
    const s: PlayerStats = {
      ...d,
      maxHp: Math.max(8, Math.round(d.maxHp * am.hpMul - p.mutations * 2)),
      atk: Math.max(1, d.atk * am.atkMul),
      speed: d.speed * am.speedMul,
      perception: d.perception,
      stage,
      mortality: dailyMortality(p.ageDays, d.lifeDays),
    };
    // перки наследия
    for (const perk of p.perks) {
      const g = GENES[perk];
      if (g) for (const [k, v] of Object.entries(g.mods)) {
        const key = k as keyof DerivedStats;
        if (typeof s[key] === 'number') (s[key] as number) = (s[key] as number) + (v as number) * 0.6;
      }
    }
    return s;
  }

  refreshStats() { this.stats = this.recomputeStats(); }

  // ===== Сезоны года (Этап 2 карты мира): экология дышит по календарю =====
  /** Текущий сезон. Родовая норка — мини-вселенная: туда сезоны не доходят (тепло кладки). */
  get season(): Season {
    return seasonOf(this.clock.totalDays);
  }
  /** Модификаторы сезона; в норке и подземельях — нейтральные (свой микроклимат). */
  get seasonMods(): SeasonMods {
    if (this.seasonModsCache) return this.seasonModsCache;
    const neutral: SeasonMods = { birthMul: 1, aggressionAdd: 0, dietMul: 1, villageFoodDelta: 0, fireflyMul: 1, hint: 'Микроклимат подземелья: сезонам сюда не дотянуться' };
    const m = this.data.activeDungeon ? neutral : SEASON_MODS[this.season];
    this.seasonModsCache = m;
    return m;
  }
  /** Сброс кеша сезонных модификаторов (после смены сезона/слоя). */
  invalidateSeasonCache() { this.seasonModsCache = null; }

  // ===== Спавн =====
  spawnPosNear(poi: Poi, rMin = 0.5, rMax?: number): Vec {
    const rMaxE = rMax ?? poi.r;
    for (let i = 0; i < 30; i++) {
      const a = this.rng() * Math.PI * 2;
      const r = this.rng.range(rMin, rMaxE);
      const x = poi.pos.x + Math.cos(a) * r, y = poi.pos.y + Math.sin(a) * r * 0.85;
      if (this.isWalkable(x, y)) return { x, y };
    }
    return { x: poi.pos.x, y: poi.pos.y };
  }

  spawnCreature(species: SpeciesId, pos: Vec, gen: number, opts: Partial<Creature> = {}): Creature {
    const def = SPECIES[species];
    const c: Creature = {
      id: this.data.nextId++,
      species,
      pos: { ...pos },
      vel: { x: 0, y: 0 },
      hp: def.base.maxHp,
      ageDays: this.rng.range(0.5, def.lifespan * 0.4),
      ai: 'wander',
      stateT: this.rng.range(0, 2),
      atkCd: 0,
      hitFlash: 0,
      dead: false,
      gen,
    };
    Object.assign(c, opts);
    c.hp = opts.hp ?? def.base.maxHp;
    this.data.creatures.push(c);
    return c;
  }

  private spawnGuards() {
    const v = this.data.pois.village.pos;
    for (let i = 0; i < 2; i++) {
      this.spawnCreature('guard', { x: v.x + (i ? 1.5 : -1.5), y: v.y + 2.8 }, 1, { ai: 'guard', wanderHome: { x: v.x + (i ? 1.5 : -1.5), y: v.y + 2.8 } });
    }
  }

  /** Спавн существа в проходимой точке рядом с заданной позицией (первая добыча слепого слизня). */
  spawnNearPos(pos: Vec, species: SpeciesId, rMin = 0.8, rMax = 2.2): Creature {
    for (let i = 0; i < 24; i++) {
      const a = this.rng() * Math.PI * 2;
      const r = this.rng.range(rMin, rMax);
      const x = pos.x + Math.cos(a) * r;
      const y = pos.y + Math.sin(a) * r;
      if (this.isWalkable(x, y)) return this.spawnCreature(species, { x, y }, this.data.gen);
    }
    return this.spawnCreature(species, { ...pos }, this.data.gen);
  }

  private spawnInitialPops() {
    const plan: [SpeciesId, Poi][] = [];
    const p = this.data.pois;
    for (let i = 0; i < 6; i++) plan.push(['slug', p.village]);
    for (let i = 0; i < 4; i++) plan.push(['beetle', p.village]);
    plan.push(['beetle', p.pond]);
    for (let i = 0; i < 4; i++) plan.push(['firefly', p.pond]);
    for (let i = 0; i < 5; i++) plan.push(['rabbit', p.village]);
    for (let i = 0; i < 3; i++) plan.push(['rabbit', p.burrow]);
    for (let i = 0; i < 3; i++) plan.push(['deer', p.village]);
    for (let i = 0; i < 3; i++) plan.push(['wolf', p.burrow]);
    for (let i = 0; i < 2; i++) plan.push(['toad', p.pond]);
    for (let i = 0; i < 3; i++) plan.push(['spider', p.mushroom_grove]);
    for (let i = 0; i < 4; i++) plan.push(['mushling', p.mushroom_grove]);
    for (let i = 0; i < 2; i++) plan.push(['crystalslug', p.crystal_hollow]);
    for (let i = 0; i < 2; i++) plan.push(['shardling', p.crystal_hollow]);
    plan.push(['spirit', p.stone_circle]);
    for (const [sp, poi] of plan) this.spawnCreature(sp, this.spawnPosNear(poi, 0.5), 1);
    // вожак стаи пока не заспавнен — появится по сюжету
  }

  popCount(species: SpeciesId): number {
    // популяции считает только для надземного слоя (§6 уровень C)
    return this.data.creatures.filter((c) => c.species === species && !c.dead && c.dungeonFloor == null).length;
  }

  // ===== Проходимость =====
  /** Активный контекст карты: подземелье перекрывает надземье (§6: инстанс-регион). */
  /** id подземелья, в котором сейчас игрок (null — надземье). */
  get dungeonId(): DungeonId | null {
    return this.data.activeDungeon ?? null;
  }

  get dun(): DungeonState | null {
    const id = this.dungeonId;
    return id ? this.data.dungeons?.[id] ?? null : null;
  }

  get dungeonDef(): DungeonDef | null {
    const id = this.dungeonId;
    return id ? DUNGEONS[id] : null;
  }

  get dungeonActive(): boolean {
    const d = this.dun;
    return !!(d && d.active);
  }

  /** Существа активного слоя: надземные — вне подземелий, глубинные — только своей глубины и своего инстанса. */
  activeCreatures(): Creature[] {
    const id = this.dungeonId;
    const depth = this.dungeonActive ? this.dun!.depth : 0;
    return this.data.creatures.filter((c) => (c.dungeonFloor ?? 0) === depth && (depth === 0 || c.dungeonId === id));
  }

  isWalkable(x: number, y: number): boolean {
    if (this.dungeonActive) {
      const f = this.dun!.floors[this.dun!.depth - 1];
      const xi = Math.floor(x), yi = Math.floor(y);
      if (xi < 0 || yi < 0 || xi >= f.w || yi >= f.h) return false;
      return f.terrain[yi * f.w + xi] !== 'rock';
    }
    const { w, h, terrain } = this.data.mapJson;
    const xi = Math.floor(x), yi = Math.floor(y);
    if (xi < 0 || yi < 0 || xi >= w || yi >= h) return false;
    const t = terrain[yi * w + xi];
    if (t === 'water' || t === 'mountain') return false;
    for (const d of this.data.mapJson.deco) {
      if (d.r <= 0) continue;
      if (d.kind === 'crack' && d.r >= 0.5) {
        // тайная трещина: узкий проход только для малого тела (п.3 карты мира —
        // маленький слизень пролезает туда, куда не пролезает эволюционировавшая форма)
        if (dist(x, y, d.pos.x, d.pos.y) < d.r * 0.8) {
          if (this.data.player.genome.installed.length > CRACK_MAX_ORGANS) {
            if (this.crackHintT <= 0) {
              this.crackHintT = 6;
              this.pushLog('Трещина слишком узка для вашего взрослого тела. Малый слизень прошёл бы… (нужно не больше 1 установленного гена)', 'life');
            }
            return false;
          }
          continue; // малое тело проскальзывает сквозь трещину
        }
        continue;
      }
      if (dist(x, y, d.pos.x, d.pos.y) < d.r * 0.8) return false;
    }
    return true;
  }

  private moveWithCollision(pos: Vec, dx: number, dy: number, r = 0.28) {
    const tryX = pos.x + dx;
    if (this.walkableRadius(tryX, pos.y, r)) pos.x = tryX;
    const tryY = pos.y + dy;
    if (this.walkableRadius(pos.x, tryY, r)) pos.y = tryY;
  }

  private walkableRadius(x: number, y: number, r: number): boolean {
    if (!this.isWalkable(x, y)) return false;
    // проверка окружности по 4 точкам
    for (const [ox, oy] of [[r, 0], [-r, 0], [0, r], [0, -r]]) {
      if (!this.isWalkable(x + ox, y + oy)) return false;
    }
    return true;
  }

  /**
   * Самолечение позиции героя: если сейв/откат/сбой оставил игрока вне проходимой
   * зоны (вне границ слоя, в скале, в пустоте) — вытягиваем на заведомо безопасную точку.
   * Герой обязан быть всегда reachable и видимым; иначе — «пропавший герой».
   */
  healPlayerPosition(): boolean {
    const p = this.data.player;
    if (this.isWalkable(p.pos.x, p.pos.y)) return false;
    const dun = this.dun;
    if (this.dungeonActive && dun) {
      const f = dun.floors[dun.depth - 1];
      // родовая норка — комочек у кладки; прочие подземелья — у лестницы вверх
      const safe = this.data.activeDungeon === 'birth_nest'
        ? { x: f.down.x + 0.5, y: f.down.y + 0.9 }
        : { x: f.up.x + 0.5, y: f.up.y + 0.5 };
      p.pos = { ...safe };
    } else {
      // спираль по ближайшим проходимым тайлам от текущей точки
      const cx = Math.floor(p.pos.x), cy = Math.floor(p.pos.y);
      const { w, h } = this.data.mapJson;
      let found: Vec | null = null;
      for (let r = 0; r < 14 && !found; r++) {
        for (let dy = -r; dy <= r && !found; dy++) {
          for (let dx = -r; dx <= r && !found; dx++) {
            if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue;
            const tx = cx + dx, ty = cy + dy;
            if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
            if (this.isWalkable(tx + 0.5, ty + 0.5)) found = { x: tx + 0.5, y: ty + 0.5 };
          }
        }
      }
      if (!found) {
        const v = this.data.pois.village;
        found = { x: v.pos.x, y: v.pos.y };
      }
      p.pos = found;
    }
    p.vel = { x: 0, y: 0 };
    this.pushLog('Поток вытолкнул вас на твёрдую поверхность — тело снова на месте.', 'life');
    return true;
  }

  // ===== Главный шаг симуляции =====
  update(dtReal: number, speedName: keyof typeof TIME_SPEED) {
    if (this.data.deathCause) return;
    // страховка «пропавшего героя»: невалидная позиция чинится до любого шага симуляции
    this.healPlayerPosition();
    const speed = TIME_SPEED[speedName] ?? TIME_SPEED.explore;
    // Мировое время — авторитетно: течёт по реальному dt даже при низком FPS (§7).
    // Движение/AI — с жёстким clamp dt, чтобы физика не «протыкала» стены на слабых устройствах (§19).
    const dtSim = Math.min(dtReal, 0.05);
    this.shakeT = Math.max(0, this.shakeT - dtReal);
    const crossedDays = this.clock.advance(dtReal * speed);
    this.data.minutes = this.clock.minutes;
    this.data.dayTicks = this.clock.dayTickCounter;
    if (crossedDays > 0) this.dailyTick(crossedDays);

    const p = this.data.player;
    // Скорость движения игрока не ускоряется во время fast (отдых = стоим на месте у костра)
    if (speedName !== 'fast') {
      this.updatePlayer(dtSim);
      for (const c of this.activeCreatures()) this.updateCreature(c, dtSim);
    } else {
      p.vel.x = 0; p.vel.y = 0;
      // быстрый отдых: регенерация усиленная
      p.hp = Math.min(this.stats.maxHp, p.hp + this.stats.hpRegen * dtReal * 5 + 2 * dtReal);
      p.energy = Math.min(this.stats.energyMax, p.energy + dtReal * 2);
      for (const c of this.activeCreatures()) this.updateCreature(c, dtSim * 0.5);
    }
    // переваривание — фоновый биопроцесс: идёт и в покое (§14.2 — не блокирует движение)
    this.tickDigestion(dtSim);
    this.updateProximity();
    this.updateSenses(dtReal);
    this.updateRegion(dtReal);
    this.crackHintT = Math.max(0, this.crackHintT - dtReal);
    this.updateAtlasDiscovery();
    this.uiDirty = true;
  }

  private updatePlayer(dt: number) {
    const p = this.data.player;
    const s = this.stats;
    p.ageDays += 0; // возраст растёт только дневными тиками (§10 — проверки по игровому времени)
    // §5.5: перегруженное тело движется медленнее (InternalPressure > 0.6)
    const pr = this.digestivePressure();
    const loadPenalty = pr > 0.6 ? 1 - (pr - 0.6) * 0.45 : 1;
    const speed = s.speed * loadPenalty * (this.input.sprint && p.energy > 0.5 ? 1.45 : 1);
    const len = Math.hypot(this.input.dx, this.input.dy);
    // Инерция слизня (§9.1 — механика слизи): тело разгоняется вязко и скользит после
    // отпускания клавиш, а не телепортируется. Торможение чуть резче разгона.
    const accel = len > 0.01 ? 9.5 : 13;
    const blend = Math.min(1, dt * accel);
    if (len > 0.01) {
      const nx = this.input.dx / len, ny = this.input.dy / len;
      p.vel.x += (nx * speed - p.vel.x) * blend;
      p.vel.y += (ny * speed - p.vel.y) * blend;
      if (Math.abs(nx) > 0.1) p.facing = nx > 0 ? 1 : -1;
      if (this.input.sprint && p.energy > 0.5) p.energy = Math.max(0, p.energy - dt * 1.6);
    } else {
      p.vel.x += (0 - p.vel.x) * blend;
      p.vel.y += (0 - p.vel.y) * blend;
      if (Math.hypot(p.vel.x, p.vel.y) < 0.02) { p.vel.x = 0; p.vel.y = 0; }
    }
    if (Math.hypot(p.vel.x, p.vel.y) > 0.005) {
      const beforeX = p.pos.x, beforeY = p.pos.y;
      this.moveWithCollision(p.pos, p.vel.x * dt, p.vel.y * dt);
      // Обтекание препятствия (механика жидкости): упёрлись — слизь течёт вдоль стены,
      // выбирая тангенс ближе к желаемому направлению, а не скользит вслепую
      const wantX = p.vel.x * dt, wantY = p.vel.y * dt;
      const blockedX = Math.abs(wantX) > 1e-5 && Math.abs(p.pos.x - (beforeX + wantX)) > 1e-6;
      const blockedY = Math.abs(wantY) > 1e-5 && Math.abs(p.pos.y - (beforeY + wantY)) > 1e-6;
      if ((blockedX || blockedY) && len > 0.01) {
        const sp = Math.max(0.2, Math.hypot(p.vel.x, p.vel.y));
        let nx = 0, ny = 0;
        if (blockedX && blockedY) { nx = -Math.sign(p.vel.x); ny = -Math.sign(p.vel.y); }
        else if (blockedX) { nx = -Math.sign(p.vel.x); }
        else { ny = -Math.sign(p.vel.y); }
        const nl = Math.max(0.001, Math.hypot(nx, ny)); nx /= nl; ny /= nl;
        const dxn = this.input.dx / Math.max(0.001, len), dyn = this.input.dy / Math.max(0.001, len);
        const d1 = -ny * dxn + nx * dyn;
        const d2 = ny * dxn + -nx * dyn;
        const tx = d1 >= d2 ? -ny : ny;
        const ty = d1 >= d2 ? nx : -nx;
        const flow = Math.min(1, dt * 7);
        p.vel.x += (tx * sp * 0.92 - p.vel.x) * flow;
        p.vel.y += (ty * sp * 0.92 - p.vel.y) * flow;
        p.flowT = 0.35; // канвас вытягивает тело вдоль стены
      } else {
        p.flowT = Math.max(0, (p.flowT ?? 0) - dt);
      }
    }
    // энергия и регенерация
    p.energyAcc += dt;
    if (p.energyAcc > 0.8) {
      p.energyAcc = 0;
      p.energy = Math.min(s.energyMax, p.energy + 0.6);
    }
    p.regenAcc += dt;
    if (p.regenAcc > 1) {
      p.regenAcc = 0;
      let regen = s.hpRegen;
      const fast = s.synergies.some((x) => x.id === 'fast_regen');
      if (fast) regen *= 3;
      // мицелиальная связь: в Катакомбах грибница лечит вас (Регион 2)
      if (this.dungeonActive && p.genome.installed.includes('mycelial_bond')) regen *= 1.8;
      p.hp = Math.min(s.maxHp, p.hp + regen);
    }
    p.atkCd = Math.max(0, p.atkCd - dt);
    p.hitFlash = Math.max(0, p.hitFlash - dt * 3);
    p.attackAnim = Math.max(0, p.attackAnim - dt * 4);

    // атака по кнопке
    if (this.input.attackQueued) {
      this.input.attackQueued = false;
      this.playerAttack();
    }

    // токсичная аура (синергия)
    if (s.synergies.some((x) => x.special === 'toxicAura')) {
      for (const c of this.activeCreatures()) {
        if (c.dead || c.species === 'guard') continue;
        if (dist(c.pos.x, c.pos.y, p.pos.x, p.pos.y) < 1.7) {
          c.hp -= 1.6 * dt;
          c.slowT = 0.5;
          if (c.hp <= 0) this.killCreature(c, 'aura');
        }
      }
    }
  }

  private updateProximity() {
    const p = this.data.player;
    this.nearCorpseId = null;
    let best = 1.2;
    for (const c of this.activeCreatures()) {
      if (c.dead && !c.absorbed && (c.corpseT ?? 0) > 0) {
        const d = dist(c.pos.x, c.pos.y, p.pos.x, p.pos.y);
        if (d < best) { best = d; this.nearCorpseId = c.id; }
      }
    }
    this.nearNpcId = null;
    this.nearGateId = null;
    this.nearStairsUp = false;
    this.nearStairsDown = false;
    this.nearCacheId = null;
    if (this.dungeonActive) {
      const dun = this.dun!;
      const f = dun.floors[dun.depth - 1];
      if (dun.depth < dun.floors.length && dist(f.down.x, f.down.y, p.pos.x, p.pos.y) < 1.25) this.nearStairsDown = true;
      if (dist(f.up.x, f.up.y, p.pos.x, p.pos.y) < 1.25) this.nearStairsUp = true;
      let bestC = 1.2;
      for (const cache of dun.caches) {
        if (cache.taken || cache.depth !== dun.depth) continue;
        const d = dist(cache.pos.x, cache.pos.y, p.pos.x, p.pos.y);
        if (d < bestC) { bestC = d; this.nearCacheId = cache.id; }
      }
    } else {
      for (const def of Object.values(DUNGEONS)) {
        const gate = def.gatePos(this.data.pois);
        if (dist(gate.x, gate.y, p.pos.x, p.pos.y) < 1.5) { this.nearGateId = def.id; break; }
      }
      let bestN = 1.3;
      for (const n of this.data.npcs) {
        if (!n.alive) continue;
        const d = dist(n.pos.x, n.pos.y, p.pos.x, p.pos.y);
        if (d < bestN) { bestN = d; this.nearNpcId = n.id; }
      }
    }
  }

  // ===== Эволюция восприятия (Дополнение к ТЗ) =====
  /** Ядро — свойство души, зеркалируется в флагах мира (переживает смерть). */
  get coreActive(): boolean {
    return this.data.flags.senseCore === 1;
  }

  senseModsNow(): SenseMods {
    const p = this.data.player;
    const lifeDays = Math.max(1, this.stats.lifeDays);
    const sm = senseMods(
      p.genome.installed,
      p.ageDays / lifeDays,
      this.karmaPath.name,
      this.data.flags.spiritMemory ?? 0,
      this.data.flags.traitDepth ?? 0, // глубина души ускоряет рецепторы, но не даёт зрения (п.6)
    );
    sm.spiritR *= this.regionDef.mods.spiritRmul; // регион рождения влияет на чутьё
    if (this.data.flags.riftStorm) sm.spiritR *= 1.5; // буря Разлома обостряет духовное зрение
    return sm;
  }

  visionRadius(mods: SenseMods): number {
    return this.dungeonActive ? mods.visionR * 0.62 : mods.visionR;
  }

  isTileExplored(tx: number, ty: number): boolean {
    if (this.dungeonActive) {
      const f = this.dun!.floors[this.dun!.depth - 1];
      return this.expDunS.has(ty * f.w + tx);
    }
    return this.expS.has(ty * this.data.mapJson.w + tx);
  }

  private markExplored(tx: number, ty: number) {
    if (this.dungeonActive) {
      const dun = this.dun!;
      const f = dun.floors[dun.depth - 1];
      if (tx < 0 || ty < 0 || tx >= f.w || ty >= f.h) return;
      const i = ty * f.w + tx;
      if (this.expDunS.has(i)) return;
      this.expDunS.add(i);
      dun.explored![dun.depth - 1].push(i);
    } else {
      const { w, h } = this.data.mapJson;
      if (tx < 0 || ty < 0 || tx >= w || ty >= h) return;
      const i = ty * w + tx;
      if (this.expS.has(i)) return;
      this.expS.add(i);
      this.data.senses!.explored.push(i);
    }
  }

  private rebuildExplored() {
    this.expS = new Set(this.data.senses?.explored ?? []);
    const dun = this.dun;
    this.expDunS = new Set(dun && dun.explored ? dun.explored[dun.depth - 1] ?? [] : []);
  }

  private addImprint(x: number, y: number, kind: 'solid' | 'entity', diet?: string) {
    const key = `${Math.round(x * 2)}:${Math.round(y * 2)}`;
    const cur = this.imprints.get(key);
    if (cur) {
      cur.age = 0;
      if (diet) cur.diet = diet;
      return;
    }
    if (this.imprints.size > 240) {
      let oldest: string | null = null;
      let oa = -1;
      for (const [k, v] of this.imprints) if (v.age > oa) { oa = v.age; oldest = k; }
      if (oldest) this.imprints.delete(oldest);
    }
    this.imprints.set(key, { x, y, age: 0, kind, diet });
  }

  /** Отпечаток события для духовного чутья (п.10) — хранится в мире, переживает смерть. */
  private addTrace(x: number, y: number, kind: WorldTrace['kind']) {
    const tr = this.data.traces!;
    tr.push({ x, y, day: this.clock.totalDays, kind });
    if (tr.length > 40) tr.shift();
  }

  /** Усвоение магической руды (п.8): до ядра — счётчик, после — энергия и риск перегрузки. */
  private gainOre(n: number) {
    const sn = this.data.senses!;
    grantOre(sn, n, this.coreActive);
    this.pushLog(`Усвоена магическая руда: +${n} (всего ${sn.ore}).`, 'life');
    if (!this.coreActive && sn.ore >= ORE_CORE_THRESHOLD) {
      this.data.flags.senseCore = 1;
      this.data.flags.senseCoreJust = 1; // движок покажет тост и запомнит ядро в душе
      this.pushLog('Из усвоенной руды в теле сплетается резонансное ядро — духовное чутьё пробуждается.', 'life');
    }
  }

  /** Существо/труп когда-либо ощущалось хоть одним органом (запрет утечки информации, п.7). */
  isEntitySensed(id: number): boolean {
    return this.senseMap.has(id);
  }

  isNpcSensed(id: string): boolean {
    return this.npcSense.has(id);
  }

  /** Срез данных восприятия для канваса (презентация читает — симуляция решает, §17). */
  senseView() {
    const sn = this.data.senses!;
    const mods = this.senseModsNow();
    const cap = ORE_OVERLOAD + (this.data.player.genome.installed.includes('prismatic_core') ? 3 : 0);
    return {
      unlocked: sn.unlocked,
      visionOn: sn.unlocked.includes('vision'),
      core: this.coreActive,
      overload: sn.magicEnergy > cap,
      magicEnergy: sn.magicEnergy,
      ore: sn.ore,
      imprints: this.imprints,
      creature: (id: number) => this.senseMap.get(id),
      npc: (id: string) => this.npcSense.get(id),
      explored: (tx: number, ty: number) => this.isTileExplored(tx, ty),
      visionRadius: this.visionRadius(mods),
      touchR: mods.touchR,
      lightR: mods.lightR,
      spiritR: mods.spiritR,
      now: this.senseTime,
    };
  }

  /** Посекундный тик восприятия: перегрузка, угасание отпечатков, сканирование органов. */
  private updateSenses(dt: number) {
    const p = this.data.player;
    const sn = this.data.senses!;
    this.senseTime += dt;
    tickSenses(sn, dt);

    // перегрузка магической энергией (п.11 Дополнения)
    const cap = ORE_OVERLOAD + (p.genome.installed.includes('prismatic_core') ? 3 : 0);
    if (sn.magicEnergy > cap) {
      sn.overloadT -= dt;
      if (sn.overloadT <= 0) {
        sn.overloadT = 6;
        p.hp -= 2;
        p.hitFlash = 1;
        this.shakeT = 0.2;
        this.fxPush({ kind: 'dmg', x: p.pos.x, y: p.pos.y, text: '-2', color: '190,120,235' });
        if (p.hp <= 0) {
          this.onPlayerDeath('mutation');
          return;
        }
      }
      if (!this.data.flags.senseOverloaded) {
        this.data.flags.senseOverloaded = 1;
        this.pushLog('Перегрузка: магическая энергия обжигает рецепторы изнутри.', 'life');
      }
    } else {
      sn.overloadT = 0;
      if (this.data.flags.senseOverloaded) {
        delete this.data.flags.senseOverloaded;
        this.pushLog('Перегрузка спала: энергия осела в резонансном ядре.', 'life');
      }
    }

    // угасание отпечатков осязания (п.6 Дополнения)
    for (const [, imp] of this.imprints) imp.age += dt;
    for (const [k, imp] of this.imprints) if (imp.age > IMPRINT_LIFE) this.imprints.delete(k);

    // сканирование органов — 3 раза в секунды (мир симулируется кадрами независимо)
    this.senseAcc += dt;
    if (this.senseAcc < 0.3) return;
    this.senseAcc = 0;

    const mods = this.senseModsNow();
    const px = p.pos.x, py = p.pos.y;
    const unlocked = sn.unlocked;
    const vr = this.visionRadius(mods);

    // память «что ощущается прямо сейчас» чистится; память «когда-то чувствовал» (t/x/y) остаётся
    for (const e of this.senseMap.values()) e.organs.clear();
    for (const e of this.npcSense.values()) e.organs.clear();

    // 1) Осязание (врождённое): контуры поверхностей вокруг тела
    const tR = mods.touchR;
    const tx0 = Math.floor(px - tR - 1), tx1 = Math.floor(px + tR + 1);
    const ty0 = Math.floor(py - tR - 1), ty1 = Math.floor(py + tR + 1);
    for (let ty = ty0; ty <= ty1; ty++) {
      for (let tx = tx0; tx <= tx1; tx++) {
        if (Math.hypot(tx + 0.5 - px, ty + 0.5 - py) > tR + 0.7) continue;
        const solid = !this.isWalkable(tx + 0.5, ty + 0.5);
        if (!this.isTileExplored(tx, ty)) this.markExplored(tx, ty);
        if (solid) this.addImprint(tx + 0.5, ty + 0.5, 'solid');
      }
    }
    if (!this.dungeonActive) {
      // декорации под телом: стволы, камни, изгороди — щупаем и запоминаем
      for (const d of this.data.mapJson.deco) {
        if (d.r <= 0) continue;
        if (Math.abs(d.pos.x - px) > tR + 1 || Math.abs(d.pos.y - py) > tR + 1) continue;
        if (dist(d.pos.x, d.pos.y, px, py) > tR + d.r) continue;
        this.markExplored(Math.floor(d.pos.x), Math.floor(d.pos.y));
        if (d.r >= 0.3) this.addImprint(d.pos.x, d.pos.y, 'solid');
      }
    }

    // 2) Существа активного слоя: каждый орган чувствует по-своему
    for (const c of this.activeCreatures()) {
      const def = SPECIES[c.species];
      const d = dist(c.pos.x, c.pos.y, px, py);
      let e = this.senseMap.get(c.id);
      if (!e) {
        e = { organs: new Set(), t: -99, x: c.pos.x, y: c.pos.y };
        this.senseMap.set(c.id, e);
      }
      const moving = Math.hypot(c.vel.x, c.vel.y) > 0.12;
      const warm = c.species === 'rabbit' || c.species === 'deer' || c.species === 'wolf' || c.species === 'guard';
      const glowy = c.species === 'firefly' || c.species === 'spirit' || c.species === 'myco_monarch';
      let sensed = false;
      if (d <= tR + 0.15) {
        e.organs.add('touch');
        sensed = true;
        this.addImprint(c.pos.x, c.pos.y, 'entity', def.diet);
      }
      if (unlocked.includes('chemo') && d <= mods.chemoR * (c.dead ? 1.5 : 1)) { e.organs.add('chemo'); sensed = true; }
      if (unlocked.includes('vibration') && !c.dead && moving && d <= mods.vibroR) { e.organs.add('vibration'); sensed = true; }
      if (unlocked.includes('thermal') && d <= mods.thermalR * (warm ? 1 : 0.55)) { e.organs.add('thermal'); sensed = true; }
      if (unlocked.includes('light') && d <= mods.lightR * (glowy ? 1 : 0.5)) { e.organs.add('light'); sensed = true; }
      if (unlocked.includes('vision') && d <= vr) { e.organs.add('vision'); sensed = true; }
      if (unlocked.includes('hearing') && !c.dead && d <= mods.hearingR && (moving || def.diet === 'carnivore')) { e.organs.add('hearing'); sensed = true; }
      if (this.coreActive && d <= mods.spiritR) { e.organs.add('spirit'); sensed = true; }
      if (sensed) { e.t = this.senseTime; e.x = c.pos.x; e.y = c.pos.y; }
    }

    // 3) Зрение: глаза открывают тайлы (память разведанного остаётся телу)
    if (unlocked.includes('vision')) {
      const v0x = Math.floor(px - vr), v1x = Math.floor(px + vr);
      const v0y = Math.floor(py - vr), v1y = Math.floor(py + vr);
      for (let ty = v0y; ty <= v1y; ty++) {
        for (let tx = v0x; tx <= v1x; tx++) {
          if (Math.hypot(tx + 0.5 - px, ty + 0.5 - py) > vr) continue;
          this.markExplored(tx, ty);
        }
      }
    }

    // 4) Светочувствительность: источники света открывают окрестности
    if (unlocked.includes('light')) {
      const lr = mods.lightR;
      const reveal = (x: number, y: number, r: number) => {
        for (let ty = Math.floor(y - r); ty <= Math.floor(y + r); ty++) {
          for (let tx = Math.floor(x - r); tx <= Math.floor(x + r); tx++) {
            if (Math.hypot(tx + 0.5 - x, ty + 0.5 - y) > r) continue;
            this.markExplored(tx, ty);
          }
        }
      };
      for (const c of this.activeCreatures()) {
        if (c.dead) continue;
        const glowy = c.species === 'firefly' || c.species === 'spirit' || c.species === 'myco_monarch';
        if (glowy && dist(c.pos.x, c.pos.y, px, py) <= lr) reveal(c.pos.x, c.pos.y, 2.4);
      }
      if (this.dungeonActive) {
        const f = this.dun!.floors[this.dun!.depth - 1];
        const g0x = Math.max(0, Math.floor(px - lr)), g1x = Math.min(f.w - 1, Math.floor(px + lr));
        const g0y = Math.max(0, Math.floor(py - lr)), g1y = Math.min(f.h - 1, Math.floor(py + lr));
        for (let ty = g0y; ty <= g1y; ty++) {
          for (let tx = g0x; tx <= g1x; tx++) {
            if (f.terrain[ty * f.w + tx] !== 'glow') continue;
            if (Math.hypot(tx + 0.5 - px, ty + 0.5 - py) > lr) continue;
            reveal(tx + 0.5, ty + 0.5, 1.6);
          }
        }
      } else {
        for (const def of Object.values(DUNGEONS)) {
          const g = def.gatePos(this.data.pois);
          if (dist(g.x, g.y, px, py) <= lr) reveal(g.x, g.y, 2.2);
        }
        for (const d of this.data.mapJson.deco) {
          if (d.kind !== 'campfire') continue;
          if (dist(d.pos.x, d.pos.y, px, py) <= lr) reveal(d.pos.x, d.pos.y, 2.2);
        }
      }
    }

    // 5) Поселенцы (теплокровны, говорят — слышны)
    if (!this.dungeonActive) {
      for (const n of this.data.npcs) {
        if (!n.alive) continue;
        const d = dist(n.pos.x, n.pos.y, px, py);
        let e = this.npcSense.get(n.id);
        if (!e) {
          e = { organs: new Set(), t: -99, x: n.pos.x, y: n.pos.y };
          this.npcSense.set(n.id, e);
        }
        let sensed = false;
        if (d <= tR + 0.15) { e.organs.add('touch'); sensed = true; this.addImprint(n.pos.x, n.pos.y, 'entity'); }
        if (unlocked.includes('thermal') && d <= mods.thermalR) { e.organs.add('thermal'); sensed = true; }
        if (unlocked.includes('vision') && d <= vr) { e.organs.add('vision'); sensed = true; }
        if (unlocked.includes('hearing') && d <= mods.hearingR) { e.organs.add('hearing'); sensed = true; }
        if (this.coreActive && d <= mods.spiritR) { e.organs.add('spirit'); sensed = true; }
        if (sensed) { e.t = this.senseTime; e.x = n.pos.x; e.y = n.pos.y; }
      }
    }
  }

  // ===== Бой =====
  playerAttack() {
    const p = this.data.player;
    if (p.atkCd > 0 || p.energy < 1) return;
    p.atkCd = 0.65;
    p.energy = Math.max(0, p.energy - 1);
    p.attackAnim = 1;
    const s = this.stats;
    // ближайшая живая цель в радиусе (только активный слой)
    let target: Creature | null = null;
    let bd = 1.35;
    for (const c of this.activeCreatures()) {
      if (c.dead) continue;
      const d = dist(c.pos.x, c.pos.y, p.pos.x, p.pos.y);
      if (d < bd) { bd = d; target = c; }
    }
    if (!target) return;
    const def = SPECIES[target.species];
    let dmg = Math.max(1, s.atk * this.rng.range(0.85, 1.15) - def.base.def * 0.5);
    // путь Тьмы (§12): на грани смерти хищник внутри вас просыпается
    if (this.karmaPath.name === 'Тьма' && p.hp < s.maxHp * 0.5) dmg *= 1.2;
    // спецэффекты синергий
    if (s.synergies.some((x) => x.special === 'stun') && this.rng.chance(0.18)) target.stunT = 1.4;
    target.hp -= dmg;
    target.hitFlash = 1;
    // FX: выплеск слизи и цифра урона над целью
    this.fxPush({ kind: 'hit', x: target.pos.x, y: target.pos.y, color: '212,160,23' });
    this.fxPush({ kind: 'dmg', x: target.pos.x, y: target.pos.y, text: `-${Math.round(dmg)}`, color: '235,210,90' });
    target.aggroByPlayer = true;
    if (def.diet === 'herbivore' || def.diet === 'detritivore') { target.ai = 'flee'; target.stateT = 4; }
    else if (!def.sentient) { target.ai = 'chase'; target.targetId = -1; target.stateT = 8; }
    if (target.hp <= 0) this.killCreature(target, 'player');
  }

  killCreature(c: Creature, by: 'player' | 'aura' | 'npc' | 'other') {
    if (c.dead) return;
    c.dead = true;
    c.corpseT = 90; // труп лежит ~1.5 игровых часа
    c.ai = 'dead';
    this.addTrace(c.pos.x, c.pos.y, c.questTag === 'monarch' || c.questTag === 'titan' ? 'boss' : 'death');
    const def = SPECIES[c.species];
    if (by === 'player') {
      this.data.player.kills++;
      // карма за убийство разумных (§12)
      if (def.sentient) {
        this.karma('kill_sentient', `Убийство разумного: ${def.name}`, -20, 'bad');
      }
      // FX: гибель существа — брызги (цвет по разумности, §12)
      this.fxPush({ kind: 'kill', x: c.pos.x, y: c.pos.y, color: def.sentient ? '201,160,220' : '150,190,110' });
      if (c.questTag === 'alpha' && !this.data.flags.alphaKilled) {
        this.data.flags.alphaKilled = 1;
        this.onAlphaKilled();
      }
      if (c.species === 'wolf') {
        this.data.flags.wolvesKilled = (this.data.flags.wolvesKilled ?? 0) + 1;
      }
      // Боссы подземелий (Регион 2/3): фирменный ген — гарантированная добыча
      for (const def of Object.values(DUNGEONS)) {
        if (c.questTag === def.bossTag && !this.data.flags[def.bossFlag]) {
          this.data.flags[def.bossFlag] = 1;
          this.data.flags[`${def.bossFlag}Just`] = 1; // движок покажет тост
          this.data.player.biomass += def.bossReward;
          if (!this.data.player.knownGenes.includes(def.bossGene)) {
            this.data.player.knownGenes.push(def.bossGene);
            this.pushLog(`Из праха павшего вы извлекли «${GENES[def.bossGene].name}».`, 'life');
          }
          this.pushLog(`${def.bossName} рассыпается мерцающей пылью. Подземелье выдыхает.`, 'world');
        }
      }
      // прогресс заданий: убийства и боссы (§13)
      this.trackQuestKill(c.species, c.questTag);
    }
  }

  private onAlphaKilled() {
    // Стая без вожака уходит из региона (пощада: не истребляем всех — §12)
    const wolves = this.data.creatures.filter((c) => c.species === 'wolf' && !c.dead);
    const keep = wolves.slice(0, 2);
    for (const w of this.data.creatures) {
      if (w.species === 'wolf' && !w.dead && !keep.includes(w)) {
        w.dead = true; w.absorbed = true; w.corpseT = 0; // уходят за край леса
      }
    }
    this.data.player.biomass += 40;
    this.npc('dar').attitude = clamp(this.npc('dar').attitude + 10, 0, 100);
    this.data.village.raidCdDays = 5;
    if (this.data.village.raidPending) {
      this.data.village.raidPending = false;
      this.data.village.raidsSurvived++;
    }
    this.pushLog('Вожак стаи пал. Остальные волки ушли в глубину леса.', 'world');
    this.karma('spare_pack', 'Пощада стаи: стая изгнана убийством вожака, а не истреблением', 12, 'good', 8);
  }

  private updateCreature(c: Creature, dt: number) {
    const def = SPECIES[c.species];
    if (c.dead) {
      c.corpseT = Math.max(0, (c.corpseT ?? 0) - dt);
      return;
    }
    c.hitFlash = Math.max(0, c.hitFlash - dt * 3);
    c.atkCd = Math.max(0, c.atkCd - dt);
    if ((c.stunT ?? 0) > 0) { c.stunT = Math.max(0, (c.stunT ?? 0) - dt); return; }
    c.stateT -= dt;
    const p = this.data.player;
    const dToPlayer = dist(c.pos.x, c.pos.y, p.pos.x, p.pos.y);
    const perceive = def.base.perception / 64;

    // Охранники: защита поселения
    if (c.species === 'guard') {
      const v = this.data.pois.village.pos;
      let enemy: Creature | null = null;
      for (const o of this.data.creatures) {
        if (!o.dead && o.species === 'wolf' && dist(o.pos.x, o.pos.y, v.x, v.y) < 9) { enemy = o; break; }
      }
      if (enemy) {
        const d = dist(c.pos.x, c.pos.y, enemy.pos.x, enemy.pos.y);
        if (d > 0.9) {
          const nx = (enemy.pos.x - c.pos.x) / d, ny = (enemy.pos.y - c.pos.y) / d;
          this.moveWithCollision(c.pos, nx * def.base.speed * dt, ny * def.base.speed * dt);
        } else if (c.atkCd <= 0) {
          c.atkCd = 1.1;
          enemy.hp -= Math.max(1, def.base.atk * this.rng.range(0.8, 1.2) - SPECIES.wolf.base.def * 0.5);
          enemy.hitFlash = 1;
          if (enemy.hp <= 0) this.killCreature(enemy, 'npc');
        }
      } else if (c.wanderHome && dist(c.pos.x, c.pos.y, c.wanderHome.x, c.wanderHome.y) > 1 && c.stateT <= 0) {
        c.stateT = 1.5;
        c.wanderTo = { ...c.wanderHome };
      }
      return;
    }

    // Агрессия волков и хищников
    if (def.diet === 'carnivore' && !def.sentient) {
      // споровая вуаль: хищники замечают позже (Регион 2)
      const veil = p.genome.installed.includes('spore_veil') ? 0.7 : 1;
      // путь Тьмы (§12): родные хищники не трогают своего
      const kin = this.karmaPath.name === 'Тьма' && !c.aggroByPlayer
        && (c.species === 'wolf' || c.species === 'spider' || c.species === 'toad');
      // сезонный голод (Этап 2): Тьма — хищники дерзкие, Цветение — сытые и ленивые
      const seasonAggro = 1 + Math.max(0, this.seasonMods.aggressionAdd) * 1.5 + Math.min(0, this.seasonMods.aggressionAdd) * 0.6;
      const aggroR = perceive * veil * (c.aggroByPlayer ? 1.8 : 0.8) * this.regionDef.mods.aggressionMul * seasonAggro;
      if (!kin && dToPlayer < aggroR && (this.rng.chance(def.aggression * seasonAggro) || c.aggroByPlayer)) {
        c.ai = 'chase';
        c.stateT = Math.max(c.stateT, 3);
      }
    }
    if (c.aggroByPlayer && def.diet === 'carnivore') c.ai = 'chase';

    switch (c.ai) {
      case 'wander': {
        if (c.stateT <= 0) {
          c.stateT = this.rng.range(1.5, 4);
          if (this.rng.chance(0.6)) {
            const a = this.rng() * Math.PI * 2, r = this.rng.range(1, 4);
            c.wanderTo = { x: c.pos.x + Math.cos(a) * r, y: c.pos.y + Math.sin(a) * r };
          } else c.wanderTo = undefined;
        }
        if (c.wanderTo) this.stepToward(c, c.wanderTo, def.base.speed * 0.5, dt);
        break;
      }
      case 'flee': {
        if (c.stateT <= 0) { c.ai = 'wander'; c.aggroByPlayer = false; break; }
        const d = Math.max(0.001, dToPlayer);
        this.moveWithCollision(c.pos, ((c.pos.x - p.pos.x) / d) * def.base.speed * dt, ((c.pos.y - p.pos.y) / d) * def.base.speed * dt);
        break;
      }
      case 'chase': {
        if (c.stateT <= 0 || dToPlayer > perceive * 2.5) { c.ai = 'wander'; c.aggroByPlayer = false; break; }
        const slow = (c.slowT ?? 0) > 0 ? 0.6 : 1;
        if ((c.slowT ?? 0) > 0) c.slowT = Math.max(0, (c.slowT ?? 0) - dt);
        if (dToPlayer > 0.85) this.stepToward(c, p.pos, def.base.speed * slow, dt);
        else if (c.atkCd <= 0) {
          c.atkCd = 1.25;
          let dmg = Math.max(1, def.base.atk * this.rng.range(0.85, 1.15) - this.stats.def * 0.55);
          // коррозионная защита
          if (this.stats.synergies.some((x) => x.special === 'corrosion')) dmg = Math.max(1, dmg - 2);
          p.hp -= dmg;
          p.hitFlash = 1;
          this.shakeT = 0.28; // тряска экрана — удар пришёлся по игроку
          this.fxPush({ kind: 'dmg', x: p.pos.x, y: p.pos.y, text: `-${Math.round(dmg)}`, color: '224,90,58' });
          if (p.hp <= 0) this.onPlayerDeath('killed');
        }
        break;
      }
      default: c.ai = 'wander';
    }
  }

  private stepToward(c: Creature, to: Vec, speed: number, dt: number) {
    const d = Math.max(0.001, dist(c.pos.x, c.pos.y, to.x, to.y));
    const nx = (to.x - c.pos.x) / d, ny = (to.y - c.pos.y) / d;
    const before = { x: c.pos.x, y: c.pos.y };
    this.moveWithCollision(c.pos, nx * speed * dt, ny * speed * dt);
    if (dist(before.x, before.y, c.pos.x, c.pos.y) < speed * dt * 0.3) {
      // застряли — пробуем скользить вбок
      this.moveWithCollision(c.pos, -ny * speed * dt, nx * speed * dt);
      c.stateT = Math.min(c.stateT, 0.8);
    }
  }

  /** Обработка пробуждения органов после поглощения (тосты движка, достижения). */
  private afterAbsorbSenses(unlockedSenses: SenseOrgan[]) {
    for (const o of unlockedSenses) {
      this.data.flags[`sense_${o}`] = 1; // движок покажет тост
      this.pushLog(`Развилось чувство: ${ORGAN_INFO[o].name}.${o === 'vision' ? ' Мир обретает краски.' : ''}`, 'life');
      if (o === 'vision') this.data.flags.senseVisionUnlock = 1; // достижение души
    }
  }

  // ===== Поглощение (ТЗ §5, §9.1): захват ≠ переваривание =====
  absorbNearest(): boolean {
    const id = this.nearCorpseId;
    if (id == null) return false;
    const c = this.data.creatures.find((x) => x.id === id);
    if (!c || c.absorbed) return false;
    const def = SPECIES[c.species];
    const p = this.data.player;
    const dig = this.data.digestion!;
    // регион рождения: условия этой жизни (богатство добычи × модификатор — п.7 Дополнения)
    const rmods = this.regionDef.mods;
    const biomass = Math.max(1, Math.round(this.rng.range(def.biomass[0], def.biomass[1]) * rmods.biomassMul * (1 + this.data.player.genome.nodes * 0.05)));
    // §5.2/§5.5: проверка полостей и вместимости — отказ обязан быть понятным игроку
    const check = canAbsorb(dig, this.absorbCapacity(), biomass);
    if (!check.ok) {
      this.lastAbsorbReject = check.reason ?? 'Захват невозможен.';
      return false;
    }
    this.lastAbsorbReject = null;
    p.absorbedCount++;
    c.absorbed = true;
    c.corpseT = 0;
    // FX: струя биомассы втягивается в слизня — цель остаётся ВИДИМОЙ внутри тела (§5.3 E)
    this.fxPush({ kind: 'absorb', x: c.pos.x, y: c.pos.y, text: `+${biomass}`, color: '140,230,160' });
    this.addTrace(c.pos.x, c.pos.y, 'absorb');
    // события захвата: открытия, карма, задания — в момент захвата (§5.4, §11, §13)
    if (!p.discoveredSpecies.includes(c.species)) {
      p.discoveredSpecies.push(c.species);
      this.pushLog(`Строение существа изучено: ${def.name}.`, 'life');
      if (def.sentient) {
        this.karma('absorb_sentient', `Поглощение разумного: ${def.name}`, -15, 'bad');
      }
      // редкая память за духа
      if (c.species === 'spirit') this.data.flags.spiritAbsorbed = 1;
    }
    this.trackQuestAbsorb(c.species);
    // §6.1: ресурсы НЕ выдаются сразу — добыча ложится в полость грузом и перерабатывается
    const comp = PREY_COMPOSITION[c.species] ?? PREY_DEFAULT;
    const diet = DIET_FEED[c.species];
    const item = putItem(dig, {
      kind: comp.kind,
      name: `Останки: ${def.name}`,
      species: c.species,
      mass: biomass,
      hardness: comp.hardness,
      diet: diet ? { ...diet } : {},
      oreYield: ORE_YIELD_BY_SPECIES[c.species],
      genePool: [...def.geneDrop],
      geneChance: def.geneChance * rmods.geneChanceMul,
      toxicity: comp.toxicity,
      progress: 0,
      seed: this.rng.int(0, 100000),
    });
    this.pushLog(`Захвачено: ${def.name} (+${biomass}). ${item ? `${FOOD_KINDS[comp.kind].name} вещество — в полости ${item.chamber + 1} началась переработка.` : ''}`, 'life');
    return true;
  }

  // ===== Переваривание — отдельная внутренняя система (ТЗ §6) =====
  private tickDigestion(dt: number) {
    const dig = this.data.digestion!;
    const p = this.data.player;
    // §6.4: перегруженный организм работает менее эффективно
    const load = 1 + this.digestivePressure() * 0.6;
    const { completed } = tickDigestive(dig, dt, this.digestiveEnzyme(), this.absorbCapacity(), load);
    for (const item of completed) this.completeDigestion(item);
    if (this.data.deathCause) return; // отравление могло убить — тело остановилось
    // §6.6: ускоренный режим жжёт энергию
    p.energy = Math.max(0, p.energy - MODE_INFO[dig.mode].energyDrain * dt);
    // §6.6: режимы — открываемые функции (не обязательная система в первые минуты)
    for (const m of unlockModes(dig, this.data.senses!.biomassTotal)) {
      this.data.flags[`dig_${m}_just`] = 1;
      this.pushLog(`Тело научилось иначе перерабатывать пищу: открыт режим «${MODE_INFO[m].name}».`, 'life');
    }
  }

  /** Завершение реакции (§7): ресурсы выдаются ровно один раз, по классу вещества. */
  private completeDigestion(item: StoredItem) {
    const p = this.data.player;
    const mode = MODE_INFO[this.data.digestion!.mode];
    p.biomass += item.mass;
    p.energy = Math.min(this.stats.energyMax, p.energy + 2);
    // диета × сезон на момент завершения (питательность среды меняется)
    let diet = item.diet;
    const mul = this.seasonMods.dietMul;
    if (diet && mul !== 1) {
      diet = {};
      for (const k of Object.keys(item.diet) as (keyof typeof item.diet)[]) diet[k] = (item.diet[k] as number) * mul;
    }
    this.afterAbsorbSenses(grantBiomass(this.data.senses!, item.mass, this.senseModsNow().thr, diet));
    if (item.oreYield) this.gainOre(item.oreYield);
    // §6.1: генетическая ценность раскрывается при переваривании; бережный бережёт структуры (§6.6)
    if (item.genePool.length > 0 && this.rng.chance(item.geneChance * mode.geneMul)) {
      const gene = this.rng.pick(item.genePool);
      if (!p.knownGenes.includes(gene)) {
        p.knownGenes.push(gene);
        this.pushLog(`Генетический анализ завершён: получен ген «${GENES[gene].name}». Доступен в панели генома.`, 'life');
      }
    }
    // §6.2: токсичное вещество может отравить
    if (item.toxicity > 0 && this.rng.chance(item.toxicity * 0.5 * mode.toxMul)) {
      const dmg = Math.max(1, Math.round(this.rng.range(1.5, 3.5)));
      p.hp -= dmg;
      p.hitFlash = 1;
      this.shakeT = 0.2;
      this.data.flags.digToxicJust = 1;
      this.pushLog(`Отравление: токсины «${item.name}» жгут изнутри (-${dmg}).`, 'life');
      if (p.hp <= 0) { this.onPlayerDeath('mutation'); return; }
    }
    // §7: нерастворимый остаток накапливается (панель позволяет его выбросить)
    this.data.digestion!.insoluble += item.mass * FOOD_KINDS[item.kind].residue;
    this.fxPush({ kind: 'absorb', x: p.pos.x, y: p.pos.y, text: `+${item.mass}`, color: '140,230,160' });
    this.pushLog(`Переварено: ${item.name}. +${item.mass} биомассы.`, 'life');
  }

  // ===== Геном: установка / узлы =====
  installGene(geneId: string): boolean {
    const p = this.data.player;
    const g = GENES[geneId];
    if (!g) return false;
    const check = canInstall(p.genome, geneId);
    if (!check.ok) return false;
    if (p.biomass < g.cost) return false;
    p.biomass -= g.cost;
    p.genome.installed.push(geneId);
    this.refreshStats();
    p.hp = Math.min(p.hp, this.stats.maxHp);
    this.checkSynergies();
    this.pushLog(`Ген «${g.name}» вплетён в геном.`, 'life');
    return true;
  }

  removeGene(geneId: string): boolean {
    const p = this.data.player;
    const i = p.genome.installed.indexOf(geneId);
    if (i < 0) return false;
    p.genome.installed.splice(i, 1);
    p.biomass += Math.round(GENES[geneId].cost * 0.4); // частичный возврат биомассы
    this.refreshStats();
    p.hp = Math.min(p.hp, this.stats.maxHp);
    this.pushLog(`Ген «${GENES[geneId].name}» распущен.`, 'life');
    return true;
  }

  buyNode(): boolean {
    const p = this.data.player;
    const cost = nodeNextCost(p.genome.nodes);
    if (cost == null || p.biomass < cost) return false;
    p.biomass -= cost;
    const plan = NODE_PLAN[p.genome.nodes];
    p.genome.nodes++;
    p.genome.slots += plan.slots;
    p.genome.capacity += plan.capacity;
    this.refreshStats();
    this.pushLog('Эволюционный узел вырос: +1 слот органов, +1 энергия генома.', 'life');
    return true;
  }

  private checkSynergies() {
    const p = this.data.player;
    for (const s of this.stats.synergies) {
      if (!p.synDiscovered.includes(s.id)) {
        p.synDiscovered.push(s.id);
        this.pushLog(`Открыта синергия: «${s.name}»! ${s.desc}`, 'life');
        this.data.flags.synDiscovered = (this.data.flags.synDiscovered ?? 0) + 1;
      }
    }
  }

  // ===== Карма и журнал =====
  /** Путь души (§12): база — карма прошлых жизней + поступки этой жизни. */
  get karmaPath() {
    return karmaPathOf((this.data.flags.soulBaseKarma ?? 0) + (this.data.flags.karmaTotal ?? 0));
  }

  karma(id: string, title: string, delta: number, tag: 'good' | 'bad' | 'neutral', xp = 0) {
    this.data.decisions.push({ id, title, karma: delta, tag, soulXp: xp, day: this.clock.totalDays, year: this.clock.year });
    this.data.flags.karmaTotal = (this.data.flags.karmaTotal ?? 0) + delta;
    if (xp) this.data.flags.soulXpLife = (this.data.flags.soulXpLife ?? 0) + xp;
    this.pushLog(title, 'karma');
  }

  pushLog(text: string, kind: HistoryEntry['kind']) {
    this.data.log.push({ day: this.clock.totalDays, year: this.clock.year, text, kind });
    if (this.data.log.length > 220) this.data.log.splice(0, this.data.log.length - 220);
  }

  // ===== Задания посёлка (§13) =====
  private advanceQuest(qid: string, add: number) {
    const f = this.data.flags;
    const q = QUESTS.find((x) => x.id === qid);
    if (!q || !questAccepted(f, qid) || questDone(f, qid)) return;
    const cur = questProgress(f, qid);
    if (cur >= q.need) return;
    const nv = Math.min(q.need, cur + add);
    f[`quest_progress_${qid}`] = nv;
    if (nv >= q.need) this.pushLog(`Задание выполнено: «${q.title}». Награда ждёт у давшего его жителя посёлка.`, 'village');
  }

  private trackQuestKill(species: SpeciesId, questTag?: string) {
    for (const q of QUESTS) {
      if (q.type === 'kill' && q.target === species) this.advanceQuest(q.id, 1);
      if (q.type === 'boss' && q.target === questTag) this.advanceQuest(q.id, 1);
    }
  }

  private trackQuestAbsorb(species: SpeciesId) {
    for (const q of QUESTS) {
      if (q.type === 'absorb' && q.target === species) this.advanceQuest(q.id, 1);
    }
  }

  private trackQuestDungeon(id: DungeonId) {
    for (const q of QUESTS) {
      if (q.type === 'dungeon_enter' && q.target === id) this.advanceQuest(q.id, 1);
    }
  }

  private trackQuestDonate() {
    for (const q of QUESTS) {
      if (q.type === 'donate') this.advanceQuest(q.id, 1);
    }
  }

  acceptQuest(qid: string): boolean {
    const q = QUESTS.find((x) => x.id === qid);
    const f = this.data.flags;
    if (!q || questAccepted(f, qid) || questDone(f, qid)) return false;
    if (q.requires && !questDone(f, q.requires)) return false;
    f[`quest_accepted_${qid}`] = 1;
    // ретроактивный прогресс: убитые до принятия волки засчитываются
    f[`quest_progress_${qid}`] = qid === 'wolves' ? Math.min(3, f.wolvesKilled ?? 0) : 0;
    // боссы и подземелья тоже засчитываются задним числом — мир помнит подвиги (§13)
    if (q.type === 'boss' && q.target) {
      const def = Object.values(DUNGEONS).find((d) => d.bossTag === q.target);
      if (def && this.data.flags[def.bossFlag]) f[`quest_progress_${qid}`] = 1;
    }
    if (q.type === 'dungeon_enter' && q.target && this.data.dungeons?.[q.target as DungeonId]) {
      f[`quest_progress_${qid}`] = 1;
    }
    if (qid === 'wolves') f.wolfQuestAccepted = 1; // совместимость: маркер логова вожака
    this.pushLog(`Принято задание: «${q.title}».`, 'village');
    return true;
  }

  turnInQuest(qid: string): boolean {
    const q = QUESTS.find((x) => x.id === qid);
    const f = this.data.flags;
    if (!q || !questAccepted(f, qid) || questDone(f, qid) || questProgress(f, qid) < q.need) return false;
    f[`quest_done_${qid}`] = 1;
    f.questJustDone = 1; // движок покажет тост
    if (qid === 'wolves') f.wolfQuestDone = 1; // совместимость со старыми флагами
    const p = this.data.player;
    const parts: string[] = [];
    if (q.reward.biomass) { p.biomass += q.reward.biomass; parts.push(`+${q.reward.biomass} биомассы`); }
    this.karma(`quest_${qid}`, `Задание выполнено: «${q.title}»`, q.reward.karma ?? 0, 'good', q.reward.soulXp ?? 0);
    this.pushLog(`Награда за «${q.title}»: ${parts.length > 0 ? parts.join(', ') : 'благодарность посёлка'}.`, 'village');
    return true;
  }

  // ===== Поселение и NPC =====
  npc(id: string): Npc {
    return this.data.npcs.find((n) => n.id === id)!;
  }

  stealFood(): boolean {
    const p = this.data.player;
    const cd = this.data.flags.stealCd ?? 0;
    if (cd > this.clock.totalDays) return false;
    this.data.flags.stealCd = this.clock.totalDays + 1;
    p.biomass += 12;
    this.data.village.food = Math.max(0, this.data.village.food - 10);
    this.data.village.stability = clamp(this.data.village.stability - 3, 0, 100);
    for (const n of this.data.npcs) n.attitude = clamp(n.attitude - 12, 0, 100);
    this.karma('steal_food', 'Тайная кража еды со склада поселения', -8, 'bad');
    return true;
  }

  donateFood(): boolean {
    const p = this.data.player;
    if (p.biomass < 10) return false;
    p.biomass -= 10;
    this.data.village.food += 12;
    this.data.village.stability = clamp(this.data.village.stability + 2, 0, 100);
    for (const n of this.data.npcs) n.attitude = clamp(n.attitude + 5, 0, 100);
    this.karma('donate_food', 'Передача биомассы в запасы поселения', 6, 'good', 3);
    this.data.flags.donateCount = (this.data.flags.donateCount ?? 0) + 1; // хищная природа видит щедрость
    // прогресс заданий: пожертвования (§13)
    this.trackQuestDonate();
    return true;
  }

  markHostileIfBetrayed() {
    // резкий ход: атака поселенца (доступно в бою, осуждается)
  }

  dangerNear(): boolean {
    const p = this.data.player;
    for (const c of this.activeCreatures()) {
      if (c.dead || SPECIES[c.species].diet !== 'carnivore' || c.species === 'guard') continue;
      if (dist(c.pos.x, c.pos.y, p.pos.x, p.pos.y) < 5.5) return true;
    }
    return false;
  }

  // ===== Подземелья-инстансы (Регион 2/3, §6) =====
  // ===== Родовая норка: мини-вселенная новой жизни (Дополнение от игрока) =====
  /** Новая жизнь начинается в родовой норке: слепой комочек растёт в безопасности, потом выходит в большой мир. */
  enterBirthNest(): boolean {
    if (this.data.activeDungeon === 'birth_nest') return false;
    if (!this.data.dungeons) this.data.dungeons = {};
    const spawn = { ...this.data.player.pos };
    if (!this.data.dungeons['birth_nest']) {
      this.data.dungeons['birth_nest'] = createDungeonState(this.data.seed, 'birth_nest', spawn);
    }
    const dun = this.data.dungeons['birth_nest']!;
    dun.active = true;
    dun.depth = 1;
    dun.returnPos = spawn;
    this.data.activeDungeon = 'birth_nest';
    this.invalidateSeasonCache(); // мини-вселенная: свой микроклимат
    this.data.creatures = this.data.creatures.filter((c) => c.dungeonId !== 'birth_nest');
    this.spawnDungeonFloor('birth_nest', 1);
    const f0 = dun.floors[0];
    // комочек выпадает из кладки — в паре шагов от выхода-лунки, но выходит не сразу
    this.data.player.pos = { x: f0.down.x + 0.5, y: f0.down.y + 0.9 };
    this.data.player.vel = { x: 0, y: 0 };
    this.data.player.facing = 1;
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.data.flags.inNest = 1;
    this.pushLog('Родовая норка. Тьма тёплая и плотная, вы — бесформенный комочек слизи у кладки. Поглощайте живность и принос: лаза наружу ещё не достать.', 'world');
    return true;
  }

  /** Готовность к выходу: усвоено ≥ 8 биомассы ИЛИ открыто ≥ 2 органа помимо осязания. */
  nestReady(): { ready: boolean; cur: number; need: number } {
    const s = this.data.senses!;
    const extra = s.unlocked.filter((o) => o !== 'touch').length;
    const ready = s.biomassTotal >= 8 || extra >= 2;
    const cur = extra >= 2 ? 2 : Math.min(8, s.biomassTotal);
    return { ready, cur, need: extra >= 2 ? 2 : 8 };
  }

  /** Выход из норки в большой мир (только у лунки, только когда комочек подрос). */
  exitBirthNest(): boolean {
    const dun = this.dun;
    if (!this.dungeonActive || !dun || this.data.activeDungeon !== 'birth_nest') return false;
    const f = dun.floors[dun.depth - 1];
    const p = this.data.player;
    if (dist(f.up.x, f.up.y, p.pos.x, p.pos.y) > 1.45) return false;
    if (!this.nestReady().ready) {
      this.pushLog(`Проход ещё тесен: усвойте 8 биомассы (${Math.floor(this.data.senses!.biomassTotal)}/8) или откройте 2 чувства, чтобы пролезть.`, 'life');
      return false;
    }
    dun.active = false;
    this.data.activeDungeon = null;
    this.invalidateSeasonCache(); // вернулись в большой мир — сезоны снова действуют
    p.pos = { ...dun.returnPos };
    p.vel = { x: 0, y: 0 };
    this.data.creatures = this.data.creatures.filter((c) => !(c.dungeonId != null && c.dead));
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.data.flags.inNest = 0;
    this.data.flags.nestExited = 1;
    this.pushLog('Комочек протекает сквозь узкий лаз... и большой мир обрушивается ощущениями: ветер, трава, даль.', 'world');
    return true;
  }

  enterDungeon(id: DungeonId): boolean {
    if (this.dungeonActive || this.data.deathCause) return false;
    const def = DUNGEONS[id];
    const gate = def.gatePos(this.data.pois);
    const p = this.data.player;
    if (dist(gate.x, gate.y, p.pos.x, p.pos.y) > 1.7) return false;
    if (!this.data.dungeons![id]) {
      this.data.dungeons![id] = createDungeonState(this.data.seed, id, { x: gate.x + 0.9, y: gate.y + 1.5 });
    }
    const dun = this.data.dungeons![id]!;
    dun.active = true;
    dun.depth = 1;
    dun.returnPos = { x: gate.x + 0.9, y: gate.y + 1.5 };
    this.data.activeDungeon = id;
    this.invalidateSeasonCache(); // подземелье: нейтральный микроклимат
    // подземелье переросло прошлый визит: убираем всё, что осталось, и зарождаем заново
    this.data.creatures = this.data.creatures.filter((c) => c.dungeonId !== id);
    this.spawnDungeonFloor(id, 1);
    this.data.flags.dungeonEntered = 1;
    this.trackQuestDungeon(id);
    // Игрок переносится ко входу первого этажа (сцена меняется целиком).
    const f0 = dun.floors[0];
    p.pos = { x: f0.up.x + 0.5, y: f0.up.y + 0.5 };
    p.vel = { x: 0, y: 0 };
    p.facing = 1;
    // смена слоя: отпечатки и память чувств другого слоя недействительны (Дополнение к ТЗ)
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.pushLog(def.enterLog, 'world');
    return true;
  }

  descendStairs(): boolean {
    const dun = this.dun;
    const id = this.dungeonId;
    if (!this.dungeonActive || !dun || !id || dun.depth >= dun.floors.length) return false;
    const f = dun.floors[dun.depth - 1];
    const p = this.data.player;
    if (dist(f.down.x, f.down.y, p.pos.x, p.pos.y) > 1.45) return false;
    dun.depth++;
    this.data.creatures = this.data.creatures.filter((c) => !(c.dungeonId != null && c.dead));
    this.spawnDungeonFloor(id, dun.depth);
    const nf = dun.floors[dun.depth - 1];
    p.pos = { x: nf.up.x + 0.5, y: nf.up.y + 0.5 };
    p.vel = { x: 0, y: 0 };
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.pushLog(`Глубина ${dun.depth}. Давление камня растёт.`, 'world');
    return true;
  }

  ascendStairs(): boolean {
    const dun = this.dun;
    const id = this.dungeonId;
    if (!this.dungeonActive || !dun || !id) return false;
    const f = dun.floors[dun.depth - 1];
    const p = this.data.player;
    if (dist(f.up.x, f.up.y, p.pos.x, p.pos.y) > 1.45) return false;
    if (dun.depth <= 1) {
      dun.active = false;
      this.data.activeDungeon = null;
      this.invalidateSeasonCache(); // поверхность: сезоны действуют
      p.pos = { ...dun.returnPos };
      p.vel = { x: 0, y: 0 };
      this.data.creatures = this.data.creatures.filter((c) => !(c.dungeonId != null && c.dead));
      this.imprints.clear();
      this.senseMap.clear();
      this.npcSense.clear();
      this.rebuildExplored();
      this.pushLog(DUNGEONS[id].exitLog, 'world');
      return true;
    }
    dun.depth--;
    this.data.creatures = this.data.creatures.filter((c) => !(c.dungeonId != null && c.dead));
    this.spawnDungeonFloor(id, dun.depth);
    const nf = dun.floors[dun.depth - 1];
    p.pos = { x: nf.down.x + 0.5, y: nf.down.y + 0.5 };
    p.vel = { x: 0, y: 0 };
    this.imprints.clear();
    this.senseMap.clear();
    this.npcSense.clear();
    this.rebuildExplored();
    this.pushLog(`Вы поднимаетесь на глубину ${dun.depth}.`, 'world');
    return true;
  }

  takeCache(): boolean {
    const dun = this.dun;
    if (!this.dungeonActive || !dun || this.nearCacheId == null) return false;
    const cache = dun.caches.find((c) => c.id === this.nearCacheId);
    if (!cache || cache.taken) return false;
    const def = this.dungeonDef!;
    cache.taken = true;
    const amount = 8 + Math.round(this.rng.range(0, 6)) + cache.depth * 2;
    this.data.player.biomass += amount;
    // диета: принос тоже питает рецепторы по своему составу
    const cacheDiet = DIET_FEED[`cache_${def.cacheKind}`];
    let cacheScaled = cacheDiet;
    if (cacheDiet && this.seasonMods.dietMul !== 1) {
      cacheScaled = {};
      for (const k of Object.keys(cacheDiet) as (keyof typeof cacheDiet)[]) cacheScaled[k] = (cacheDiet[k] as number) * this.seasonMods.dietMul;
    }
    this.afterAbsorbSenses(grantBiomass(this.data.senses!, amount, this.senseModsNow().thr, cacheScaled));
    if (def.cacheKind === 'crystal') this.gainOre(2 + this.regionDef.mods.oreBonus); // жила — источник руды (Забытая жила богаче — п.7)
    this.addTrace(cache.pos.x, cache.pos.y, 'absorb');
    this.pushLog(`${def.cacheKind === 'crystal' ? 'Кристальная жила выломана' : 'Запас грибницы собран'}: +${amount} биомассы.`, 'life');
    return true;
  }

  private spawnDungeonFloor(id: DungeonId, depth: number) {
    const def = DUNGEONS[id];
    const bossAlive = !this.data.flags[def.bossFlag];
    const plan = def.spawnPlan(depth, bossAlive);
    const f = this.data.dungeons![id]!.floors[depth - 1];
    for (const [sp, count] of plan) {
      for (let i = 0; i < count; i++) {
        const c = this.spawnCreature(sp, this.dungeonSpawnPos(f), this.data.gen);
        c.dungeonFloor = depth;
        c.dungeonId = id;
        if (sp === def.bossSpecies) { c.questTag = def.bossTag; c.name = def.bossName; }
      }
    }
  }

  private dungeonSpawnPos(f: { w: number; h: number; terrain: string[]; up: Vec; down: Vec }): Vec {
    for (let i = 0; i < 60; i++) {
      const x = this.rng.int(1, f.w - 2), y = this.rng.int(1, f.h - 2);
      if (f.terrain[y * f.w + x] === 'rock') continue;
      if (Math.hypot(x - f.up.x, y - f.up.y) < 3) continue; // не у самого входа
      return { x: x + 0.5, y: y + 0.5 };
    }
    return { x: f.up.x + 2, y: f.up.y };
  }

  // ===== Дневные тики (уровень C симуляции, §6) =====
  /**
   * Миграции при смене сезона (Этап 2 карты): часть популяций стягивается к пищевым узлам.
   * Тьма — теплокровные жмутся к посёлку/пруду, хищники идут за добычей; Увядание — к грибной роще.
   * Мягкая сила: переназначаем wanderHome и подтягиваем часть особей шагом (мир остаётся живым, без телепортов).
   */
  private migrateForSeason(season: Season) {
    const poiNames: Record<string, string> = {
      village: 'людским очагам', pond: 'пруду', mushroom_grove: 'грибной роще',
      crystal_hollow: 'кристальной нише', stone_circle: 'каменному кругу', burrow: 'норе у корней', rift: 'разлому',
    };
    const plan = SEASON_MIGRATIONS[season];
    if (!plan) return;
    // агрегируем по точке приюта: одна запись в хронику на направление (без ломкой плюрализации)
    const byPoi = new Map<string, string[]>();
    for (const [spKey, poiKey] of Object.entries(plan)) {
      const sp = spKey as SpeciesId;
      const poi = this.data.pois[poiKey as string];
      if (!poi) continue;
      const crowd = this.data.creatures.filter((c) => c.species === sp && !c.dead && c.dungeonFloor == null);
      // мигрирует до половины стаи — мир меняется постепенно, стая не исчезает целиком
      const movers = crowd.slice(0, Math.ceil(crowd.length * 0.5));
      for (const c of movers) {
        const a = this.rng() * Math.PI * 2;
        const r = 1 + this.rng() * poi.r * 0.7;
        const home = { x: poi.pos.x + Math.cos(a) * r, y: poi.pos.y + Math.sin(a) * r * 0.7 };
        c.wanderHome = home;
        if (c.ai === 'idle' || c.ai === 'wander') { c.ai = 'wander'; c.wanderTo = { ...home }; c.stateT = 6 + this.rng() * 8; }
      }
      if (movers.length > 0) {
        const list = byPoi.get(poiKey as string) ?? [];
        list.push(SPECIES[sp].name.toLowerCase());
        byPoi.set(poiKey as string, list);
      }
    }
    for (const [poiKey, names] of byPoi) {
      this.pushLog(`Зверь смещается: ${names.join(', ')} — к ${poiNames[poiKey] ?? poiKey}.`, 'world');
    }
  }

  private dailyTick(crossed: number) {
    const p = this.data.player;
    const d = this.data;

    // 0. СЕЗОНЫ (Этап 2 карты): смена сезона → хроника + миграции. Норка — вне сезонов.
    const inNestLike = d.activeDungeon != null; // подземелья и норка — свой микроклимат
    const curSeason = seasonOf(this.clock.totalDays);
    const seasonChanged = this.lastSeason != null && this.lastSeason !== curSeason;
    this.lastSeason = curSeason;
    this.invalidateSeasonCache();
    if (seasonChanged && !inNestLike) {
      this.pushLog(SEASON_LOG[curSeason], 'world');
      this.migrateForSeason(curSeason);
    }

    // 0. Родовая норка: добыча восполняется медленно — сидеть в безопасности невыгодно
    if (d.activeDungeon === 'birth_nest') {
      const alive = d.creatures.filter((c) => c.dungeonId === 'birth_nest' && !c.dead).length;
      const planTotal = DUNGEONS['birth_nest'].spawnPlan(1, true).reduce((a, [, n]) => a + n, 0);
      if (alive < planTotal && this.rng() < 0.45) {
        const [sp] = DUNGEONS['birth_nest'].spawnPlan(1, true)[Math.floor(this.rng() * 5) % 5];
        const c = this.spawnCreature(sp as SpeciesId, this.dungeonSpawnPos(this.dun!.floors[0]), d.gen);
        if (c) { c.dungeonId = 'birth_nest'; c.dungeonFloor = 1; }
      }
    }

    // 1. Старение игрока (§10)
    p.ageDays += crossed;
    this.refreshStats();
    if (p.hp > this.stats.maxHp) p.hp = this.stats.maxHp;
    const newStage = this.stats.stage;
    const stageKey = 'lastStage';
    if (d.flags[stageKey] !== stageNum(newStage)) {
      d.flags[stageKey] = stageNum(newStage);
      if (newStage === 'Старение') this.pushLog('Ваше тело вступает в стадию Старения. Силы уже не те, зато опыт глубже.', 'life');
      if (newStage === 'Угасание') this.pushLog('Угасание. Дни сочатся медленно, и вы чувствуете, как душа готовится к пути.', 'life');
    }

    // 2. Проверка естественной смерти — по игровому дню, не по кадру (§10)
    for (let i = 0; i < crossed; i++) {
      if (this.rng.chance(this.stats.mortality)) {
        this.onPlayerDeath('old_age');
        return;
      }
    }

    // 3. Мутации от нестабильного генома (§9.2)
    if (this.stats.stability < 40 && this.rng.chance(0.22)) {
      p.mutations++;
      p.hp = Math.min(p.hp, this.stats.maxHp);
      this.pushLog('Геном дрожит: клеточная мутация ослабила тело (−2 к максимуму здоровья). Стабилизируйте геном.', 'life');
      if (this.stats.stability < 15 && this.rng.chance(0.3)) {
        p.hp -= 4;
        if (p.hp <= 0) { this.onPlayerDeath('mutation'); return; }
      }
    }

    // 4. Старение NPC и смена поколений (§15: правители стареют и умирают)
    for (const n of d.npcs) {
      if (!n.alive) continue;
      n.ageDays += crossed;
      if (n.ageDays > n.lifespan && this.rng.chance(dailyMortality(n.ageDays, n.lifespan))) {
        n.alive = false;
        d.village.stability = clamp(d.village.stability - 8, 0, 100);
        this.pushLog(`${n.name} умер(ла) от старости. Поселение скорбит.`, 'village');
        // смена власти / преемник
        if (n.id === 'mirra') {
          d.npcs.push({ id: 'ivo', name: 'Старейшина Иво', role: 'новый старейшина', pos: n.pos, ageDays: 40, lifespan: 400, alive: true, attitude: 40, dialogue: 'hello_successor' });
          this.pushLog('Молодой Иво становится старейшиной. Его помнят только ваши рассказы.', 'village');
        }
        if (n.id === 'dar') {
          d.npcs.push({ id: 'rinsa', name: 'Охотница Ринса', role: 'новый охотник', pos: n.pos, ageDays: 30, lifespan: 320, alive: true, attitude: 35, dialogue: 'hello_successor' });
        }
        if (n.id === 'klumba') {
          d.npcs.push({ id: 'prokh', name: 'Огородник Прох', role: 'фермер', pos: n.pos, ageDays: 36, lifespan: 340, alive: true, attitude: 40, dialogue: 'hello_successor' });
        }
      }
    }

    // 5. Популяции: рождаемость и поколения (§6 уровень C) — сезоны дышат (Этап 2)
    d.gen++;
    const sm = this.seasonMods;
    const speciesSeen: SpeciesId[] = ['slug', 'beetle', 'firefly', 'rabbit', 'deer', 'wolf', 'toad', 'spider', 'mushling', 'crystalslug', 'shardling', 'spirit'];
    for (const sp of speciesSeen) {
      const def = SPECIES[sp];
      const pop = this.popCount(sp);
      if (pop < def.popCap) {
        const rate = sp === 'spirit' ? 0.05 : sp === 'crystalslug' || sp === 'shardling' ? 0.08 : sp === 'wolf' ? 0.22 : 0.35;
        const seasonal = rate * sm.birthMul * seasonalSpawnBoost(sp, curSeason);
        if (this.rng.chance(seasonal * crossed)) {
          // духи возрождаются и у Разлома (регион предельной сложности — п.3)
          let poiKey: string;
          if (sp === 'wolf') poiKey = 'burrow';
          else if (sp === 'toad' || sp === 'firefly') poiKey = 'pond';
          else if (sp === 'spider' || sp === 'mushling') poiKey = 'mushroom_grove';
          else if (sp === 'crystalslug' || sp === 'shardling') poiKey = 'crystal_hollow';
          else if (sp === 'spirit') poiKey = this.data.pois.rift && this.rng.chance(0.4) ? 'rift' : 'stone_circle';
          else poiKey = 'village';
          this.spawnCreature(sp, this.spawnPosNear(d.pois[poiKey]), d.gen);
        }
      }
    }

    // 6. Поселение: экономика дня (§15) + стадия роста
    const v = d.village;
    const stage = villageStageOf(v.population);
    if (v.stage !== stage.name) {
      if (stage.name === 'Деревня') this.pushLog('Опушка разрослась: теперь это Деревня. Тропы стали шире.', 'village');
      if (stage.name === 'Посад') this.pushLog('Опушка стала Посадом — слава о спокойном месте тянет людей.', 'village');
      v.stage = stage.name;
    }
    v.food = v.food + 3 + stage.foodBonus - Math.ceil(v.population * 0.4) + sm.villageFoodDelta;
    if (v.food < 0) {
      v.food = 0;
      if (this.rng.chance(0.5)) {
        v.population = Math.max(3, v.population - 1);
        v.stability = clamp(v.stability - 4, 0, 100);
        this.pushLog('В Опушке голод: запасы иссякли, кто-то покинул поселение.', 'village');
      }
    } else if (v.food > 60 && this.rng.chance(0.3)) {
      v.population += 1;
      this.pushLog('В Опушке родился ребёнок. Поселение растёт.', 'village');
    }
    v.stability += v.stability < 50 ? 1 : v.stability > 70 ? -1 : 0;
    if (v.raidCdDays > 0) v.raidCdDays -= crossed;

    // 6.5 Пути кармы (§12): мир реагирует на то, кем стала душа
    const path = this.karmaPath;
    if (path.name === 'Свет') {
      v.food += 2;
      p.biomass += 1;
      if (!d.flags.lightBlessing) {
        d.flags.lightBlessing = 1;
        this.pushLog('Путь Света: посёлок оставляет вам подношение у тропы (+2 еды посёлку, +1 биомассы вам каждый день).', 'karma');
      }
    } else if (path.name === 'Тьма' && !d.flags.darkWhisper) {
      d.flags.darkWhisper = 1;
      this.pushLog('Путь Тьмы: волки больше не видят в вас добычу, а голод внутри стал сильнее.', 'karma');
    }

    // 7. Набег волков
    if (!v.raidPending && v.raidCdDays <= 0) {
      const wolves = this.popCount('wolf');
      if (wolves >= 4 && this.rng.chance(0.4)) {
        v.raidPending = true;
        v.raidDeadline = this.clock.totalDays + 1;
        this.pushLog('ВОЛКИ У ОГРАДЫ! Стая кружит у поселения. Дар зовёт к обороне.', 'village');
      }
    }
    if (v.raidPending) {
      if (this.clock.totalDays >= (v.raidDeadline ?? 0)) {
        // игрок проигнорировал — поселение платит
        v.raidPending = false;
        v.raidCdDays = 3;
        v.food = Math.max(0, v.food - 16);
        v.population = Math.max(3, v.population - 2);
        v.stability = clamp(v.stability - 12, 0, 100);
        this.pushLog('Стая прорвалась в посёлок: запасы разграблены, люди погибли. Вы не пришли.', 'village');
        this.karma('ignored_raid', 'Игнорирование набега: поселение пострадало', -10, 'bad');
        // волки уходят сытыми
        const wolves = d.creatures.filter((c) => c.species === 'wolf' && !c.dead);
        for (const w of wolves) { w.pos.x = d.pois.burrow.pos.x + this.rng.range(-2, 2); w.pos.y = d.pois.burrow.pos.y + this.rng.range(-2, 2); }
      } else {
        // волки подошли к деревне
        const wolves = d.creatures.filter((c) => c.species === 'wolf' && !c.dead);
        wolves.slice(0, 5).forEach((w, i) => {
          const a = (i / 5) * Math.PI * 2;
          const tx = d.pois.village.pos.x + Math.cos(a) * (d.pois.village.r + 2.5);
          const ty = d.pois.village.pos.y + Math.sin(a) * (d.pois.village.r + 2.5) * 0.82;
          this.stepToward(w, { x: tx, y: ty }, SPECIES.wolf.base.speed, 0.016);
        });
      }
    }
  }

  onPlayerDeath(cause: DeathCause) {
    if (this.data.deathCause) return;
    this.data.deathCause = cause;
    this.data.deathDay = this.clock.totalDays;
    this.addTrace(this.data.player.pos.x, this.data.player.pos.y, 'death'); // след души для следующей жизни
    this.pushLog(cause === 'old_age' ? 'Тело угасло. Сердце слизня остановилось — тихо, как осенний дождь.' : 'Ваше тело было разорвано. Мир на мгновение стал тёмным.', 'life');
  }

  // ===== Диалоги (упрощённые решения с последствиями, §3.2) =====
  npcDialogue(id: string): { name: string; role: string; text: string; options: { id: string; label: string }[] } {
    const n = this.npc(id);
    const v = this.data.village;
    const options: { id: string; label: string }[] = [];
    let text = '';
    const att = n.attitude;
    if (id === 'mirra' || id === 'ivo') {
      const eq = questsForGiver(this.data.flags, 'elder');
      const ready = eq.filter((x) => x.status === 'ready');
      const active = eq.filter((x) => x.status === 'active');
      const avail = eq.filter((x) => x.status === 'available');
      text = att > 65
        ? '«Лес шепчет о тебе, странник. Ты не такой, как прочий зверь.»'
        : att > 35
          ? '«Слизень, что мыслит... В наши годы чудес не ждали. Но лес полон загадок.»'
          : '«Уходи, пока не прогнали. Ты пахнешь бедой.»';
      if (active.length > 0) text += ` ${active[0].q.hint}`;
      if (ready.length > 0) {
        text = '«Ты вернулся не с пустыми руками — я чую это. Говори.»';
        for (const r of ready) options.unshift({ id: `turnin_${r.q.id}`, label: `Сдать задание: «${r.q.title}»` });
      } else if (avail.length > 0) {
        options.push({ id: `accept_${avail[0].q.id}`, label: `Взять задание: «${avail[0].q.title}»` });
      }
      options.push({ id: 'ask_rift', label: 'Спросить о Разломе' });
      if (v.raidPending) options.push({ id: 'raid_info', label: 'Что за шум у ограды?' });
      options.push({ id: 'leave', label: 'Уйти' });
    } else if (id === 'dar' || id === 'rinsa') {
      if (v.raidPending) {
        text = '«Стая у забора! Возьми ополчение и отгони их — или мы все сдохнем этой ночью.»';
        options.push({ id: 'start_raid', label: 'Начать оборону (Magic Chess)' });
        if (!this.data.flags.alphaSpawned) options.push({ id: 'hunt_alpha', label: 'Я найду вожака сам — стая уйдёт' });
        options.push({ id: 'leave', label: 'Уйти' });
      } else {
        const hq = questsForGiver(this.data.flags, 'hunter');
        const ready = hq.filter((x) => x.status === 'ready');
        const active = hq.filter((x) => x.status === 'active');
        const avail = hq.filter((x) => x.status === 'available');
        if (ready.length > 0) {
          text = '«Ну? Говори, что принёс — не томи.»';
          for (const r of ready) options.unshift({ id: `turnin_${r.q.id}`, label: `Сдать задание: «${r.q.title}»` });
        } else if (active.length > 0) {
          text = active[0].q.type === 'kill'
            ? `«Волков ещё ходит: ${this.popCount('wolf')}. Не возвращайся с пустыми руками.»`
            : '«Пока тихо. Но камни на северо-западе звенят — проверь, как там осколки.»';
        } else if (avail.length > 0) {
          text = avail[0].q.hint;
        } else {
          text = '«Лес опустел. Хороший знак... или плохой.»';
        }
        if (avail.length > 0 && ready.length === 0) options.push({ id: `accept_${avail[0].q.id}`, label: `Взять задание: «${avail[0].q.title}»` });
        options.push({ id: 'leave', label: 'Уйти' });
      }
    } else if (id === 'klumba' || id === 'prokh') {
      const fq = questsForGiver(this.data.flags, 'farmer');
      const ready = fq.filter((x) => x.status === 'ready');
      const avail = fq.filter((x) => x.status === 'available');
      text = v.food < 25
        ? '«Запрыгали волки — а мы сидим без еды. Если у тебя есть лишняя биомасса...»'
        : '«Ферма даёт плоды, пока духи леса добры. Иди с миром, слизень.»';
      if (ready.length > 0) {
        text = '«Ты славно потрудился для посёлка. Держи, что обещала.»';
        for (const r of ready) options.unshift({ id: `turnin_${r.q.id}`, label: `Сдать задание: «${r.q.title}»` });
      } else if (avail.length > 0) {
        text = avail[0].q.hint;
        options.push({ id: `accept_${avail[0].q.id}`, label: `Взять задание: «${avail[0].q.title}»` });
      }
      options.push({ id: 'donate', label: 'Отдать 10 биомассы в запасы (+карма)' });
      options.push({ id: 'steal', label: 'Тихо обчистить склад (−карма)' });
      options.push({ id: 'ask_spirits', label: 'Спросить о духах' });
      options.push({ id: 'leave', label: 'Уйти' });
    }
    return { name: n.name, role: n.role, text, options };
  }

  npcAction(id: string, opt: string): void {
    const v = this.data.village;
    switch (opt) {
      case 'donate':
        if (this.donateFood()) this.npc(id).attitude = clamp(this.npc(id).attitude + 6, 0, 100);
        break;
      case 'steal':
        this.stealFood();
        break;
      case 'hunt_alpha': {
        this.data.flags.alphaSpawned = 1;
        const burrow = this.data.pois.burrow.pos;
        this.spawnCreature('wolf', { x: burrow.x + 1, y: burrow.y }, this.data.gen, { questTag: 'alpha', name: 'Рваное Ухо' });
        this.npc(id).attitude = clamp(this.npc(id).attitude + 4, 0, 100);
        this.pushLog('Дар: «Вожака зовут Рваное Ухо. Убей его — стая разбредётся. Так честнее.»', 'village');
        break;
      }
      case 'start_raid':
        this.data.flags.startRaid = 1; // движок прочитает флаг и откроет Magic Chess
        break;
      case 'ask_spirits':
        this.pushLog('Клумба: «Духи собираются у старых камней на северо-востоке. Не тревожь их попусту.»', 'world');
        this.data.flags.showStoneCircle = 1;
        break;
      case 'ask_rift':
        this.pushLog('«Разлом? Не смей произносить это у детей. Пустошь на юге — не для живых.»', 'world');
        this.data.flags.loreRift = (this.data.flags.loreRift ?? 0) + 1;
        break;
      case 'raid_info':
        this.pushLog('«Волки. Собирай союзников и отгоняй их — или посёлок обнищает.»', 'village');
        break;
      default: {
        // задания: принять / сдать (§13)
        if (opt.startsWith('accept_')) {
          const qid = opt.slice('accept_'.length);
          if (this.acceptQuest(qid)) this.npc(id).attitude = clamp(this.npc(id).attitude + 5, 0, 100);
        } else if (opt.startsWith('turnin_')) {
          const qid = opt.slice('turnin_'.length);
          if (this.turnInQuest(qid)) this.npc(id).attitude = clamp(this.npc(id).attitude + 10, 0, 100);
        }
        break;
      }
    }
  }

  // ===== Доступ к геному из UI =====
  get genomeApi() {
    return {
      install: (g: string) => this.installGene(g),
      remove: (g: string) => this.removeGene(g),
      buyNode: () => this.buyNode(),
    };
  }
}

function stageNum(s: string): number {
  return ['Зарождение', 'Молодость', 'Расцвет', 'Старение', 'Угасание'].indexOf(s);
}