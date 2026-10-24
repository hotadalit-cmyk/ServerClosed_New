// Слой Gameplay: душа, карма, Суд души и реинкарнация (ТЗ §11, §12).
// Три сущности данных разделены: Body (тело) — в world.data.player,
// Soul (душа) — здесь, Legacy (наследие) — pastLives + память мира (world сохраняется между жизнями).

import type {
  SoulState, SoulStats, JudgmentResult, Achievement, MemoryFragment,
  ReincarnOption, KarmaEvent, DeathCause, SpeciesId,
} from './types';
import type { WorldData } from './world';
import { SPECIES } from './creatures';
import { dailyMortality } from './time';
import { mulberry32 } from './rng';
import { QUESTS } from './quests';
import { defaultTraits, updateSoulTraits, birthWeights, normalizeWeights, explainBirth } from './regions';

export const SOUL_LEVELS = [0, 60, 150, 300, 520]; // xp-пороги уровней 1..5

export function soulLevelOf(xp: number): number {
  let lvl = 1;
  for (let i = 0; i < SOUL_LEVELS.length; i++) if (xp >= SOUL_LEVELS[i]) lvl = i + 1;
  return lvl;
}

export function newSoul(): SoulState {
  return {
    life: 1,
    xp: 0,
    level: 1,
    karma: 0,
    stats: { memory: 1, will: 1, adaptation: 1, perception: 1, resonance: 1, integrity: 10 },
    pastLives: [],
    decisions: [],
    resonantCore: false,
    spiritMemory: 0,
    traits: defaultTraits(),          // «Резонанс души»: нейтральная душа первой жизни
    birthRegion: 'quiet_cradle',      // первая жизнь — сравнительно благоприятная среда (п.5 баланса)
    memories: [
      { id: 'm_first', title: 'Первая память', text: 'Ты помнишь тепло — не солнца, а чего-то большего. Мир был цел, а потом что-то треснуло над горизонтом. Разлом?', unlocked: true },
      { id: 'm_rift', title: 'Шёпот духа', text: '«Разлом дышит, — пело нечто древнее. — Он ждёт того, кто соберёт себя из чужих жизней.»', unlocked: false },
      { id: 'm_light', title: 'Свет помнит тебя', text: 'На мгновение после смерти ты видел сад из стекла и листвы. Он ждал того, кто не сломает чужую волю.', unlocked: false },
      { id: 'm_dark', title: 'Тьма узнала твой вкус', text: 'Что-то под Разломом причмокнуло, когда твоя душа прошла мимо. Ему понравилось.', unlocked: false },
      { id: 'm_age', title: 'Предсмертная ясность', text: 'Дожив до глубокой старости, ты вспомнил(а) поле из твоего самого первого сна. Оно существует.', unlocked: false },
      { id: 'm_chess', title: 'Поле сражений', text: 'Память о другом ты: ты стоял над доской мира и двигал существа, как фигуры.', unlocked: false },
    ],
  };
}

