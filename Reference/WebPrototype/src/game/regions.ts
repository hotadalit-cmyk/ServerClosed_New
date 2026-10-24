// Дополнение к ТЗ: «Резонанс души и место перерождения».
// Слой Gameplay (переносим в C# без изменений — §17):
//  - 5 регионов рождения с разной экологией, опасностями и эволюционными путями;
//  - вероятностный выбор места рождения из черт души + состояния мира (не жёсткое назначение);
//  - многомерная модель души: черты растут только от реальных действий (§11, §12);
//  - мир живой: регионы «меняются между жизнями», потому что вес зависит от состояния мира.
// Мир продолжает симулироваться независимо — регионы задают условия рождения, а не скрипты.

import { clamp } from './rng';
import type { Poi } from './mapgen';
import type { RegionId, SoulTraits, SpeciesId } from './types';
import type { WorldData } from './world';

/** Срез карты для достройки Разлома (mapJson сериализуем — terrain как строки). */
interface RiftMapSlice {
  w: number; h: number;
  terrain: string[];
  deco: { id: number; kind: string; pos: { x: number; y: number }; r: number; seed: number }[];
}

/** Достройка Разлома на старых картах (миграция v3→v4) — структурный тип, без цикла импорта. */
export function ensureRiftWorldData(data: { seed: number; pois: Record<string, Poi>; mapJson: RiftMapSlice }): void {
  if (data.pois.rift) return;
  // детерминированные фиксированные вариации (§23): согласованы с addRiftToMap в mapgen
  const pos = { x: 45, y: 45 }; // MAP_W-7, MAP_H-7
  const r = 5.5;
  data.pois.rift = { kind: 'rift', pos, r };
  const map = data.mapJson;
  const idx = (x: number, y: number) => y * map.w + x;
  for (let y = 0; y < map.h; y++) {
    for (let x = 0; x < map.w; x++) {
      const d = Math.hypot(x - pos.x, y - pos.y);
      if (d < r * (0.82 + ((x * 7 + y * 13) % 5) * 0.05)) map.terrain[idx(x, y)] = 'ruin';
    }
  }
  let id = map.deco.reduce((m, d) => Math.max(m, d.id), 0) + 1;
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * Math.PI * 2;
    map.deco.push({ id: id++, kind: 'stone', pos: { x: pos.x + Math.cos(a) * r * 0.8, y: pos.y + Math.sin(a) * r * 0.8 }, r: 0.5, seed: (i * 7919) % 1e9 });
  }
  for (let i = 0; i < 4; i++) {
    const a = (i / 4) * Math.PI * 2 + 0.5, rr = 1.2 + (i % 2) * 1.4;
    map.deco.push({ id: id++, kind: 'crystal', pos: { x: pos.x + Math.cos(a) * rr, y: pos.y + Math.sin(a) * rr }, r: 0.45, seed: (i * 104729) % 1e9 });
  }
  map.deco.push({ id: id++, kind: 'bones', pos: { x: pos.x, y: pos.y }, r: 0.2, seed: 42 });
}

export interface RegionMods {
  biomassMul: number;       // богатство добычи этой жизни
  geneChanceMul: number;    // щедрость генофонда
  aggressionMul: number;    // злость хищников
  oreBonus: number;         // доп. руда с кристальных жил
  spiritRmul: number;       // радиус духовного чутья
  depthMul: number;         // скорость роста духовной глубины за жизнь
}

export type RegionHazard = 'none' | 'poison_spores' | 'rift_storm';

export interface RegionDef {
  id: RegionId;
  name: string;
  difficulty: string;
  desc: string;
  perk: string;
  minus: string;
  hazard: RegionHazard;
  mods: RegionMods;
  spawnPoint(pois: Record<string, Poi>): { x: number; y: number };
  spawnPlan: [SpeciesId, number][];   // гарантированная первая пища — первая (п.8: без неразрешимых ситуаций)
}

