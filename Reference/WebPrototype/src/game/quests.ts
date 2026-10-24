// Слой Simulation/Gameplay: задания посёлка (ТЗ §13 — RPG-цикл, §15 — жители).
// Данные квестов — чистые определения; состояние — во flags мира (quest_accepted_/quest_done_/quest_progress_),
// поэтому задания сохраняются вместе с миром и переживают смерти (§11, §18).
// Прогресс пишут хуки мира (убийства/поглощения/вход в подземелье/пожертвования), сдача — через диалог NPC.

import type { SpeciesId } from './types';

export type QuestGiver = 'elder' | 'hunter' | 'farmer';

export interface QuestDef {
  id: string;
  giver: QuestGiver;
  title: string;
  desc: string;               // что требуется (текст журнала)
  hint: string;               // реплика-подсказка NPC при выдаче
  type: 'kill' | 'absorb' | 'dungeon_enter' | 'boss' | 'donate';
  target?: string;            // species id | DungeonId | тег босса
  need: number;
  reward: { biomass?: number; karma?: number; soulXp?: number; gene?: string };
  requires?: string;          // id квеста, который нужно завершить раньше
}

export const QUESTS: QuestDef[] = [
  {
    id: 'wolves', giver: 'hunter',
    title: 'Три шкуры',
    desc: 'Убить трёх лесных волков — посёлок боится зимы.',
    hint: '«Волки наглеют с каждым днём. Убей трёх — и я поверю, что лес не съест нас зимой.»',
    type: 'kill', target: 'wolf', need: 3,
    reward: { biomass: 25, karma: 3, soulXp: 2 },
  },
  {
    id: 'descend', giver: 'elder',
    title: 'Свет во тьме',
    desc: 'Спуститься в Грибные Катакомбы у грибной рощи.',
    hint: '«Под рощей лежат старые катакомбы. Грибница там поёт по ночам. Посмотри, что гниёт под нами — и расскажи.»',
    type: 'dungeon_enter', target: 'catacombs', need: 1,
    reward: { biomass: 20, karma: 4, soulXp: 3 },
  },
  {
    id: 'monarch', giver: 'elder',
    title: 'Сердце грибницы',
    desc: 'Повергнуть Грибного Монарха на третьей глубине Катакомб.',
    hint: '«Грибница видит сон — и в этом сне есть король. Сорви с него корону, и катакомбы выдохнут.»',
    type: 'boss', target: 'monarch', need: 1, requires: 'descend',
    reward: { biomass: 40, karma: 6, soulXp: 5 },
  },
  {
    id: 'shards', giver: 'hunter',
    title: 'Хрустальные осколки',
    desc: 'Поглотить трёх Осколышей из Кристальных Пещер у каменной ложбинки.',
    hint: '«На северо-западе камни звенят, будто в них спрятаны колокольчики. Принеси мне три осколочные туши — дознайся, что это.»',
    type: 'absorb', target: 'shardling', need: 3, requires: 'wolves',
    reward: { biomass: 30, karma: 3, soulXp: 4 },
  },
  {
    id: 'harvest', giver: 'farmer',
    title: 'Голодный год',
    desc: 'Трижды передать биомассу в запасы посёлка (по 10 за раз).',
    hint: '«Зима будет тощая. Три раза поделись своей диковинной биомассой — и я отпою тебе у костра.»',
    type: 'donate', need: 3,
    reward: { biomass: 0, karma: 6, soulXp: 4 },
  },
  {
    id: 'titan', giver: 'elder',
    title: 'Падший титан',
    desc: 'Повергнуть Кристального Титана на третьей глубине Кристальных Пещер.',
    hint: '«Сны о каменном исполине приходят теперь даже детям. Если грибница пала — пади и ты, титан пещер.»',
    type: 'boss', target: 'titan', need: 1, requires: 'monarch',
    reward: { biomass: 60, karma: 5, soulXp: 8 },
  },
];

export type QuestStatus = 'available' | 'active' | 'ready' | 'done';

export interface QuestView {
  q: QuestDef;
  status: QuestStatus;
  progress: number;
}

type Flags = Record<string, number>;

export function questAccepted(f: Flags, id: string): boolean { return f[`quest_accepted_${id}`] === 1; }
export function questDone(f: Flags, id: string): boolean { return f[`quest_done_${id}`] === 1; }
export function questProgress(f: Flags, id: string): number { return f[`quest_progress_${id}`] ?? 0; }

export function questStatus(f: Flags, q: QuestDef): QuestStatus {
  if (questDone(f, q.id)) return 'done';
  if (!questAccepted(f, q.id)) return 'available';
  return questProgress(f, q.id) >= q.need ? 'ready' : 'active';
}

/** Список квестов в порядке получения со статусами (для журнала). */
export function questViews(f: Flags): QuestView[] {
  return QUESTS.map((q) => ({ q, status: questStatus(f, q), progress: questProgress(f, q.id) }));
}

/** Квесты, которые этот NPC может выдать или принять. */
export function questsForGiver(f: Flags, giver: QuestGiver): QuestView[] {
  return QUESTS
    .filter((q) => q.giver === giver)
    .map((q) => ({ q, status: questStatus(f, q), progress: questProgress(f, q.id) }))
    .filter(({ q, status }) => status !== 'done' && (!q.requires || questDone(f, q.requires)));
}

/** Сколько заданий всего завершено (для достижения). */
export function questsCompletedCount(f: Flags): number {
  return QUESTS.reduce((n, q) => n + (questDone(f, q.id) ? 1 : 0), 0);
}

export function giverLabel(giver: QuestGiver): string {
  return giver === 'elder' ? 'Старейшина' : giver === 'hunter' ? 'Охотник' : 'Фермер';
}
