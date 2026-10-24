// Слой Gameplay: генетика и эволюция (ТЗ §9).
// - Слоты органов, энергозатраты, стабильность генома, конфликты генов.
// - Синергии: примеры из §9.2 (электричество+паутина, кислота/коррозия+панцирь,
//   регенерация+жизненная энергия, яд+споровое облако).
// - Эволюция меняет не только цифры, но и внешний вид (визуальные теги — §9.3).

import type { GeneDef, SynergyDef, Genome } from './types';

export const GENES: Record<string, GeneDef> = {
  slime_coat: {
    id: 'slime_coat', name: 'Слизистый покров', category: 'physical',
    desc: 'Вязкая слизь смягчает удары. База любого слизня.',
    cost: 0, energy: 1, instability: 0,
    mods: { def: 1, maxHp: 4 }, source: 'Слизни',
  },
  sensors: {
    id: 'sensors', name: 'Химические сенсоры', category: 'instinct',
    desc: 'Щупальца читают запахи страха и крови. Расширяют восприятие.',
    cost: 0, energy: 1, instability: 0,
    mods: { perception: 40 }, source: 'Слизни',
  },
  chitin_plate: {
    id: 'chitin_plate', name: 'Хитиновый панцирь', category: 'physical',
    desc: 'Сегменты хитина жука наросли на спину. Тяжело, но прочно.',
    cost: 25, energy: 2, instability: 6,
    mods: { def: 3, maxHp: 10, speed: -0.15 }, source: 'Жуки-скребни',
  },
  strong_jaws: {
    id: 'strong_jaws', name: 'Сильные челюсти', category: 'physical',
    desc: 'Ротовой аппарат перестроен под рвущий укус.',
    cost: 20, energy: 2, instability: 5,
    mods: { atk: 4 }, source: 'Жуки-скребни',
  },
  wolf_muscle: {
    id: 'wolf_muscle', name: 'Волчья мощь', category: 'physical',
    desc: 'Мышечные пучки хищника. Резкие рывки, тяжёлое дыхание.',
    cost: 40, energy: 3, instability: 12,
    mods: { atk: 7, maxHp: 8, speed: 0.25 }, source: 'Лесные волки',
  },
  hunter_instinct: {
    id: 'hunter_instinct', name: 'Охотничий инстинкт', category: 'instinct',
    desc: 'Чужая память загонов и засад. Вы видите слабые места.',
    cost: 30, energy: 2, instability: 8,
    mods: { perception: 60, atk: 2 }, source: 'Лесные волки',
  },
  fast_legs: {
    id: 'fast_legs', name: 'Быстрые ноги', category: 'physical',
    desc: 'Мощная мускулатура бега. Слизень... бежит? Теперь да.',
    cost: 18, energy: 2, instability: 4,
    mods: { speed: 0.45 }, source: 'Полевки, косули',
  },
  metabolism: {
    id: 'metabolism', name: 'Эффективный метаболизм', category: 'instinct',
    desc: 'Каждая крошка идёт в дело. Жизненная энергия бьёт ключом.',
    cost: 22, energy: 1, instability: 3,
    mods: { energyMax: 4, hpRegen: 0.6, lifeDays: 2 }, source: 'Полевки, косули',
  },
  electric_organ: {
    id: 'electric_organ', name: 'Электрогенератор', category: 'elemental',
    desc: 'Органы светлячков шпарят разрядом. Требует проводящей слизи.',
    cost: 35, energy: 3, instability: 12,
    mods: { atk: 5 }, source: 'Искровые светлячки',
  },
  web_glands: {
    id: 'web_glands', name: 'Паутинные железы', category: 'elemental',
    desc: 'Липкие нити оплетают жертву и держат её на месте.',
    cost: 30, energy: 2, instability: 9,
    mods: { def: 2 }, source: 'Лесные ткачи',
  },
  poison_glands: {
    id: 'poison_glands', name: 'Ядовитые железы', category: 'elemental',
    desc: 'Укус несёт токсин, разъедающий волю к сопротивлению.',
    cost: 32, energy: 2, instability: 10,
    mods: { atk: 3 }, source: 'Пепельные жабы',
  },
  spore_cloud: {
    id: 'spore_cloud', name: 'Споровое облако', category: 'elemental',
    desc: 'Пора гриба-симбионта: вокруг вас висит облако спор.',
    cost: 28, energy: 2, instability: 8,
    mods: { maxHp: 6 }, source: 'Спорники',
  },
  regeneration: {
    id: 'regeneration', name: 'Регенерация', category: 'crystal',
    desc: 'Кристаллическая решётка срастается сама. Тело чинит себя.',
    cost: 45, energy: 3, instability: 11,
    mods: { hpRegen: 1.2, lifeDays: 2 }, source: 'Кристальные слизни',
  },
  crystal_armor: {
    id: 'crystal_armor', name: 'Кристаллическая броня', category: 'crystal',
    desc: 'Грани минерала торчат из тела. Почти неуязвимы, но медленны.',
    cost: 50, energy: 3, instability: 13,
    mods: { def: 7, maxHp: 14, speed: -0.25, lifeDays: 2 }, source: 'Кристальные слизни',
  },
  spirit_resonance: {
    id: 'spirit_resonance', name: 'Духовный резонанс', category: 'spirit',
    desc: 'Эхо лесного духа звучит в вашей душе. Суд души щедрее.',
    cost: 40, energy: 2, instability: 5,
    mods: { perception: 30, lifeDays: 3 }, source: 'Лесные духи',
  },
  life_core: {
    id: 'life_core', name: 'Ядро жизненной силы', category: 'spirit',
    desc: 'Сгусток чистой энергии жизни, согревающий плоть.',
    cost: 38, energy: 2, instability: 6,
    mods: { maxHp: 18, hpRegen: 0.5, energyMax: 3 }, source: 'Лесные духи, сердцевины',
  },
  // ===== Гены Грибных Катакомб (Регион 2, §6) =====
  bioluminescent: {
    id: 'bioluminescent', name: 'Изначальное свечение', category: 'elemental',
    desc: 'Тело горит холодным светом Монарха: вы видите то, что скрыто во тьме.',
    cost: 42, energy: 2, instability: 7,
    mods: { perception: 55, atk: 1 }, source: 'Грибной Монарх',
  },
  mycelial_bond: {
    id: 'mycelial_bond', name: 'Мицелиальная связь', category: 'instinct',
    desc: 'Ваша плоть сплетена с грибницей: в Катакомбах раны затягиваются почти на глазах.',
    cost: 30, energy: 2, instability: 5,
    mods: { hpRegen: 0.9, maxHp: 8 }, source: 'Мицелиальные ужасы',
  },
  spore_veil: {
    id: 'spore_veil', name: 'Споровая вуаль', category: 'elemental',
    desc: 'Облако спор искажает ваш запах: хищники замечают вас на треть позже.',
    cost: 26, energy: 2, instability: 6,
    mods: { def: 2, maxHp: 4 }, source: 'Спорыши',
  },
  crystal_vein: {
    id: 'crystal_vein', name: 'Кристальные жилы', category: 'crystal',
    desc: 'Жилы минерала проросли сквозь грибницу: тяжёлая, но твёрдая поросль.',
    cost: 44, energy: 3, instability: 10,
    mods: { def: 4, atk: 2, speed: -0.1, lifeDays: 1 }, source: 'Грибной Монарх',
  },
  // ===== Гены Кристальных Пещер (Регион 3, §6) =====
  shard_skin: {
    id: 'shard_skin', name: 'Осколочная шкура', category: 'crystal',
    desc: 'Крошечные гранулы кварца въелись в дерму: каждый удар глохнет о крошку.',
    cost: 22, energy: 2, instability: 6,
    mods: { def: 3, speed: -0.05 }, source: 'Осколыши',
  },
  geo_sense: {
    id: 'geo_sense', name: 'Гео-сенсоры', category: 'instinct',
    desc: 'Тело ловит вибрацию камня за сотни шагов. Стены больше не обманут.',
    cost: 28, energy: 2, instability: 5,
    mods: { perception: 70, atk: 1 }, source: 'Кристальные стражи',
  },
  prismatic_core: {
    id: 'prismatic_core', name: 'Призматическое ядро', category: 'crystal',
    desc: 'Сердце Титана бьётся у вас в брюхе: свет преломляется в кровь, и тело становится крепче камня.',
    cost: 55, energy: 3, instability: 11,
    mods: { maxHp: 16, def: 3, energyMax: 3, hpRegen: 0.5, lifeDays: 1 }, source: 'Кристальный Титан',
  },
};

