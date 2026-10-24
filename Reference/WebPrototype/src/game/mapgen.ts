// Слой Simulation: генерация карты «Лес Первого Дыхания» (ТЗ §5.1, Регион 1).
// Детерминированная по сиду мира — одна и та же карта хранится в сохранении и живёт
// между воплощениями (постоянный мир, §2).

import { mulberry32 } from './rng';
import type { TileMap, TerrainKind, Deco, DecoKind } from './types';
import {
  RIVER_WAYPOINTS, FAULT_LINE, CRACK_SPOTS, CRITICAL_POINTS,
} from './atlas';

export const MAP_W = 52;
export const MAP_H = 52;

export interface Poi { kind: 'village' | 'pond' | 'mushroom_grove' | 'crystal_hollow' | 'stone_circle' | 'burrow' | 'rift' | 'ruins' | 'bogs'; pos: { x: number; y: number }; r: number }

export interface GeneratedMap { map: TileMap; pois: Record<string, Poi> }

export function generateWorldMap(seed: number): GeneratedMap {
  const rng = mulberry32(seed ^ 0x9e3779b9);
  const w = MAP_W, h = MAP_H;
  const terrain: TerrainKind[] = new Array(w * h).fill('moss');

  const idx = (x: number, y: number) => y * w + x;
  const put = (x: number, y: number, t: TerrainKind) => {
    if (x >= 0 && y >= 0 && x < w && y < h) terrain[idx(x, y)] = t;
  };

  const pois: Record<string, Poi> = {
    village: { kind: 'village', pos: { x: 26, y: 24 }, r: 7 },
    pond: { kind: 'pond', pos: { x: 38, y: 36 }, r: 5.5 },
    mushroom_grove: { kind: 'mushroom_grove', pos: { x: 9, y: 40 }, r: 6 },
    crystal_hollow: { kind: 'crystal_hollow', pos: { x: 7, y: 7 }, r: 5 },
    stone_circle: { kind: 'stone_circle', pos: { x: 42, y: 8 }, r: 5 },
    burrow: { kind: 'burrow', pos: { x: 40, y: 24 }, r: 4 },
  };

  // Пруд
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const d = Math.hypot(x - pois.pond.pos.x, (y - pois.pond.pos.y) * 1.15);
    if (d < pois.pond.r * (0.8 + rng() * 0.35)) put(x, y, 'water');
  }
  // Поляна поселения
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const d = Math.hypot(x - pois.village.pos.x, y - pois.village.pos.y);
    if (d < pois.village.r + 1.5 && terrain[idx(x, y)] !== 'water') put(x, y, 'glade');
  }
  // Грибная роща и кристальная ложбинка — земля иная
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (Math.hypot(x - pois.mushroom_grove.pos.x, y - pois.mushroom_grove.pos.y) < pois.mushroom_grove.r) put(x, y, 'dirt');
    if (Math.hypot(x - pois.crystal_hollow.pos.x, y - pois.crystal_hollow.pos.y) < pois.crystal_hollow.r) put(x, y, 'dirt');
  }
  // Круг камней — руинный грунт
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (Math.hypot(x - pois.stone_circle.pos.x, y - pois.stone_circle.pos.y) < pois.stone_circle.r) put(x, y, 'ruin');
  }
  // Тропы: поселение → пруд, поселение → роща, поселение → ложбинка, поселение → камни
  const path = (a: { x: number; y: number }, b: { x: number; y: number }) => {
    let x = a.x, y = a.y;
    let guard = 0;
    while ((x !== b.x || y !== b.y) && guard++ < 400) {
      put(x, y, 'path');
      if (rng.chance(0.35)) put(x + 1, y, 'path');
      if (x !== b.x && (y === b.y || rng.chance(0.6))) x += Math.sign(b.x - x);
      else y += Math.sign(b.y - y);
    }
    put(b.x, b.y, 'path');
  };
  path(pois.village.pos, pois.pond.pos);
  path(pois.village.pos, pois.mushroom_grove.pos);
  path({ x: pois.village.pos.x, y: pois.village.pos.y }, pois.crystal_hollow.pos);
  path(pois.village.pos, pois.stone_circle.pos);
  path(pois.village.pos, pois.burrow.pos);

  // Декорации
  const deco: Deco[] = [];
  let idc = 1;
  const add = (kind: DecoKind, x: number, y: number, r: number, seedv = rng.int(0, 1e9)) => {
    deco.push({ id: idc++, kind, pos: { x, y }, r, seed: seedv });
  };

  const farFromVillage = (x: number, y: number, d: number) => Math.hypot(x - pois.village.pos.x, y - pois.village.pos.y) > d;

  // Лес: деревья гуще к краям, редко в центре
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const t = terrain[idx(x, y)];
    if (t === 'path' || t === 'water' || t === 'glade' || t === 'ruin') continue;
    const edge = Math.min(x, y, w - 1 - x, h - 1 - y);
    let density = 0.10 + (1 - Math.min(1, edge / 12)) * 0.16;
    if (farFromVillage(x, y, 10)) density += 0.05;
    if (t === 'dirt') density *= 0.35;
    if (rng.chance(density)) {
      add(rng.chance(0.35) ? 'pine' : 'tree', x + rng.range(-0.3, 0.3), y + rng.range(-0.3, 0.3), 0.55);
    } else if (rng.chance(0.02)) {
      add('bush', x, y, 0.4);
    } else if (rng.chance(0.008)) {
      add('rock', x, y, 0.5);
    }
  }
  // Грибная роща: грибы
  for (let i = 0; i < 26; i++) {
    const a = rng() * Math.PI * 2, r = rng.range(0.5, pois.mushroom_grove.r);
    add('mushroom', pois.mushroom_grove.pos.x + Math.cos(a) * r, pois.mushroom_grove.pos.y + Math.sin(a) * r, 0.35);
  }
  // Кристальная ложбинка: кристаллы и кристальная гряда
  for (let i = 0; i < 14; i++) {
    const a = rng() * Math.PI * 2, r = rng.range(0.5, pois.crystal_hollow.r);
    add('crystal', pois.crystal_hollow.pos.x + Math.cos(a) * r, pois.crystal_hollow.pos.y + Math.sin(a) * r, 0.45);
  }
  // Круг камней: менгиры по кругу + в центре алтарь
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * Math.PI * 2;
    add('stone', pois.stone_circle.pos.x + Math.cos(a) * pois.stone_circle.r * 0.75, pois.stone_circle.pos.y + Math.sin(a) * pois.stone_circle.r * 0.75, 0.5);
  }
  add('bones', pois.stone_circle.pos.x + 1.2, pois.stone_circle.pos.y - 1.2, 0.2);

  // Поселение: домики, колодец, костёр, ферма, забор
  const v = pois.village.pos;
  add('hut', v.x - 2.4, v.y - 2.2, 0.95);
  add('hut', v.x + 2.6, v.y - 1.9, 0.95);
  add('hut', v.x - 2.2, v.y + 2.4, 0.95);
  add('hut', v.x + 2.7, v.y + 2.6, 0.95);
  add('well', v.x + 0.2, v.y + 0.2, 0.55);
  add('campfire', v.x - 0.9, v.y + 1.4, 0.35);
  add('farm', v.x - 4.6, v.y + 0.4, 0.2);
  // Забор по краю поляны с проходом у троп
  for (let a = 0; a < Math.PI * 2; a += Math.PI / 22) {
    const fx = v.x + Math.cos(a) * (pois.village.r + 0.4);
    const fy = v.y + Math.sin(a) * (pois.village.r + 0.4) * 0.82;
    // проходы на восток и на юг-запад
    if (Math.abs(Math.cos(a) - 1) < 0.22 || Math.abs(Math.sin(a) - 0.75) < 0.2) continue;
    add('fence', fx, fy, 0.28);
  }

  // Разлом забытых душ (регион рождения предельной сложности, Дополнение к ТЗ)
  addRiftToMap({ w, h, terrain, deco }, pois, rng);

  // «Чаша Отголосов»: география мира — хребты, река, топи, пепел, руины, трещины.
  // Детерминирована без rng (совпадает с миграцией старых сейвов).
  paintAtlas({ w, h, terrain, deco }, pois);

  return { map: { w, h, terrain, deco }, pois };
}

