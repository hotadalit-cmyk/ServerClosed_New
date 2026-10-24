// Дополнение к ТЗ: «Эволюция восприятия слизня».
// Слой Gameplay (переносим в C# без изменений — §17):
//  - состояние органов восприятия тела (последовательное развитие от усвоенной биомассы);
//  - отдельный счётчик магической руды → резонансное ядро → духовное чутьё;
//  - перегрузка от избытка магической энергии;
//  - гены/старение/карма/реинкарнация влияют на рецепторы (п.12 Дополнения).
// Принцип: мир симулируется ПОЛНОСТЬЮ и независимо — эта система ограничивает
// только ИНФОРМАЦИЮ, доступную игроку, а не поведение мира.

import type { SpeciesId } from './types';

export type SenseOrgan =
  | 'touch' | 'chemo' | 'vibration' | 'thermal' | 'light' | 'vision' | 'hearing' | 'spirit';

/** Порядок развития телесных рецепторов; духовное чутьё — отдельный путь (руда → ядро). */
export const SENSE_ORDER: SenseOrgan[] = ['touch', 'chemo', 'vibration', 'thermal', 'light', 'vision', 'hearing'];

export interface OrganInfo { name: string; short: string; desc: string }

export const ORGAN_INFO: Record<SenseOrgan, OrganInfo> = {
  touch: {
    name: 'Осязание', short: 'Осяз',
    desc: 'Единственный врождённый канал. Контур поверхностей и существ вплотную к телу; отпечатки угасают.',
  },
  chemo: {
    name: 'Химическое восприятие', short: 'Хим',
    desc: 'Рецепторы читают запахи: съедобное и падаль чуются на расстоянии даже во тьме.',
  },
  vibration: {
    name: 'Вибрационное восприятие', short: 'Вибр',
    desc: 'Почва доносит шаги: движущиеся существа ощущаются как расходящиеся кольца.',
  },
  thermal: {
    name: 'Тепловое восприятие', short: 'Тепл',
    desc: 'Тепло кровью существ и людей проступает тёплым пятном; холоднокровных едва заметно.',
  },
  light: {
    name: 'Светочувствительность', short: 'Свет',
    desc: 'Светочувствительные пятна видят источники света: огни, светляков, свечение врат.',
  },
  vision: {
    name: 'Зрение', short: 'Зрен',
    desc: 'Настоящие глаза. Мир вокруг обретает цвет, форму и глубину.',
  },
  hearing: {
    name: 'Слух', short: 'Слух',
    desc: 'Шаги, погоня и хищники выдаюут себя звуком дальше, чем видно глазами.',
  },
  spirit: {
    name: 'Духовное чутьё', short: 'Дух',
    desc: 'Свойство души, а не тела: следы душ, магические поля и отпечатки событий.',
  },
};

/** Порог усвоенной биомассы для пробуждения органа (осязание врождённое, чутьё — от руды). */
export const ORGAN_THRESHOLDS: Record<Exclude<SenseOrgan, 'touch' | 'spirit'>, number> = {
  chemo: 8,
  vibration: 18,
  thermal: 30,
  light: 44,
  vision: 60,
  hearing: 80,
};

export const ORE_CORE_THRESHOLD = 6;   // руды до формирования резонансного ядра
export const ORE_OVERLOAD = 10;        // энергия сверх запаса — перегрузка (п.11)
export const MAGIC_ENERGY_MAX = 16;
export const IMPRINT_LIFE = 8;         // секунд жизни отпечатка осязания (п.6)

/** Магическая руда: кристаллическая и духовная плоть (п.8 — отдельный счётчик усвоения). */
export const ORE_YIELD: Partial<Record<SpeciesId, number>> = {
  crystalslug: 2,
  shardling: 2,
  golem: 3,
  crystal_titan: 6,
  spirit: 3,
};

