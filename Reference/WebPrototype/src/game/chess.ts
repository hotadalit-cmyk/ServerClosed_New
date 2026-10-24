// Слой Gameplay: Magic Chess — стратегический автобой (ТЗ §14, §20).
// - Расстановка: мировое время ОСТАНОВЛЕНО (§7).
// - Бой: автобой тиками; по завершении в мир уходит фиксированный небольшой интервал времени.
// - Временные боевые копии (не постоянные существа мира) — возраст не убивает юнитов посреди матча (§14).
// - Синергии видов/элементов, роли юнитов, командир.
// - Последствия боя меняют поселение и фракции (§14, §20).

import { mulberry32, type Rng, clamp } from './rng';
import type { ChessState, ChessUnit, ChessUnitDef } from './types';
import type { World } from './world';

export const BOARD_R = 5;
export const BOARD_C = 7;

/** Состав строится из того, что игрок открыл и от чего посёлок может выставить (§14) */
export function buildRoster(world: World): ChessUnitDef[] {
  const p = world.data.player;
  const roster: ChessUnitDef[] = [];
  roster.push({
    id: 'cmdr', name: 'Командир-слизень', role: 'Командир', species: 'slug',
    hp: Math.max(60, Math.round(world.stats.maxHp * 1.6)),
    atk: Math.max(6, Math.round(world.stats.atk * 1.15)),
    def: Math.max(2, Math.round(world.stats.def)),
    range: 1, atkCdMs: 900,
    trait: 'Аура командира: соседние союзники бьют на 20% сильнее.',
    synergies: ['Лес'],
  });
  roster.push({
    id: 'militia', name: 'Ополченец', role: 'Защитник', species: 'guard',
    hp: 100, atk: 7, def: 7, range: 1, atkCdMs: 1000,
    trait: 'Крепкая ограда: держит строй перед стрелками.',
    synergies: ['Стража', 'Лес'],
  });
  roster.push({
    id: 'archer', name: 'Лучник Дара', role: 'Стрелок', species: 'guard',
    hp: 50, atk: 10, def: 1, range: 3, atkCdMs: 1300,
    trait: 'Стреляет через полполя, но хрупок.',
    synergies: ['Стража'],
  });
  if (p.discoveredSpecies.includes('wolf')) {
    roster.push({
      id: 'ally_wolf', name: 'Волк-изгой', role: 'Боец', species: 'wolf',
      hp: 55, atk: 10, def: 2, range: 1, atkCdMs: 850,
      trait: 'Изгнанный из стаи — злее сородичей.',
      synergies: ['Хищник', 'Лес'],
    });
  }
  if (p.discoveredSpecies.includes('spirit')) {
    roster.push({
      id: 'grove_spirit', name: 'Дух рощи', role: 'Маг', species: 'spirit',
      hp: 36, atk: 11, def: 2, range: 2, atkCdMs: 1600,
      trait: 'Лечит союзников на 2 HP за ход (аура Духа).',
      synergies: ['Дух', 'Лес'],
    });
  }
  if (p.discoveredSpecies.includes('crystalslug')) {
    roster.push({
      id: 'crystal_guard', name: 'Кристальный страж', role: 'Защитник', species: 'crystalslug',
      hp: 90, atk: 5, def: 9, range: 1, atkCdMs: 1200,
      trait: 'Живая скала. Медленный, но почти несокрушимый.',
      synergies: ['Лес'],
    });
  }
  if (p.discoveredSpecies.includes('golem')) {
    roster.push({
      id: 'rune_golem', name: 'Рунный голем', role: 'Защитник', species: 'golem',
      hp: 130, atk: 9, def: 11, range: 1, atkCdMs: 1400,
      trait: 'Оживший камень пещер: медленный валун с тяжёлыми кулаками.',
      synergies: ['Стража', 'Лес'],
    });
  }
  if (p.discoveredSpecies.includes('mushling')) {
    roster.push({
      id: 'spore_choir', name: 'Хор спор', role: 'Поддержка', species: 'mushling',
      hp: 46, atk: 4, def: 3, range: 2, atkCdMs: 1500,
      trait: 'Каждый ход лечит самого раненого союзника на 5 HP.',
      synergies: ['Лес'],
    });
  }
  return roster;
}