export function initAchievements(): Achievement[] {
  return [
    { id: 'first_absorb', title: 'Первое поглощение', desc: 'Впустить чужую жизнь в свою.', soulXp: 8, got: false },
    { id: 'five_species', title: 'Исследователь плоти', desc: 'Изучить строение пяти видов.', soulXp: 15, got: false },
    { id: 'syn1', title: 'Химия жизни', desc: 'Открыть первую синергию генов.', soulXp: 15, got: false },
    { id: 'syn3', title: 'Композитор генома', desc: 'Открыть три синергии.', soulXp: 25, got: false },
    { id: 'defend_village', title: 'Щит Опушки', desc: 'Отбить набег волков на поселение.', soulXp: 20, got: false },
    { id: 'merciful', title: 'Милосердный хищник', desc: 'Изгнать стаю, убив лишь вожака.', soulXp: 20, got: false },
    { id: 'old_age', title: 'Полный круг', desc: 'Умереть от старости, прожив долгую жизнь.', soulXp: 25, got: false },
    { id: 'spirit_touch', title: 'Коснуться духа', desc: 'Поглотить лесного духа.', soulXp: 18, got: false },
    { id: 'apex', title: 'Вершина пищевой цепи', desc: 'Поглотить волка, будучи слизнем.', soulXp: 20, got: false },
    { id: 'crystal', title: 'Минеральная жизнь', desc: 'Поглотить кристального слизня.', soulXp: 15, got: false },
    { id: 'descender', title: 'Вниз по грибнице', desc: 'Спуститься в Грибные Катакомбы.', soulXp: 15, got: false },
    { id: 'monarch', title: 'Трон из спор', desc: 'Повергнуть Грибного Монарха на третьей глубине.', soulXp: 30, got: false },
    { id: 'titan', title: 'Гора, что ходила', desc: 'Повергнуть Кристального Титана в Кристальных Пещерах.', soulXp: 30, got: false },
    { id: 'quests3', title: 'Друг Опушки', desc: 'Выполнить три задания поселян.', soulXp: 22, got: false },
    { id: 'senses_awake', title: 'Мир обрёл краски', desc: 'Развить зрение из одного лишь осязания.', soulXp: 18, got: false },
    { id: 'core_formed', title: 'Резонансное ядро', desc: 'Сплесть ядро из магической руды и открыть духовное чутьё.', soulXp: 25, got: false },
  ];
}

