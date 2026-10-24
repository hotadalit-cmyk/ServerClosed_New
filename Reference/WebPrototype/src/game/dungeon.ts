// Слой Simulation: подземелья-инстансы (ТЗ §6 — регионы).
// Регион 2 «Грибные Катакомбы» и Регион 3 «Кристальные Пещеры» построены на одном
// реестре DungeonDef: генерация, план спавна, врата, палитра, босс. Карта детерминирована
// сидом мира (§23 воспроизводимость); время в подземелье — то же мировое (§7).

import { mulberry32, type Rng } from './rng';
import type {
  DungeonFloorData, DungeonState, DungeonTerrainKind, DungeonId, SpeciesId, Vec,
} from './types';
import type { Poi } from './mapgen';

export type { DungeonId };

export const DUNGEON_W = 22;
export const DUNGEON_H = 22;

export interface DungeonPalette {
  rockA: string; rockB: string;
  floorA: string; floorB: string;
  glowA: string; glowB: string;
  glowColor: string;        // 'rgba(r,g,b,' — дописывается alpha
  puddle: string;
  sporeColor: string;       // 'rgba(r,g,b,' — дописывается alpha
  gateGlow: string;         // hex свечения врат
}

export interface DungeonDef {
  id: DungeonId;
  name: string;
  depths: number;
  gatePos(pois: Record<string, Poi>): Vec;
  enterLog: string;
  exitLog: string;
  cacheLabel: string;       // текст кнопки подбора
  cacheKind: 'mushroom' | 'crystal' | 'river';
  bossName: string;         // пусто — подземелье без владыки (транзитная артерия)
  bossTag: string;          // Creature.questTag босса
  bossSpecies: SpeciesId;   // вид босса (для спавна)
  bossFlag: string;         // flags[...] = 1 когда босс повержен
  bossReward: number;       // биомасса за босса
  bossGene: string;         // гарантированный ген-трофей
  spawnPlan(depth: number, bossAlive: boolean): [SpeciesId, number][];
  palette: DungeonPalette;
}

const CATALOGS_PALETTE: DungeonPalette = {
  rockA: '#0a0a0d', rockB: '#08080b',
  floorA: '#1d1a15', floorB: '#1b1814',
  glowA: '#17301f', glowB: '#152c1e',
  glowColor: 'rgba(120,220,150,',
  puddle: '#101f22',
  sporeColor: 'rgba(150,240,170,',
  gateGlow: '#8fe0a0',
};

const CRYSTAL_PALETTE: DungeonPalette = {
  rockA: '#0b0b12', rockB: '#090910',
  floorA: '#191d21', floorB: '#161a1e',
  glowA: '#16242f', glowB: '#14222c',
  glowColor: 'rgba(110,210,235,',
  puddle: '#0e1a20',
  sporeColor: 'rgba(150,220,240,',
  gateGlow: '#7fd4e8',
};

const CHANNEL_PALETTE: DungeonPalette = {
  rockA: '#080b10', rockB: '#070910',
  floorA: '#141a20', floorB: '#121820',
  glowA: '#15252e', glowB: '#132230',
  glowColor: 'rgba(100,170,220,',
  puddle: '#0d1a24',
  sporeColor: 'rgba(120,190,230,',
  gateGlow: '#6fb8e0',
};

// Родовая норка: тёплая органическая мини-вселенная первой/новой жизни
export const NEST_PALETTE: DungeonPalette = {
  rockA: '#0d0a07', rockB: '#0a0806',
  floorA: '#262019', floorB: '#211b14',
  glowA: '#32281a', glowB: '#2d2416',
  glowColor: 'rgba(232,192,110,',
  puddle: '#141a18',
  sporeColor: 'rgba(226,196,140,',
  gateGlow: '#e8c07e',
};

export const NEST_W = 18;
export const NEST_H = 13;

