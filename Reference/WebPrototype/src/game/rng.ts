// Слой Core: управляемый генератор случайных чисел (ТЗ §23 — воспроизводимость тестов).
// mulberry32 + хеш-функция для сидов.

export interface Rng {
  (): number;
  int(min: number, max: number): number;      // включительно
  range(min: number, max: number): number;
  chance(p: number): boolean;
  pick<T>(arr: T[]): T;
  shuffle<T>(arr: T[]): T[];
  seed(): number;
}

export function mulberry32(seed: number): Rng {
  let a = seed >>> 0;
  const fn = () => {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  const rng = fn as Rng;
  rng.int = (min, max) => Math.floor(rng() * (max - min + 1)) + min;
  rng.range = (min, max) => rng() * (max - min) + min;
  rng.chance = (p) => rng() < p;
  rng.pick = <T,>(arr: T[]) => arr[Math.floor(rng() * arr.length)];
  rng.shuffle = <T,>(arr: T[]) => {
    const a2 = arr.slice();
    for (let i = a2.length - 1; i > 0; i--) {
      const j = Math.floor(rng() * (i + 1));
      [a2[i], a2[j]] = [a2[j], a2[i]];
    }
    return a2;
  };
  rng.seed = () => seed >>> 0;
  return rng;
}

/** Хеш строки → 32-битный сид (FNV-1a) */
export function hashSeed(s: string): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

export const dist = (ax: number, ay: number, bx: number, by: number) =>
  Math.hypot(ax - bx, ay - by);

export const clamp = (v: number, lo: number, hi: number) => Math.max(lo, Math.min(hi, v));

export const lerp = (a: number, b: number, t: number) => a + (b - a) * t;
