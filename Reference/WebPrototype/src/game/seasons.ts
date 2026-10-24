// Этап 2 карты мира: СЕЗОНЫ — год живёт по календарю, экология дышит (ТЗ §7 календарь).
// - 4 сезона × 6 дней: Пробуждение, Цветение, Увядание, Тьма.
// - Сезоны меняют: рождаемость популяций, голод хищников, урожай посёлка,
//   питательность пищи для органов восприятия (продолжение диеты — Дополнение от игрока).
// - Миграции: при смене сезона часть популяций смещается к пищевым узлам
//   (Тьма — теплокровные жмутся к посёлку и пруду, хищники идут за ними).
// - Родовая норка — мини-вселенная: тепло кладки, сезоны туда не доходят (лор Задачи 20).

import type { SpeciesId } from './types';

export type Season = 'Пробуждение' | 'Цветение' | 'Увядание' | 'Тьма';

export interface SeasonMods {
  birthMul: number;         // множитель рождаемости популяций (dailyTick §6)
  aggressionAdd: number;    // прибавка к радиусу/шансу агрессии хищников (голод)
  dietMul: number;          // питательность пищи для органов (диета → перцепция)
  villageFoodDelta: number; // ежедневная прибавка/убыль еды посёлка (урожай)
  fireflyMul: number;       // яркость светляков/света (визуал)
  hint: string;             // HUD-подсказка «что сезон делает»
}

export const SEASON_MODS: Record<Season, SeasonMods> = {
  'Пробуждение': {
    birthMul: 1.5, aggressionAdd: 0, dietMul: 1.0, villageFoodDelta: 1, fireflyMul: 1,
    hint: 'Молодая поросль: зверь выводит потомство, лес полон жизни',
  },
  'Цветение': {
    birthMul: 1.7, aggressionAdd: -0.05, dietMul: 1.05, villageFoodDelta: 2, fireflyMul: 1.15,
    hint: 'Пик изобилия: добычи много, хищники сыты и спокойны',
  },
  'Увядание': {
    birthMul: 0.9, aggressionAdd: 0.05, dietMul: 1.3, villageFoodDelta: 0, fireflyMul: 0.95,
    hint: 'Грибы плодоносят: пища жирнее, химическое восприятие растёт быстрее',
  },
  'Тьма': {
    birthMul: 0.25, aggressionAdd: 0.2, dietMul: 0.8, villageFoodDelta: -2, fireflyMul: 0.8,
    hint: 'Голод и стужа: добыча редеет, хищники дерзки, зверь жмётся к людям',
  },
};

/** Сезон по дню мира (6 дней в сезоне — календарь §7). */
export function seasonOf(totalDays: number): Season {
  const idx = Math.floor(totalDays / 6) % 4;
  return (['Пробуждение', 'Цветение', 'Увядание', 'Тьма'] as Season[])[idx];
}

/** Кто куда тянется в какой сезон (миграции): POI-ключи приюта. */
export const SEASON_MIGRATIONS: Partial<Record<Season, Partial<Record<string, string>>>> = {
  // Тьма: теплокровные жмутся к посёлку (там подкармливают) и пруду (не замерзает у ключей);
  // хищники стягиваются за добычей — волки к посёлку, пауки к грибной роще.
  'Тьма': {
    rabbit: 'village', deer: 'village', wolf: 'village',
    toad: 'pond', spider: 'mushroom_grove', firefly: 'stone_circle',
  },
  // Увядание: грибы плодоносят — грибная роща кормит слизней и жуков
  'Увядание': { mushling: 'mushroom_grove', slug: 'mushroom_grove', beetle: 'mushroom_grove' },
  // Пробуждение: рассредоточение по летним угодьям
  'Пробуждение': { deer: 'pond', rabbit: 'pond', wolf: 'burrow' },
};

/** Атмосферная запись в хронику при наступлении сезона. */
export const SEASON_LOG: Record<Season, string> = {
  'Пробуждение': 'Пробуждение. Талые воды шепчут, зверь выводит потомство — лес полон лёгкой добычи.',
  'Цветение': 'Цветение. Тёплый свет, нектар и споры в воздухе: пик изобилия. Хищники сыты и ленивы.',
  'Увядание': 'Увядание. Листва рыжеет, грибы плодоносят в полную силу — время химического восприятия.',
  'Тьма': 'Тьма. Стужа сжимает мир: добыча редеет, хищники дерзкие, зверь жмётся к людским очагам.',
};

/** Виды, чьи спавны сезонно усиливаются (осень — грибы, лето — светляки). */
export function seasonalSpawnBoost(sp: SpeciesId, season: Season): number {
  if (season === 'Увядание' && (sp === 'mushling' || sp === 'sporeling' || sp === 'mycenoid')) return 2.0;
  if (season === 'Цветение' && (sp === 'firefly' || sp === 'toad')) return 1.6;
  if (season === 'Тьма' && (sp === 'wolf' || sp === 'spider')) return 1.4; // голодные хищники плодятся на падали
  return 1;
}
