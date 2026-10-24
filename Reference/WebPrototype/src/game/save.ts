// Слой Core: сохранения (ТЗ §18).
// - Версионирование формата, контрольная сумма, резервная копия.
// - Безопасная запись: сначала бэкап, затем основная копия — восстановление
//   после аварийного закрытия (реинкарнация устойчива к прерыванию).
// - Привязки к системным часам НЕТ: мировое время хранится в сохранении и течёт
//   только пока открыто приложение (§7).

import type { WorldData } from './world';
import { ensureAtlasWorldData } from './world';
import type { SoulState, Achievement, ChessState } from './types';
import { createSensoryState, migrateSensesV5toV6 } from './senses';
import { createDigestive } from './digestion';
import { ensureRiftWorldData } from './regions';

export const SAVE_VERSION = 7; // v7: пищеварение — внутренняя система (полости/содержимое/режимы, ТЗ §5–§7)
const KEY_MAIN = 'serverclosed_save_v1';
const KEY_BACKUP = 'serverclosed_save_v1_bak';

export interface SaveFile {
  v: number;
  checksum: number;
  phase: EnginePhase;
  world: WorldData;
  soul: SoulState;
  achievements: Achievement[];
  chess: ChessState | null;
  judgment: unknown | null;   // JudgmentResult между смертью и реинкарнацией
  savedAtWallClock?: number;  // только для отладки, временем мира НЕ управляет
}

export type EnginePhase =
  | 'title' | 'life' | 'chess' | 'death' | 'judgment' | 'reincarnate';

