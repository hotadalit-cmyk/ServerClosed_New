// ТЗ §5–§7: поглощение — центральная механика, переваривание — ОТДЕЛЬНАЯ внутренняя система.
// Поглощение ≠ переваривание (§5.1): захваченный объект остаётся внутри тела видимым
// через мембрану (§5.3 фаза E), перерабатывается в полостях по времени (§6.4) и лишь
// по завершении даёт ресурсы (§7). Реальный прогресс хранится в данных и независим
// от анимации (§6.3, критерий «Процесс корректно возобновляется после загрузки»).
//
// Переносимо в C# без изменений (§17): чистые данные + функции.

import type { SpeciesId } from './types';
import type { SenseOrgan } from './senses';

// ===== Классы перевариваемого вещества (§6.2) =====
export type FoodKind = 'organic' | 'mineral' | 'magic' | 'toxic';

export interface FoodKindInfo {
  name: string;
  tint: string;          // 'r,g,b' — цветовое состояние тела (§2.3)
  compat: number;        // совместимость с ферментами начальной слизи (§6.4)
  residue: number;       // доля массы в нерастворимый остаток (§7)
}

export const FOOD_KINDS: Record<FoodKind, FoodKindInfo> = {
  organic: { name: 'Органическое', tint: '154,200,96',  compat: 1.0, residue: 0.06 },
  mineral: { name: 'Минеральное',  tint: '150,170,192', compat: 0.55, residue: 0.16 },
  magic:   { name: 'Магическое',   tint: '168,138,224', compat: 0.5, residue: 0.10 },
  toxic:   { name: 'Токсичное',    tint: '122,138,74',  compat: 0.7, residue: 0.08 },
};

/** Состав добычи по виду: твёрдость, класс, риск отравления (§6.2, §6.4). */
export const PREY_COMPOSITION: Partial<Record<SpeciesId, { kind: FoodKind; hardness: number; toxicity: number }>> = {
  slug:          { kind: 'organic', hardness: 1.0, toxicity: 0 },
  beetle:        { kind: 'organic', hardness: 1.2, toxicity: 0 },
  mushling:      { kind: 'organic', hardness: 1.0, toxicity: 0.05 },
  mycenoid:      { kind: 'organic', hardness: 1.0, toxicity: 0.10 },
  sporeling:     { kind: 'toxic',   hardness: 1.0, toxicity: 0.35 },
  myco_monarch:  { kind: 'organic', hardness: 1.6, toxicity: 0.15 },
  toad:          { kind: 'organic', hardness: 1.1, toxicity: 0.10 },
  spider:        { kind: 'toxic',   hardness: 1.2, toxicity: 0.40 },
  rabbit:        { kind: 'organic', hardness: 1.0, toxicity: 0 },
  deer:          { kind: 'organic', hardness: 1.3, toxicity: 0 },
  wolf:          { kind: 'organic', hardness: 1.4, toxicity: 0.08 },
  firefly:       { kind: 'magic',   hardness: 1.0, toxicity: 0 },
  spirit:        { kind: 'magic',   hardness: 1.8, toxicity: 0.20 },
  crystalslug:   { kind: 'mineral', hardness: 2.2, toxicity: 0 },
  shardling:     { kind: 'mineral', hardness: 2.6, toxicity: 0 },
  golem:         { kind: 'mineral', hardness: 3.4, toxicity: 0 },
  crystal_titan: { kind: 'mineral', hardness: 4.4, toxicity: 0 },
  guard:         { kind: 'organic', hardness: 1.2, toxicity: 0 },
};

export const PREY_DEFAULT = { kind: 'organic' as FoodKind, hardness: 1.0, toxicity: 0 };