/**
 * Разлом забытых душ (Дополнение: «Резонанс души», регион предельной сложности).
 * Детерминирован: координаты и состав фиксированы, вариации — от переданного rng.
 * Используется и при генерации новых карт, и при миграции старых сейвов (v3→v4).
 */
export function addRiftToMap(
  map: TileMap,
  pois: Record<string, Poi>,
  rng: { range(a: number, b: number): number; chance(p: number): boolean },
): void {
  if (pois.rift) return;
  const pos = { x: MAP_W - 7, y: MAP_H - 7 }; // (45,45) — юго-восток, дальний от посёлка угол
  const r = 5.5;
  pois.rift = { kind: 'rift', pos, r };
  const idx = (x: number, y: number) => y * map.w + x;
  // искажённый руинный грунт с рваными краями
  for (let y = 0; y < map.h; y++) {
    for (let x = 0; x < map.w; x++) {
      const d = Math.hypot(x - pos.x, y - pos.y);
      if (d < r * (0.82 + ((x * 7 + y * 13) % 5) * 0.05)) map.terrain[idx(x, y)] = 'ruin';
    }
  }
  // декорации: кольцо менгиров, кристаллы-потоки, кости — id продолжают существующие
  let id = map.deco.reduce((m, d) => Math.max(m, d.id), 0) + 1;
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * Math.PI * 2;
    map.deco.push({ id: id++, kind: 'stone', pos: { x: pos.x + Math.cos(a) * r * 0.8, y: pos.y + Math.sin(a) * r * 0.8 }, r: 0.5, seed: rng.range(0, 1e9) });
  }
  for (let i = 0; i < 4; i++) {
    const a = rng.range(0, Math.PI * 2), rr = rng.range(0.8, r * 0.6);
    map.deco.push({ id: id++, kind: 'crystal', pos: { x: pos.x + Math.cos(a) * rr, y: pos.y + Math.sin(a) * rr }, r: 0.45, seed: rng.range(0, 1e9) });
  }
  map.deco.push({ id: id++, kind: 'bones', pos: { x: pos.x, y: pos.y }, r: 0.2, seed: rng.range(0, 1e9) });
}