export function computeChecksum(obj: unknown): number {
  const s = JSON.stringify(obj);
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

function writeKey(key: string, file: SaveFile): boolean {
  if (typeof window === 'undefined') return false;
  try {
    localStorage.setItem(key, JSON.stringify(file));
    return true;
  } catch {
    return false;
  }
}

/** Транзакционная запись: бэкап → основная. Возвращает успех. */
export function saveGame(file: Omit<SaveFile, 'v' | 'checksum' | 'savedAtWallClock'>): boolean {
  if (typeof window === 'undefined') return false;
  const full = { ...file, v: SAVE_VERSION, savedAtWallClock: Date.now(), checksum: 0 } as SaveFile;
  full.checksum = computeChecksum({ world: full.world, soul: full.soul, phase: full.phase });
  // транзакция: текущий основной → бэкап, затем новая основная
  try {
    const old = localStorage.getItem(KEY_MAIN);
    if (old) localStorage.setItem(KEY_BACKUP, old);
  } catch { /* бэкап не критичен */ }
  const ok = writeKey(KEY_MAIN, full);
  return ok;
}

/**
 * Миграция v1 → v2: одиночное поле world.dungeon → реестр world.dungeons (Регион 2/3).
 * Контрольная сумма проверена до вызова — меняем структуру только после проверки.
 */
function migrateV1toV2(f: SaveFile): SaveFile {
  const w = f.world as SaveFile['world'] & { dungeon?: unknown };
  if (!w.dungeons && 'dungeon' in w) {
    const legacy = (w as unknown as { dungeon?: Record<string, unknown> | null }).dungeon ?? null;
    w.dungeons = legacy ? { catacombs: legacy as never } : {};
    w.activeDungeon = null;
    delete (w as unknown as { dungeon?: unknown }).dungeon;
  }
  if (w.activeDungeon === undefined) w.activeDungeon = null;
  for (const c of w.creatures ?? []) {
    if (c && typeof c === 'object' && (c as { dungeonFloor?: number }).dungeonFloor != null
      && !(c as { dungeonId?: string }).dungeonId) {
      (c as { dungeonId?: string }).dungeonId = 'catacombs';
    }
  }
  f.v = SAVE_VERSION;
  return f;
}

/**
 * Миграция v2 → v3: эволюция восприятия (Дополнение к ТЗ).
 * Старое тело считаем слепым: органы развиваются заново с текущей жизни.
 * Контрольная сумма проверена до вызова — меняем структуру только после проверки.
 */
function migrateV2toV3(f: SaveFile): SaveFile {
  const w = f.world;
  if (!w.senses) w.senses = createSensoryState();
  if (!w.traces) w.traces = [];
  for (const dun of Object.values(w.dungeons ?? {})) {
    if (dun && !dun.explored) dun.explored = dun.floors.map(() => [] as number[]);
  }
  const s = f.soul as SoulState;
  if (s && s.resonantCore === undefined) s.resonantCore = false;
  if (s && s.spiritMemory === undefined) s.spiritMemory = 0;
  f.v = SAVE_VERSION;
  return f;
}

/**
 * Миграция v3 → v4: «Резонанс души» (Дополнение к ТЗ).
 * Черты души выводятся из старых статов, регион рождения — Тихая колыбель,
 * на карту детерминированно достраивается Разлом.
 */
function migrateV3toV4(f: SaveFile): SaveFile {
  const s = f.soul as SoulState;
  if (s && !s.traits) {
    const st = s.stats ?? { memory: 1, will: 1, adaptation: 1, perception: 1, resonance: 1, integrity: 10 };
    const cl = (x: number) => Math.max(0, Math.min(100, Math.round(x)));
    s.traits = {
      resilience: cl(20 + st.integrity * 1.5),
      depth: cl(st.resonance * 6),
      predator: 5,
      memory: cl(st.memory * 6),
    };
  }
  if (s && !s.birthRegion) s.birthRegion = 'quiet_cradle'; // старые жизни рождались у посёлка
  ensureRiftWorldData(f.world); // Разлом на картах старых миров
  f.world.flags.traitDepth = s?.traits?.depth ?? 0;
  f.v = SAVE_VERSION;
  return f;
}

/**
 * Миграция v4 → v5: «Чаша Отголосов» (Дополнение к ТЗ: карта мира).
 * На карту детерминированно наносится география (хребты/река/топи/пепел/руины/трещины),
 * добавляются POI, карман трещины засевается добычей, открытым остаётся
 * только место, где игрок стоит (п.9 — никаких утечек).
 */
function migrateV4toV5(f: SaveFile): SaveFile {
  ensureAtlasWorldData(f.world);
  f.v = SAVE_VERSION;
  return f;
}

function migrateV5toV6(f: SaveFile): SaveFile {
  // диета/выбор: у восприятия появляются feed/focus — прогресс старых сейвов сохраняется
  if (f.world?.senses) migrateSensesV5toV6(f.world.senses);
  else if (f.world) f.world.senses = createSensoryState();
  f.v = SAVE_VERSION;
  return f;
}

/**
 * Миграция v6 → v7: пищеварение (ТЗ §5–§7).
 * Старое тело считается пустым: полости свободны, режим — сбалансированный.
 */
function migrateV6toV7(f: SaveFile): SaveFile {
  if (f.world && !f.world.digestion) f.world.digestion = createDigestive();
  f.v = SAVE_VERSION;
  return f;
}

export function loadGame(): SaveFile | null {
  if (typeof window === 'undefined') return null;
  const tryParse = (raw: string | null): SaveFile | null => {
    if (!raw) return null;
    try {
      const f = JSON.parse(raw) as SaveFile;
      if (f.v < 1 || f.v > SAVE_VERSION) return null;
      const expected = computeChecksum({ world: f.world, soul: f.soul, phase: f.phase });
      if (expected !== f.checksum) return null;
      // цепочка миграций: v1 → … → v7 (контрольная сумма проверена до изменения структуры)
      if (f.v === 1) migrateV1toV2(f);
      if (f.v <= 2) migrateV2toV3(f);
      if (f.v <= 3) migrateV3toV4(f);
      if (f.v <= 4) migrateV4toV5(f);
      if (f.v <= 5) migrateV5toV6(f);
      if (f.v <= 6) migrateV6toV7(f);
      return f;
    } catch {
      return null;
    }
  };
  const main = tryParse(localStorage.getItem(KEY_MAIN));
  if (main) return main;
  const backup = tryParse(localStorage.getItem(KEY_BACKUP));
  if (backup) {
    // восстановились из аварийной копии — перезаписываем основную
    writeKey(KEY_MAIN, backup);
    return backup;
  }
  return null;
}

export function hasSave(): boolean {
  if (typeof window === 'undefined') return false;
  return localStorage.getItem(KEY_MAIN) != null || localStorage.getItem(KEY_BACKUP) != null;
}

export function wipeSaves() {
  if (typeof window === 'undefined') return;
  try {
    localStorage.removeItem(KEY_MAIN);
    localStorage.removeItem(KEY_BACKUP);
  } catch { /* noop */ }
}