export interface StoredItem {
  id: number;
  kind: FoodKind;
  name: string;                              // «Останки: …» / «Запас: …»
  species?: SpeciesId;                       // источник (для последующих систем)
  mass: number;                              // биомасса, которую вернёт завершение (§7)
  hardness: number;                          // твёрдость структуры (§6.1)
  diet: Partial<Record<SenseOrgan, number>>; // питание рецепторов при переваривании
  oreYield?: number;                         // магическая руда (п.8 Дополнения)
  genePool: string[];                        // гены, способные раскрыться при переваривании
  geneChance: number;                        // базовый шанс гена (бережный режим умножает, §6.6)
  toxicity: number;                          // 0..1 — риск отравления при завершении
  progress: number;                          // 0..1 — РЕАЛЬНЫЙ прогресс (не анимация, §6.3)
  seed: number;                              // детерминированная вариация отрисовки
  chamber: number;                           // индекс полости (0..n-1)
  elapsed: number;                           // секунд в текущей полости
}

export type DigestMode = 'balanced' | 'fast' | 'gentle';

export interface ModeInfo {
  name: string;
  desc: string;
  speedMul: number;   // §6.6: сбалансированный ~1, ускоренный >1, бережный <1
  geneMul: number;    // бережный бережёт структуру → больше шансов на ген (§6.1)
  toxMul: number;     // ускоренный — риск перегрева/нестабильности
  energyDrain: number; // сек⁻¹
  unlockAt: number;   // порог усвоенной биомассы тела (§6.6: открываемые функции)
}

export const MODE_INFO: Record<DigestMode, ModeInfo> = {
  balanced: { name: 'Сбалансированный', desc: 'Нормальная скорость, небольшое энергопотребление.', speedMul: 1.0,  geneMul: 1.0, toxMul: 1.0, energyDrain: 0.03, unlockAt: 0 },
  fast:     { name: 'Ускоренный',       desc: 'Ферменты на максимум: быстро, но жжёт энергию и повышает риск отравления.', speedMul: 1.8, geneMul: 0.8, toxMul: 1.6, energyDrain: 0.22, unlockAt: 25 },
  gentle:   { name: 'Бережный',         desc: 'Медленно, но хрупкие структуры сохраняются: выше шанс раскрыть ген.', speedMul: 0.62, geneMul: 1.65, toxMul: 0.6, energyDrain: 0.06, unlockAt: 45 },
};

export interface DigestiveState {
  chambers: (StoredItem | null)[];   // изначально две полости (§6.5)
  mode: DigestMode;
  modeUnlocked: DigestMode[];        // «открываемые функции, а не обязательная система» (§6.6)
  insoluble: number;                 // нерастворимый остаток (§7)
  seq: number;                       // счётчик id предметов
}

export function createDigestive(): DigestiveState {
  return { chambers: [null, null], mode: 'balanced', modeUnlocked: ['balanced'], insoluble: 0, seq: 1 };
}

// ===== Вместимость и давление (§5.5) =====
/** AbsorptionCapacity: максимальная масса содержимого. Растёт с телом и слотами генома. */
export function capacityOf(biomass: number, slots: number): number {
  return 12 + biomass * 0.3 + slots * 5;
}

export function storedMassOf(d: DigestiveState): number {
  let m = 0;
  for (const it of d.chambers) if (it) m += it.mass;
  return m;
}

/** InternalPressure 0..1: нагрузка на оболочку (§5.5). */
export function pressureOf(d: DigestiveState, cap: number): number {
  return cap > 0 ? Math.min(1, storedMassOf(d) / cap) : 0;
}

/** Сила ферментов: эволюция тела и геном ускоряют переработку (§6.4). */
export function enzymeStrength(installedGenes: number, biomassTotal: number): number {
  return 1 + installedGenes * 0.12 + Math.min(1.2, biomassTotal / 55);
}

/** Сколько секунд займёт переваривание предмета при текущих условиях (§6.4). */
export function digestSeconds(
  item: StoredItem, enzyme: number, mode: DigestMode, load: number,
): number {
  const base = 7;                                 // базовое время, сек
  const compat = FOOD_KINDS[item.kind].compat;    // совместимость вещества
  const m = MODE_INFO[mode].speedMul;
  // Время = База × Твёрдость / (Ферменты × Совместимость) × Нагрузка × Режим
  const t = (base * item.hardness) / (enzyme * compat) * load / m;
  return Math.max(1.5, t);                        // безопасный диапазон: не 0 и не отрицательное (§6.4)
}