export const DUNGEONS: Record<DungeonId, DungeonDef> = {
  catacombs: {
    id: 'catacombs',
    name: 'Грибные Катакомбы',
    depths: 3,
    gatePos: (pois) => ({ x: pois.mushroom_grove.pos.x + 2.4, y: pois.mushroom_grove.pos.y - 1.6 }),
    enterLog: 'Вы спускаетесь в Грибные Катакомбы. Споры светятся, и темнота дышит.',
    exitLog: 'Вы поднимаетесь к свету. Лес встречает вас запахом дождя.',
    cacheLabel: 'Взять запасы',
    cacheKind: 'mushroom',
    bossName: 'Грибной Монарх',
    bossTag: 'monarch',
    bossSpecies: 'myco_monarch',
    bossFlag: 'monarchKilled',
    bossReward: 30,
    bossGene: 'bioluminescent',
    spawnPlan: (depth, bossAlive) => {
      switch (depth) {
        case 1: return [['sporeling', 4], ['mycenoid', 1]];
        case 2: return [['sporeling', 3], ['mycenoid', 3]];
        case 3: return bossAlive
          ? [['myco_monarch', 1], ['mycenoid', 1], ['sporeling', 1]]
          : [['mycenoid', 2], ['sporeling', 3]];
        default: return [];
      }
    },
    palette: CATALOGS_PALETTE,
  },
  crystal_caves: {
    id: 'crystal_caves',
    name: 'Кристальные Пещеры',
    depths: 3,
    gatePos: (pois) => ({ x: pois.crystal_hollow.pos.x + 2.4, y: pois.crystal_hollow.pos.y + 1.6 }),
    enterLog: 'Вы входите в Кристальные Пещеры. Воздух звенит, и стены поют на грани слуха.',
    exitLog: 'Вы выбираетесь наружу. Каменная звенящая тишина остаётся за спиной.',
    cacheLabel: 'Выломать жилу',
    cacheKind: 'crystal',
    bossName: 'Кристальный Титан',
    bossTag: 'titan',
    bossSpecies: 'crystal_titan',
    bossFlag: 'titanKilled',
    bossReward: 45,
    bossGene: 'prismatic_core',
    spawnPlan: (depth, bossAlive) => {
      switch (depth) {
        case 1: return [['shardling', 4], ['golem', 1]];
        case 2: return [['shardling', 3], ['golem', 2]];
        case 3: return bossAlive
          ? [['crystal_titan', 1], ['golem', 1], ['shardling', 1]]
          : [['golem', 2], ['shardling', 3]];
        default: return [];
      }
    },
    palette: CRYSTAL_PALETTE,
  },
  // «Чаша Отголосов»: Подземное русло — центральная транспортная артерия пещерного
  // мира (регион 5 из карты). Реки приносят пищу из разных биомов, течения разносят
  // споры. Владыки нет: это дорога мира, а не тронный зал (Этап 1 — играбельный участок).
  underground_channel: {
    id: 'underground_channel',
    name: 'Подземное русло',
    depths: 2,
    gatePos: (pois) => ({ x: 33.8, y: 42.2 }), // затопленный вход у юго-западного берега пруда
    enterLog: 'Вы проскальзываете в Подземное русло. Холодное течение несёт запахи чужих земель.',
    exitLog: 'Течение выносит вас наружу. Вода Чаши всегда возвращается к топям.',
    cacheLabel: 'Подобрать принос',
    cacheKind: 'river',
    bossName: '',
    bossTag: '',
    bossSpecies: 'toad',
    bossFlag: '',
    bossReward: 0,
    bossGene: '',
    spawnPlan: (depth) => {
      switch (depth) {
        case 1: return [['slug', 2], ['toad', 2], ['mycenoid', 1]]; // течения принесли споры Катакомб
        case 2: return [['toad', 2], ['spider', 2], ['slug', 1]];
        default: return [];
      }
    },
    palette: CHANNEL_PALETTE,
  },
  birth_nest: {
    id: 'birth_nest',
    name: 'Родовая норка',
    depths: 1,
    gatePos: (pois) => ({ x: pois.burrow?.pos.x ?? 20, y: pois.burrow?.pos.y ?? 45 }), // не используется для входа — норка открывается сама при рождении
    enterLog: '',
    exitLog: 'Вы вылазите наружу из родовой норки.',
    cacheLabel: 'Разрыть принос',
    cacheKind: 'mushroom',
    bossName: '',
    bossTag: '',
    bossSpecies: 'beetle',
    bossFlag: '',
    bossReward: 0,
    bossGene: '',
    spawnPlan: () => [['slug', 2], ['beetle', 2], ['mushling', 1]],
    palette: NEST_PALETTE,
  },
};

