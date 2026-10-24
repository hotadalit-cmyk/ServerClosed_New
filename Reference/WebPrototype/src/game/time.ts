// Слой Core: единое мировое время (ТЗ §7) и старение (ТЗ §10).
// - Единый игровой календарь: год из 4 сезонов, в сезоне 6 дней, в дне 24 ч.
// - Время проходит ТОЛЬКО пока приложение открыто (привязки к системным часам нет — §7).
// - Скорости: исследование (1x), отдых/ускорение (FAST), расстановка Magic Chess — пауза,
//   бой автобоя — фиксированный интервал мирового времени.
// - Проверки смертности — по игровым дням, не по кадрам (§10).

export const MINUTES_IN_HOUR = 60;
export const HOURS_IN_DAY = 24;
export const MINUTES_IN_DAY = HOURS_IN_DAY * MINUTES_IN_HOUR; // 1440
export const DAYS_IN_SEASON = 6;
export const SEASONS = ['Пробуждение', 'Цветение', 'Увядание', 'Тьма'] as const;

/** Скорости течения времени: игровых минут за реальную секунду */
export const TIME_SPEED = {
  pause: 0,
  explore: 3,      // 1 реальная секунда = 3 игровые минуты → день ≈ 8 реальных минут
  fast: 36,        // отдых: день ≈ 40 секунд
  battle: 1.5,     // бой в мире (RPG) течёт медленнее, игроку легче реагировать
} as const;

export type TimeSpeedName = keyof typeof TIME_SPEED;

export class WorldClock {
  minutes: number = 0;    // всего игровых минут с сотворения мира
  dayTickCounter = 0;     // сколько раз наступил новый день (для разовых дневных проверок)

  constructor(minutes = 0) { this.minutes = minutes; }

  /** Продвинуть время; возвращает число наступивших НОВЫХ дней (для дневных проверок).
   *  Защита от NaN/Infinity: некорректный интервал отбрасывается, часы не отравляются (§18 — живучесть). */
  advance(gameMinutes: number): number {
    if (!Number.isFinite(gameMinutes)) return 0;
    const before = Math.floor(this.minutes / MINUTES_IN_DAY);
    this.minutes += Math.max(0, gameMinutes);
    if (!Number.isFinite(this.minutes)) this.minutes = before * MINUTES_IN_DAY; // страховка от порчи извне
    const after = Math.floor(this.minutes / MINUTES_IN_DAY);
    const crossed = after - before;
    this.dayTickCounter += crossed;
    return crossed;
  }

  get totalDays() { return Math.floor(this.minutes / MINUTES_IN_DAY); }
  get year() { return Math.floor(this.totalDays / (DAYS_IN_SEASON * 4)) + 1; }
  get season() { return SEASONS[Math.floor(this.totalDays / DAYS_IN_SEASON) % 4]; }
  get dayOfSeason() { return (this.totalDays % DAYS_IN_SEASON) + 1; }
  get hour() { return Math.floor((this.minutes % MINUTES_IN_DAY) / MINUTES_IN_HOUR); }
  get minute() { return Math.floor(this.minutes % MINUTES_IN_HOUR); }

  /** Часы суток 0..24 — для света/тени */
  get hourF() { return (this.minutes % MINUTES_IN_DAY) / MINUTES_IN_HOUR; }
  get isNight() { return this.hourF < 5 || this.hourF >= 21; }
  get isDusk() { return (this.hourF >= 19 && this.hourF < 21) || (this.hourF >= 5 && this.hourF < 7); }

  label(): string {
    const hh = String(this.hour).padStart(2, '0');
    const mm = String(this.minute).padStart(2, '0');
    return `Год ${this.year}, ${this.season}, день ${this.dayOfSeason} — ${hh}:${mm}`;
  }
}

// ===== Старение (§10) =====
export type LifeStage = 'Зарождение' | 'Молодость' | 'Расцвет' | 'Старение' | 'Угасание';

/** Стадия жизни по долге прожитого от ожидаемой продолжительности */
export function lifeStageOf(ageDays: number, lifeDays: number): LifeStage {
  const f = ageDays / Math.max(1, lifeDays);
  if (f < 0.15) return 'Зарождение';
  if (f < 0.45) return 'Молодость';
  if (f < 0.70) return 'Расцвет';
  if (f < 0.85) return 'Старение';
  return 'Угасание';
}

/**
 * Вероятность естественной смерти за один игровой день (закон Гомпертца).
 * Утверждённая механика §10: постепенное угасание, не смерть в заранее известный момент.
 * onsetF — доля жизни, после которой риск начинает расти (0.7).
 */
export function dailyMortality(ageDays: number, lifeDays: number, onsetF = 0.7): number {
  if (lifeDays <= 0) return 0;
  if (ageDays <= lifeDays * onsetF) return 0.0005; // фоновый риск
  const k = 0.85;                                  // крутизна роста риска
  const x = (ageDays - lifeDays * onsetF) / Math.max(1, lifeDays * (1 - onsetF));
  return Math.min(0.9, 0.02 * Math.exp(k * 2.2 * x));
}

/**
 * Модификаторы характеристик от возраста.
 * Расцвет — пик; старение снижает выносливость и скорость, но даёт опыт (§10).
 */
export function ageModifiers(ageDays: number, lifeDays: number): {
  hpMul: number; atkMul: number; speedMul: number; xpMul: number;
} {
  const f = ageDays / Math.max(1, lifeDays);
  if (f < 0.15) return { hpMul: 0.8, atkMul: 0.7, speedMul: 0.9, xpMul: 1.1 };  // Зарождение — слаб
  if (f < 0.45) return { hpMul: 1.0, atkMul: 0.9, speedMul: 1.0, xpMul: 1.1 };  // Молодость — растём
  if (f < 0.70) return { hpMul: 1.1, atkMul: 1.0, speedMul: 1.0, xpMul: 1.0 };  // Расцвет — пик
  if (f < 0.85) return { hpMul: 1.0, atkMul: 0.95, speedMul: 0.92, xpMul: 1.2 };// Старение
  return { hpMul: 0.85, atkMul: 0.85, speedMul: 0.82, xpMul: 1.3 };             // Угасание
}