// ===== «Чаша Отголосов»: географический слой карты (Дополнение к ТЗ: карта мира) =====
// Детерминирован БЕЗ rng — формула-хеш вместо случайностей, чтобы миграция старых
// сейвов давала пиксель-в-пиксель ту же географию, что и генерация новых карт.

const h01 = (n: number) => {
  let x = Math.imul(n ^ 0x9e3779b9, 0x85ebca6b);
  x ^= x >>> 13;
  x = Math.imul(x, 0xc2b2ae35);
  x ^= x >>> 16;
  return (x >>> 0) / 4294967296;
};

/** Раскрасить географию «Чаши Отголосов» на карте (новой или старой — результат идентичен).
 *  Структурный тип (string) — работает и с TileMap, и с mapJson из сейвов. */
export function paintAtlas(
  map: { w: number; h: number; terrain: string[]; deco: { id: number; kind: string; pos: { x: number; y: number }; r: number; seed: number }[] },
  pois: Record<string, Poi>,
): void {
  // идемпотентность: камыш красится только этим слоем — если он есть, география уже нанесена
  // (generateWorldMap и конструктор World могут вызвать paintAtlas на одной и той же карте)
  if (map.deco.some((d) => d.kind === 'reed')) return;
  const { w, h } = map;
  const idx = (x: number, y: number) => y * w + x;
  const put = (x: number, y: number, t: TerrainKind) => {
    if (x >= 0 && y >= 0 && x < w && y < h) map.terrain[idx(x, y)] = t;
  };

  // Новые POI географии (для маркеров карты-восприятия и будущих спавнов)
  if (!pois.ruins) pois.ruins = { kind: 'ruins', pos: { x: 14, y: 20 }, r: 5 };
  if (!pois.bogs) pois.bogs = { kind: 'bogs', pos: { x: 24, y: 45 }, r: 6 };

  // Защита: рядом с критическими точками и своими POI непроходимое не красим
  const nearCritical = (x: number, y: number, pad = 2.3): boolean => {
    for (const c of CRITICAL_POINTS) if (Math.hypot(c.x - x, c.y - y) < pad) return true;
    if (Math.hypot(42 - x, 8 - y) < 6.2) return true;    // круг камней
    if (Math.hypot(45 - x, 45 - y) < 6.6) return true;   // Разлом
    if (Math.hypot(38 - x, 36 - y) < 7.2) return true;   // пруд (воду рисует генератор)
    if (Math.hypot(14 - x, 20 - y) < 5.4) return true;   // руины (свой слой ниже)
    if (Math.hypot(24 - x, 45 - y) < 6.8) return true;   // топи (свой слой ниже)
    return false;
  };

  // 1. Гребень Эха: северный хребет с рваной кромкой (исток реки) + юго-восточные отроги у брода
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const t = map.terrain[idx(x, y)];
      if (t === 'path' || t === 'glade' || t === 'water') continue;
      const jag = h01(x * 131 + 7) * 3.2;
      const isRidge = y <= 2.5 + jag;
      const isSpur = Math.hypot(x - 37, y - 10.5) < 1.6 + h01(x * 17 + y) * 0.7
        || Math.hypot(x - 47.5, y - 11) < 1.5 + h01(x * 23 + y) * 0.7;
      if ((isRidge || isSpur) && !nearCritical(x, y)) put(x, y, 'mountain');
    }
  }

  // 2. Река Чаши: с хребта на юг, в Илистые топи. Броды там, где русло встречает тропы/поляны.
  const paintRiver = (cx: number, cy: number, width: number) => {
    const ri = Math.ceil(width) + 1;
    for (let dy = -ri; dy <= ri; dy++) {
      for (let dx = -ri; dx <= ri; dx++) {
        const x = Math.round(cx + dx), y = Math.round(cy + dy);
        if (x < 0 || y < 0 || x >= w || y >= h) continue;
        const d = Math.hypot(x - cx, y - cy);
        if (d > width + h01(x * 61 + y * 7) * 0.5) continue;
        const t = map.terrain[idx(x, y)];
        if (t === 'path' || t === 'glade') continue; // брод / обход посёлка
        put(x, y, 'water');
      }
    }
  };
  for (let i = 0; i < RIVER_WAYPOINTS.length - 1; i++) {
    const a = RIVER_WAYPOINTS[i], b = RIVER_WAYPOINTS[i + 1];
    const steps = Math.ceil(Math.hypot(b.x - a.x, b.y - a.y) * 2);
    for (let s = 0; s <= steps; s++) {
      const t = s / steps;
      const cx = a.x + (b.x - a.x) * t + Math.sin(t * Math.PI * 2 + i) * 0.6;
      const cy = a.y + (b.y - a.y) * t;
      paintRiver(cx, cy, 0.72);
    }
  }

  // 3. Илистые топи: марш вокруг низовья реки, камыш
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const d = Math.hypot(x - 24, y - 45);
      if (d > 6.4 * (0.82 + h01(x * 13 + y * 29) * 0.36)) continue;
      const t = map.terrain[idx(x, y)];
      if (t === 'path' || t === 'glade' || t === 'water' || t === 'ruin' || t === 'mountain') continue;
      put(x, y, 'marsh');
    }
  }

  // 4. Пепельные поля: выжженный грунт вокруг волчьих нор
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const d = Math.hypot(x - 40, y - 24);
      if (d > 6.2 * (0.8 + h01(x * 11 + y * 3) * 0.4)) continue;
      const t = map.terrain[idx(x, y)];
      if (t === 'path' || t === 'glade' || t === 'water' || t === 'mountain' || t === 'marsh') continue;
      put(x, y, 'ash');
    }
  }

  // 5. Затонувшие руины: руинный грунт с затопленными залами, стены и кости
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const d = Math.hypot(x - 14, y - 20);
      if (d > 5.2 * (0.85 + h01(x * 19 + y * 41) * 0.3)) continue;
      const t = map.terrain[idx(x, y)];
      if (t === 'path' || t === 'glade') continue;
      put(x, y, h01(x * 7 + y * 101) < 0.16 ? 'water' : 'ruin');
    }
  }
  let nextId = map.deco.reduce((m, dd) => Math.max(m, dd.id), 0) + 1;
  for (let i = 0; i < 9; i++) {
    const a = (i / 9) * Math.PI * 2 + h01(i * 31) * 0.7;
    const rr = 2.6 + h01(i * 57) * 2.1;
    map.deco.push({ id: nextId++, kind: 'stone', pos: { x: 14 + Math.cos(a) * rr, y: 20 + Math.sin(a) * rr * 0.85 }, r: 0.5, seed: h01(i * 91) * 1e9 });
  }
  map.deco.push({ id: nextId++, kind: 'bones', pos: { x: 14.5, y: 19.4 }, r: 0.2, seed: 77 });

  // 6. Линза северного разлома: визуальные трещины от Чёрного брода к Разлому
  for (let i = 0; i < FAULT_LINE.length; i++) {
    const p = FAULT_LINE[i];
    const jx = (h01(i * 71) - 0.5) * 1.6, jy = (h01(i * 97) - 0.5) * 1.6;
    map.deco.push({ id: nextId++, kind: 'crack', pos: { x: p.x + jx, y: p.y + jy }, r: 0, seed: h01(i * 131) * 1e9 });
  }

  // 7. Тайные трещины: карман, запечатанный скалой; вход — только для малого тела
  for (const spot of CRACK_SPOTS) {
    // внутри кармана и у входа не должно быть случайных деревьев/камней — проход обязан работать
    map.deco = map.deco.filter((d) => {
      if (d.r <= 0) return true;
      const dPocket = Math.hypot(d.pos.x - spot.pocket.x, d.pos.y - spot.pocket.y);
      const dEntry = Math.hypot(d.pos.x - spot.entrance.x, d.pos.y - spot.entrance.y);
      return !(dPocket < spot.rOut + 1.4 || dEntry < 1.8);
    });
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const d = Math.hypot(x + 0.5 - spot.pocket.x, y + 0.5 - spot.pocket.y);
        if (d < spot.rIn) {
          if (map.terrain[idx(x, y)] === 'water' || map.terrain[idx(x, y)] === 'mountain') put(x, y, 'moss');
          continue;
        }
        if (d > spot.rOut || d < spot.rIn + 0.35) continue;
        // вход: прореха в кольце у трещины
        if (Math.hypot(x + 0.5 - spot.entrance.x, y + 0.5 - spot.entrance.y) < 0.95) continue;
        put(x, y, 'mountain');
      }
    }
    map.deco.push({ id: nextId++, kind: 'crack', pos: { ...spot.entrance }, r: 0.9, seed: 123456789 });
    // добыча внутри: грибы (проходимы) и кристальный отросток
    for (let i = 0; i < 3; i++) {
      const a = (i / 3) * Math.PI * 2 + 0.5;
      map.deco.push({ id: nextId++, kind: 'mushroom', pos: { x: spot.pocket.x + Math.cos(a) * 0.9, y: spot.pocket.y + Math.sin(a) * 0.9 }, r: 0, seed: h01(i * 17 + 5) * 1e9 });
    }
    map.deco.push({ id: nextId++, kind: 'crystal', pos: { x: spot.pocket.x, y: spot.pocket.y - 1.1 }, r: 0, seed: 555 });
  }

  // 8. Камыш топей
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (map.terrain[idx(x, y)] !== 'marsh') continue;
      if (h01(x * 37 + y * 53) > 0.085) continue;
      map.deco.push({ id: nextId++, kind: 'reed', pos: { x: x + 0.5, y: y + 0.5 }, r: 0, seed: h01(x * 7 + y * 3) * 1e9 });
    }
  }

  // 9. Страховка миграции: критические точки обязаны остаться проходимыми
  for (const c of CRITICAL_POINTS) {
    const xi = Math.floor(c.x), yi = Math.floor(c.y);
    if (xi < 0 || yi < 0 || xi >= w || yi >= h) continue;
    const t = map.terrain[idx(xi, yi)];
    if (t === 'water' || t === 'mountain') map.terrain[idx(xi, yi)] = 'moss';
  }
}