/** Хеш строки в число (для смешивания id подземелья в сид генерации). */
function hashStr(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

/** Генерация одного этажа: комнаты + коридоры, детерминированно по сиду подземелья и глубине. */
/** Родовая норка: маленькая замкнутая мини-вселенная (18×13) с мягким органическим контуром. */
export function generateNestFloor(seed: number): DungeonFloorData {
  const rng = mulberry32((seed ^ 0x0e57cafe) >>> 0);
  const w = NEST_W, h = NEST_H;
  const terrain: DungeonTerrainKind[] = new Array(w * h).fill('rock');
  const idx = (x: number, y: number) => y * w + x;
  const cx = w / 2, cy = h / 2 - 0.5;
  // неровный органический контур: эллипс с шумом по углу
  for (let y = 1; y < h - 1; y++) {
    for (let x = 1; x < w - 1; x++) {
      const dx = (x + 0.5 - cx) / (w / 2 - 1.6);
      const dy = (y + 0.5 - cy) / (h / 2 - 1.4);
      const a = Math.atan2(dy, dx);
      const wob = 1 + 0.16 * Math.sin(a * 3 + 1.7) + 0.11 * Math.sin(a * 5 + 0.4) + 0.07 * Math.sin(a * 8 + 2.2);
      if (dx * dx + dy * dy <= wob * wob) terrain[idx(x, y)] = 'floor';
    }
  }
  // тёплый мох-свечение и маленькая лужа — источник влаги
  for (let y = 1; y < h - 1; y++) for (let x = 1; x < w - 1; x++) {
    if (terrain[idx(x, y)] !== 'floor') continue;
    const r = rng();
    if (r < 0.09) terrain[idx(x, y)] = 'glow';
    else if (r < 0.115) terrain[idx(x, y)] = 'puddle';
  }
  // выход-лунка снизу (лаз наружу) и кладка сверху — где слепой комочек рождается
  const up: Vec = { x: Math.floor(cx), y: h - 3 };   // лунка-выход
  const down: Vec = { x: Math.floor(cx), y: 2 };     // кладка (декоративный центр норки)
  for (const [px, py] of [[up.x, up.y], [down.x, down.y]] as const) {
    terrain[idx(px, py)] = 'floor';
    terrain[idx(px - 1, py)] = 'floor';
    terrain[idx(px + 1, py)] = 'floor';
  }
  return { depth: 1, w, h, terrain, up, down };
}

export function generateDungeonFloor(seed: number, depth: number, dungeonId: DungeonId): DungeonFloorData {
  const rng = mulberry32((seed ^ (depth * 0x5f356495) ^ hashStr(dungeonId)) >>> 0);
  const w = DUNGEON_W, h = DUNGEON_H;
  const terrain: DungeonTerrainKind[] = new Array(w * h).fill('rock');
  const idx = (x: number, y: number) => y * w + x;
  const carve = (x: number, y: number, t: DungeonTerrainKind = 'floor') => {
    if (x > 0 && y > 0 && x < w - 1 && y < h - 1) terrain[idx(x, y)] = t;
  };

  // комнаты
  const rooms: { x: number; y: number; w: number; h: number }[] = [];
  const roomCount = 5 + (depth >= 2 ? 1 : 0);
  let guard = 0;
  while (rooms.length < roomCount && guard++ < 200) {
    const rw = rng.int(3, 6), rh = rng.int(3, 5);
    const rx = rng.int(1, w - rw - 2), ry = rng.int(1, h - rh - 2);
    const overlaps = rooms.some((r) => rx < r.x + r.w + 1 && rx + rw + 1 > r.x && ry < r.y + r.h + 1 && ry + rh + 1 > r.y);
    if (overlaps) continue;
    rooms.push({ x: rx, y: ry, w: rw, h: rh });
    for (let y = ry; y < ry + rh; y++) for (let x = rx; x < rx + rw; x++) carve(x, y);
  }

  // коридоры между последовательными комнатами (L-образные)
  for (let i = 1; i < rooms.length; i++) {
    const a = rooms[i - 1], b = rooms[i];
    const ax = Math.floor(a.x + a.w / 2), ay = Math.floor(a.y + a.h / 2);
    const bx = Math.floor(b.x + b.w / 2), by = Math.floor(b.y + b.h / 2);
    let x = ax, y = ay;
    while (x !== bx) { carve(x, y); x += Math.sign(bx - x); }
    while (y !== by) { carve(x, y); y += Math.sign(by - y); }
    carve(bx, by);
  }

  // светящийся мох / кристаллы и лужи на полу
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (terrain[idx(x, y)] !== 'floor') continue;
    const r = rng();
    if (r < 0.07 + depth * 0.02) carve(x, y, 'glow');
    else if (r < 0.095) carve(x, y, 'puddle');
  }

  const first = rooms[0];
  const last = rooms[rooms.length - 1];
  const up: Vec = first ? { x: Math.floor(first.x + first.w / 2), y: Math.floor(first.y + first.h / 2) } : { x: 2, y: 2 };
  const down: Vec = last && rooms.length > 1
    ? { x: Math.floor(last.x + last.w / 2), y: Math.floor(last.y + last.h / 2) }
    : { x: w - 3, y: h - 3 };
  if (terrain[idx(up.x, up.y)] === 'rock') carve(up.x, up.y);
  if (terrain[idx(down.x, down.y)] === 'rock') carve(down.x, down.y);

  return { depth, w, h, terrain, up, down };
}

