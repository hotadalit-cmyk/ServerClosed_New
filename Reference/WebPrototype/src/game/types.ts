// SERVERCLOSED — прототип «Две жизни одного слизня»
// Слой Core: общие типы. Архитектура повторяет слои ТЗ §17:
// Core (rng/time/save) → Simulation (world/creatures/village) → Gameplay (genes/soul/chess) → Presentation (React/canvas).

export type Vec = { x: number; y: number };

export type TerrainKind = 'moss' | 'dirt' | 'path' | 'water' | 'glade' | 'ruin'
  | 'mountain' | 'marsh' | 'ash'; // «Чаша Отголосов»: хребты, топи, пепел (Дополнение: карта мира)

export type DecoKind =
  | 'tree' | 'pine' | 'bush' | 'rock' | 'mushroom' | 'stone'
  | 'hut' | 'fence' | 'campfire' | 'farm' | 'well' | 'crystal' | 'bones'
  | 'crack' | 'reed'; // трещины разлома/тайные проходы, камыш топей

// ===== Подземелья-инстансы (Регион 2 «Катакомбы», Регион 3 «Пещеры» — ТЗ §6) =====
// 'birth_nest' — родовая норка: мини-вселенная, где каждая новая жизнь начинается
export type DungeonId = 'catacombs' | 'crystal_caves' | 'underground_channel' | 'birth_nest';
export type DungeonTerrainKind = 'rock' | 'floor' | 'glow' | 'puddle';

export interface DungeonFloorData {
  depth: number;               // 1..3
  w: number;
  h: number;
  terrain: DungeonTerrainKind[];
  up: Vec;                     // лестница наверх
  down: Vec;                   // лестница вниз (только глубина < 3)
}

export interface DungeonCache {
  id: number;
  depth: number;
  pos: Vec;
  taken: boolean;
}

export interface DungeonState {
  active: boolean;
  depth: number;               // текущая глубина 1..3
  returnPos: Vec;              // куда вернуться в мире (у врат)
  floors: DungeonFloorData[];  // детерминированы сидом мира
  caches: DungeonCache[];
  explored?: number[][];       // разведанные тайлы по глубинам (восприятие тела, Дополнение к ТЗ)
}

/** Отпечаток события для духовного чутья (Дополнение к ТЗ п.10). */
export interface WorldTrace {
  x: number;
  y: number;
  day: number;
  kind: 'death' | 'absorb' | 'raid' | 'boss';
}

export type RegionId = 'quiet_cradle' | 'root_garden' | 'bone_narrows' | 'forgotten_vein' | 'soul_rift';

/** Многомерная модель души (Дополнение: «Резонанс души»). Карма — отдельное поле soul.karma. */
export interface SoulTraits {
  resilience: number;   // 0..100 — устойчивость: переносит экстремальные условия
  depth: number;        // 0..100 — духовная глубина: чувствительность к магии и душам
  predator: number;     // 0..100 — хищная природа: склонность к насилию и поглощению
  memory: number;       // 0..100 — память души: сила унаследованных впечатлений
}

/** Путь кармы (§12): мир реагирует на то, кем стала душа. */
export type KarmaPath = 'Свет' | 'Равновесие' | 'Тьма';

export interface Deco {
  id: number;
  kind: DecoKind;
  pos: Vec;           // координаты мира (в тайлах)
  r: number;          // радиус коллизии (0 — проходимо)
  seed: number;       // вариация отрисовки
  hp?: number;        // для разрушаемых (забор)
}

export interface TileMap {
  w: number;
  h: number;
  terrain: TerrainKind[];   // w*h
  deco: Deco[];
}

/** Ген-карточка (статическое определение — аналог ScriptableObject из ТЗ §17) */
export type GeneCategory = 'physical' | 'elemental' | 'instinct' | 'spirit' | 'crystal';

export interface GeneDef {
  id: string;
  name: string;
  category: GeneCategory;
  desc: string;
  cost: number;            // цена установки в биомассе
  energy: number;          // энергозатраты (ёмкость генома)
  instability: number;     // штраф стабильности генома
  mods: Partial<Record<'maxHp' | 'atk' | 'def' | 'speed' | 'perception' | 'energyMax' | 'hpRegen' | 'lifeDays', number>>;
  source?: string;         // от какого существа обычно добывается
}