export interface SensoryState {
  biomassTotal: number;      // усвоено биомассы этим телом (общий счётчик)
  feed: Record<SenseOrgan, number>; // питание по органам: диета решает, ЧТО развивается (Дополнение: диета/выбор)
  focus: SenseOrgan | null;  // фокус игрока: выбранный орган растёт быстрее (выбор игрока)
  ore: number;               // усвоено магической руды — отдельный счётчик (п.8)
  magicEnergy: number;       // накопленная магическая энергия после ядра (перегрузка)
  overloadT: number;         // таймер до следующего урона перегрузки
  unlocked: SenseOrgan[];    // пробуждённые ТЕЛЕСНЫЕ органы (умирают вместе с телом)
  explored: number[];        // память разведанных тайлов надземья (тело)
}

export function createSensoryState(): SensoryState {
  const feed = {} as Record<SenseOrgan, number>;
  for (const o of SENSE_ORDER) feed[o] = 0;
  feed.spirit = 0;
  return { biomassTotal: 0, feed, focus: null, ore: 0, magicEnergy: 0, overloadT: 0, unlocked: ['touch'], explored: [] };
}

// ===== Диета: что пожираешь — то и развивается (Дополнение от игрока) =====
// Каждый источник пищи даёт основной вклад в «свои» органы + общий ток всем.
export const DIET_FEED: Partial<Record<string, Partial<Record<SenseOrgan, number>>>> = {
  // падаль и детрит — химия разложения
  slug: { chemo: 0.9, vibration: 0.25 },
  beetle: { chemo: 0.7, vibration: 0.7 },
  mushling: { chemo: 1.0, vision: 0.15 },
  mycenoid: { chemo: 1.0, light: 0.2 },
  sporeling: { chemo: 0.9, light: 0.25 },
  myco_monarch: { chemo: 1.4, vision: 0.4 },
  // подвижная мелочь — вибрации
  spider: { vibration: 1.0, chemo: 0.3 },
  toad: { vibration: 0.7, thermal: 0.35 },
  // тёплые тела — тепловое восприятие и звук
  rabbit: { thermal: 0.9, hearing: 0.4 },
  deer: { thermal: 1.1, hearing: 0.5 },
  wolf: { thermal: 1.0, hearing: 0.8, vibration: 0.4 },
  // свет и образы
  firefly: { light: 1.2, vision: 0.6 },
  crystalslug: { light: 1.0, vision: 0.7 },
  shardling: { light: 0.8, vision: 0.8 },
  golem: { vision: 1.0, light: 0.5, vibration: 0.4 },
  crystal_titan: { vision: 1.6, light: 1.0 },
  // разумные — голоса и тепло
  guard: { hearing: 1.2, thermal: 0.6 },
  spirit: { light: 0.8, hearing: 0.6 },
  // принос в норку (тайник)
  cache_mushroom: { chemo: 0.8, vibration: 0.4 },
  cache_crystal: { light: 0.9, vision: 0.7 },
  cache_river: { chemo: 0.6, vibration: 0.4, thermal: 0.3 },
};

export const DIET_LABELS: Record<SenseOrgan, string> = {
  touch: 'любое соприкосновение',
  chemo: 'грибы, споры, падаль, жуки',
  vibration: 'жуки, пауки, жабы, камень',
  thermal: 'теплокровные: кролики, олени, волки',
  light: 'светляки и кристальные существа',
  vision: 'кристаллы, светляки, крупные образы',
  hearing: 'волки, олени, стражи, духи',
  spirit: 'только руда и ядро',
};

const UNIVERSAL_TRICKLE = 0.12; // всякая биомасса слегка питает все рецепторы
const FOCUS_MUL = 1.75;         // фокус игрока усиливает приток (выбор развития)

/**
 * Усвоение пищи питает рецепторы ПО ДИЕТЕ: основной вклад — совпадающим органам,
 * слабый общий ток — всем; фокус игрока усиливает выбранный канал.
 * Возвращает список ВНОВЬ пробуждённых органов.
 */