export const SYNERGIES: SynergyDef[] = [
  {
    id: 'electric_trap', name: 'Электрическая ловушка',
    genes: ['electric_organ', 'web_glands'],
    desc: 'Паутина проводит разряд: удары иногда парализуют врага.',
    mods: { atk: 2 }, special: 'stun',
  },
  {
    id: 'toxic_zone', name: 'Токсичное заражение',
    genes: ['poison_glands', 'spore_cloud'],
    desc: 'Споры несут яд: вокруг вас зоной вредит и ослабляет врагов.',
    mods: { maxHp: 4 }, special: 'toxicAura',
  },
  {
    id: 'fast_regen', name: 'Быстрое восстановление',
    genes: ['regeneration', 'life_core'],
    desc: 'Регенерация на жизненной силе: раны затягиваются втрое быстрее.',
    mods: { hpRegen: 2.0, maxHp: 8 }, special: undefined,
  },
  {
    id: 'corrosion_shield', name: 'Коррозионная защита',
    genes: ['poison_glands', 'chitin_plate'],
    desc: 'Панцирь сочится кислотой: атакующие получают ответный урон.',
    mods: { def: 2 }, special: 'corrosion',
  },
  {
    id: 'living_battery', name: 'Живая батарея',
    genes: ['crystal_armor', 'electric_organ'],
    desc: 'Кристаллы копируют разряд: повышенный запас энергии.',
    mods: { energyMax: 6, atk: 2 }, special: 'battery',
  },
  {
    id: 'apex_sense', name: 'Чутьё апекса',
    genes: ['hunter_instinct', 'sensors'],
    desc: 'Инстинкт хищника и химия слизи: вы чувствуете всё живое.',
    mods: { perception: 50, speed: 0.1 }, special: undefined,
  },
  {
    id: 'myco_network', name: 'Мицелиальная сеть',
    genes: ['mycelial_bond', 'spore_cloud'],
    desc: 'Вы — часть грибницы: регенерация втрое глубже, тело наливается жизнью.',
    mods: { hpRegen: 1.5, maxHp: 10 }, special: undefined,
  },
  {
    id: 'crystal_harmony', name: 'Кристальная гармония',
    genes: ['crystal_armor', 'prismatic_core'],
    desc: 'Кристаллы звучат в унисон: броня становится второй шкурой, а жизнь — долгой, как камень.',
    mods: { def: 4, maxHp: 14, lifeDays: 2 }, special: undefined,
  },
];