/** Полное состояние подземелья при первом входе в этой жизни. */
export function createDungeonState(seed: number, dungeonId: DungeonId, returnPos: Vec): DungeonState {
  const def = DUNGEONS[dungeonId];
  const rng: Rng = mulberry32((seed ^ 0xd00ca7e ^ hashStr(dungeonId)) >>> 0);
  const floors: DungeonFloorData[] = [];
  if (dungeonId === 'birth_nest') {
    floors.push(generateNestFloor(seed)); // мини-вселенная с собственным генератором
  } else {
    for (let d = 1; d <= def.depths; d++) floors.push(generateDungeonFloor(seed, d, dungeonId));
  }
  const caches: DungeonState['caches'] = [];
  let cid = 1;
  for (const f of floors) {
    const count = f.depth < def.depths ? 2 : 1;
    let guard = 0;
    let placed = 0;
    while (placed < count && guard++ < 300) {
      const x = rng.int(1, f.w - 2), y = rng.int(1, f.h - 2);
      const t = f.terrain[y * f.w + x];
      if (t !== 'floor') continue;
      if (Math.hypot(x - f.up.x, y - f.up.y) < 2.5) continue;
      if (f.depth < def.depths && Math.hypot(x - f.down.x, y - f.down.y) < 2.5) continue;
      caches.push({ id: cid++, depth: f.depth, pos: { x, y }, taken: false });
      placed++;
    }
  }
  return { active: false, depth: 1, returnPos, floors, caches, explored: floors.map(() => [] as number[]) };
}

// ===== Пути кармы (§12) =====

export interface KarmaPathDef {
  name: 'Свет' | 'Равновесие' | 'Тьма';
  desc: string;
}

export function karmaPathOf(karma: number): KarmaPathDef {
  if (karma >= 40) {
    return {
      name: 'Свет',
      desc: 'Благодать: посёлок отвечает взаимностью — каждый день +2 к запасам еды и +1 биомассы вам.',
    };
  }
  if (karma <= -40) {
    return {
      name: 'Тьма',
      desc: 'Родство хищников: волки, ткачи и жабы не трогают вас первыми; на грани смерти вы бьёте на пятую часть сильнее.',
    };
  }
  return {
    name: 'Равновесие',
    desc: 'Лес нейтрален к вам: ни милости, ни голодных глаз в темноте.',
  };
}

// ===== Стадии посёлка (§15: цивилизация растёт) =====

export function villageStageOf(population: number): { name: string; foodBonus: number } {
  if (population >= 16) return { name: 'Посад', foodBonus: 2 };
  if (population >= 10) return { name: 'Деревня', foodBonus: 1 };
  return { name: 'Хутор', foodBonus: 0 };
}