/** Свободная полость (§6.5: каждая обрабатывает отдельный предмет). */
export function freeChamber(d: DigestiveState): number {
  return d.chambers.findIndex((c) => c === null);
}

export interface AbsorbCheck { ok: boolean; reason?: string; noChamber?: boolean; overloaded?: boolean }

/**
 * Проверка возможности поглощения (§5.2): свободная полость и вместимость.
 * Причина отказа обязана быть понятной игроку (§5.2).
 */
export function canAbsorb(d: DigestiveState, cap: number, mass: number): AbsorbCheck {
  if (freeChamber(d) < 0) {
    return { ok: false, noChamber: true, reason: 'Свободной пищеварительной полости нет — содержимое сначала нужно переварить или извергнуть.' };
  }
  if (storedMassOf(d) + mass > cap) {
    return { ok: false, overloaded: true, reason: 'Тело не вместит столько: внутреннее давление опасно. Поглощение заблокировано.' };
  }
  return { ok: true };
}

/** Положить предмет в свободную полость. Возвращает предмет или null (нет места). */
export function putItem(
  d: DigestiveState,
  item: Omit<StoredItem, 'id' | 'chamber' | 'elapsed'>,
): StoredItem | null {
  const idx = freeChamber(d);
  if (idx < 0) return null;
  const full: StoredItem = { ...item, id: d.seq++, chamber: idx, elapsed: 0 };
  d.chambers[idx] = full;
  return full;
}

/** Номер визуальной фазы 1..12 по прогрессу (§6.3 — двенадцать фаз анимации). */
export function phaseOf(progress: number): number {
  return Math.min(12, 1 + Math.floor(progress * 12));
}

export interface DigestTickResult {
  completed: StoredItem[];
  ejected: boolean;              // была ли критическая перегрузка (зарезервировано)
}

/**
 * Посекундный тик полостей. Прогресс — только в данных (§6.3); анимация читает его.
 * Ускоренный режим жжёт энергию (возвращает расход через out-параметр-поле? — нет,
 * расход применяет вызывающий по MODE_INFO).
 */
export function tickDigestive(
  d: DigestiveState,
  dt: number,
  enzyme: number,
  cap: number,
  load: number,
): DigestTickResult {
  const completed: StoredItem[] = [];
  for (let i = 0; i < d.chambers.length; i++) {
    const it = d.chambers[i];
    if (!it) continue;
    it.elapsed += dt;
    const total = digestSeconds(it, enzyme, d.mode, load);
    it.progress = Math.min(1, it.progress + dt / total);
    if (it.progress >= 1) {
      completed.push(it);
      d.chambers[i] = null;
    }
  }
  void cap;
  return { completed, ejected: false };
}

/** Открытие режимов пищеварения (§6.6 — открываемые функции). Возвращает НОВЫЕ режимы. */
export function unlockModes(d: DigestiveState, biomassTotal: number): DigestMode[] {
  const opened: DigestMode[] = [];
  for (const m of ['fast', 'gentle'] as DigestMode[]) {
    if (!d.modeUnlocked.includes(m) && biomassTotal >= MODE_INFO[m].unlockAt) {
      d.modeUnlocked.push(m);
      opened.push(m);
    }
  }
  return opened;
}

/** Смена режима (если открыт). */
export function setMode(d: DigestiveState, m: DigestMode): boolean {
  if (m !== 'balanced' && !d.modeUnlocked.includes(m)) return false;
  d.mode = m;
  return true;
}

export function modeIsUnlocked(d: DigestiveState, m: DigestMode): boolean {
  return d.modeUnlocked.includes(m);
}

/** Выведение содержимого полости (§6.6): предмет выбрасывается, часть массы теряется. */
export function egestChamber(d: DigestiveState, idx: number): StoredItem | null {
  if (idx < 0 || idx >= d.chambers.length) return null;
  const it = d.chambers[idx];
  d.chambers[idx] = null;
  return it;
}