// Конфликты генов (§9.2: несовместимость) — пара не может быть установлена вместе.
export const GENE_CONFLICTS: [string, string][] = [
  ['crystal_armor', 'fast_legs'],   // кристалл ломается от резкого бега
  ['crystal_vein', 'fast_legs'],    // кристальные жилы — тот же минерал, тот же конфликт
  ['chitin_plate', 'slime_coat'],   // хитин не дружит со слизью (замещение покрова)
  ['spore_cloud', 'crystal_armor'], // кристалл не даёт спорам жить
  ['prismatic_core', 'spore_cloud'],// призматическое ядро выпаливает споры калением
];

export interface DerivedStats {
  maxHp: number; atk: number; def: number; speed: number; perception: number;
  energyMax: number; hpRegen: number; lifeDays: number;
  stability: number;       // 100 - штрафы
  energyUsed: number;
  synergies: SynergyDef[];
  visuals: Set<string>;    // визуальные теги для отрисовки эволюции
}

const BASE_BODY = { maxHp: 34, atk: 4, def: 1, speed: 2.0, perception: 130, energyMax: 4, hpRegen: 0.25, lifeDays: 14 };

export function computeDerived(genome: Genome): DerivedStats {
  const d: DerivedStats = {
    maxHp: BASE_BODY.maxHp, atk: BASE_BODY.atk, def: BASE_BODY.def,
    speed: BASE_BODY.speed, perception: BASE_BODY.perception,
    energyMax: BASE_BODY.energyMax, hpRegen: BASE_BODY.hpRegen, lifeDays: BASE_BODY.lifeDays,
    stability: 100, energyUsed: 0, synergies: [], visuals: new Set(['slug']),
  };
  const rec = d as unknown as Record<string, number>;
  for (const id of genome.installed) {
    const g = GENES[id];
    if (!g) continue;
    d.energyUsed += g.energy;
    d.stability -= g.instability;
    for (const [k, v] of Object.entries(g.mods)) {
      rec[k] = (rec[k] ?? 0) + (v as number);
    }
    d.visuals.add(g.category);
  }
  // Синергии
  const has = (id: string) => genome.installed.includes(id);
  for (const s of SYNERGIES) {
    if (has(s.genes[0]) && has(s.genes[1])) {
      d.synergies.push(s);
      d.visuals.add(s.id);
      for (const [k, v] of Object.entries(s.mods)) {
        rec[k] = (rec[k] ?? 0) + (v as number);
      }
    }
  }
  // Конфликты: за каждую пару — жёсткий штраф стабильности
  for (const [a, b] of GENE_CONFLICTS) {
    if (has(a) && has(b)) d.stability -= 15;
  }
  d.stability = Math.max(0, Math.min(100, d.stability));
  return d;
}

/** Сколько слотов/ёмкости дают узлы эволюции (базово 3 слота, 4 энергии) */
export const NODE_PLAN = [
  { cost: 30, slots: 1, capacity: 1 },
  { cost: 55, slots: 1, capacity: 1 },
  { cost: 90, slots: 1, capacity: 1 },
  { cost: 140, slots: 1, capacity: 1 },
];

export function nodeNextCost(nodes: number): number | null {
  return nodes < NODE_PLAN.length ? NODE_PLAN[nodes].cost : null;
}

export function canInstall(genome: Genome, geneId: string): { ok: boolean; reason?: string } {
  const g = GENES[geneId];
  if (!g) return { ok: false, reason: 'Неизвестный ген' };
  if (genome.installed.includes(geneId)) return { ok: false, reason: 'Уже установлен' };
  // конфликты — абсолютная несовместимость: показываем их раньше починки слотов/энергии (§9.2)
  for (const [a, b] of GENE_CONFLICTS) {
    if ((a === geneId && genome.installed.includes(b)) || (b === geneId && genome.installed.includes(a))) {
      return { ok: false, reason: `Конфликт с геном: ${GENES[a === geneId ? b : a].name}` };
    }
  }
  if (genome.installed.length >= genome.slots) return { ok: false, reason: 'Нет свободных слотов органов' };
  const d = computeDerived(genome);
  if (d.energyUsed + g.energy > genome.capacity) return { ok: false, reason: 'Недостаточно энергии генома' };
  return { ok: true };
}