export interface SynergyDef {
  id: string;
  name: string;
  genes: [string, string];
  desc: string;
  mods: Partial<Record<'maxHp' | 'atk' | 'def' | 'speed' | 'perception' | 'energyMax' | 'hpRegen' | 'lifeDays', number>>;
  special?: 'stun' | 'toxicAura' | 'corrosion' | 'battery';
}

/** Установленный геном игрока */
export interface Genome {
  installed: string[];        // id генов
  capacity: number;           // энергопотолок
  slots: number;              // слоты органов
  nodes: number;              // купленные эволюционные узлы
}

export type SpeciesId =
  | 'slug' | 'beetle' | 'firefly' | 'rabbit' | 'deer' | 'wolf'
  | 'toad' | 'spider' | 'spirit' | 'crystalslug' | 'mushling' | 'guard'
  | 'sporeling' | 'mycenoid' | 'myco_monarch'
  | 'shardling' | 'golem' | 'crystal_titan';

export interface SpeciesDef {
  id: SpeciesId;
  name: string;
  sentient: boolean;          // разумная душа (карма за убийство)
  lifespan: number;           // дни
  base: { maxHp: number; atk: number; def: number; speed: number; perception: number };
  biomass: [number, number];  // диапазон биомассы
  geneDrop: string[];         // возможные гены
  geneChance: number;
  diet: 'herbivore' | 'carnivore' | 'detritivore' | 'spirit';
  aggression: number;         // 0..1 — вероятность агро на игрока в радиусе
  pack?: number;              // размер стаи при спавне
  popCap: number;             // потолок популяции в регионе
}

export type AIState = 'idle' | 'wander' | 'flee' | 'chase' | 'attack' | 'eat' | 'guard' | 'dead';

export interface Creature {
  id: number;
  species: SpeciesId;
  name?: string;              // имя уникальных NPC
  pos: Vec;
  vel: Vec;
  hp: number;
  ageDays: number;
  ai: AIState;
  targetId?: number;
  wanderTo?: Vec;
  stateT: number;             // таймер состояния
  atkCd: number;
  hitFlash: number;
  dead: boolean;
  corpseT?: number;           // время до исчезновения трупа
  absorbed?: boolean;
  aggroByPlayer?: boolean;
  stunT?: number;             // паралич (электрическая ловушка)
  slowT?: number;             // замедление (токсичная зона)
  questTag?: string;          // 'alpha' — вожак стаи, 'monarch'/'titan' — боссы подземелий
  gen: number;                // поколение (для живого мира)
  wanderHome?: Vec;           // точка возврата (охранники)
  dungeonFloor?: number;      // глубина подземелья, где живёт существо (нет — надземное)
  dungeonId?: DungeonId;      // к какому подземелью привязано (для мульти-инстансов)
}

export interface Npc {
  id: string;                 // 'mirra' | 'dar' | 'klumba'
  name: string;
  role: string;
  pos: Vec;
  ageDays: number;
  lifespan: number;
  alive: boolean;
  attitude: number;           // 0..100 к игроку
  dialogue: string;           // текущая реплика-ключ
}

export interface VillageState {
  name: string;
  stage: string;              // «Хутор» / «Деревня» / «Посад» (§15)
  population: number;
  food: number;
  stability: number;          // 0..100
  military: number;           // 0..100
  raidCdDays: number;         // дней до возможного набега
  raidsSurvived: number;
  ruinChanceT: number;
  raidPending?: boolean;      // идёт тревога: стая у ограды
  raidDeadline?: number;      // день, до которого нужно отбиться
}

export type KarmaTag = 'good' | 'bad' | 'neutral';

export interface KarmaEvent {
  day: number;
  year: number;
  id: string;
  title: string;
  karma: number;              // ±
  tag: KarmaTag;
  soulXp?: number;
}

export interface HistoryEntry {
  day: number;
  year: number;
  text: string;
  kind: 'world' | 'karma' | 'life' | 'chess' | 'village';
}

export interface BodyState {
  species: SpeciesId;
  pos: Vec;
  hp: number;
  ageDays: number;
  biomass: number;
  genome: Genome;
  knownGenes: string[];       // библиотека генофонда
  discoveredSpecies: SpeciesId[];
  kills: number;
  absorbedCount: number;
  synDiscovered: string[];
  hunger?: number;
}

export interface SoulStats {
  memory: number;       // Память
  will: number;         // Воля
  adaptation: number;   // Адаптация
  perception: number;   // Восприятие
  resonance: number;    // Резонанс
  integrity: number;    // Целостность
}