export function grantBiomass(
  s: SensoryState,
  amount: number,
  thrMul: Record<string, number>,
  diet?: Partial<Record<SenseOrgan, number>>,
): SenseOrgan[] {
  s.biomassTotal += amount;
  const unlocked: SenseOrgan[] = [];
  for (const o of SENSE_ORDER) {
    if (o === 'touch' || o === 'spirit') continue;
    let gain = UNIVERSAL_TRICKLE * amount;
    if (diet && diet[o]) gain += (diet[o] as number) * amount;
    if (s.focus === o) gain *= FOCUS_MUL;
    s.feed[o] += gain;
    if (!s.unlocked.includes(o) && s.feed[o] >= ORGAN_THRESHOLDS[o] * (thrMul[o] ?? 1)) {
      s.unlocked.push(o);
      unlocked.push(o);
    }
  }
  return unlocked;
}

/** Выбор игрока: вложить биомассу тела напрямую в орган (конкурирует с генами). */
export function investInOrgan(s: SensoryState, organ: SenseOrgan, amount: number, thrMul: Record<string, number>): SenseOrgan[] {
  if (organ === 'touch' || organ === 'spirit' || amount <= 0) return [];
  s.feed[organ] += amount;
  const unlocked: SenseOrgan[] = [];
  if (!s.unlocked.includes(organ) && s.feed[organ] >= ORGAN_THRESHOLDS[organ] * (thrMul[organ] ?? 1)) {
    s.unlocked.push(organ);
    unlocked.push(organ);
  }
  return unlocked;
}

export function setFocus(s: SensoryState, organ: SenseOrgan | null) {
  s.focus = organ;
}

/** Прогресс органа для HUD: сколько питания накоплено и сколько нужно. */
export function organProgress(s: SensoryState, organ: Exclude<SenseOrgan, 'touch' | 'spirit'>, thrMul: Record<string, number>): { cur: number; need: number } {
  return { cur: Math.floor(s.feed[organ] * 10) / 10, need: Math.round(ORGAN_THRESHOLDS[organ] * (thrMul[organ] ?? 1)) };
}

/** Миграция v5→v6: старые unlocks → feed по порогам; излишек биомассы — в первый закрытый орган. */
export function migrateSensesV5toV6(s: SensoryState): void {
  if (!s.feed) s.feed = createSensoryState().feed;
  let leftover = Math.max(0, s.biomassTotal);
  for (const o of SENSE_ORDER) {
    if (o === 'touch' || o === 'spirit') continue;
    if (s.unlocked.includes(o)) { s.feed[o] = Math.max(s.feed[o] ?? 0, ORGAN_THRESHOLDS[o]); leftover = Math.max(0, leftover - ORGAN_THRESHOLDS[o]); }
    else s.feed[o] = s.feed[o] ?? 0;
  }
  for (const o of SENSE_ORDER) {
    if (o === 'touch' || o === 'spirit') continue;
    if (!s.unlocked.includes(o) && leftover > 0) { s.feed[o] += leftover; break; }
  }
  if (s.focus === undefined) s.focus = null;
}

export interface SenseMods {
  touchR: number; chemoR: number; vibroR: number; thermalR: number;
  lightR: number; visionR: number; hearingR: number; spiritR: number;
  thr: Record<string, number>;   // множители порогов (гены облегчают развитие, п.12)
  overloadAdd: number;           // запас прочности от Призматического ядра
}

const BASE_R: Record<string, number> = {
  touch: 1.75, chemo: 3.6, vibro: 4.6, thermal: 4.0, light: 5.5, vision: 6.5, hearing: 6.0, spirit: 7.5,
};

/**
 * Модификаторы восприятия (п.12 Дополнения):
 *  - гены: Химические сенсоры, Гео-сенсоры, Изначальное свечение, Охотничий инстинкт,
 *          Духовный резонанс, Призматическое ядро;
 *  - старение §10: незрелое тело и угасание слабее чувствуют;
 *  - карма §12: выраженный путь усиливает духовное чутьё.
 */