export function createRaidChess(world: World): ChessState {
  const roster = buildRoster(world);
  const v = world.data.village;
  const limit = clamp(3 + Math.floor(v.military / 30), 3, 6);
  const raidNo = v.raidsSurvived + 1;
  const st: ChessState = {
    phase: 'setup',
    round: raidNo,
    boardR: BOARD_R,
    boardC: BOARD_C,
    playerRoster: roster,
    placed: [],
    enemies: [],
    reserveLimit: limit,
    battleLog: ['Стая подходит к ограде. Расставьте оборону — время остановилось.'],
    worldMinutesSpent: 0,
    scenario: `Набег №${raidNo}: «Волчья стая у Опушки»`,
    stake: 'На кону: запасы еды, жизни поселян и покой леса.',
  };
  // волчья стая: сила растёт с каждым набегом; первый набег — 2 волка и вожак,
  // далее стая полнеет (кривая сложности); вожак всегда в центре (0,3), никто не пересекается
  const rng = mulberry32(world.data.seed ^ (raidNo * 7919));
  const wolfHp = 40 + raidNo * 4;
  const spawnWolf = (r: number, c: number, alpha = false) => {
    st.enemies.push({
      uid: 100 + st.enemies.length, defId: alpha ? 'e_alpha' : 'e_wolf',
      side: 'enemy', r, c, hp: alpha ? 66 + raidNo * 4 : wolfHp, cd: rng.range(0.3, 1.2),
      alive: true, stunT: 0, slowT: 0, poisonT: 0,
    });
  };
  spawnWolf(0, 1); spawnWolf(0, 5);
  if (raidNo >= 2) { spawnWolf(1, 2); spawnWolf(1, 4); }
  if (raidNo >= 3) spawnWolf(1, 3);
  if (raidNo >= 4) spawnWolf(0, 2); // стая ширит фланги
  spawnWolf(0, 3, true); // вожак в центре
  return st;
}

let uidSeq = 1000;

export function chessPlace(st: ChessState, defId: string, r: number, c: number): boolean {
  if (st.phase !== 'setup') return false;
  if (r < 3) return false; // свои ряды — нижние два
  const occupied = (r: number, c: number) => st.placed.some((u) => u.alive && u.r === r && u.c === c);
  if (occupied(r, c)) return false;
  const existing = st.placed.find((u) => u.defId === defId);
  if (existing) {
    existing.r = r; existing.c = c; // переставить
    return true;
  }
  if (st.placed.length >= st.reserveLimit) return false;
  const def = st.playerRoster.find((d) => d.id === defId);
  if (!def) return false; // неизвестный юнит (не в ростере) — тихо отклоняем
  st.placed.push({
    uid: ++uidSeq, defId, side: 'player', r, c,
    hp: def.hp, cd: 0.4, alive: true, stunT: 0, slowT: 0, poisonT: 0,
  });
  return true;
}

export function chessRemove(st: ChessState, uid: number) {
  st.placed = st.placed.filter((u) => u.uid !== uid);
}

export function chessStart(st: ChessState) {
  if (st.placed.length === 0) return;
  st.phase = 'battle';
  st.battleLog.push('Рог ополчения трубит! Время снова пошло.');
}

const ENEMY_STATS: Record<string, { name: string; atk: number; def: number; range: number; cd: number }> = {
  e_wolf: { name: 'Лесной волк', atk: 8, def: 2, range: 1, cd: 0.95 },
  e_alpha: { name: 'Рваное Ухо, вожак', atk: 11, def: 3, range: 1, cd: 0.85 },
};

/** Данные врагов для UI (имена и базовый HP — для полосок здоровья) */
export const CHESS_ENEMY_UI: Record<string, { short: string; name: string; hp: number }> = {
  e_wolf: { short: 'Волк', name: 'Лесной волк', hp: 44 },
  e_alpha: { short: 'Вожак', name: 'Рваное Ухо, вожак стаи', hp: 70 },
};

function allUnits(st: ChessState): ChessUnit[] {
  return [...st.placed, ...st.enemies];
}