/** Суд души (§11.2): достижения, решения, карма — НЕ продолжительность жизни. */
export function judgeSoul(
  soul: SoulState,
  worldData: WorldData,
  achievements: Achievement[],
): JudgmentResult {
  const cause: DeathCause = worldData.deathCause ?? 'killed';
  const ageDays = worldData.player.ageDays;
  const karmaDelta = worldData.flags.karmaTotal ?? 0;
  const lifeXp = worldData.flags.soulXpLife ?? 0;

  // достижения этой жизни
  const unlockedNow: string[] = [];
  const discovered = worldData.player.discoveredSpecies;
  const syn = worldData.player.synDiscovered.length;
  if (worldData.player.absorbedCount > 0) unlockedNow.push('first_absorb');
  if (discovered.length >= 5) unlockedNow.push('five_species');
  if (syn >= 1) unlockedNow.push('syn1');
  if (syn >= 3) unlockedNow.push('syn3');
  if (worldData.flags.raidDefended) unlockedNow.push('defend_village');
  if (worldData.flags.alphaKilled && !worldData.flags.wolfQuestDone) unlockedNow.push('merciful');
  if (cause === 'old_age') unlockedNow.push('old_age');
  if (discovered.includes('spirit')) unlockedNow.push('spirit_touch');
  if (discovered.includes('wolf') && worldData.player.species === 'slug') unlockedNow.push('apex');
  if (discovered.includes('crystalslug')) unlockedNow.push('crystal');
  if (worldData.flags.dungeonEntered) unlockedNow.push('descender');
  if (worldData.flags.monarchKilled) unlockedNow.push('monarch');
  if (worldData.flags.titanKilled) unlockedNow.push('titan');
  if (QUESTS.filter((q) => worldData.flags[`quest_done_${q.id}`] === 1).length >= 3) unlockedNow.push('quests3');
  // эволюция восприятия (Дополнение к ТЗ): зрение и ядро — достижения души
  if (worldData.flags.senseVisionUnlock) unlockedNow.push('senses_awake');
  if (worldData.flags.senseCore) unlockedNow.push('core_formed');

  const gotNow: Achievement[] = [];
  for (const a of achievements) {
    if (!a.got && unlockedNow.includes(a.id)) {
      a.got = true;
      gotNow.push(a);
    }
  }

  // карма: поступки важнее прожитых дней (§11.2 — не награждаем за долголетие)
  const karma = Math.max(-100, Math.min(100, soul.karma + karmaDelta));
  const verdict: JudgmentResult['verdict'] = karma >= 40 ? 'Свет' : karma <= -40 ? 'Тьма' : 'Равновесие';

  // духовный опыт: достижения + поступки + чуть-чуть за сам опыт жизни
  const xpGained = gotNow.reduce((s, a) => s + a.soulXp, 0) + lifeXp + Math.min(6, Math.floor(ageDays / 4));
  const oldLevel = soul.level;
  const newLevel = soulLevelOf(soul.xp + xpGained);

  // воспоминания
  const memUnlocked: MemoryFragment[] = [];
  for (const m of soul.memories) {
    if (m.unlocked) continue;
    let open = false;
    if (m.id === 'm_rift' && (worldData.flags.spiritAbsorbed || (worldData.flags.loreRift ?? 0) >= 2)) open = true;
    if (m.id === 'm_light' && verdict === 'Свет') open = true;
    if (m.id === 'm_dark' && verdict === 'Тьма') open = true;
    if (m.id === 'm_age' && cause === 'old_age') open = true;
    if (m.id === 'm_chess' && worldData.flags.raidDefended) open = true;
    if (open) { m.unlocked = true; memUnlocked.push(m); }
  }

  // характеристики души растут от образа жизни (§11.1)
  const st = soul.stats;
  st.adaptation += Math.min(2, discovered.length / 3 | 0) > 0 ? 1 : 0;
  st.will += gotNow.some((a) => a.id === 'defend_village') ? 1 : 0;
  st.perception += worldData.player.discoveredSpecies.length >= 5 ? 1 : 0;
  st.resonance += verdict === 'Свет' ? 1 : verdict === 'Тьма' ? 0 : 0;
  st.integrity = Math.min(20, st.integrity + (cause === 'old_age' ? 2 : 1));
  st.memory += memUnlocked.length > 0 ? 1 : 0;

  // сводка жизни
  const causeText = cause === 'old_age' ? 'угас от старости' : cause === 'killed' ? 'погиб в схватке' : cause === 'mutation' ? 'разрушен нестабильным геномом' : 'погиб';
  const summary = `Воплощение №${soul.life}: ${SPECIES[worldData.player.species].name} — ${causeText} на ${Math.floor(ageDays)}-й день жизни. Изучено видов: ${discovered.length}. Поглощений: ${worldData.player.absorbedCount}. Карма жизни: ${karmaDelta > 0 ? '+' : ''}${karmaDelta}.`;

  // ===== Резонанс души (Дополнение к ТЗ, п.1–2, п.10): черты растут только от реальных действий =====
  const prevTraits = soul.traits ?? defaultTraits();
  const newTraits = updateSoulTraits(prevTraits, worldData, { memUnlocked: memUnlocked.length, cause });
  soul.traits = newTraits;
  const traitDeltas = [
    { key: 'karma', name: 'Карма', from: Math.max(-100, Math.min(100, soul.karma)), to: karma },
    { key: 'resilience', name: 'Устойчивость', from: prevTraits.resilience, to: newTraits.resilience },
    { key: 'depth', name: 'Духовная глубина', from: prevTraits.depth, to: newTraits.depth },
    { key: 'predator', name: 'Хищная природа', from: prevTraits.predator, to: newTraits.predator },
    { key: 'memory', name: 'Память души', from: prevTraits.memory, to: newTraits.memory },
  ];
  const regionOdds = normalizeWeights(birthWeights(newTraits, karma, worldData));
  const regionReasons = explainBirth(newTraits, karma, worldData);

  return {
    ageDays,
    cause,
    karma,
    verdict,
    xpGained,
    achievements: gotNow,
    decisions: worldData.decisions as KarmaEvent[],
    memoriesUnlocked: memUnlocked,
    summary,
    newSoulLevel: newLevel,
    levelUp: newLevel > oldLevel,
    traitDeltas,
    regionOdds,
    regionReasons,
  };
}