export interface Achievement {
  id: string;
  title: string;
  desc: string;
  soulXp: number;
  got: boolean;
}

export interface MemoryFragment {
  id: string;
  title: string;
  text: string;
  unlocked: boolean;
}

export interface SoulState {
  life: number;               // номер воплощения
  xp: number;
  level: number;              // уровень души 1..5
  karma: number;              // -100..100
  stats: SoulStats;
  pastLives: PastLifeRecord[];
  decisions: KarmaEvent[];    // решения последней жизни (для суда)
  memories: MemoryFragment[];
  // ===== Дополнение к ТЗ: свойства души, отдельные от тела (п.12) =====
  resonantCore?: boolean;     // резонансное ядро сплетено — переходит между жизнями
  spiritMemory?: number;      // сколько жизней душа жила с ядром (усиливает чутьё)
  // ===== Дополнение: «Резонанс души» — многомерная модель и место рождения =====
  traits?: SoulTraits;        // черты души (устойчивость/глубина/хищность/память)
  birthRegion?: RegionId;     // регион, в котором душа родилась последний раз
  // ===== Дополнение: «Чаша Отголосов» — открытые места мира (карта-восприятие) =====
  discoveredRegions?: string[]; // id мировых регионов, открытых за все жизни (память души о географии)
}

export interface PastLifeRecord {
  life: number;
  species: SpeciesId;
  ageDays: number;
  cause: string;
  karma: number;
  xpGained: number;
  summary: string;
}

export type DeathCause = 'old_age' | 'killed' | 'starved' | 'mutation';

export interface JudgmentResult {
  ageDays: number;
  cause: DeathCause;
  karma: number;
  verdict: 'Свет' | 'Равновесие' | 'Тьма';
  xpGained: number;
  achievements: Achievement[];
  decisions: KarmaEvent[];
  memoriesUnlocked: MemoryFragment[];
  summary: string;
  newSoulLevel: number;
  levelUp: boolean;
  // ===== Дополнение: «Резонанс души» — итог для экрана Суда (п.10) =====
  traitDeltas?: { key: string; name: string; from: number; to: number }[];
  regionOdds?: { id: RegionId; name: string; pct: number; difficulty: string }[];
  regionReasons?: string[];
}

export interface ReincarnOption {
  id: string;
  species: SpeciesId;
  name: string;
  desc: string;
  perkGene?: string;
  mods?: Partial<Record<'maxHp' | 'atk' | 'def' | 'speed' | 'perception', number>>;
}

// ===== Magic Chess (ТЗ §14) =====
export type ChessRole = 'Защитник' | 'Боец' | 'Стрелок' | 'Маг' | 'Поддержка' | 'Командир';

export interface ChessUnitDef {
  id: string;
  name: string;
  role: ChessRole;
  species: SpeciesId;
  hp: number;
  atk: number;
  def: number;
  range: number;              // 1 — ближний, 2+ — дальний
  atkCdMs: number;
  trait: string;
  synergies: string[];        // теги: 'Лес' | 'Хищник' | 'Стража' | 'Дух'
}

export interface ChessUnit {
  uid: number;
  defId: string;
  side: 'player' | 'enemy';
  r: number;                  // ряд 0..4
  c: number;                  // колонка 0..6
  hp: number;
  cd: number;
  alive: boolean;
  stunT: number;
  slowT: number;
  poisonT: number;
}

// Слой Presentation-событий (ТЗ §17): симуляция сообщает канвасу «что произошло»,
// рендер превращает это в частицы/цифры урона. Не сохраняется (§18 — только WorldData).
export interface FxEvent {
  kind: 'dmg' | 'hit' | 'kill' | 'absorb';
  x: number; y: number;             // мировые координаты события
  text?: string;                    // для 'dmg'/'absorb' — всплывающий текст
  color?: string;                   // 'r,g,b'
}

export interface ChessState {
  phase: 'setup' | 'battle' | 'result';
  round: number;
  boardR: number;             // 5
  boardC: number;             // 7
  playerRoster: ChessUnitDef[];
  placed: ChessUnit[];
  enemies: ChessUnit[];
  reserveLimit: number;
  battleLog: string[];
  result?: 'win' | 'lose';
  worldMinutesSpent: number;
  scenario: string;
  stake: string;              // что поставлено на кон (описание ставок мира)
}