export const REGIONS: Record<RegionId, RegionDef> = {
  quiet_cradle: {
    id: 'quiet_cradle',
    name: 'Тихая колыбель',
    difficulty: 'низкая',
    desc: 'Мягкая почва у поселения, много слабых организмов, минимум хищников. Хорошее место для развития первых органов чувств.',
    perk: 'Спокойный старт: хищники замечают вас позже.',
    minus: 'Мало редких генов и магических минералов.',
    hazard: 'none',
    mods: { biomassMul: 1.0, geneChanceMul: 0.85, aggressionMul: 0.7, oreBonus: 0, spiritRmul: 1.0, depthMul: 1.0 },
    spawnPoint: (pois) => ({ x: pois.village.pos.x - 6.5, y: pois.village.pos.y + 6.5 }),
    spawnPlan: [['slug', 2], ['rabbit', 1], ['firefly', 1]],
  },
  root_garden: {
    id: 'root_garden',
    name: 'Сад глубинных корней',
    difficulty: 'умеренная',
    desc: 'Богатая экосистема грибной рощи: конкуренты за пищу, ядовитые грибы, небольшие хищники.',
    perk: 'Быстрый рост: +20% биомассы с добычи.',
    minus: 'Ядовитые споры жалят тело без споровой защиты.',
    hazard: 'poison_spores',
    mods: { biomassMul: 1.2, geneChanceMul: 1.0, aggressionMul: 1.0, oreBonus: 0, spiritRmul: 1.0, depthMul: 1.0 },
    spawnPoint: (pois) => ({ x: pois.mushroom_grove.pos.x + 2.2, y: pois.mushroom_grove.pos.y + 1.6 }),
    spawnPlan: [['slug', 2], ['mushling', 2], ['beetle', 1]],
  },
  bone_narrows: {
    id: 'bone_narrows',
    name: 'Костяные теснины',
    difficulty: 'высокая',
    desc: 'Логова у волчьей норы: агрессивные существа, дефицит пищи, чужая территория. Приходится прятаться и доедать остатки чужой добычи.',
    perk: 'Сильные защитные и хищные гены: шанс добыть ген +30%.',
    minus: 'Добыча тощая (−20% биомассы), хищники злее и зорче.',
    hazard: 'none',
    mods: { biomassMul: 0.8, geneChanceMul: 1.3, aggressionMul: 1.35, oreBonus: 0, spiritRmul: 1.0, depthMul: 1.0 },
    spawnPoint: (pois) => ({ x: pois.burrow.pos.x + 3.6, y: pois.burrow.pos.y + 3.4 }),
    spawnPlan: [['slug', 2], ['beetle', 2], ['wolf', 1]],
  },
  forgotten_vein: {
    id: 'forgotten_vein',
    name: 'Забытая жила',
    difficulty: 'экстремальная',
    desc: 'Кристальная ложбинка: недостаток органической пищи, насыщенные магией минералы, нестабильная среда.',
    perk: 'Магическая руда рядом: кристальные жилы дают +1 руды, чутьё души растёт в полтора раза быстрее.',
    minus: 'Органическая пища бедна (−25% биомассы).',
    hazard: 'none',
    mods: { biomassMul: 0.75, geneChanceMul: 1.15, aggressionMul: 1.0, oreBonus: 1, spiritRmul: 1.15, depthMul: 1.5 },
    spawnPoint: (pois) => ({ x: pois.crystal_hollow.pos.x + 2.6, y: pois.crystal_hollow.pos.y + 3.0 }),
    spawnPlan: [['slug', 2], ['crystalslug', 1], ['shardling', 1]],
  },
  soul_rift: {
    id: 'soul_rift',
    name: 'Разлом забытых душ',
    difficulty: 'предельная',
    desc: 'Искажённая реальность у Разлома: древние сущности, опасные магические потоки. Обычные способы выживания могут оказаться бесполезными.',
    perk: 'Уникальные духовные эволюции: бури обостряют чутьё (+50% радиуса), глубина души растёт в полтора раза быстрее.',
    minus: 'Магический шторм выжигает тело рядом с Разломом.',
    hazard: 'rift_storm',
    mods: { biomassMul: 0.9, geneChanceMul: 1.15, aggressionMul: 1.1, oreBonus: 0, spiritRmul: 1.1, depthMul: 1.5 },
    spawnPoint: (pois) => ({ x: pois.rift.pos.x - 2.2, y: pois.rift.pos.y - 2.2 }),
    spawnPlan: [['slug', 2], ['spirit', 1]],
  },
};