/** Один тик автобоя (~400 мс). Возвращает true, если бой завершён. */
export function chessTick(st: ChessState, rng: Rng): boolean {
  if (st.phase !== 'battle') return true;
  st.worldMinutesSpent += 3.3; // бой съедает небольшой фиксированный интервал мирового времени (§7)

  // синергии игрока
  const tags = new Set<string>();
  for (const u of st.placed) {
    if (!u.alive) continue;
    const d = st.playerRoster.find((x) => x.id === u.defId);
    d?.synergies.forEach((t) => tags.add(t));
  }
  const tagCount = (t: string) => [...tags].filter((x) => x === t).length;
  const hpMul = 1 + (tagCount('Лес') >= 3 ? 0.15 : tagCount('Лес') >= 2 ? 0.08 : 0);
  const atkMul = 1 + (tagCount('Хищник') >= 1 ? 0.12 : 0) + (tagCount('Лес') >= 3 ? 0.1 : 0);
  const defMul = 1 + (tagCount('Стража') >= 2 ? 0.15 : 0);
  const spiritHeal = tagCount('Дух') >= 1;

  for (const u of allUnits(st)) {
    if (!u.alive) continue;
    if ((u.stunT ?? 0) > 0) { u.stunT = Math.max(0, (u.stunT ?? 0) - 0.4); continue; }
    u.cd -= 0.4;
    const isPlayer = u.side === 'player';
    const def = isPlayer
      ? st.playerRoster.find((d) => d.id === u.defId)!
      : undefined;
    const estats = !isPlayer ? ENEMY_STATS[u.defId] : undefined;
    const myAtkBase = isPlayer ? def!.atk : estats!.atk;
    const myDef = isPlayer ? def!.def : estats!.def;
    const myRange = isPlayer ? def!.range : estats!.range;
    const myCd = isPlayer ? def!.atkCdMs / 1000 : estats!.cd;

    // цели
    const foes = (isPlayer ? st.enemies : st.placed).filter((x) => x.alive);
    if (foes.length === 0) break;
    let target = foes[0];
    let bd = Math.abs(foes[0].r - u.r) + Math.abs(foes[0].c - u.c);
    for (const f of foes) {
      const d = Math.abs(f.r - u.r) + Math.abs(f.c - u.c);
      if (d < bd) { bd = d; target = f; }
    }

    // командирская аура
    let aura = 1;
    if (isPlayer) {
      const cmdr = st.placed.find((x) => x.alive && x.defId === 'cmdr');
      if (cmdr && u.uid !== cmdr.uid && Math.abs(cmdr.r - u.r) + Math.abs(cmdr.c - u.c) === 1) aura = 1.2;
    }

    if (bd <= myRange) {
      if (u.cd <= 0) {
        u.cd = myCd;
        const foe = target;
        const foeDef = isPlayer
          ? ENEMY_STATS[foe.defId]?.def ?? 2
          : defMul * (st.playerRoster.find((d) => d.id === foe.defId)?.def ?? 1);
        const isSupport = isPlayer && def!.role === 'Поддержка';
        const dmg = Math.max(1, myAtkBase * (isPlayer ? atkMul * aura : 1) * rng.range(0.85, 1.15) - foeDef * 0.5);
        if (!isSupport) {
          foe.hp -= dmg;
          if (isPlayer && def!.role === 'Маг') {
            // маг бьёт по площади: 50% урона соседям цели
            for (const f of foes) {
              if (f !== foe && Math.abs(f.r - foe.r) + Math.abs(f.c - foe.c) === 1) f.hp -= dmg * 0.5;
            }
          }
        }
        if (isPlayer && def!.role === 'Поддержка') {
          // поддержка лечит самого раненого союзника вместо урона
          const wounded = st.placed.filter((x) => x.alive)
            .sort((a, b) => a.hp / st.playerRoster.find((d) => d.id === a.defId)!.hp - b.hp / st.playerRoster.find((d) => d.id === b.defId)!.hp)[0];
          if (wounded) {
            wounded.hp = Math.min(st.playerRoster.find((d) => d.id === wounded.defId)!.hp * hpMul, wounded.hp + 5);
            st.battleLog.push(`${def!.name} исцеляет союзника (+5).`);
          }
        }
      }
    } else {
      // шаг к цели
      const dr = Math.sign(target.r - u.r);
      const dc = Math.sign(target.c - u.c);
      const canR = dr !== 0 && !isOccupied(st, u, u.r + dr, u.c);
      const canC = dc !== 0 && !isOccupied(st, u, u.r, u.c + dc);
      if (canR && (Math.abs(target.r - u.r) >= Math.abs(target.c - u.c) || !canC)) u.r += dr;
      else if (canC) u.c += dc;
    }
  }

  // аура Духа лечит всех игроков
  if (spiritHeal) {
    for (const u of st.placed) {
      if (!u.alive) continue;
      const max = st.playerRoster.find((d) => d.id === u.defId)!.hp * hpMul;
      u.hp = Math.min(max, u.hp + 1);
    }
  }

  // смерти
  for (const u of st.placed) {
    if (u.alive) {
      const max = st.playerRoster.find((d) => d.id === u.defId)!.hp * hpMul;
      if (u.hp <= 0) { u.alive = false; st.battleLog.push(`${st.playerRoster.find((d) => d.id === u.defId)!.name} пал.`); }
      else u.hp = Math.min(u.hp, max);
    }
  }
  for (const u of st.enemies) {
    if (u.alive && u.hp <= 0) {
      u.alive = false;
      st.battleLog.push(`${ENEMY_STATS[u.defId].name} убит.`);
    }
  }

  const playersAlive = st.placed.some((u) => u.alive);
  const enemiesAlive = st.enemies.some((u) => u.alive);
  if (!enemiesAlive) {
    st.phase = 'result';
    st.result = 'win';
    st.battleLog.push('Стая разбита! Ограда устояла.');
    return true;
  }
  if (!playersAlive) {
    st.phase = 'result';
    st.result = 'lose';
    st.battleLog.push('Ополчение разбито... Волки рвутся в посёлок.');
    return true;
  }
  if (st.worldMinutesSpent > 240) {
    st.phase = 'result';
    st.result = 'lose';
    st.battleLog.push('Стая измотала защитников и ушла с добычей.');
    return true;
  }
  return false;
}