/** Варианты тел для нового воплощения (§11.3: слизень — базовая универсальная форма) */
export function reincarnationOptions(soul: SoulState): ReincarnOption[] {
  const opts: ReincarnOption[] = [
    {
      id: 'slug', species: 'slug', name: 'Лесной слизень',
      desc: 'Базовая форма. Слабая, пластичная, благодарная генетическим экспериментам.',
    },
  ];
  if (soul.level >= 2 || soul.stats.resonance >= 2) {
    opts.push({
      id: 'light_slug', species: 'slug', name: 'Светлый слизень',
      desc: 'Тело, помнящее резонанс духов: чуть крепче и зорче, родится с Духовным резонансом.',
      perkGene: 'spirit_resonance',
      mods: { maxHp: 4, perception: 20 },
    });
  }
  if (soul.stats.will >= 2 || soul.xp >= SOUL_LEVELS[1]) {
    opts.push({
      id: 'wolfblood_slug', species: 'slug', name: 'Слизень волчьей крови',
      desc: 'Память хищника вплетена в душу: агрессивнее и быстрее, родится с Охотничьим инстинктом.',
      perkGene: 'hunter_instinct',
      mods: { atk: 1, speed: 0.2 },
    });
  }
  return opts;
}

/**
 * Переход мира после смерти (§3.3): временной скачок + последствия.
 * Мир НЕ сбрасывается: посёлок живёт, популяции восстанавливаются, NPC стареют.
 */
export function advanceWorldAfterDeath(worldData: WorldData, judgment: JudgmentResult, days: number) {
  worldData.minutes += days * 1440;
  // посёлок переживает эти годы (упрощённо, уровень C)
  const v = worldData.village;
  const seasons = Math.max(1, Math.round(days / 24));
  for (let i = 0; i < seasons; i++) {
    v.food = Math.max(0, Math.min(80, v.food + (v.stability > 45 ? 8 : -4)));
    v.population = Math.max(3, Math.min(30, v.population + (v.food > 30 ? 1 : v.food < 12 ? -1 : 0)));
    v.stability = Math.max(5, Math.min(90, v.stability + (judgment.verdict === 'Свет' ? 2 : judgment.verdict === 'Тьма' ? -2 : 0)));
  }
  if (judgment.verdict === 'Свет') v.raidCdDays = Math.max(v.raidCdDays, 4);
  // NPC стареют за время перехода; старые могут умереть — мир живёт без игрока (§15, §20)
  const rng = mulberry32(worldData.seed ^ Math.round(worldData.minutes));
  for (const n of worldData.npcs) {
    if (!n.alive) continue;
    n.ageDays += days;
    if (n.ageDays > n.lifespan && rng.chance(Math.max(0.4, dailyMortality(n.ageDays, n.lifespan)))) {
      n.alive = false;
      v.stability = Math.max(5, v.stability - 8);
      worldData.log.push({ day: Math.floor(worldData.minutes / 1440), year: 1, text: `${n.name} не дожил(а) до ваших новых дней.`, kind: 'world' });
      if (n.id === 'mirra') {
        worldData.npcs.push({ id: 'ivo', name: 'Старейшина Иво', role: 'новый старейшина', pos: n.pos, ageDays: 40, lifespan: 400, alive: true, attitude: 40, dialogue: 'hello_successor' });
      }
      if (n.id === 'dar') {
        worldData.npcs.push({ id: 'rinsa', name: 'Охотница Ринса', role: 'новый охотник', pos: n.pos, ageDays: 30, lifespan: 320, alive: true, attitude: 35, dialogue: 'hello_successor' });
      }
      if (n.id === 'klumba') {
        worldData.npcs.push({ id: 'prokh', name: 'Огородник Прох', role: 'фермер', pos: n.pos, ageDays: 36, lifespan: 340, alive: true, attitude: 40, dialogue: 'hello_successor' });
      }
    }
  }
  // трупы прошлой жизни исчезают — мир чистится статистически (уровень C, §6)
  worldData.creatures = worldData.creatures.filter((c) => !c.dead);
  // наследие: отметка для истории
  worldData.log.push({ day: Math.floor(worldData.minutes / 1440), year: 1, text: `Воплощение завершилось (${judgment.cause === 'old_age' ? 'угасание' : 'гибель'}). Мир продолжается.`, kind: 'world' });
}