export function senseMods(installed: string[], ageFrac: number, path: string, spiritMemory: number, depth = 0): SenseMods {
  const m: SenseMods = {
    touchR: BASE_R.touch, chemoR: BASE_R.chemo, vibroR: BASE_R.vibro, thermalR: BASE_R.thermal,
    lightR: BASE_R.light, visionR: BASE_R.vision, hearingR: BASE_R.hearing, spiritR: BASE_R.spirit,
    thr: {}, overloadAdd: 0,
  };
  const has = (id: string) => installed.includes(id);
  if (has('sensors')) { m.chemoR *= 1.35; m.thr.chemo = 0.6; }
  if (has('geo_sense')) { m.vibroR *= 1.3; m.thr.vibration = 0.6; }
  if (has('bioluminescent')) { m.lightR *= 1.3; m.thr.light = 0.6; m.visionR *= 1.15; }
  if (has('hunter_instinct')) {
    m.chemoR *= 1.12; m.vibroR *= 1.12; m.thermalR *= 1.12;
    m.lightR *= 1.12; m.visionR *= 1.12; m.hearingR *= 1.12;
  }
  if (has('spirit_resonance')) m.spiritR *= 1.3;
  if (has('prismatic_core')) m.overloadAdd = 3;
  // Дополнение «Резонанс души» (п.6): глубина души ускоряет формирование рецепторов,
  // но полноценного зрения не даёт — пороги мягко ниже, тело всё равно начинается слепым
  const thrBoost = Math.min(0.25, depth * 0.0025);
  if (thrBoost > 0) {
    for (const k of ['chemo', 'vibration', 'thermal', 'light', 'vision', 'hearing']) {
      m.thr[k] = (m.thr[k] ?? 1) * (1 - thrBoost);
    }
  }
  m.spiritR *= 1 + Math.min(0.2, depth * 0.002);
  // старение (§10): рецепторы развиваются к расцвету и слабеют к угасанию
  let ageMul = 1;
  if (ageFrac < 0.15) ageMul = 0.85;        // Зарождение — рецепторы незрелы
  else if (ageFrac > 0.85) ageMul = 0.78;   // Угасание
  else if (ageFrac > 0.7) ageMul = 0.9;     // Старение
  if (ageMul !== 1) {
    m.touchR *= ageMul; m.chemoR *= ageMul; m.vibroR *= ageMul; m.thermalR *= ageMul;
    m.lightR *= ageMul; m.visionR *= ageMul; m.hearingR *= ageMul; m.spiritR *= ageMul;
  }
  // путь души (§12): и Свет, и Тьма обостряют взгляд на души
  if (path === 'Свет' || path === 'Тьма') m.spiritR *= 1.25;
  // память души: чем больше жизней с ядром, тем увереннее чутьё (отдельные правила души)
  if (spiritMemory > 0) m.spiritR *= Math.min(1.6, 1 + spiritMemory * 0.15);
  return m;
}

/** Усвоение руды (п.8): до ядра копит счётчик, после ядра — магическую энергию (риск перегрузки). */
export function grantOre(s: SensoryState, n: number, coreActive: boolean): void {
  s.ore += n;
  if (coreActive) s.magicEnergy = Math.min(MAGIC_ENERGY_MAX, s.magicEnergy + 2 * n);
}

/** Посекундный тик: энергия медленно оседает, перегрузка спадает сама. */
export function tickSenses(s: SensoryState, dt: number): void {
  if (s.magicEnergy > 0) s.magicEnergy = Math.max(0, s.magicEnergy - dt * 0.05);
}

/** Ближайшая цель развития для HUD/панели: орган с максимальной долей питания. */
export function nextOrganGoal(
  s: SensoryState,
  thrMul: Record<string, number>,
): { organ: Exclude<SenseOrgan, 'touch' | 'spirit'>; cur: number; need: number } | null {
  let best: { organ: Exclude<SenseOrgan, 'touch' | 'spirit'>; cur: number; need: number } | null = null;
  let bestFrac = -1;
  for (const o of SENSE_ORDER) {
    if (o === 'touch' || o === 'spirit' || s.unlocked.includes(o)) continue;
    const organ = o as Exclude<SenseOrgan, 'touch' | 'spirit'>;
    const need = Math.max(1, Math.round(ORGAN_THRESHOLDS[organ] * (thrMul[organ] ?? 1)));
    const frac = s.feed[organ] / need;
    if (frac > bestFrac) { bestFrac = frac; best = { organ, cur: Math.floor(s.feed[organ] * 10) / 10, need }; }
  }
  return best;
}