function isOccupied(st: ChessState, self: ChessUnit, r: number, c: number): boolean {
  if (r < 0 || c < 0 || r >= st.boardR || c >= st.boardC) return true;
  return allUnits(st).some((u) => u.alive && u !== self && u.r === r && u.c === c);
}

/** Применить результат боя к миру (последствия §14/§20) */
export function applyChessResult(world: World, st: ChessState) {
  const v = world.data.village;
  v.raidPending = false;
  v.raidCdDays = 4;
  if (st.result === 'win') {
    v.food = Math.max(0, v.food - 5);
    v.military = clamp(v.military + 8, 0, 100);
    v.stability = clamp(v.stability + 8, 0, 100);
    v.raidsSurvived++;
    world.data.flags.raidDefended = 1;
    world.npc('dar').attitude = clamp(world.npc('dar').attitude + 14, 0, 100);
    world.karma('raid_defended', 'Оборона Опушки: стая отброшена от поселения', 8, 'good', 6);
    // волки обескровлены: половина стаи гибнет
    const wolves = world.data.creatures.filter((c) => c.species === 'wolf' && !c.dead);
    for (const w of wolves.slice(0, Math.ceil(wolves.length / 2))) {
      w.dead = true; w.absorbed = true; w.corpseT = 0;
    }
    world.pushLog('Вы вели ополчение. Стая разбита, посёлок спасён. Ополченцы шепчутся о «командире-слизне».', 'chess');
  } else {
    v.food = Math.max(0, v.food - 14);
    v.population = Math.max(3, v.population - 2);
    v.stability = clamp(v.stability - 10, 0, 100);
    world.npc('dar').attitude = clamp(world.npc('dar').attitude - 6, 0, 100);
    world.pushLog('Оборона провалилась. Волки ушли с добычей, посёлок оплакивает погибших.', 'chess');
    world.karma('raid_failed', 'Поражение при обороне Опушки', 0, 'neutral', 2);
  }
}