export function regionDef(id: RegionId | undefined | null): RegionDef {
  return REGIONS[id ?? 'quiet_cradle'];
}

// ===== Многомерная модель души: рост от реальных действий (п.2 Дополнения) =====

export function defaultTraits(): SoulTraits {
  return { resilience: 25, depth: 5, predator: 5, memory: 0 };
}

/**
 * Пересчёт черт души по итогам прожитой жизни. Только реальные события:
 * убийства, милосердие, пожертвования, боссы, шторма Разлома, воспоминания.
 * Прирост за одну жизнь ограничен — душа меняется постепенно (баланс п.9).
 */
export function updateSoulTraits(
  prev: SoulTraits,
  worldData: WorldData,
  info: { memUnlocked: number; cause: string },
): SoulTraits {
  const f = worldData.flags;
  const p = worldData.player;
  const evil = Math.max(0, -(f.karmaTotal ?? 0));
  const good = Math.max(0, f.karmaTotal ?? 0);
  const questsDone = (['wolves', 'hides', 'myco', 'shards', 'famine', 'titan'] as string[])
    .filter((q) => f[`quest_done_${q}`] === 1).length;
  const mercy = f.alphaKilled && !f.wolfQuestDone ? 1 : 0;

  const t: SoulTraits = { ...prev };
  // Устойчивость: старость, боссы, оборона, глубины, бури
  t.resilience += clamp(
    (info.cause === 'old_age' ? 5 : 0) + (f.monarchKilled ? 4 : 0) + (f.titanKilled ? 5 : 0)
    + (f.raidDefended ? 4 : 0) + (f.dungeonEntered ? 2 : 0) + p.mutations * 0.5,
    0, 24,
  );
  // Духовная глубина: ядро, духи, воспоминания, Разлом
  t.depth += clamp(
    (f.senseCore ? 10 : 0) + (f.spiritAbsorbed ? 6 : 0) + info.memUnlocked * 4
    + (f.loreRift ?? 0) * 1.5 + (f.riftStormSurvived ? 3 : 0),
    0, 22,
  );
  // Хищная природа: убийства и хищные поступки против милосердия и щедрости
  t.predator += clamp(
    Math.min(18, p.kills * 1.1) + evil * 0.10 - Math.min(9, (f.donateCount ?? 0) * 1.5) - mercy * 3,
    -20, 24,
  );
  // Память души: воспоминания, изученный мир, прошлое
  t.memory += clamp(
    info.memUnlocked * 5 + Math.min(8, p.discoveredSpecies.length * 0.6) + questsDone * 1.5,
    0, 20,
  );
  for (const k of ['resilience', 'depth', 'predator', 'memory'] as const) {
    t[k] = Math.round(clamp(t[k], 0, 100));
  }
  return t;
}

// ===== Вероятности рождения (п.3: вероятностная система, не жёсткое назначение) =====

/** Вес региона: черты души + состояние живого мира (мир сам реагирует — п.4). */
export function birthWeights(t: SoulTraits, karma: number, worldData: WorldData): Record<RegionId, number> {
  const good = Math.max(0, karma);
  const evil = Math.max(0, -karma);
  const eq = Math.max(0, 20 - Math.abs(karma)); // души равновесия
  const w: Record<RegionId, number> = {
    // добрая душа НЕ гарантированно получает лёгкую жизнь (п.9) — только склонность
    quiet_cradle: 26 + good * 0.30 + (100 - t.predator) * 0.18 - t.depth * 0.06,
    root_garden: 20 + eq * 0.35 + t.memory * 0.08 + t.depth * 0.10,
    bone_narrows: 8 + t.predator * 0.42 + evil * 0.12 + t.resilience * 0.12,
    forgotten_vein: 5 + t.resilience * 0.28 + t.depth * 0.18,
    soul_rift: 1.5 + t.depth * 0.32 + t.memory * 0.16 + (t.depth >= 50 ? 8 : 0),
  };
  // мир помнит и реагирует: голодный/неспокойный мир хуже колыбели, побеждённые боссы открывают магию
  const v = worldData.village;
  if (v.food < 20 || v.stability < 35) w.quiet_cradle -= 8;
  if (worldData.flags.monarchKilled) w.forgotten_vein += 6;
  if (worldData.flags.titanKilled) w.soul_rift += 5;
  if ((v.raidsSurvived ?? 0) >= 2) w.bone_narrows += 5;
  for (const k of Object.keys(w) as RegionId[]) w[k] = Math.max(1, w[k]); // невозможных регионов нет
  return w;
}

export interface RegionOdds { id: RegionId; name: string; pct: number; difficulty: string }

export function normalizeWeights(w: Record<RegionId, number>): RegionOdds[] {
  const total = Object.values(w).reduce((s, x) => s + x, 0) || 1;
  return (Object.keys(w) as RegionId[]).map((id) => ({
    id,
    name: REGIONS[id].name,
    difficulty: REGIONS[id].difficulty,
    pct: Math.round((w[id] / total) * 100),
  })).sort((a, b) => b.pct - a.pct);
}

/** Детерминированный ролл места рождения по накопленным весам (§23 воспроизводимость). */
export function rollRegion(w: Record<RegionId, number>, roll: number): RegionId {
  const total = Object.values(w).reduce((s, x) => s + x, 0);
  let acc = roll * total;
  for (const id of Object.keys(w) as RegionId[]) {
    acc -= w[id];
    if (acc <= 0) return id;
  }
  return 'quiet_cradle';
}

/** Объяснение вероятностей для экрана Суда (п.10: какие качества что повысили). */
export function explainBirth(t: SoulTraits, karma: number, worldData: WorldData): string[] {
  const lines: string[] = [];
  if (t.predator >= 40) lines.push(`Хищная природа ${Math.round(t.predator)} — тянет к территориям постоянной борьбы (Костяные теснины).`);
  if (t.depth >= 35) lines.push(`Духовная глубина ${Math.round(t.depth)} — открывает магические области (Забытая жила, Разлом забытых душ).`);
  if (t.resilience >= 45) lines.push(`Устойчивость ${Math.round(t.resilience)} — выживете там, где слабые души гибнут.`);
  if (t.memory >= 35) lines.push(`Память души ${Math.round(t.memory)} — дольше пребывания в небытии между жизнями.`);
  if (karma >= 40) lines.push(`Карма +${Math.round(karma)} — мир склонен дать вам передышку, но не гарантирует её.`);
  if (karma <= -40) lines.push(`Карма ${Math.round(karma)} — тьма в душе зовёт туда, где сила решает всё.`);
  if (Math.abs(karma) < 40 && t.predator < 30 && lines.length === 0) lines.push('Равновесная душа без резких склонностей — мир предлагает самые разнообразные пути.');
  const v = worldData.village;
  if (v.food < 20 || v.stability < 35) lines.push('Мир истощён прошлой жизнью — Тихая колыбель уже не так тиха.');
  if (worldData.flags.titanKilled || worldData.flags.monarchKilled) lines.push('Павшие владыки подземелий — магия ищет новые русла.');
  return lines.slice(0, 4);
}
