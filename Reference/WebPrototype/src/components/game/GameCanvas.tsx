'use client';

// Слой Presentation: изометрический рендер мира на Canvas 2D + ввод (тап/клик, WASD, джойстик).
// Управление соответствует будущей Android-схеме (§13): тап — идти/атаковать/поглощать,
// виртуальный джойстик на левой половине экрана, кнопки действий в HUD.

import { useEffect, useRef } from 'react';
import type { Engine } from '@/game/engine';
import type { Creature, Deco, Npc } from '@/game/types';
import { SPECIES } from '@/game/creatures';
import { DUNGEONS, type DungeonDef } from '@/game/dungeon';
import type { SenseOrgan } from '@/game/senses';
import { SlimeJelly, resolvePalette } from './SlimeJelly';
import type { World } from '@/game/world';

// Срез восприятия: симуляция решает, что известно игроку (Дополнение к ТЗ п.7)
type SenseView = ReturnType<World['senseView']>;

const TW = 64;  // ширина ромба тайла
const TH = 32;  // высота ромба тайла

const w2sX = (wx: number, wy: number) => (wx - wy) * (TW / 2);
const w2sY = (wx: number, wy: number) => (wx + wy) * (TH / 2);
const s2wX = (sx: number, sy: number) => (sx / (TW / 2) + sy / (TH / 2)) / 2;
const s2wY = (sx: number, sy: number) => (sy / (TH / 2) - sx / (TW / 2)) / 2;

function hash01(n: number): number {
  const x = Math.sin(n * 127.1) * 43758.5453;
  return x - Math.floor(x);
}

// ===== Жидкое желе-тело слизня: рендерер с состоянием между кадрами (референс «милый слайм») =====
const jelly = new SlimeJelly();

// желе-тела существ-слизней (slug/crystalslug): состояние между кадрами по id существа
const creatureJellies = new Map<number, SlimeJelly>();

// регенерация (референс «Регенерация / Восстановление»): золотые капли падают в тело + искры
let lastHpSeen = -1;
let regenT = 0;
let regenSpawnT = 0;
const regenDrops: { ax: number; ay: number; vy: number; life: number; max: number; r: number }[] = [];

// ===== Сезоны (Этап 2 карты): текущий сезон надземья для визуала; null — подземелья/норка =====
let curSeason: 'Пробуждение' | 'Цветение' | 'Увядание' | 'Тьма' | null = null;

/** Сезонная палитра травяных тайлов (надземье): мох/поляна/тропа/грунт. */
function seasonTerrainCol(t: string, v: number): string | null {
  if (!curSeason) return null;
  if (t === 'moss') {
    if (curSeason === 'Пробуждение') return v > 0.5 ? '#1a2c1c' : '#182a1d';
    if (curSeason === 'Цветение') return v > 0.5 ? '#1d331e' : '#1b301f';
    if (curSeason === 'Увядание') return v > 0.5 ? '#262816' : '#232515'; // рыжий листопад
    return v > 0.5 ? '#1a2228' : '#182027'; // Тьма — холодный синеватый наст
  }
  if (t === 'glade') {
    if (curSeason === 'Пробуждение') return v > 0.5 ? '#203623' : '#1e3322';
    if (curSeason === 'Цветение') return v > 0.5 ? '#233a24' : '#213826';
    if (curSeason === 'Увядание') return v > 0.5 ? '#2b2a17' : '#282716';
    return v > 0.5 ? '#1d2630' : '#1b242e';
  }
  if (t === 'dirt') {
    if (curSeason === 'Тьма') return v > 0.5 ? '#1f2026' : '#1d1e24';
    if (curSeason === 'Увядание') return v > 0.5 ? '#262014' : '#231e13';
  }
  if (t === 'marsh') {
    if (curSeason === 'Тьма') return v > 0.5 ? '#17201f' : '#151e1d';
  }
  return null;
}

// FX-события приходят из симуляции (World.fx, §17) и превращаются рендером в частицы/цифры урона.

export default function GameCanvas({
  engine, onNpcInteract, onTapCreatureInfo,
}: {
  engine: Engine;
  onNpcInteract: (npcId: string) => void;
  onTapCreatureInfo: (c: Creature) => void;
}) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const keys = useRef<Record<string, boolean>>({});
  const joy = useRef<{ active: boolean; ox: number; oy: number; dx: number; dy: number; id: number }>({ active: false, ox: 0, oy: 0, dx: 0, dy: 0, id: -1 });
  const moveTo = useRef<{ x: number; y: number } | null>(null);
  const attackTarget = useRef<number | null>(null);
  const tapPending = useRef<{ x: number; y: number; t: number } | null>(null);
  const camZoom = useRef(0.95);
  const spores = useRef<{ x: number; y: number; vx: number; vy: number; a: number }[]>([]);
  const trail = useRef<{ x: number; y: number; t: number; r: number }[]>([]);   // слизиный след (мировые коорд.)
  const fxParts = useRef<{ x: number; y: number; vx: number; vy: number; life: number; max: number; color: string; size: number; homing?: boolean }[]>([]);
  const floatersW = useRef<{ x: number; y: number; t: number; text: string; color: string }[]>([]);
  const camS = useRef({ x: 0, y: 0, init: false });
  const camWorld = useRef<unknown>(null);
  const absorbPulse = useRef(0);
  const lastLayer = useRef('');

  useEffect(() => {
    const canvas = canvasRef.current!;
    const ctx = canvas.getContext('2d')!;
    let raf = 0;
    let last = performance.now();
    let time = 0;

    const resize = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.floor(canvas.clientWidth * dpr);
      canvas.height = Math.floor(canvas.clientHeight * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      camZoom.current = canvas.clientWidth < 720 ? 0.78 : 0.95;
    };
    resize();
    window.addEventListener('resize', resize);

    // частицы спор
    for (let i = 0; i < 36; i++) {
      spores.current.push({ x: Math.random(), y: Math.random(), vx: (Math.random() - 0.5) * 0.01, vy: -0.004 - Math.random() * 0.006, a: Math.random() * Math.PI * 2 });
    }

    const onKey = (e: KeyboardEvent, down: boolean) => {
      const k = e.key.toLowerCase();
      keys.current[k] = down;
      if (down && (k === ' ' || k === 'e')) {
        e.preventDefault();
        if (k === ' ') engine.queueAttack();
        if (k === 'e') engine.interact();
      }
      if (down && (k === 'w' || k === 'a' || k === 's' || k === 'd' || k.startsWith('arrow'))) {
        moveTo.current = null;
        attackTarget.current = null;
      }
    };
    const kd = (e: KeyboardEvent) => onKey(e, true);
    const ku = (e: KeyboardEvent) => onKey(e, false);
    window.addEventListener('keydown', kd);
    window.addEventListener('keyup', ku);

    // ===== Указатели: тап/клик + джойстик =====
    const pos = (e: PointerEvent) => {
      const r = canvas.getBoundingClientRect();
      return { x: e.clientX - r.left, y: e.clientY - r.top };
    };
    const onDown = (e: PointerEvent) => {
      const p = pos(e);
      if (e.pointerType === 'touch' && p.x < canvas.clientWidth * 0.45 && p.y > canvas.clientHeight * 0.35) {
        joy.current = { active: true, ox: p.x, oy: p.y, dx: 0, dy: 0, id: e.pointerId };
        return;
      }
      tapPending.current = { x: p.x, y: p.y, t: performance.now() };
    };
    const onMove = (e: PointerEvent) => {
      if (joy.current.active && e.pointerId === joy.current.id) {
        const p = pos(e);
        let dx = p.x - joy.current.ox, dy = p.y - joy.current.oy;
        const len = Math.hypot(dx, dy);
        if (len > 56) { dx = (dx / len) * 56; dy = (dy / len) * 56; }
        joy.current.dx = dx; joy.current.dy = dy;
      }
    };
    const onUp = (e: PointerEvent) => {
      if (joy.current.active && e.pointerId === joy.current.id) {
        joy.current.active = false; joy.current.dx = 0; joy.current.dy = 0;
        return;
      }
      const t = tapPending.current;
      tapPending.current = null;
      if (!t) return;
      const dt = performance.now() - t.t;
      if (dt > 350) return; // длинное нажатие — не тап
      handleTap(t.x, t.y);
    };
    const handleTap = (sx: number, sy: number) => {
      const world = engine.world;
      if (!world) return;
      const z = camZoom.current;
      const cam = cameraPos();
      const wx = s2wX((sx - canvas.clientWidth / 2) / z + cam.sx, (sy - canvas.clientHeight / 2) / z + cam.sy);
      const wy = s2wY((sx - canvas.clientWidth / 2) / z + cam.sx, (sy - canvas.clientHeight / 2) / z + cam.sy);
      const dun = world.dun;
      const inDun = !!(dun && dun.active);
      const p = world.data.player;
      // врата/объекты видны тапом только если известны восприятию (п.7 — без утечек)
      const tapKnown = (gx: number, gy: number) =>
        world.isTileExplored(Math.floor(gx), Math.floor(gy))
        || world.coreActive
        || (world.data.senses!.unlocked.includes('light') && Math.hypot(gx - p.pos.x, gy - p.pos.y) < world.senseModsNow().lightR);
      // интерактивные объекты слоя: врата / лестницы / запасы — тап по ним = [E]
      if (inDun) {
        const f = dun!.floors[dun!.depth - 1];
        const nearDown = dun!.depth < dun!.floors.length
          && Math.hypot(f.down.x - wx, f.down.y - wy) < 1.15 && Math.hypot(f.down.x - p.pos.x, f.down.y - p.pos.y) < 1.5;
        const nearUp = Math.hypot(f.up.x - wx, f.up.y - wy) < 1.15 && Math.hypot(f.up.x - p.pos.x, f.up.y - p.pos.y) < 1.5;
        const cache = dun!.caches.find((c) => !c.taken && c.depth === dun!.depth
          && world.isTileExplored(Math.floor(c.pos.x), Math.floor(c.pos.y))
          && Math.hypot(c.pos.x - wx, c.pos.y - wy) < 1.0 && Math.hypot(c.pos.x - p.pos.x, c.pos.y - p.pos.y) < 1.45);
        if (nearDown || nearUp || cache) { engine.interact(); return; }
      } else {
        for (const def of Object.values(DUNGEONS)) {
          const gate = def.gatePos(world.data.pois);
          if (tapKnown(gate.x, gate.y) && Math.hypot(gate.x - wx, gate.y - wy) < 1.3 && Math.hypot(gate.x - p.pos.x, gate.y - p.pos.y) < 1.7) {
            engine.interact();
            return;
          }
        }
      }
      // тап по существу? (только активный слой и только ощущённое — п.7)
      const layerDepth = inDun ? dun!.depth : 0;
      let hit: Creature | Npc | null = null;
      let bd = 0.9;
      for (const c of world.data.creatures) {
        if (c.dead) continue;
        if ((c.dungeonFloor ?? 0) !== layerDepth) continue;
        if (!world.isEntitySensed(c.id)) continue;
        const d = Math.hypot(c.pos.x - wx, (c.pos.y - wy) * 1.6);
        if (d < bd) { bd = d; hit = c; }
      }
      if (!inDun) {
        for (const n of world.data.npcs) {
          if (!n.alive) continue;
          if (!world.isNpcSensed(n.id)) continue;
          const d = Math.hypot(n.pos.x - wx, (n.pos.y - wy) * 1.6);
          if (d < bd) { bd = d; hit = n; }
        }
      }
      if (hit) {
        if ('ai' in hit) {
          const c = hit as Creature;
          if (c.dead) return;
          attackTarget.current = c.id;
          moveTo.current = null;
          onTapCreatureInfo(c);
        } else {
          const n = hit as Npc;
          if (Math.hypot(n.pos.x - world.data.player.pos.x, n.pos.y - world.data.player.pos.y) < 1.6) {
            onNpcInteract(n.id);
          } else {
            moveTo.current = { x: n.pos.x + 0.5, y: n.pos.y + 0.6 };
            attackTarget.current = null;
          }
        }
        return;
      }
      // тап по трупу?
      if (world.nearCorpseId != null) {
        const corpse = world.data.creatures.find((c) => c.id === world.nearCorpseId);
        if (corpse && Math.hypot(corpse.pos.x - wx, (corpse.pos.y - wy) * 1.6) < 1.0 && Math.hypot(corpse.pos.x - world.data.player.pos.x, corpse.pos.y - world.data.player.pos.y) < 1.4) {
          engine.tryAbsorb();
          return;
        }
      }
      moveTo.current = { x: wx, y: wy };
      attackTarget.current = null;
    };
    canvas.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);

    const cameraPos = () => ({ sx: camS.current.x, sy: camS.current.y });

    // Камера следует за слизнем плавно (инерция взгляда), но мгновенно при телепорте
    // (вход в подземелье / реинкарнация / загрузка сейва) — иначе видно перелёт через карту.
    const updateCam = (dt: number) => {
      const world = engine.world;
      if (!world) return;
      const pos = world.data.player.pos;
      const tx = w2sX(pos.x, pos.y), ty = w2sY(pos.x, pos.y);
      if (!camS.current.init || camWorld.current !== world
        || Math.hypot(tx - camS.current.x, ty - camS.current.y) > 500) {
        camS.current = { x: tx, y: ty, init: true };
        camWorld.current = world;
        return;
      }
      const ct = Math.min(1, dt * 7);
      camS.current.x += (tx - camS.current.x) * ct;
      camS.current.y += (ty - camS.current.y) * ct;
    };

    // ===== Ввод в мировые координаты =====
    const gatherInput = () => {
      const world = engine.world;
      if (!world || engine.phase !== 'life') return;
      let sdx = 0, sdy = 0;
      const k = keys.current;
      if (k['w'] || k['arrowup'] || k['ц']) sdy -= 1;
      if (k['s'] || k['arrowdown'] || k['ы']) sdy += 1;
      if (k['a'] || k['arrowleft'] || k['ф']) sdx -= 1;
      if (k['d'] || k['arrowright'] || k['в']) sdx += 1;
      if (joy.current.active) { sdx = joy.current.dx / 56; sdy = joy.current.dy / 56; }
      const sprint = !!(k['shift'] && (sdx || sdy));
      if (Math.abs(sdx) > 0.05 || Math.abs(sdy) > 0.05) {
        moveTo.current = null;
      }
      // движение к точке тапа
      if (!sdx && !sdy && moveTo.current) {
        const p = world.data.player.pos;
        const dx = moveTo.current.x - p.x, dy = moveTo.current.y - p.y;
        const d = Math.hypot(dx, dy);
        if (d < 0.18) moveTo.current = null;
        else {
          // в экранные координаты, чтобы направление совпадало с тапом
          sdx = (w2sX(dx, dy)) / 40; sdy = (w2sY(dx, dy)) / 40;
        }
      }
      // автопогоня за целью атаки
      if (attackTarget.current != null) {
        const t = world.data.creatures.find((c) => c.id === attackTarget.current && !c.dead);
        if (!t) { attackTarget.current = null; }
        else {
          const p = world.data.player.pos;
          const d = Math.hypot(t.pos.x - p.x, t.pos.y - p.y);
          if (d > 0.95) {
            sdx = w2sX(t.pos.x - p.x, t.pos.y - p.y) / 40;
            sdy = w2sY(t.pos.x - p.x, t.pos.y - p.y) / 40;
          } else {
            sdx = 0; sdy = 0;
            engine.queueAttack();
          }
        }
      }
      // экранное направление → мировое
      const wx = s2wX(sdx, sdy), wy = s2wY(sdx, sdy);
      const len = Math.hypot(wx, wy);
      if (len > 0.02) engine.setInput(wx / len, wy / len, sprint);
      else engine.setInput(0, 0, false);
    };

    // ===== Отрисовка =====
    const draw = (dt: number) => {
      const world = engine.world;
      const W = canvas.clientWidth, H = canvas.clientHeight;
      ctx.fillStyle = '#070907';
      ctx.fillRect(0, 0, W, H);
      if (!world) return;
      time += dt;
      const z = camZoom.current;
      const cam = cameraPos();
      const night = world.clock.isNight;
      const dusk = world.clock.isDusk;
      // сезоны (Этап 2): подземелья/норка — вне сезонов, палитра нейтральная
      curSeason = world.data.activeDungeon ? null : world.season;
      // срез восприятия: что известно игроку в этом кадре
      const sn = world.senseView();
      const playerPos = world.data.player.pos;

      ctx.save();
      ctx.translate(W / 2, H / 2);
      ctx.scale(z, z);
      ctx.translate(-cam.sx, -cam.sy);

      // тряска экрана от полученного урона — мир дрожит вместе с камерой
      const sh = world.shakeT > 0 ? Math.min(1, world.shakeT / 0.28) : 0;
      if (sh > 0) ctx.translate((Math.random() - 0.5) * 7 * sh, (Math.random() - 0.5) * 5 * sh);
      // перегрузка магической энергией: тело дрожит мелкой дрожью (Дополнение к ТЗ п.11)
      if (sn.overload) ctx.translate(Math.sin(time * 17) * 1.6, Math.cos(time * 13) * 1.4);

      // ---- тайлы (только видимые; активный слой — подземелье или надземье) ----
      const dun = world.dun;
      const inDun = !!(dun && dun.active);
      const curFloor = inDun ? dun!.floors[dun!.depth - 1] : null;
      const ddef = inDun ? DUNGEONS[world.dungeonId!] : null;
      const pal = ddef?.palette;
      const pois = world.data.pois;
      const terr = curFloor ? curFloor.terrain : world.data.mapJson.terrain;
      const mw = curFloor ? curFloor.w : world.data.mapJson.w;
      const mh = curFloor ? curFloor.h : world.data.mapJson.h;
      const corner1x = s2wX(cam.sx - W / 2 / z, cam.sy - H / 2 / z);
      const corner1y = s2wY(cam.sx - W / 2 / z, cam.sy - H / 2 / z);
      const corner2x = s2wX(cam.sx + W / 2 / z, cam.sy + H / 2 / z);
      const corner2y = s2wY(cam.sx + W / 2 / z, cam.sy + H / 2 / z);
      const pad = 2;
      const minTX = Math.max(0, Math.floor(Math.min(corner1x, corner2x) - pad));
      const maxTX = Math.min(mw - 1, Math.ceil(Math.max(corner1x, corner2x) + pad));
      const minTY = Math.max(0, Math.floor(Math.min(corner1y, corner2y) - pad));
      const maxTY = Math.min(mh - 1, Math.ceil(Math.max(corner1y, corner2y) + pad));

      for (let ty = minTY; ty <= maxTY; ty++) {
        for (let tx = minTX; tx <= maxTX; tx++) {
          const t = terr[ty * mw + tx];
          // туман информации: неразведанный тайл — тьма, пока его не ощутил хоть один орган (п.7)
          if (!sn.explored(tx, ty)) continue;
          const sx = w2sX(tx, ty), sy = w2sY(tx, ty);
          const v = hash01(tx * 57 + ty * 131);
          let col = '#17251a';
          if (curFloor && pal) {
            if (t === 'rock') col = v > 0.5 ? pal.rockA : pal.rockB;
            else if (t === 'floor') col = v > 0.5 ? pal.floorA : pal.floorB;
            else if (t === 'glow') col = v > 0.5 ? pal.glowA : pal.glowB;
            else if (t === 'puddle') col = pal.puddle;
          }
          else {
            const sc = seasonTerrainCol(t, v); // сезоны перекрашивают травы/грунт (Этап 2)
            if (sc) col = sc;
            else if (t === 'moss') col = v > 0.5 ? '#182619' : '#16231a';
            else if (t === 'glade') col = v > 0.5 ? '#1d2f1f' : '#1b2c1e';
            else if (t === 'path') col = v > 0.5 ? '#2b2419' : '#292217';
            else if (t === 'dirt') col = v > 0.5 ? '#221d16' : '#1f1b14';
            else if (t === 'ruin') col = v > 0.5 ? '#232022' : '#201e20';
            else if (t === 'water') col = '#122a29';
            // «Чаша Отголосов»: горы/топи/пепел
            else if (t === 'mountain') col = v > 0.5 ? '#26272c' : '#222329';
            else if (t === 'marsh') col = v > 0.5 ? '#1a2317' : '#172015';
            else if (t === 'ash') col = v > 0.5 ? '#242426' : '#212123';
          }
          ctx.fillStyle = col;
          ctx.beginPath();
          ctx.moveTo(sx, sy - TH / 2);
          ctx.lineTo(sx + TW / 2, sy);
          ctx.lineTo(sx, sy + TH / 2);
          ctx.lineTo(sx - TW / 2, sy);
          ctx.closePath();
          ctx.fill();
          // тело само «освещает» то, что чувствует вплотную: осязание — свой пул света,
          // зрение — свой радиус; память без осязания — приглушённая (критерий №4)
          const dpx = Math.hypot(tx + 0.5 - playerPos.x, ty + 0.5 - playerPos.y);
          const visNow = (sn.visionOn && dpx <= sn.visionRadius) || dpx <= (sn.touchR ?? 1.75) + 0.4;
          if (!visNow) {
            ctx.fillStyle = sn.visionOn ? 'rgba(4,6,9,0.34)' : 'rgba(4,6,9,0.46)';
            ctx.beginPath();
            ctx.moveTo(sx, sy - TH / 2);
            ctx.lineTo(sx + TW / 2, sy);
            ctx.lineTo(sx, sy + TH / 2);
            ctx.lineTo(sx - TW / 2, sy);
            ctx.closePath();
            ctx.fill();
          }
          if (curFloor && t === 'glow' && pal) {
            const g = 0.10 + Math.sin(time * 1.8 + tx * 1.1 + ty * 0.7) * 0.05;
            ctx.fillStyle = `${pal.glowColor}${g})`;
            ctx.fill();
          }
          if (curFloor && t === 'puddle') {
            const g = 0.10 + Math.sin(time * 1.2 + tx + ty) * 0.04;
            ctx.fillStyle = `rgba(80,140,150,${g})`;
            ctx.fill();
          }
          if (t === 'water') {
            const g = 0.12 + Math.sin(time * 1.4 + tx * 0.9 + ty * 1.3) * 0.05;
            ctx.fillStyle = `rgba(70,140,130,${g})`;
            ctx.fill();
          }
          // «Чаша Отголосов»: фактуры новых террейнов
          if (t === 'mountain') {
            // грань хребта: холодный свет на верхней кромке ромба
            ctx.strokeStyle = 'rgba(190,200,225,0.10)';
            ctx.lineWidth = 1;
            ctx.beginPath(); ctx.moveTo(sx - TW / 2, sy); ctx.lineTo(sx, sy - TH / 2); ctx.lineTo(sx + TW / 2, sy); ctx.stroke();
          } else if (t === 'marsh') {
            const g = 0.07 + Math.sin(time * 1.1 + tx * 2.1 + ty * 0.7) * 0.03;
            ctx.fillStyle = `rgba(120,190,120,${g})`;
            ctx.fill();
          } else if (t === 'ash' && v > 0.82) {
            ctx.fillStyle = 'rgba(215,215,205,0.10)';
            ctx.beginPath(); ctx.arc(sx - 3, sy - 1, 1.6, 0, Math.PI * 2); ctx.fill();
          }
        }
      }

      // ---- отпечатки осязания: контуры поверхностей угасают со временем (п.6 Дополнения) ----
      for (const imp of sn.imprints.values()) {
        const a = Math.max(0, 1 - imp.age / 8);
        if (a <= 0.02) continue;
        const ix = w2sX(imp.x, imp.y), iy = w2sY(imp.x, imp.y);
        if (imp.kind === 'solid') {
          ctx.strokeStyle = `rgba(214,224,206,${a * 0.5})`;
          ctx.lineWidth = 1.1;
          ctx.beginPath();
          ctx.moveTo(ix, iy - 7);
          ctx.lineTo(ix + 15, iy);
          ctx.lineTo(ix, iy + 7);
          ctx.lineTo(ix - 15, iy);
          ctx.closePath();
          ctx.stroke();
        } else {
          // контур существа: опасность читается даже вслепую — цвет по рациону
          const col = imp.diet === 'carnivore' ? '235,120,90'
            : imp.diet === 'spirit' ? '200,150,230'
            : imp.diet === 'herbivore' || imp.diet === 'detritivore' ? '170,225,150'
            : '214,224,206';
          const wob = Math.sin(time * 5 + imp.x * 3) * 1.5;
          ctx.strokeStyle = `rgba(${col},${a * 0.75})`;
          ctx.lineWidth = 1.4;
          ctx.beginPath();
          ctx.ellipse(ix, iy - 5, 12 + wob, 7, 0, 0, Math.PI * 2);
          ctx.stroke();
        }
      }

      // ---- потребление FX-событий симуляции (§17: симуляция сообщает — рендер рисует) ----
      for (const e of world.fx.splice(0)) {
        if (e.kind === 'dmg') {
          floatersW.current.push({ x: e.x, y: e.y, t: 0, text: e.text ?? '', color: e.color ?? '232,224,204' });
        } else if (e.kind === 'hit' || e.kind === 'kill') {
          const n = e.kind === 'kill' ? 12 : 6;
          for (let i = 0; i < n; i++) {
            const a = Math.random() * Math.PI * 2;
            const sp = e.kind === 'kill' ? 1.2 + Math.random() * 2.2 : 0.7 + Math.random() * 1.2;
            fxParts.current.push({ x: e.x, y: e.y - 0.2, vx: Math.cos(a) * sp, vy: Math.sin(a) * sp * 0.5 - 0.9, life: 0, max: 0.4 + Math.random() * 0.3, color: e.color ?? '212,160,23', size: 1.2 + Math.random() * 1.8 });
          }
        } else if (e.kind === 'absorb') {
          absorbPulse.current = 1;
          for (let i = 0; i < 12; i++) {
            const a = Math.random() * Math.PI * 2, r = 0.25 + Math.random() * 0.6;
            fxParts.current.push({ x: e.x + Math.cos(a) * r, y: e.y + Math.sin(a) * r * 0.5, vx: 0, vy: 0, life: 0, max: 0.55 + Math.random() * 0.2, color: e.color ?? '140,230,160', size: 1 + Math.random() * 1.7, homing: true });
          }
          floatersW.current.push({ x: e.x, y: e.y - 0.6, t: 0, text: e.text ?? '', color: e.color ?? '140,230,160' });
        }
      }
      absorbPulse.current = Math.max(0, absorbPulse.current - dt * 2.2);

      // ---- слизиный след: тело оставляет угасающую дорожку (механика слизи, §9.1) ----
      const inst = world.data.player.genome.installed;
      const trailCol = inst.includes('crystal_armor') || inst.includes('prismatic_core') || inst.includes('crystal_vein')
        ? '110,210,235'
        : inst.includes('bioluminescent') ? '150,240,170' : '96,118,74';
      const pl = world.data.player;
      const layerKey = inDun ? `d${world.dungeonId}:${dun!.depth}` : 's';
      if (layerKey !== lastLayer.current) { trail.current.length = 0; lastLayer.current = layerKey; }
      const spdNow = Math.hypot(pl.vel.x, pl.vel.y);
      const lastP = trail.current[trail.current.length - 1];
      if (spdNow > 0.35 && (!lastP || Math.hypot(pl.pos.x - lastP.x, pl.pos.y - lastP.y) > 0.24)) {
        trail.current.push({ x: pl.pos.x + (Math.random() - 0.5) * 0.16, y: pl.pos.y + (Math.random() - 0.5) * 0.1, t: 0, r: 0.16 + Math.random() * 0.09 });
        if (trail.current.length > 90) trail.current.shift();
      }
      for (const s of trail.current) s.t += dt;
      while (trail.current.length && trail.current[0].t > 2.6) trail.current.shift();
      for (const s of trail.current) {
        const a = (1 - s.t / 2.6) * 0.20;
        const tx2 = w2sX(s.x, s.y), ty2 = w2sY(s.x, s.y);
        const rr = 9 * (s.r / 0.2);
        if (trailCol !== '96,118,74') { // светящиеся гены подсвечивают след (Регион 2/3)
          ctx.fillStyle = `rgba(${trailCol},${a * 0.45})`;
          ctx.beginPath(); ctx.ellipse(tx2, ty2, rr * 1.8, rr * 0.9, 0, 0, Math.PI * 2); ctx.fill();
        }
        ctx.fillStyle = `rgba(${trailCol},${a})`;
        ctx.beginPath(); ctx.ellipse(tx2, ty2, rr, rr * 0.5, 0, 0, Math.PI * 2); ctx.fill();
      }

      // ---- объекты слоя: врата / лестницы / запасы (нижний слой) — только разведанные (п.7) ----
      if (inDun) {
        const f = dun!.floors[dun!.depth - 1];
        if (sn.explored(f.up.x, f.up.y)) drawStairs(ctx, f.up.x + 0.5, f.up.y + 0.5, 'up', time);
        if (dun!.depth < dun!.floors.length && sn.explored(f.down.x, f.down.y)) drawStairs(ctx, f.down.x + 0.5, f.down.y + 0.5, 'down', time);
        for (const cache of dun!.caches) {
          if (cache.taken || cache.depth !== dun!.depth) continue;
          if (!sn.explored(Math.floor(cache.pos.x), Math.floor(cache.pos.y))) continue;
          drawCache(ctx, cache.pos.x, cache.pos.y, time, ddef!.cacheKind);
        }
      }

      // ---- маркеры POI ----
      const drawMarker = (x: number, y: number, color: string) => {
        const sx = w2sX(x, y), sy = w2sY(x, y) - 26;
        const bob = Math.sin(time * 3) * 4;
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(sx, sy - 10 + bob);
        ctx.lineTo(sx + 7, sy - 20 + bob);
        ctx.lineTo(sx - 7, sy - 20 + bob);
        ctx.closePath();
        ctx.fill();
      };
      const f = world.data.flags;
      // врата/объекты известны, если тайл разведан, их свет пойман светочувствительностью или чутьём
      const gateKnown = (gx: number, gy: number) =>
        sn.explored(Math.floor(gx), Math.floor(gy))
        || (sn.unlocked.includes('light') && Math.hypot(gx - playerPos.x, gy - playerPos.y) <= sn.lightR)
        || sn.core;
      if (f.wolfQuestAccepted && !f.alphaKilled && sn.explored(Math.floor(pois.burrow.pos.x), Math.floor(pois.burrow.pos.y)))
        drawMarker(world.data.pois.burrow.pos.x, world.data.pois.burrow.pos.y, '#c9553d');
      if (f.showStoneCircle && sn.explored(Math.floor(pois.stone_circle.pos.x), Math.floor(pois.stone_circle.pos.y)))
        drawMarker(world.data.pois.stone_circle.pos.x, world.data.pois.stone_circle.pos.y, '#9a7ab8');
      if (!inDun) {
        for (const def of Object.values(DUNGEONS)) {
          const g = def.gatePos(pois);
          if (gateKnown(g.x, g.y)) drawMarker(g.x, g.y, def.palette.gateGlow);
        }
      }

      // ---- сущности, сортированные по глубине (только активный слой; только sensed, п.7) ----
      type Item = { depth: number; kind: 'deco' | 'creature' | 'player' | 'npc' | 'gate'; obj: unknown };
      const items: Item[] = [];
      const layerDepth = inDun ? dun!.depth : 0;
      if (!inDun) {
        for (const d of world.data.mapJson.deco) {
          if (d.pos.x < minTX - 3 || d.pos.x > maxTX + 3 || d.pos.y < minTY - 3 || d.pos.y > maxTY + 3) continue;
          if (!sn.explored(Math.floor(d.pos.x), Math.floor(d.pos.y))) continue;
          items.push({ depth: d.pos.x + d.pos.y, kind: 'deco', obj: d });
        }
        // врата подземелий — в общем глубинном слое, но только разведанные
        for (const def of Object.values(DUNGEONS)) {
          const g = def.gatePos(pois);
          if (!gateKnown(g.x, g.y)) continue;
          items.push({ depth: g.x + g.y + 0.02, kind: 'gate', obj: def });
        }
      }
      for (const c of world.data.creatures) {
        if (c.dead && (c.absorbed || (c.corpseT ?? 0) <= 0)) continue;
        if ((c.dungeonFloor ?? 0) !== layerDepth) continue;
        if (!sn.creature(c.id)) continue; // ни один орган ещё не ощутил — скрыто (п.7)
        items.push({ depth: c.pos.x + c.pos.y + (c.dead ? 0.01 : 0.02), kind: 'creature', obj: c });
      }
      // сборка мусора желе-состояний погибших/поглощённых существ
      if (creatureJellies.size > 120) {
        const aliveIds = new Set(world.data.creatures.map((x) => x.id));
        for (const id of Array.from(creatureJellies.keys())) if (!aliveIds.has(id)) creatureJellies.delete(id);
      }
      if (!inDun) {
        for (const n of world.data.npcs) {
          if (!n.alive) continue;
          if (!sn.npc(n.id)) continue;
          items.push({ depth: n.pos.x + n.pos.y + 0.015, kind: 'npc', obj: n });
        }
      }
      items.push({ depth: world.data.player.pos.x + world.data.player.pos.y + 0.03, kind: 'player', obj: world.data.player });
      items.sort((a, b) => a.depth - b.depth);

      for (const it of items) {
        if (it.kind === 'deco') drawDeco(ctx, it.obj as Deco, time);
        else if (it.kind === 'creature') {
          const c = it.obj as Creature;
          const info = sn.creature(c.id)!;
          // режим восприятия: самый информативный орган определяет картинку
          const mode = info.organs.size > 0 ? senseModeOf(info.organs) : 'ghost';
          if (mode === 'vision') drawCreature(ctx, engine, c, time, dt);
          else drawSenseView(ctx, mode, c, info, time, sn.now);
        }
        else if (it.kind === 'npc') {
          const n = it.obj as Npc;
          const info = sn.npc(n.id)!;
          const mode = info.organs.size > 0 ? npcModeOf(info.organs) : 'ghost';
          if (mode === 'vision') drawNpc(ctx, n, time);
          else drawNpcView(ctx, info, time, sn.now);
        }
        else if (it.kind === 'gate') { const def = it.obj as DungeonDef; const g = def.gatePos(pois); drawGate(ctx, g.x + 0.5, g.y + 0.5, time, def.cacheKind); }
        else drawPlayer(ctx, engine, time, absorbPulse.current, dt);
      }

      // ---- духовное чутьё (ядро): души, магические поля, отпечатки событий (п.10) ----
      if (sn.core) {
        const sr = sn.spiritR;
        for (const c of world.data.creatures) {
          if (c.dead && (c.absorbed || (c.corpseT ?? 0) <= 0)) continue;
          if ((c.dungeonFloor ?? 0) !== layerDepth) continue;
          if (Math.hypot(c.pos.x - playerPos.x, c.pos.y - playerPos.y) > sr) continue;
          const sx = w2sX(c.pos.x, c.pos.y), sy = w2sY(c.pos.x, c.pos.y) - 6;
          const a = 0.15 + Math.sin(time * 2.2 + c.id) * 0.06;
          ctx.fillStyle = `rgba(186,140,235,${a})`;
          ctx.beginPath(); ctx.ellipse(sx, sy, 14, 9, 0, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = `rgba(232,214,255,${a + 0.16})`;
          ctx.beginPath(); ctx.arc(sx - 3, sy - 2, 1.4, 0, Math.PI * 2); ctx.arc(sx + 3, sy - 2, 1.4, 0, Math.PI * 2); ctx.fill();
        }
        for (const tr of world.data.traces ?? []) {
          const ageD = world.clock.totalDays - tr.day;
          if (ageD > 4) continue;
          if (Math.hypot(tr.x - playerPos.x, tr.y - playerPos.y) > sr) continue;
          const a = (1 - ageD / 4) * 0.42;
          const sx = w2sX(tr.x, tr.y), sy = w2sY(tr.x, tr.y);
          const col = tr.kind === 'boss' ? '255,180,120' : tr.kind === 'absorb' ? '160,230,180' : tr.kind === 'raid' ? '235,120,120' : '200,140,235';
          ctx.strokeStyle = `rgba(${col},${a})`;
          ctx.lineWidth = 1.4;
          ctx.beginPath();
          ctx.moveTo(sx, sy - 7); ctx.lineTo(sx + 6, sy); ctx.lineTo(sx, sy + 7); ctx.lineTo(sx - 6, sy);
          ctx.closePath(); ctx.stroke();
          ctx.fillStyle = `rgba(${col},${a})`;
          ctx.beginPath(); ctx.arc(sx, sy, 1.6, 0, Math.PI * 2); ctx.fill();
        }
      }

      // ---- частицы FX и всплывающие цифры (мировые координаты, следуют за камерой) ----
      const ppos = world.data.player.pos;
      for (const q of fxParts.current) {
        q.life += dt;
        if (q.homing) {
          const dx2 = ppos.x - q.x, dy2 = ppos.y - q.y;
          const d2 = Math.max(0.001, Math.hypot(dx2, dy2));
          q.vx = (dx2 / d2) * 3.4; q.vy = (dy2 / d2) * 3.4 - 0.5;
        } else {
          q.vy += 2.4 * dt; // лёгкая гравитация брызг
        }
        q.x += q.vx * dt; q.y += q.vy * dt;
      }
      fxParts.current = fxParts.current.filter((q) => q.life < q.max);
      for (const q of fxParts.current) {
        const a = Math.max(0, 1 - q.life / q.max);
        const qx = w2sX(q.x, q.y), qy = w2sY(q.x, q.y);
        ctx.fillStyle = `rgba(${q.color},${a})`;
        ctx.beginPath(); ctx.arc(qx, qy, q.size * (0.5 + a * 0.7), 0, Math.PI * 2); ctx.fill();
      }
      for (const fl of floatersW.current) fl.t += dt;
      floatersW.current = floatersW.current.filter((fl) => fl.t < 1.1);
      for (const fl of floatersW.current) {
        const a = Math.max(0, 1 - fl.t / 1.1);
        const fx2 = w2sX(fl.x, fl.y), fy2 = w2sY(fl.x, fl.y) - 22 - fl.t * 30;
        ctx.fillStyle = `rgba(${fl.color},${a})`;
        ctx.font = 'bold 14px ui-sans-serif, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(fl.text, fx2, fy2);
      }

      // ---- столбы света над вратами: видно издалека, если врата известны (Регион 2/3 + п.7) ----
      if (!inDun) {
        for (const def of Object.values(DUNGEONS)) {
          const g = def.gatePos(pois);
          if (!gateKnown(g.x, g.y)) continue;
          const gx = w2sX(g.x + 0.5, g.y + 0.5), gy = w2sY(g.x + 0.5, g.y + 0.5);
          const rgb = def.cacheKind === 'crystal' ? '110,210,235' : def.cacheKind === 'river' ? '110,180,225' : '140,230,160';
          const a = 0.10 + Math.sin(time * 1.7) * 0.04;
          const beam = ctx.createLinearGradient(0, gy - 170, 0, gy);
          beam.addColorStop(0, `rgba(${rgb},0)`);
          beam.addColorStop(1, `rgba(${rgb},${a})`);
          ctx.fillStyle = beam;
          ctx.fillRect(gx - 11, gy - 170, 22, 170);
          ctx.fillStyle = `rgba(${rgb},${a * 1.4})`;
          ctx.beginPath(); ctx.ellipse(gx, gy, 26, 12, 0, 0, Math.PI * 2); ctx.fill();
        }
      }

      // маркер движения
      if (moveTo.current) {
        const sx = w2sX(moveTo.current.x, moveTo.current.y), sy = w2sY(moveTo.current.x, moveTo.current.y);
        ctx.strokeStyle = 'rgba(212,160,23,0.5)';
        ctx.lineWidth = 1.5;
        const r = 8 + Math.sin(time * 6) * 2;
        ctx.beginPath(); ctx.ellipse(sx, sy, r, r / 2, 0, 0, Math.PI * 2); ctx.stroke();
      }

      // подсказка поглощения
      if (world.nearCorpseId != null) {
        const c = world.data.creatures.find((x) => x.id === world.nearCorpseId);
        if (c && !c.absorbed) {
          const sx = w2sX(c.pos.x, c.pos.y), sy = w2sY(c.pos.x, c.pos.y);
          ctx.fillStyle = 'rgba(232,224,204,0.9)';
          ctx.font = '12px ui-sans-serif, sans-serif';
          ctx.textAlign = 'center';
          ctx.fillText('Поглотить [E]', sx, sy - 22 - Math.sin(time * 4) * 2);
        }
      }

      // подсказки взаимодействия слоя (врата/лестницы/запасы)
      const prompt = (x: number, y: number, text: string) => {
        const sx = w2sX(x, y), sy = w2sY(x, y);
        ctx.fillStyle = 'rgba(232,224,204,0.92)';
        ctx.font = '12px ui-sans-serif, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText(text, sx, sy - 30 - Math.sin(time * 4) * 2);
      };
      if (inDun) {
        const f = dun!.floors[dun!.depth - 1];
        if (world.nearStairsDown) prompt(f.down.x + 0.5, f.down.y + 0.5, 'Спуститься [E]');
        if (world.nearStairsUp) prompt(f.up.x + 0.5, f.up.y + 0.5, dun!.depth > 1 ? 'Подняться [E]' : 'Наверх [E]');
        if (world.nearCacheId != null) {
          const cache = dun!.caches.find((c) => c.id === world.nearCacheId);
          if (cache) prompt(cache.pos.x, cache.pos.y, `${ddef!.cacheKind === 'crystal' ? 'Выломать жилу' : ddef!.cacheKind === 'river' ? 'Подобрать принос' : 'Взять запасы'} [E]`);
        }
      } else if (world.nearGateId) {
        const gdef = DUNGEONS[world.nearGateId];
        const g = gdef.gatePos(pois);
        prompt(g.x + 0.5, g.y + 0.5, `${gdef.name} — войти [E]`);
      }

      ctx.restore();

      // ---- атмосфера: ночь, сумерки, туман, споры; в подземелье — своя тьма ----
      if (inDun) {
        // тьма катакомб: свет — вокруг слизня, но комнаты читаются (Регион 2/3)
        ctx.fillStyle = 'rgba(2,4,3,0.22)';
        ctx.fillRect(0, 0, W, H);
        const glowBonus = world.data.player.genome.installed.includes('bioluminescent') ? 0.10 : 0;
        // слепое тело видит только осязанием — круг света уже (Дополнение к ТЗ)
        const blindShrink = sn.visionOn ? 0 : 0.08;
        const lightR = Math.max(W, H) * (0.54 + glowBonus - blindShrink);
        const grd = ctx.createRadialGradient(W / 2, H / 2, 70, W / 2, H / 2, lightR);
        grd.addColorStop(0, 'rgba(0,0,0,0)');
        grd.addColorStop(0.75, 'rgba(1,3,2,0.32)');
        grd.addColorStop(1, 'rgba(1,3,2,0.55)');
        ctx.fillStyle = grd;
        ctx.fillRect(0, 0, W, H);
      } else {
        if (night) { ctx.fillStyle = 'rgba(4,6,10,0.42)'; ctx.fillRect(0, 0, W, H); }
        else if (dusk) { ctx.fillStyle = 'rgba(48,30,12,0.18)'; ctx.fillRect(0, 0, W, H); }
      }
      // виньетка (слепому телу — мягче: экран и так читается осязанием)
      const vgA = sn.visionOn ? 0.55 : 0.38;
      const vg = ctx.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.38, W / 2, H / 2, Math.max(W, H) * 0.75);
      vg.addColorStop(0, 'rgba(0,0,0,0)');
      vg.addColorStop(1, `rgba(0,0,0,${vgA})`);
      ctx.fillStyle = vg;
      ctx.fillRect(0, 0, W, H);
      // слепота чуть притемняет экран — зрению есть что менять (критерий №4),
      // но герой обязан оставаться видимым (обратная связь от игрока)
      if (!sn.visionOn) {
        ctx.fillStyle = 'rgba(2,3,5,0.10)';
        ctx.fillRect(0, 0, W, H);
      }
      // ---- «маяк героя»: слизень всегда различим даже сквозь туман войны ----
      // (экранные координаты — слой поверх всей тьмы)
      {
        const p = world.data.player;
        const hx = W / 2 + (w2sX(p.pos.x, p.pos.y) - cam.sx) * z;
        const hy = H / 2 + (w2sY(p.pos.x, p.pos.y) - cam.sy) * z;
        const beat = 0.5 + Math.sin(time * 2.4) * 0.5;
        const hg = ctx.createRadialGradient(hx, hy - 4, 2, hx, hy - 4, 46 + beat * 7);
        hg.addColorStop(0, 'rgba(170,240,150,0.20)');
        hg.addColorStop(1, 'rgba(170,240,150,0)');
        ctx.fillStyle = hg;
        ctx.beginPath(); ctx.ellipse(hx, hy - 4, 46 + beat * 7, 30 + beat * 5, 0, 0, Math.PI * 2); ctx.fill();
        // пульсирующее кольцо-указатель «ты здесь»
        ctx.strokeStyle = `rgba(190,250,170,${0.16 + beat * 0.12})`;
        ctx.lineWidth = 1.4;
        ctx.beginPath(); ctx.ellipse(hx, hy, 20 + beat * 5, 10 + beat * 2.5, 0, 0, Math.PI * 2); ctx.stroke();
      }
      // перегрузка: фиолетовая рябь по краям и статика (Дополнение к ТЗ п.11)
      if (sn.overload) {
        const oa = 0.10 + Math.sin(time * 6.3) * 0.05 + Math.random() * 0.03;
        const og = ctx.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.30, W / 2, H / 2, Math.max(W, H) * 0.72);
        og.addColorStop(0, 'rgba(120,60,180,0)');
        og.addColorStop(1, `rgba(130,70,190,${oa})`);
        ctx.fillStyle = og;
        ctx.fillRect(0, 0, W, H);
        for (let i = 0; i < 20; i++) {
          ctx.fillStyle = `rgba(200,150,255,${0.05 + Math.random() * 0.12})`;
          ctx.fillRect(Math.random() * W, Math.random() * H, 2, 2);
        }
      }
      // буря Разлома (регион «Разлом забытых душ»): диагональные потоки и треск реальности
      if (world.data.flags.riftStorm === 1) {
        const ra = 0.10 + Math.sin(time * 5.1) * 0.05 + Math.random() * 0.04;
        ctx.fillStyle = `rgba(140,80,200,${ra})`;
        ctx.fillRect(0, 0, W, H);
        ctx.strokeStyle = `rgba(210,150,255,${0.25 + Math.random() * 0.3})`;
        ctx.lineWidth = 1.4;
        for (let i = 0; i < 7; i++) {
          const x0 = Math.random() * W;
          ctx.beginPath();
          ctx.moveTo(x0, -10);
          ctx.lineTo(x0 + 40 + Math.random() * 30, H + 10);
          ctx.stroke();
        }
        if (Math.random() < 0.06) {
          ctx.fillStyle = 'rgba(235,215,255,0.5)';
          ctx.fillRect(0, 0, W, H);
        }
      }
      // споры; в Тьму — снег (сезонная погода, Этап 2)
      ctx.save();
      const snowing = curSeason === 'Тьма' && !inDun;
      for (const s of spores.current) {
        if (snowing) {
          // снег: медленно падает, слегка рыскает
          s.x += (s.vx + Math.sin(time * 0.8 + s.a * 3) * 0.004) * dt * 60;
          s.y += (0.010 + s.vy * 0.8) * dt * 60;
          if (s.y > 1.05) { s.y = -0.05; s.x = Math.random(); }
          if (s.x < -0.05 || s.x > 1.05) s.x = Math.random();
          ctx.fillStyle = `rgba(225,232,242,${0.10 + Math.sin(s.a) * 0.05})`;
          ctx.beginPath();
          ctx.arc(s.x * W, s.y * H, 1.7, 0, Math.PI * 2);
          ctx.fill();
          continue;
        }
        s.x += s.vx * dt * 60; s.y += s.vy * dt * 60; s.a += dt;
        if (s.y < -0.05) { s.y = 1.05; s.x = Math.random(); }
        if (s.x < -0.05 || s.x > 1.05) s.x = Math.random();
        ctx.fillStyle = inDun && pal
          ? `${pal.sporeColor}${0.09 + Math.sin(s.a) * 0.05})`
          : `rgba(190,200,150,${0.05 + Math.sin(s.a) * 0.03})`;
        ctx.beginPath();
        ctx.arc(s.x * W, s.y * H, 1.6, 0, Math.PI * 2);
        ctx.fill();
      }
      ctx.restore();

      // джойстик
      if (joy.current.active) {
        ctx.strokeStyle = 'rgba(232,224,204,0.25)';
        ctx.lineWidth = 2;
        ctx.beginPath(); ctx.arc(joy.current.ox, joy.current.oy, 52, 0, Math.PI * 2); ctx.stroke();
        ctx.fillStyle = 'rgba(232,224,204,0.35)';
        ctx.beginPath(); ctx.arc(joy.current.ox + joy.current.dx, joy.current.oy + joy.current.dy, 22, 0, Math.PI * 2); ctx.fill();
      }
    };

    const loop = (now: number) => {
      const dt = Math.min(0.05, (now - last) / 1000);        // dt физики/анимаций (кламп против прыжков)
      const dtClock = Math.min(1.0, (now - last) / 1000);    // мировые часы — авторитетны (§7); потолок 1с от фоновых вкладок
      last = now;
      gatherInput();
      if (engine.phase === 'life') {
        engine.update(dt, dtClock);
        updateCam(dt);
        draw(dt);
      } else {
        // вне жизни — тёмный кадр
        ctx.fillStyle = '#070907';
        ctx.fillRect(0, 0, canvas.clientWidth, canvas.clientHeight);
      }
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);

    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener('resize', resize);
      window.removeEventListener('keydown', kd);
      window.removeEventListener('keyup', ku);
      canvas.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    };
  }, [engine, onNpcInteract, onTapCreatureInfo]);

  return (
    <canvas
      ref={canvasRef}
      className="absolute inset-0 h-full w-full touch-none select-none"
      aria-label="Игровое поле: изометрический лес Леса Первого Дыхания"
    />
  );
}

// ===== Визуализация восприятия (Дополнение к ТЗ: у каждого органа — своя картинка) =====

type SenseInfo = { organs: Set<SenseOrgan>; t: number; x: number; y: number };

function senseModeOf(organs: Set<SenseOrgan>): 'vision' | 'thermal' | 'chemo' | 'vibration' | 'hearing' | 'light' | 'touch' {
  if (organs.has('vision')) return 'vision';
  if (organs.has('thermal')) return 'thermal';
  if (organs.has('chemo')) return 'chemo';
  if (organs.has('vibration')) return 'vibration';
  if (organs.has('hearing')) return 'hearing';
  if (organs.has('light')) return 'light';
  return 'touch';
}

function npcModeOf(organs: Set<SenseOrgan>): 'vision' | 'ghost' {
  return organs.has('vision') ? 'vision' : 'ghost';
}

function creatureR(c: Creature): number {
  const b = SPECIES[c.species].biomass[1];
  return 8 + Math.min(17, b * 0.42);
}

function dietColor(diet: string): string {
  if (diet === 'carnivore') return '235,120,90';
  if (diet === 'spirit') return '200,150,230';
  return '170,225,150';
}

/** Не-зрительное ощущение существа: у каждого органа — свой визуальный язык. */
function drawSenseView(
  ctx: CanvasRenderingContext2D,
  mode: 'thermal' | 'chemo' | 'vibration' | 'hearing' | 'light' | 'touch' | 'ghost',
  c: Creature,
  info: SenseInfo,
  time: number,
  now: number,
) {
  const def = SPECIES[c.species];
  const r = creatureR(c);
  const sx = w2sX(c.pos.x, c.pos.y);
  const sy = w2sY(c.pos.x, c.pos.y);

  if (mode === 'ghost') {
    // память: где существо было, когда его ощущали — контур угасает за ~2.5с
    const age = now - info.t;
    const a = Math.max(0, 1 - age / 2.5) * 0.5;
    if (a <= 0.02) return;
    const gx = w2sX(info.x, info.y), gy = w2sY(info.x, info.y);
    ctx.setLineDash([3, 3]);
    ctx.strokeStyle = `rgba(210,220,205,${a})`;
    ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.ellipse(gx, gy - 5, r, r * 0.62, 0, 0, Math.PI * 2); ctx.stroke();
    ctx.setLineDash([]);
    return;
  }

  if (mode === 'touch') {
    const col = dietColor(def.diet);
    const wob = Math.sin(time * 5 + c.id) * 1.5;
    ctx.fillStyle = `rgba(${col},0.10)`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 5, r + wob, r * 0.62, 0, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = `rgba(${col},0.8)`;
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.ellipse(sx, sy - 5, r + wob, r * 0.62, 0, 0, Math.PI * 2); ctx.stroke();
    return;
  }

  if (mode === 'thermal') {
    const warm = c.species === 'rabbit' || c.species === 'deer' || c.species === 'wolf' || c.species === 'guard';
    const pulse = 1 + Math.sin(time * 3 + c.id) * 0.08;
    if (warm) {
      const g = ctx.createRadialGradient(sx, sy - 6, 2, sx, sy - 6, r * 1.35 * pulse);
      g.addColorStop(0, 'rgba(240,130,60,0.5)');
      g.addColorStop(0.6, 'rgba(220,90,50,0.28)');
      g.addColorStop(1, 'rgba(200,70,40,0)');
      ctx.fillStyle = g;
      ctx.beginPath(); ctx.arc(sx, sy - 6, r * 1.35 * pulse, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(255,170,90,0.55)';
      ctx.beginPath(); ctx.ellipse(sx, sy - 6, r * 0.7, r * 0.45, 0, 0, Math.PI * 2); ctx.fill();
    } else {
      // холоднокровные — лишь слабый холодный след
      ctx.fillStyle = 'rgba(150,170,190,0.16)';
      ctx.beginPath(); ctx.ellipse(sx, sy - 5, r * 0.8, r * 0.5, 0, 0, Math.PI * 2); ctx.fill();
    }
    return;
  }

  if (mode === 'chemo') {
    const col = dietColor(def.diet);
    ctx.fillStyle = `rgba(${col},0.12)`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 5, r, r * 0.6, 0, 0, Math.PI * 2); ctx.fill();
    // восходящие запаховые motы — источник запаха читается даже во тьме
    for (let k = 0; k < 4; k++) {
      const phase = time * 1.3 + k * 1.71 + c.id;
      const ox = Math.sin(phase) * (6 + k * 2.2);
      const oy = ((time * 9 + k * 17) % 22);
      const a = Math.max(0, 1 - oy / 22) * 0.55;
      ctx.fillStyle = `rgba(${col},${a})`;
      ctx.beginPath(); ctx.arc(sx + ox, sy - 8 - oy, 1.8, 0, Math.PI * 2); ctx.fill();
    }
    return;
  }

  if (mode === 'vibration' || mode === 'hearing') {
    const moving = Math.hypot(c.vel.x, c.vel.y) > 0.12;
    if (!moving && mode === 'vibration') {
      ctx.fillStyle = 'rgba(190,200,210,0.2)';
      ctx.beginPath(); ctx.arc(sx, sy - 4, 2.2, 0, Math.PI * 2); ctx.fill();
      return;
    }
    const speed = mode === 'hearing' ? 0.8 : 1.5;
    const maxR = mode === 'hearing' ? 30 : 22;
    const col = mode === 'hearing' ? '170,200,230' : '200,205,190';
    for (let k = 0; k < 3; k++) {
      const p = ((time * speed) + k / 3) % 1;
      const rr = 4 + p * maxR;
      ctx.strokeStyle = `rgba(${col},${(1 - p) * 0.5})`;
      ctx.lineWidth = 1.3;
      ctx.beginPath(); ctx.ellipse(sx, sy - 4, rr, rr * 0.5, 0, 0, Math.PI * 2); ctx.stroke();
    }
    ctx.fillStyle = `rgba(${col},0.5)`;
    ctx.beginPath(); ctx.arc(sx, sy - 4, 2, 0, Math.PI * 2); ctx.fill();
    return;
  }

  // light: свечение источника видно издалека
  const glowCol = c.species === 'spirit' ? '190,140,230' : c.species === 'firefly' ? '255,220,120' : '220,210,160';
  const gl = 0.5 + Math.sin(time * 4 + c.id) * 0.3;
  ctx.fillStyle = `rgba(${glowCol},${gl * 0.25})`;
  ctx.beginPath(); ctx.arc(sx, sy - 8, r * 1.5, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = `rgba(${glowCol},${0.5 + gl * 0.4})`;
  ctx.beginPath(); ctx.arc(sx, sy - 8, 2.6, 0, Math.PI * 2); ctx.fill();
}

/** Поселенец, ощущённый без зрения: тёплый силуэт; призрак памяти — угасающий контур. */
function drawNpcView(ctx: CanvasRenderingContext2D, info: SenseInfo, time: number, now: number) {
  const sx = w2sX(info.x, info.y), sy = w2sY(info.x, info.y);
  const active = info.organs.size > 0;
  if (!active) {
    // память: где человек был, когда его ощущали
    const a = Math.max(0, 1 - (now - info.t) / 2.5) * 0.5;
    if (a <= 0.02) return;
    ctx.setLineDash([3, 3]);
    ctx.strokeStyle = `rgba(210,220,205,${a})`;
    ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.ellipse(sx, sy - 6, 9, 10, 0, 0, Math.PI * 2); ctx.stroke();
    ctx.setLineDash([]);
    return;
  }
  const warm = info.organs.has('thermal');
  const col = warm ? '240,150,80' : '214,224,206';
  const wob = Math.sin(time * 4) * 1;
  if (warm) {
    ctx.fillStyle = `rgba(${col},0.16)`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 12, 12, 18, 0, 0, Math.PI * 2); ctx.fill();
  }
  ctx.strokeStyle = `rgba(${col},0.65)`;
  ctx.lineWidth = 1.5;
  ctx.beginPath(); ctx.ellipse(sx, sy - 6, 9 + wob, 10 + wob, 0, 0, Math.PI * 2); ctx.stroke(); // тело
  ctx.beginPath(); ctx.arc(sx, sy - 20, 4.5, 0, Math.PI * 2); ctx.stroke(); // голова
}

// ===== Отрисовка декораций =====
function drawDeco(ctx: CanvasRenderingContext2D, d: Deco, time: number) {
  const sx = w2sX(d.pos.x, d.pos.y), sy = w2sY(d.pos.x, d.pos.y);
  const v = hash01(d.seed);
  switch (d.kind) {
    case 'tree': {
      shadow(ctx, sx, sy, 16, 7);
      ctx.fillStyle = '#1d1610';
      ctx.fillRect(sx - 3, sy - 18, 6, 20);
      const sway = Math.sin(time * 0.8 + d.seed) * 1.5;
      // сезонные кроны (Этап 2): свежая зелень / сочная зелень / рыжий листопад / голая чернота
      const crown = curSeason === 'Увядание'
        ? ['#2b2110', '#332612', '#3d2b12']
        : curSeason === 'Тьма'
          ? ['#12181c', '#171d22', '#1c2329']
          : curSeason === 'Цветение'
            ? ['#12251a', '#1a3220', '#234026']
            : ['#101f13', '#162a18', '#1e3320'];
      ctx.fillStyle = crown[0];
      ctx.beginPath(); ctx.arc(sx + sway, sy - 26, 16 + v * 5, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = crown[1];
      ctx.beginPath(); ctx.arc(sx + sway - 7, sy - 20, 11, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = crown[2];
      ctx.beginPath(); ctx.arc(sx + sway + 6, sy - 30, 9, 0, Math.PI * 2); ctx.fill();
      // Цветение: цветущие крапинки на кроне; Увядание: летящие листья-искры; Тьма: снежная шапка
      if (curSeason === 'Цветение') {
        ctx.fillStyle = 'rgba(226,170,200,0.5)';
        for (let i = 0; i < 4; i++) {
          const a = d.seed * 3 + i * 2.4;
          ctx.beginPath(); ctx.arc(sx + sway + Math.cos(a) * (6 + i * 3), sy - 24 - Math.sin(a) * (5 + i * 2.2), 1.4, 0, Math.PI * 2); ctx.fill();
        }
      } else if (curSeason === 'Увядание') {
        const fall = (time * 0.35 + d.seed) % 1;
        ctx.fillStyle = 'rgba(190,130,60,0.55)';
        ctx.beginPath(); ctx.arc(sx + sway + Math.sin(fall * 6 + d.seed) * 10, sy - 30 + fall * 26, 1.6, 0, Math.PI * 2); ctx.fill();
      } else if (curSeason === 'Тьма') {
        ctx.fillStyle = 'rgba(220,228,238,0.5)';
        ctx.beginPath(); ctx.ellipse(sx + sway, sy - 36, 8 + v * 3, 3.2, 0, 0, Math.PI * 2); ctx.fill();
      }
      break;
    }
    case 'pine': {
      shadow(ctx, sx, sy, 12, 6);
      ctx.fillStyle = '#15110c';
      ctx.fillRect(sx - 2.5, sy - 14, 5, 16);
      const pineLayers = curSeason === 'Тьма'
        ? ['#101a17', '#142019', '#18241c']
        : curSeason === 'Увядание'
          ? ['#13221a', '#182a1e', '#1d3122']
          : ['#0e1c11', '#122414', '#162a18'];
      for (let i = 0; i < 3; i++) {
        const wy = sy - 14 - i * 12;
        const ww = 16 - i * 4.5;
        ctx.fillStyle = pineLayers[i];
        ctx.beginPath();
        ctx.moveTo(sx, wy - 16);
        ctx.lineTo(sx + ww, wy + 2);
        ctx.lineTo(sx - ww, wy + 2);
        ctx.closePath();
        ctx.fill();
      }
      if (curSeason === 'Тьма') {
        // снег на ярусах ели
        ctx.fillStyle = 'rgba(222,230,240,0.55)';
        for (let i = 0; i < 3; i++) {
          const wy = sy - 14 - i * 12;
          const ww = (16 - i * 4.5) * 0.62;
          ctx.beginPath(); ctx.ellipse(sx, wy - 4, ww, 1.8, 0, 0, Math.PI * 2); ctx.fill();
        }
      }
      break;
    }
    case 'bush': {
      shadow(ctx, sx, sy, 9, 4);
      const bushCol = curSeason === 'Увядание' ? ['#25210f', '#2c2713'] : curSeason === 'Тьма' ? ['#141c22', '#19222a'] : ['#14251a', '#1b2f1f'];
      ctx.fillStyle = bushCol[0];
      ctx.beginPath(); ctx.arc(sx, sy - 6, 8, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = bushCol[1];
      ctx.beginPath(); ctx.arc(sx + 5, sy - 4, 6, 0, Math.PI * 2); ctx.fill();
      // Цветение: ягоды/цвет на кусте
      if (curSeason === 'Цветение') {
        ctx.fillStyle = 'rgba(228,180,120,0.6)';
        ctx.beginPath(); ctx.arc(sx - 3, sy - 8, 1.3, 0, Math.PI * 2); ctx.arc(sx + 4, sy - 6, 1.2, 0, Math.PI * 2); ctx.fill();
      }
      break;
    }
    case 'rock':
      shadow(ctx, sx, sy, 12, 5);
      ctx.fillStyle = '#3a3a36';
      ctx.beginPath();
      ctx.moveTo(sx - 10, sy); ctx.lineTo(sx - 6, sy - 9); ctx.lineTo(sx + 3, sy - 11); ctx.lineTo(sx + 10, sy - 2); ctx.lineTo(sx + 5, sy + 2); ctx.closePath();
      ctx.fill();
      ctx.fillStyle = '#4a4a44';
      ctx.beginPath(); ctx.moveTo(sx - 6, sy - 9); ctx.lineTo(sx + 3, sy - 11); ctx.lineTo(sx + 4, sy - 5); ctx.closePath(); ctx.fill();
      break;
    case 'mushroom': {
      shadow(ctx, sx, sy, 6, 3);
      const glow = 0.25 + Math.sin(time * 1.6 + d.seed) * 0.12;
      ctx.fillStyle = `rgba(140,200,150,${glow * 0.4})`;
      ctx.beginPath(); ctx.arc(sx, sy - 8, 10, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#c8bfa8';
      ctx.fillRect(sx - 1.5, sy - 8, 3, 8);
      ctx.fillStyle = '#5a7a52';
      ctx.beginPath(); ctx.ellipse(sx, sy - 9, 6.5, 4.5, 0, Math.PI, 0); ctx.fill();
      ctx.fillStyle = '#8fae84';
      ctx.beginPath(); ctx.arc(sx - 2, sy - 10, 1.2, 0, Math.PI * 2); ctx.arc(sx + 2.5, sy - 9.5, 1, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'stone':
      shadow(ctx, sx, sy, 10, 5);
      ctx.fillStyle = '#46464a';
      ctx.beginPath();
      ctx.moveTo(sx - 7, sy); ctx.lineTo(sx - 5, sy - 22); ctx.lineTo(sx + 2, sy - 26); ctx.lineTo(sx + 7, sy - 18); ctx.lineTo(sx + 6, sy);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#58585e';
      ctx.beginPath(); ctx.moveTo(sx - 5, sy - 22); ctx.lineTo(sx + 2, sy - 26); ctx.lineTo(sx + 3, sy - 14); ctx.lineTo(sx - 3, sy - 12); ctx.closePath(); ctx.fill();
      ctx.fillStyle = 'rgba(90,120,90,0.35)';
      ctx.fillRect(sx - 7, sy - 5, 13, 4);
      break;
    case 'crystal': {
      shadow(ctx, sx, sy, 9, 4);
      const gl = 0.4 + Math.sin(time * 2 + d.seed) * 0.2;
      ctx.fillStyle = `rgba(90,190,170,${gl * 0.25})`;
      ctx.beginPath(); ctx.arc(sx, sy - 10, 14, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#3f8a7a';
      ctx.beginPath();
      ctx.moveTo(sx, sy - 24); ctx.lineTo(sx + 6, sy - 8); ctx.lineTo(sx + 2, sy); ctx.lineTo(sx - 5, sy - 2); ctx.lineTo(sx - 6, sy - 12);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#63b3a1';
      ctx.beginPath(); ctx.moveTo(sx, sy - 24); ctx.lineTo(sx + 6, sy - 8); ctx.lineTo(sx + 1, sy - 6); ctx.closePath(); ctx.fill();
      break;
    }
    case 'hut': {
      shadow(ctx, sx, sy, 22, 10);
      // изометричный домик
      ctx.fillStyle = '#2a2018';
      ctx.beginPath();
      ctx.moveTo(sx - 18, sy - 4); ctx.lineTo(sx - 18, sy - 22); ctx.lineTo(sx + 2, sy - 30); ctx.lineTo(sx + 2, sy - 12);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#221a13';
      ctx.beginPath();
      ctx.moveTo(sx + 2, sy - 12); ctx.lineTo(sx + 2, sy - 30); ctx.lineTo(sx + 22, sy - 22); ctx.lineTo(sx + 22, sy - 4);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#171310';
      ctx.beginPath();
      ctx.moveTo(sx - 22, sy - 20); ctx.lineTo(sx + 2, sy - 30); ctx.lineTo(sx + 26, sy - 20); ctx.lineTo(sx + 2, sy - 36);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = 'rgba(224,160,64,0.85)';
      ctx.fillRect(sx - 11, sy - 16, 5, 6);
      break;
    }
    case 'fence':
      ctx.strokeStyle = '#3d3226';
      ctx.lineWidth = 2.5;
      ctx.beginPath(); ctx.moveTo(sx - 8, sy - 2); ctx.lineTo(sx - 8, sy - 12); ctx.moveTo(sx + 8, sy - 2); ctx.lineTo(sx + 8, sy - 12); ctx.stroke();
      ctx.lineWidth = 1.6;
      ctx.beginPath(); ctx.moveTo(sx - 9, sy - 9); ctx.lineTo(sx + 9, sy - 9); ctx.stroke();
      break;
    case 'campfire': {
      shadow(ctx, sx, sy, 8, 4);
      ctx.strokeStyle = '#33261a';
      ctx.lineWidth = 3;
      ctx.beginPath(); ctx.moveTo(sx - 7, sy - 2); ctx.lineTo(sx + 7, sy - 6); ctx.moveTo(sx - 6, sy - 6); ctx.lineTo(sx + 6, sy - 1); ctx.stroke();
      const fl = 1 + Math.sin(time * 9 + d.seed) * 0.25;
      ctx.fillStyle = 'rgba(224,140,40,0.25)';
      ctx.beginPath(); ctx.arc(sx, sy - 8, 14 * fl, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#e08c28';
      ctx.beginPath(); ctx.ellipse(sx, sy - 6, 4 * fl, 7 * fl, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#f2c14e';
      ctx.beginPath(); ctx.ellipse(sx, sy - 5, 2 * fl, 4 * fl, 0, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'farm':
      for (let i = 0; i < 4; i++) {
        ctx.fillStyle = '#241d12';
        ctx.save();
        ctx.translate(sx - 14, sy - 14 + i * 8);
        ctx.rotate(-0.32);
        ctx.fillRect(0, 0, 34, 4.5);
        ctx.fillStyle = '#2f4a26';
        for (let j = 0; j < 6; j++) ctx.fillRect(3 + j * 5.4, 0.5, 2.6, 3);
        ctx.restore();
      }
      break;
    case 'well':
      shadow(ctx, sx, sy, 10, 5);
      ctx.fillStyle = '#3c3c38';
      ctx.beginPath(); ctx.ellipse(sx, sy - 4, 10, 5.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#111';
      ctx.beginPath(); ctx.ellipse(sx, sy - 5, 6, 3, 0, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#2c2318'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(sx - 8, sy - 6); ctx.lineTo(sx - 8, sy - 20); ctx.moveTo(sx + 8, sy - 6); ctx.lineTo(sx + 8, sy - 20); ctx.stroke();
      ctx.fillStyle = '#171310';
      ctx.beginPath(); ctx.moveTo(sx - 11, sy - 19); ctx.lineTo(sx, sy - 25); ctx.lineTo(sx + 11, sy - 19); ctx.closePath(); ctx.fill();
      break;
    case 'bones':
      ctx.strokeStyle = 'rgba(210,205,185,0.55)';
      ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(sx, sy - 2, 7, Math.PI * 0.1, Math.PI * 0.9); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(sx - 4, sy - 6); ctx.lineTo(sx - 3, sy - 1); ctx.moveTo(sx + 1, sy - 7); ctx.lineTo(sx + 2, sy - 2); ctx.stroke();
      break;
    case 'crack': {
      // «Чаша Отголосов»: трещины разлома и тайный вход-трещина (r ≥ 0.5 — проход для малого тела)
      const isEntry = d.r >= 0.5;
      ctx.fillStyle = '#050507';
      ctx.beginPath(); ctx.ellipse(sx, sy - 2, 13, 5.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = isEntry ? 'rgba(170,140,230,0.55)' : 'rgba(150,140,170,0.26)';
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.moveTo(sx - 14, sy - 2); ctx.lineTo(sx - 5, sy - 6); ctx.lineTo(sx + 2, sy - 1); ctx.lineTo(sx + 10, sy - 5); ctx.lineTo(sx + 15, sy - 2);
      ctx.stroke();
      if (isEntry) {
        // вход в карман: слабое лиловое дыхание из глубины
        const gl = 0.35 + Math.sin(time * 1.7 + d.seed) * 0.15;
        ctx.fillStyle = `rgba(150,120,220,${gl * 0.22})`;
        ctx.beginPath(); ctx.arc(sx, sy - 4, 10 + gl * 3, 0, Math.PI * 2); ctx.fill();
      }
      break;
    }
    case 'reed': {
      // камыш Илистых топей: колышется
      const sway = Math.sin(time * 1.4 + d.seed) * 1.7;
      ctx.strokeStyle = 'rgba(115,150,90,0.75)';
      ctx.lineWidth = 1.4;
      for (let i = 0; i < 3; i++) {
        const ox = (i - 1) * 3.2;
        ctx.beginPath();
        ctx.moveTo(sx + ox, sy);
        ctx.quadraticCurveTo(sx + ox + sway * 0.5, sy - 8, sx + ox + sway, sy - 13 - v * 5);
        ctx.stroke();
      }
      break;
    }
  }
}

function shadow(ctx: CanvasRenderingContext2D, sx: number, sy: number, rx: number, ry: number) {
  ctx.fillStyle = 'rgba(0,0,0,0.30)';
  ctx.beginPath(); ctx.ellipse(sx, sy, rx, ry, 0, 0, Math.PI * 2); ctx.fill();
}

// ===== Объекты Грибных Катакомб (Регион 2) =====
function drawStairs(ctx: CanvasRenderingContext2D, x: number, y: number, dir: 'up' | 'down', time: number) {
  const sx = w2sX(x, y), sy = w2sY(x, y);
  if (dir === 'down') {
    ctx.fillStyle = '#040406';
    ctx.beginPath(); ctx.ellipse(sx, sy, 15, 7.5, 0, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = 'rgba(150,120,80,0.55)';
    ctx.lineWidth = 1.5;
    ctx.beginPath(); ctx.ellipse(sx, sy, 15, 7.5, 0, 0, Math.PI * 2); ctx.stroke();
    ctx.strokeStyle = 'rgba(150,120,80,0.3)';
    ctx.beginPath(); ctx.ellipse(sx, sy, 9, 4.5, 0, 0, Math.PI * 2); ctx.stroke();
  } else {
    const g = ctx.createRadialGradient(sx, sy, 2, sx, sy, 22);
    g.addColorStop(0, 'rgba(220,230,180,0.45)');
    g.addColorStop(1, 'rgba(220,230,180,0)');
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.ellipse(sx, sy, 22, 11, 0, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = 'rgba(220,230,180,0.4)';
    ctx.lineWidth = 1.2;
    ctx.beginPath(); ctx.ellipse(sx, sy, 10 + Math.sin(time * 2) * 1.5, 5 + Math.sin(time * 2) * 0.8, 0, 0, Math.PI * 2); ctx.stroke();
  }
}

function drawCache(ctx: CanvasRenderingContext2D, x: number, y: number, time: number, kind: 'mushroom' | 'crystal' | 'river') {
  const sx = w2sX(x, y), sy = w2sY(x, y);
  const gl = 0.5 + Math.sin(time * 2.4 + x * 3 + y) * 0.3;
  if (kind === 'river') {
    // принос течения: сплетённый пучок того, что несёт подземная река
    ctx.fillStyle = `rgba(110,180,225,${gl * 0.26})`;
    ctx.beginPath(); ctx.arc(sx, sy - 4, 12, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#4a4238';
    ctx.lineWidth = 2.2;
    ctx.beginPath(); ctx.moveTo(sx - 7, sy - 2); ctx.lineTo(sx + 6, sy - 6); ctx.moveTo(sx - 6, sy - 5); ctx.lineTo(sx + 7, sy - 2); ctx.stroke();
    ctx.fillStyle = `rgba(170,220,245,${0.4 + gl * 0.4})`;
    ctx.beginPath(); ctx.arc(sx + 1, sy - 8, 1.6, 0, Math.PI * 2); ctx.arc(sx - 4, sy - 4, 1.2, 0, Math.PI * 2); ctx.fill();
    return;
  }
  if (kind === 'crystal') {
    // кристальная жила: гряда осколков из пола
    ctx.fillStyle = `rgba(110,210,235,${gl * 0.30})`;
    ctx.beginPath(); ctx.arc(sx, sy - 5, 13, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#3e5a66';
    ctx.beginPath(); ctx.ellipse(sx, sy - 2, 10, 5.5, 0, 0, Math.PI * 2); ctx.fill();
    const shard = (dx: number, h: number, col: string) => {
      ctx.fillStyle = col;
      ctx.beginPath();
      ctx.moveTo(sx + dx - 3, sy - 2);
      ctx.lineTo(sx + dx, sy - 2 - h);
      ctx.lineTo(sx + dx + 3, sy - 2);
      ctx.closePath(); ctx.fill();
    };
    shard(-4, 11 + Math.sin(time * 2) * 1, '#6fd0d8');
    shard(0, 16, '#7fd4e8');
    shard(4, 9, '#5fb8c8');
    ctx.fillStyle = `rgba(220,250,255,${0.5 + gl * 0.5})`;
    ctx.beginPath(); ctx.arc(sx, sy - 13, 1.4, 0, Math.PI * 2); ctx.fill();
    return;
  }
  ctx.fillStyle = `rgba(140,230,160,${gl * 0.28})`;
  ctx.beginPath(); ctx.arc(sx, sy - 4, 12, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#5f5138';
  ctx.beginPath(); ctx.ellipse(sx, sy - 2, 9, 5, 0, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = `rgba(180,240,190,${0.45 + gl * 0.5})`;
  ctx.beginPath(); ctx.arc(sx - 3, sy - 6, 2, 0, Math.PI * 2); ctx.arc(sx + 3, sy - 5, 1.6, 0, Math.PI * 2); ctx.arc(sx, sy - 8, 1.2, 0, Math.PI * 2); ctx.fill();
}

function drawGate(ctx: CanvasRenderingContext2D, gx: number, gy: number, time: number, kind: 'mushroom' | 'crystal' | 'river') {
  const sx = w2sX(gx, gy), sy = w2sY(gx, gy);
  const glow = kind === 'crystal' ? '110,210,235' : kind === 'river' ? '110,180,225' : '140,230,160';
  const dotCol = kind === 'crystal' ? '#7fd4e8' : kind === 'river' ? '#6fb8e0' : '#8fe0a0';
  shadow(ctx, sx, sy, 16, 7);
  // каменная арка над тёмным проёмом
  ctx.fillStyle = '#2e2c28';
  ctx.beginPath();
  ctx.moveTo(sx - 12, sy + 2);
  ctx.lineTo(sx - 10, sy - 20);
  ctx.lineTo(sx - 4, sy - 26);
  ctx.lineTo(sx + 6, sy - 24);
  ctx.lineTo(sx + 10, sy - 16);
  ctx.lineTo(sx + 12, sy + 2);
  ctx.closePath(); ctx.fill();
  ctx.fillStyle = '#58554e';
  ctx.beginPath();
  ctx.moveTo(sx - 10, sy - 20); ctx.lineTo(sx - 4, sy - 26); ctx.lineTo(sx + 6, sy - 24); ctx.lineTo(sx + 3, sy - 19);
  ctx.closePath(); ctx.fill();
  // кристальные наросты на арке Пещер; мокрый камень и осока у входа в Русло
  if (kind === 'crystal') {
    ctx.fillStyle = '#4e7a8a';
    ctx.beginPath();
    ctx.moveTo(sx - 8, sy - 19); ctx.lineTo(sx - 4, sy - 31); ctx.lineTo(sx - 1, sy - 19);
    ctx.moveTo(sx + 4, sy - 18); ctx.lineTo(sx + 8, sy - 28); ctx.lineTo(sx + 11, sy - 17);
    ctx.closePath(); ctx.fill();
  } else if (kind === 'river') {
    ctx.strokeStyle = 'rgba(110,180,225,0.5)';
    ctx.lineWidth = 1.3;
    for (let i = 0; i < 2; i++) {
      const wy = sy - 4 - i * 7;
      ctx.beginPath();
      ctx.moveTo(sx - 9 + Math.sin(time * 2 + i) * 1.5, wy);
      ctx.quadraticCurveTo(sx + 1, wy + 3, sx + 10 + Math.sin(time * 1.6 + i * 2) * 1.5, wy);
      ctx.stroke();
    }
  }
  ctx.fillStyle = '#040506';
  ctx.beginPath(); ctx.ellipse(sx + 1, sy - 6, 7, 10, 0, 0, Math.PI * 2); ctx.fill();
  // свечение из проёма
  const gl = 0.4 + Math.sin(time * 1.8) * 0.2;
  ctx.fillStyle = `rgba(${glow},${gl * 0.3})`;
  ctx.beginPath(); ctx.arc(sx + 1, sy - 8, 10 + gl * 4, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = dotCol;
  ctx.beginPath();
  ctx.arc(sx - 2, sy - 10 + Math.sin(time * 2) * 2, 1.2, 0, Math.PI * 2);
  ctx.arc(sx + 4, sy - 12 - Math.sin(time * 1.6) * 2, 1, 0, Math.PI * 2);
  ctx.fill();
}

// ===== Существа =====
function drawCreature(ctx: CanvasRenderingContext2D, engine: Engine, c: Creature, time: number, dt = 0.016) {
  const sx = w2sX(c.pos.x, c.pos.y), sy = w2sY(c.pos.x, c.pos.y);
  const def = SPECIES[c.species];
  const flash = c.hitFlash > 0;
  const slimeCorpse = c.species === 'slug' || c.species === 'crystalslug';
  if (c.dead) {
    ctx.save();
    ctx.globalAlpha = Math.min(0.8, (c.corpseT ?? 0) / 12);
    if (slimeCorpse) {
      // тело слизня не труп — лужа желе в цвете вида (референс: распад на капли)
      const cpal = resolvePalette(c.species === 'crystalslug' ? 'mineral' : null);
      ctx.fillStyle = cpal.deep;
      ctx.beginPath(); ctx.ellipse(sx, sy, 13, 6, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = cpal.mid;
      ctx.beginPath(); ctx.ellipse(sx, sy - 2, 9, 4, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = cpal.core;
      ctx.beginPath(); ctx.ellipse(sx - 2, sy - 2, 4, 2, 0, 0, Math.PI * 2); ctx.fill();
    } else {
      ctx.fillStyle = '#101510';
      ctx.beginPath(); ctx.ellipse(sx, sy, 13, 6, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#1a221a';
      ctx.beginPath(); ctx.ellipse(sx, sy - 2, 9, 4, 0, 0, Math.PI * 2); ctx.fill();
    }
    ctx.restore();
    if (!c.absorbed && engine.world?.nearCorpseId === c.id) {
      ctx.strokeStyle = `rgba(212,160,23,${0.5 + Math.sin(time * 5) * 0.3})`;
      ctx.lineWidth = 1.6;
      ctx.beginPath(); ctx.ellipse(sx, sy, 17, 9, 0, 0, Math.PI * 2); ctx.stroke();
    }
    return;
  }
  shadow(ctx, sx, sy, 10, 4.5);
  const bob = Math.sin(time * 3 + c.id) * 1.2;
  ctx.save();
  if (flash) { ctx.filter = 'brightness(2.2)'; }
  const body = (fill: string) => { ctx.fillStyle = flash ? '#fff' : fill; };
  switch (c.species) {
    case 'slug':
    case 'crystalslug': {
      // слизни-существа — то же живое желе, что и герой (единый стиль референса)
      const crystal = c.species === 'crystalslug';
      let j = creatureJellies.get(c.id);
      if (!j) { j = new SlimeJelly(); creatureJellies.set(c.id, j); }
      const cspd = Math.hypot(c.vel.x, c.vel.y);
      const ck = Math.min(1, cspd / 2.2);
      const cvsx = w2sX(c.vel.x, c.vel.y), cvsy = w2sY(c.vel.x, c.vel.y);
      const cvl = Math.hypot(cvsx, cvsy);
      const cux = ck > 0.03 && cvl > 0.001 ? cvsx / cvl : 1;
      const cuy = ck > 0.03 && cvl > 0.001 ? cvsy / cvl : 0;
      j.update(Math.max(0.001, Math.min(0.05, dt)), {
        k: ck, ux: cux, uy: cuy, pulse: 0, flowT: 0, pressure: 0, young: 0,
        R: crystal ? 12 : 10.5, widthMul: 1, heightMul: 1,
        cx: sx, cy: sy,
        wx: c.pos.x, wy: c.pos.y,
        moving: cspd > 0.12,
        s2wX, s2wY,
        blocked: (wx, wy) => !(engine.world?.isWalkable(wx, wy) ?? true),
      });
      j.draw(ctx, {
        pal: crystal ? resolvePalette('mineral') : resolvePalette(null),
        time, pulse: 0, flash, toxicIn: false,
        organsN: 2, chemo: true, thermal: false, vision: false,
        inner: ({ cx: px2, cy: cy2, t: bt }) => {
          if (crystal) {
            // кристальные жилы внутри полупрозрачного тела
            for (let i = 0; i < 3; i++) {
              const a0 = bt * 0.4 + i * 2.1 + c.id;
              const ix2 = px2 + Math.sin(a0) * 4, iy2 = cy2 + Math.cos(a0 * 1.3) * 2;
              const s0 = 2.2 + (i % 2);
              ctx.fillStyle = 'rgba(150,220,200,0.75)';
              ctx.beginPath();
              ctx.moveTo(ix2, iy2 - s0); ctx.lineTo(ix2 + s0 * 0.62, iy2 + s0 * 0.4); ctx.lineTo(ix2 - s0 * 0.62, iy2 + s0 * 0.4);
              ctx.closePath(); ctx.fill();
            }
          } else {
            // плотное ядро обычного лесного слизня
            ctx.fillStyle = 'rgba(52,72,44,0.4)';
            ctx.beginPath(); ctx.ellipse(px2 + Math.sin(bt * 0.8 + c.id) * 2, cy2 + 1, 5, 3.4, 0, 0, Math.PI * 2); ctx.fill();
          }
        },
      });
      break;
    }
    case 'beetle':
      body('#2e2a22');
      ctx.beginPath(); ctx.ellipse(sx, sy - 5 + bob * 0.3, 9, 6.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#171511';
      ctx.lineWidth = 1.2;
      ctx.beginPath(); ctx.moveTo(sx, sy - 11); ctx.lineTo(sx, sy); ctx.stroke();
      for (let i = -1; i <= 1; i += 2) {
        ctx.beginPath(); ctx.moveTo(sx + i * 5, sy - 4); ctx.lineTo(sx + i * 10, sy - 1); ctx.stroke();
      }
      ctx.fillStyle = '#5a5142';
      ctx.beginPath(); ctx.ellipse(sx - 4, sy - 9, 3, 2.4, -0.5, 0, Math.PI * 2); ctx.fill();
      break;
    case 'firefly': {
      const gl = 0.5 + Math.sin(time * 5 + c.id) * 0.4;
      ctx.fillStyle = `rgba(220,190,80,${gl * 0.35})`;
      ctx.beginPath(); ctx.arc(sx, sy - 10 + bob, 12, 0, Math.PI * 2); ctx.fill();
      body('#3a3524');
      ctx.beginPath(); ctx.ellipse(sx, sy - 6 + bob, 4, 3, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : `rgba(235,210,90,${0.6 + gl * 0.4})`;
      ctx.beginPath(); ctx.arc(sx, sy - 10 + bob, 2.6, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'rabbit':
      body('#6a5f4c');
      ctx.beginPath(); ctx.ellipse(sx, sy - 4 + bob * 0.5, 7, 5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.ellipse(sx + (c.vel.x - c.vel.y > 0 ? 4 : -4), sy - 9, 2.2, 5, 0.3, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#1a1712';
      ctx.beginPath(); ctx.arc(sx + (c.vel.x - c.vel.y > 0 ? 5 : -5), sy - 5, 1, 0, Math.PI * 2); ctx.fill();
      break;
    case 'deer':
      body('#5c5342');
      ctx.beginPath(); ctx.ellipse(sx, sy - 8, 11, 6.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = flash ? '#fff' : '#3c3527';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(sx - 4, sy - 3); ctx.lineTo(sx - 5, sy + 2); ctx.moveTo(sx + 4, sy - 3); ctx.lineTo(sx + 5, sy + 2);
      ctx.stroke();
      ctx.strokeStyle = '#4a4032'; ctx.lineWidth = 1.4;
      ctx.beginPath(); ctx.moveTo(sx + 8, sy - 12); ctx.lineTo(sx + 10, sy - 19); ctx.moveTo(sx + 10, sy - 19); ctx.lineTo(sx + 13, sy - 21); ctx.stroke();
      ctx.fillStyle = '#8a7a5a';
      ctx.beginPath(); ctx.ellipse(sx - 10, sy - 6, 4, 2.4, 0.4, 0, Math.PI * 2); ctx.fill();
      break;
    case 'wolf': {
      const aggro = c.ai === 'chase';
      body(aggro ? '#4a4040' : '#454038');
      ctx.beginPath(); ctx.ellipse(sx, sy - 6, 12, 5.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.ellipse(sx + (aggro ? 9 : -9), sy - 8, 4.5, 3.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : '#332f28';
      ctx.beginPath(); ctx.moveTo(sx + (aggro ? 6 : -6), sy - 11); ctx.lineTo(sx + (aggro ? 8 : -8), sy - 16); ctx.lineTo(sx + (aggro ? 10 : -10), sy - 11); ctx.closePath(); ctx.fill();
      ctx.beginPath(); ctx.moveTo(sx - (aggro ? 10 : -10), sy - 6); ctx.lineTo(sx - (aggro ? 17 : -17), sy - 9 + Math.sin(time * 4 + c.id) * 2); ctx.lineTo(sx - (aggro ? 11 : -11), sy - 2); ctx.closePath(); ctx.fill();
      if (aggro) {
        ctx.fillStyle = '#e05a3a';
        ctx.beginPath(); ctx.arc(sx + (aggro ? 11 : -11), sy - 9, 1.1, 0, Math.PI * 2); ctx.fill();
      }
      break;
    }
    case 'toad':
      body('#4a4636');
      ctx.beginPath(); ctx.ellipse(sx, sy - 4 + bob * 0.3, 9, 7, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : '#5c5844';
      ctx.beginPath(); ctx.arc(sx - 3, sy - 10, 1.6, 0, Math.PI * 2); ctx.arc(sx + 3, sy - 10, 1.6, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#1a1a12';
      ctx.beginPath(); ctx.arc(sx - 3, sy - 8, 1, 0, Math.PI * 2); ctx.arc(sx + 3, sy - 8, 1, 0, Math.PI * 2); ctx.fill();
      break;
    case 'spider':
      body('#2c2430');
      ctx.strokeStyle = flash ? '#fff' : '#221c26';
      ctx.lineWidth = 1.4;
      for (let i = 0; i < 4; i++) {
        const lx = -6 + i * 4;
        ctx.beginPath();
        ctx.moveTo(sx + lx, sy - 5);
        ctx.lineTo(sx + lx * 1.7, sy - 1 + Math.sin(time * 6 + i) * 1);
        ctx.stroke();
      }
      ctx.fillStyle = flash ? '#fff' : '#2c2430';
      ctx.beginPath(); ctx.ellipse(sx, sy - 6, 6.5, 5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#c03a3a';
      ctx.beginPath(); ctx.arc(sx - 2, sy - 8, 0.9, 0, Math.PI * 2); ctx.arc(sx + 1, sy - 8, 0.9, 0, Math.PI * 2); ctx.fill();
      break;
    case 'spirit': {
      const gl = 0.5 + Math.sin(time * 2.2 + c.id) * 0.3;
      ctx.fillStyle = `rgba(200,180,230,${gl * 0.28})`;
      ctx.beginPath(); ctx.arc(sx, sy - 14 + bob, 20, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = `rgba(230,222,246,${0.55 + gl * 0.3})`;
      ctx.beginPath();
      ctx.moveTo(sx, sy - 26 + bob);
      ctx.quadraticCurveTo(sx + 9, sy - 18 + bob, sx + 6, sy - 4 + bob);
      ctx.quadraticCurveTo(sx, sy - 9 + bob, sx - 6, sy - 4 + bob);
      ctx.quadraticCurveTo(sx - 9, sy - 18 + bob, sx, sy - 26 + bob);
      ctx.fill();
      ctx.fillStyle = '#3a3450';
      ctx.beginPath(); ctx.arc(sx - 2.5, sy - 17 + bob, 1.3, 0, Math.PI * 2); ctx.arc(sx + 2.5, sy - 17 + bob, 1.3, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'mushling':
      body('#8a7a5a');
      ctx.fillRect(sx - 2, sy - 7, 4, 7);
      ctx.fillStyle = flash ? '#fff' : '#5f7a52';
      ctx.beginPath(); ctx.ellipse(sx, sy - 9, 8, 5.5, 0, Math.PI, 0); ctx.fill();
      ctx.fillStyle = '#c8d4b0';
      ctx.beginPath(); ctx.arc(sx - 3, sy - 10, 1.2, 0, Math.PI * 2); ctx.arc(sx + 2, sy - 11, 1.4, 0, Math.PI * 2); ctx.fill();
      break;
    case 'guard':
      body('#4a4640');
      ctx.fillRect(sx - 3, sy - 14, 6, 10);
      ctx.fillStyle = flash ? '#fff' : '#c9a97a';
      ctx.beginPath(); ctx.arc(sx, sy - 17, 3.2, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = '#6a6458'; ctx.lineWidth = 1.6;
      ctx.beginPath(); ctx.moveTo(sx + 5, sy - 2); ctx.lineTo(sx + 5, sy - 20); ctx.stroke();
      ctx.fillStyle = '#3a3630';
      ctx.beginPath(); ctx.ellipse(sx - 5, sy - 9, 3, 4.5, 0, 0, Math.PI * 2); ctx.fill();
      break;
    case 'sporeling': {
      const gl = 0.4 + Math.sin(time * 4 + c.id) * 0.2;
      ctx.fillStyle = `rgba(150,240,170,${gl * 0.3})`;
      ctx.beginPath(); ctx.arc(sx, sy - 6 + bob, 10, 0, Math.PI * 2); ctx.fill();
      body('#7a6a4a');
      ctx.fillRect(sx - 2, sy - 6 + bob * 0.4, 4, 6);
      ctx.fillStyle = flash ? '#fff' : '#5f9a62';
      ctx.beginPath(); ctx.ellipse(sx, sy - 8 + bob * 0.4, 6.5, 4.5, 0, Math.PI, 0); ctx.fill();
      ctx.fillStyle = '#d8f0c0';
      ctx.beginPath();
      ctx.arc(sx - 2, sy - 9 + bob * 0.4, 0.9, 0, Math.PI * 2);
      ctx.arc(sx + 2, sy - 9.5 + bob * 0.4, 0.9, 0, Math.PI * 2);
      ctx.fill();
      break;
    }
    case 'mycenoid':
      body('#4a4038');
      ctx.beginPath(); ctx.ellipse(sx, sy - 7 + bob * 0.3, 12, 9, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : '#6a5a4a';
      ctx.beginPath();
      ctx.ellipse(sx - 4, sy - 14 + bob * 0.3, 5, 4, 0, 0, Math.PI * 2);
      ctx.ellipse(sx + 5, sy - 13 + bob * 0.3, 4.4, 3.4, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = '#b0e0b8';
      ctx.beginPath();
      ctx.arc(sx - 4, sy - 15 + bob * 0.3, 1, 0, Math.PI * 2);
      ctx.arc(sx + 5, sy - 14 + bob * 0.3, 1, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = flash ? '#fff' : '#3a3430';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(sx - 8, sy - 2); ctx.lineTo(sx - 11, sy + 2);
      ctx.moveTo(sx + 8, sy - 2); ctx.lineTo(sx + 11, sy + 2);
      ctx.stroke();
      break;
    case 'myco_monarch': {
      const gl = 0.5 + Math.sin(time * 2) * 0.25;
      ctx.fillStyle = `rgba(200,230,140,${gl * 0.3})`;
      ctx.beginPath(); ctx.arc(sx, sy - 16 + bob, 30, 0, Math.PI * 2); ctx.fill();
      body('#5a5040');
      ctx.beginPath(); ctx.ellipse(sx, sy - 12 + bob * 0.3, 18, 13, 0, 0, Math.PI * 2); ctx.fill();
      // корона грибов
      for (let i = -2; i <= 2; i++) {
        const cx = sx + i * 7;
        const cy = sy - 24 + bob * 0.3 + Math.abs(i) * 1.8;
        ctx.fillStyle = flash ? '#fff' : (i === 0 ? '#c8d46a' : '#7a9a52');
        ctx.beginPath(); ctx.ellipse(cx, cy, 5.5, 3.8, 0, Math.PI, 0); ctx.fill();
        ctx.fillStyle = '#c8bfa8';
        ctx.fillRect(cx - 1, cy, 2, 4);
      }
      ctx.fillStyle = flash ? '#fff' : '#e8f0a0';
      ctx.beginPath();
      ctx.arc(sx - 5, sy - 14 + bob * 0.3, 1.6, 0, Math.PI * 2);
      ctx.arc(sx + 5, sy - 14 + bob * 0.3, 1.6, 0, Math.PI * 2);
      ctx.fill();
      break;
    }
    // ===== Жители Кристальных Пещер (Регион 3) =====
    case 'shardling': {
      const gl = 0.4 + Math.sin(time * 3.4 + c.id) * 0.2;
      ctx.fillStyle = `rgba(110,210,235,${gl * 0.25})`;
      ctx.beginPath(); ctx.arc(sx, sy - 6 + bob, 11, 0, Math.PI * 2); ctx.fill();
      body('#55666b');
      ctx.beginPath(); ctx.ellipse(sx, sy - 5 + bob * 0.4, 8, 5.5, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : '#7fd4e8';
      ctx.beginPath();
      ctx.moveTo(sx - 5, sy - 8 + bob * 0.4); ctx.lineTo(sx - 3, sy - 15 + bob * 0.4); ctx.lineTo(sx - 1, sy - 8 + bob * 0.4);
      ctx.moveTo(sx + 1, sy - 9 + bob * 0.4); ctx.lineTo(sx + 3, sy - 16 + bob * 0.4); ctx.lineTo(sx + 5, sy - 8 + bob * 0.4);
      ctx.closePath(); ctx.fill();
      ctx.fillStyle = '#d9f6fb';
      ctx.beginPath(); ctx.arc(sx + 4, sy - 6 + bob * 0.4, 0.9, 0, Math.PI * 2); ctx.fill();
      break;
    }
    case 'golem': {
      const gl = 0.35 + Math.sin(time * 1.6 + c.id) * 0.15;
      ctx.fillStyle = `rgba(110,210,235,${gl * 0.22})`;
      ctx.beginPath(); ctx.arc(sx, sy - 12 + bob, 22, 0, Math.PI * 2); ctx.fill();
      body('#46565e');
      ctx.fillRect(sx - 8, sy - 16 + bob * 0.3, 16, 14); // торс
      ctx.fillStyle = flash ? '#fff' : '#3a4a52';
      ctx.fillRect(sx - 12, sy - 14 + bob * 0.3, 4, 10); // руки
      ctx.fillRect(sx + 8, sy - 14 + bob * 0.3, 4, 10);
      ctx.fillRect(sx - 6, sy - 2, 4, 4); // ноги
      ctx.fillRect(sx + 2, sy - 2, 4, 4);
      ctx.fillStyle = flash ? '#fff' : '#6fd0d8'; // рунные жилы
      ctx.fillRect(sx - 2, sy - 14 + bob * 0.3, 4, 2);
      ctx.fillRect(sx - 1, sy - 10 + bob * 0.3, 2, 6);
      ctx.fillStyle = '#bfeef6';
      ctx.beginPath(); ctx.arc(sx, sy - 18 + bob * 0.3, 2.4, 0, Math.PI * 2); ctx.fill(); // глаз-ядро
      break;
    }
    case 'crystal_titan': {
      const gl = 0.5 + Math.sin(time * 1.4) * 0.25;
      ctx.fillStyle = `rgba(110,210,235,${gl * 0.25})`;
      ctx.beginPath(); ctx.arc(sx, sy - 20 + bob, 38, 0, Math.PI * 2); ctx.fill();
      body('#3c4a54');
      ctx.beginPath(); ctx.ellipse(sx, sy - 14 + bob * 0.3, 20, 16, 0, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = flash ? '#fff' : '#31404a';
      ctx.fillRect(sx - 18, sy - 12 + bob * 0.3, 6, 14); // конечности
      ctx.fillRect(sx + 12, sy - 12 + bob * 0.3, 6, 14);
      // корона кристаллов на спине
      ctx.fillStyle = flash ? '#fff' : '#7fd4e8';
      ctx.beginPath();
      ctx.moveTo(sx - 12, sy - 24 + bob * 0.3); ctx.lineTo(sx - 7, sy - 40 + bob * 0.3); ctx.lineTo(sx - 2, sy - 24 + bob * 0.3);
      ctx.moveTo(sx - 1, sy - 26 + bob * 0.3); ctx.lineTo(sx + 5, sy - 46 + bob * 0.3); ctx.lineTo(sx + 11, sy - 25 + bob * 0.3);
      ctx.closePath(); ctx.fill();
      // светящееся ядро в груди
      const core = 3 + Math.sin(time * 3) * 0.8;
      ctx.fillStyle = '#aee8f2';
      ctx.beginPath(); ctx.arc(sx + 2, sy - 16 + bob * 0.3, core, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#1e3238';
      ctx.beginPath();
      ctx.arc(sx - 4, sy - 18 + bob * 0.3, 1.4, 0, Math.PI * 2);
      ctx.arc(sx + 8, sy - 18 + bob * 0.3, 1.4, 0, Math.PI * 2);
      ctx.fill();
      break;
    }
  }
  ctx.restore();

  // HP-бар для повреждённых
  if (c.hp < def.base.maxHp) {
    const wBar = 22;
    ctx.fillStyle = 'rgba(0,0,0,0.55)';
    ctx.fillRect(sx - wBar / 2, sy - 24, wBar, 3.4);
    ctx.fillStyle = def.sentient ? '#c9a0dc' : '#c9563d';
    ctx.fillRect(sx - wBar / 2, sy - 24, wBar * Math.max(0, c.hp / def.base.maxHp), 3.4);
  }
}

function drawNpc(ctx: CanvasRenderingContext2D, n: Npc, time: number) {
  const sx = w2sX(n.pos.x, n.pos.y), sy = w2sY(n.pos.x, n.pos.y);
  shadow(ctx, sx, sy, 8, 3.6);
  const bob = Math.sin(time * 1.6 + n.pos.x) * 0.8;
  const colors: Record<string, [string, string]> = {
    mirra: ['#6a5a7a', '#d8ccb8'],
    dar: ['#5a4a38', '#c9a97a'],
    klumba: ['#4a6a42', '#d8c8a0'],
    ivo: ['#5a6a7a', '#d8ccb8'],
    rinsa: ['#6a4a3a', '#c9a97a'],
    prokh: ['#4a6a42', '#d8c8a0'],
  };
  const [robe, skin] = colors[n.id] ?? ['#5a5a52', '#c9a97a'];
  ctx.fillStyle = robe;
  ctx.beginPath();
  ctx.moveTo(sx - 5, sy);
  ctx.lineTo(sx - 3, sy - 12 + bob);
  ctx.lineTo(sx + 3, sy - 12 + bob);
  ctx.lineTo(sx + 5, sy);
  ctx.closePath(); ctx.fill();
  ctx.fillStyle = skin;
  ctx.beginPath(); ctx.arc(sx, sy - 15 + bob, 3.4, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = 'rgba(232,224,204,0.85)';
  ctx.font = '11px ui-sans-serif, sans-serif';
  ctx.textAlign = 'center';
  ctx.fillText(n.name.split(' ')[1] ?? n.name, sx, sy - 24 + bob);
}

function drawPlayer(ctx: CanvasRenderingContext2D, engine: Engine, time: number, pulse: number, dtF: number) {
  const world = engine.world!;
  const p = world.data.player;
  const s = world.stats;
  const sx = w2sX(p.pos.x, p.pos.y), sy = w2sY(p.pos.x, p.pos.y);
  const spd = Math.hypot(p.vel.x, p.vel.y);
  const vis = s.visuals;
  const bob = Math.sin(time * 4) * 1.1 * (spd > 0.1 ? 1.6 : 0.7);
  const flash = p.hitFlash > 0;
  const dis = engine.deathDissolve();
  const dying = dis > 0; // смерть-растекание: мир замер, тело распадается на капли (референс «Death»)

  // выпад при атаке (рывок тела к цели) — точка опоры желе-тела
  const jx = sx + p.facing * p.attackAnim * 8;
  const jy = sy - p.attackAnim * 2.5;

  // ауры синергий
  if (vis.has('toxic_zone')) {
    ctx.fillStyle = `rgba(110,170,80,${0.10 + Math.sin(time * 3) * 0.04})`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 3, 40, 20, 0, 0, Math.PI * 2); ctx.fill();
  }
  if (vis.has('spirit') || vis.has('spirit_resonance')) {
    ctx.fillStyle = `rgba(200,180,230,${0.12 + Math.sin(time * 2.4) * 0.05})`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 6, 26, 13, 0, 0, Math.PI * 2); ctx.fill();
  }
  // изначальное свечение (Регион 2): слизень сам источник света
  if (p.genome.installed.includes('bioluminescent')) {
    const gl = 0.16 + Math.sin(time * 2.6) * 0.05 + pulse * 0.1;
    ctx.fillStyle = `rgba(190,240,180,${gl})`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 6, 34 + pulse * 8, 18 + pulse * 4, 0, 0, Math.PI * 2); ctx.fill();
  } else {
    // слабая «аура жизни» — тело чуть светится само, героя видно даже слепым в ночи
    const gl = 0.07 + Math.sin(time * 2.6) * 0.02 + pulse * 0.05;
    ctx.fillStyle = `rgba(160,225,150,${gl})`;
    ctx.beginPath(); ctx.ellipse(sx, sy - 4, 26 + pulse * 5, 14 + pulse * 3, 0, 0, Math.PI * 2); ctx.fill();
  }

  // ---- тело слизня: ЖИДКОСТНОЕ мягкое тело (Дополнение от игрока) ----
  // контур из N точек с пружинами и поверхностным натяжением; лужа → горб по мере развития;
  // точки не выходят в непроходимое — тело ОБЛИЗЫВАЕТ препятствие, движение — обтекает (world.ts)
  const k = dying ? 0 : Math.min(1, spd / 2.4);
  const dt = Math.max(0.001, Math.min(0.05, dtF));

  // ---- регенерация (референс «Регенерация / Восстановление»): золотой капельный дождь ----
  regenSpawnT -= dt;
  regenT = Math.max(0, regenT - dt * 0.5);
  if (lastHpSeen >= 0 && !dying && p.hp > lastHpSeen + 0.02 && p.hp < s.maxHp && regenSpawnT <= 0) {
    regenT = 1;
    regenSpawnT = 0.16;
    regenDrops.push({
      ax: sx + (Math.random() - 0.5) * 24,
      ay: sy - 30 - Math.random() * 14,
      vy: 36 + Math.random() * 30,
      life: 0, max: 1.2, r: 1.5 + Math.random() * 1.2,
    });
  }
  lastHpSeen = p.hp;
  for (let i = regenDrops.length - 1; i >= 0; i--) {
    const d = regenDrops[i];
    d.ay += d.vy * dt; d.life += dt;
    if (d.ay > jy - 3 || d.life > d.max) regenDrops.splice(i, 1);
  }
  if (regenT > 0) {
    // мягкое золотое свечение выздоровления — под телом
    const ga = ctx.createRadialGradient(sx, sy - 6, 2, sx, sy - 6, 34);
    ga.addColorStop(0, `rgba(255,220,130,${(0.20 * regenT).toFixed(3)})`);
    ga.addColorStop(1, 'rgba(255,220,130,0)');
    ctx.fillStyle = ga;
    ctx.beginPath(); ctx.ellipse(sx, sy - 6, 34, 20, 0, 0, Math.PI * 2); ctx.fill();
  }
  const breath = 1 + Math.sin(time * 2.2) * 0.025 * (1 - k);
  const grow = 1 + Math.min(0.5, p.genome.installed.length * 0.07);
  const snS = world.data.senses!;
  const organsN = snS.unlocked.length;
  const young = Math.max(0, 4 - organsN); // 3..0 — чем меньше органов, тем бесформеннее лужа
  const widthMul = 1 + young * 0.15;
  const heightMul = Math.max(0.52, 1 - young * 0.16);
  const R = 14 * grow * breath * (1 + pulse * 0.16);

  // ---- Пищеварение (ТЗ §5–§7): содержимое видно сквозь мембрану; давление натягивает тело (§5.5) ----
  const dig = world.data.digestion;
  const digItems = dig ? dig.chambers.filter((x) => x != null) : [];
  const digPressure = world.digestivePressure();
  const toxicIn = digItems.some((it) => it != null && it.kind === 'toxic');
  // §2.3: цветовое состояние — доминирующее содержимое окрашивает желе (палитра от диеты)
  let digKind: 'organic' | 'mineral' | 'magic' | 'toxic' | null = null;
  let digBest = 0;
  for (const it of digItems) {
    if (it && it.mass > digBest) { digBest = it.mass; digKind = it.kind; }
  }

  // экранный единичный вектор скорости (вытяжение вдоль движения)
  const vsx = w2sX(p.vel.x, p.vel.y), vsy = w2sY(p.vel.x, p.vel.y);
  const vsl = Math.hypot(vsx, vsy);
  const ux = k > 0.03 && vsl > 0.001 ? vsx / vsl : 1;
  const uy = k > 0.03 && vsl > 0.001 ? vsy / vsl : 0;
  // обтекание стены — flowT активен, когда слизь течёт вокруг препятствия
  const flowT = p.flowT ?? 0;

  // ---- рендерер желе: физика контура + hop-цикл + моргание + брызги (SlimeJelly) ----
  jelly.update(dt, {
    k, ux, uy, pulse, flowT, pressure: digPressure, young,
    R, widthMul, heightMul,
    cx: jx, cy: jy,
    wx: p.pos.x, wy: p.pos.y,
    moving: spd > 0.12 && !dying,
    dissolve: dis,
    s2wX, s2wY,
    blocked: (wx, wy) => !world.isWalkable(wx, wy),
  });

  ctx.save();
  if (flash) ctx.filter = 'brightness(2)';
  else if (digPressure > 0.6) {
    // перегрузка (§5.5): хаотичные цветовые пульсации тела (референс «перегрузка», 12 кадров)
    ctx.filter = `hue-rotate(${Math.round(Math.sin(time * 7) * 90 * ((digPressure - 0.6) / 0.4))}deg)`;
  }

  jelly.draw(ctx, {
    pal: resolvePalette(digKind),
    time, pulse, flash, toxicIn,
    organsN,
    chemo: snS.unlocked.includes('chemo'),
    thermal: snS.unlocked.includes('thermal'),
    vision: snS.unlocked.includes('vision'),
    prismatic: p.genome.installed.includes('prismatic_core'),
    dissolve: dis,
    // внутренности: груз полостей и органы — сквозь мембрану желе
    inner: ({ cx: px, cy: cy0, rAcross: bh, t: blobT }) => {

  // ---- содержимое внутри тела: непереваренный груз (§5.3 E) и 12 фаз переваривания (§6.3) ----
  // предметы дрейфуют в жиже, остаются видимыми сквозь мембрану, уменьшаются по мере переработки
  for (let ci = 0; ci < digItems.length; ci++) {
    const it = digItems[ci]!;
    const ph = (it.seed % 1000) * 0.001;
    // §14.3: слои деформируются согласованно — дрейф и размер предмета привязаны к телу,
    // крупный груз не выходит за мембрану
    const driftX = 3 + Math.min(it.mass, 18) * 0.07;
    const driftY = 2 + Math.min(it.mass, 18) * 0.045;
    const ix = px + Math.sin(blobT * 0.5 + ph * 9) * driftX;
    const iy = cy0 + Math.cos(blobT * 0.72 + ph * 7) * driftY;
    const sc = 1 - it.progress * 0.62; // фаза 10: остаток заметно уменьшается
    const s0 = Math.min(bh * 0.58, 3 + it.mass * 0.42) * Math.max(0.3, sc);
    if (it.kind === 'mineral') {
      // мутные кристаллические фрагменты (§2.3 — переваривание минералов)
      ctx.fillStyle = 'rgba(168,190,212,0.85)';
      ctx.beginPath();
      ctx.moveTo(ix, iy - s0); ctx.lineTo(ix + s0 * 0.7, iy); ctx.lineTo(ix, iy + s0); ctx.lineTo(ix - s0 * 0.7, iy);
      ctx.closePath(); ctx.fill();
      ctx.strokeStyle = 'rgba(230,240,250,0.5)'; ctx.lineWidth = 1; ctx.stroke();
    } else if (it.kind === 'magic') {
      // переливы синего и фиолетового (§2.3 — магическое переваривание)
      const mg = ctx.createRadialGradient(ix, iy, 0.5, ix, iy, s0 * 1.7);
      mg.addColorStop(0, `rgba(196,160,255,${0.5 + Math.sin(blobT * 3 + ph * 6) * 0.2})`);
      mg.addColorStop(1, 'rgba(120,80,200,0)');
      ctx.fillStyle = mg;
      ctx.beginPath(); ctx.arc(ix, iy, s0 * 1.7, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = 'rgba(216,190,255,0.9)';
      ctx.beginPath();
      ctx.moveTo(ix, iy - s0 * 0.9); ctx.lineTo(ix + s0 * 0.55, iy); ctx.lineTo(ix, iy + s0 * 0.9); ctx.lineTo(ix - s0 * 0.55, iy);
      ctx.closePath(); ctx.fill();
    } else {
      // органика: янтарно-зелёные включения; токсичное — тёмные сгустки (§2.3)
      ctx.fillStyle = it.kind === 'toxic' ? 'rgba(74,84,40,0.8)' : `rgba(190,214,120,${0.75 - it.progress * 0.2})`;
      ctx.beginPath();
      ctx.ellipse(ix, iy, s0, s0 * 0.72, Math.sin(ph * 5) * 0.7, 0, Math.PI * 2);
      ctx.fill();
    }
    // фазы 2–9: активные пузырьки вокруг предмета (§6.3 кадры 2–6)
    const phase = 1 + Math.floor(it.progress * 12);
    if (phase >= 2 && phase <= 9) {
      for (let b = 0; b < 3; b++) {
        const bp = (blobT * 0.8 + b / 3 + ph) % 1;
        ctx.fillStyle = `rgba(226,240,180,${(1 - bp) * 0.5})`;
        ctx.beginPath();
        ctx.arc(ix + Math.sin(b * 2.4 + ph * 8) * s0 * 0.9, iy - s0 * 0.4 + bp * s0 * 1.3, 1.1 + b * 0.3, 0, Math.PI * 2);
        ctx.fill();
      }
    }
    // фазы 10–12: мелкие фрагменты расходятся (§6.3 кадры 8–11)
    if (phase >= 10) {
      for (let f = 0; f < 4; f++) {
        const fa = f * Math.PI / 2 + ph * 3 + blobT * 0.6;
        const fr = s0 * (1.15 + 0.2 * Math.sin(blobT * 2 + f));
        ctx.fillStyle = `rgba(200,220,150,${Math.max(0.08, 0.42 - (phase - 10) * 0.12)})`;
        ctx.beginPath();
        ctx.arc(ix + Math.cos(fa) * fr, iy + Math.sin(fa) * fr * 0.6, 1, 0, Math.PI * 2);
        ctx.fill();
      }
    }
  }
  // отравление: тёмные прожилки рисует SlimeJelly (до внутренностей)

  // ---- внутренности: органы проступают в жиже по мере пробуждения (лужа — гомогенная) ----
  if (organsN >= 2) {
    // ядро плоти (вибрация): тёмный сгусток, мерно пульсирует
    const nx = px + Math.sin(blobT * 0.9) * 3;
    const ny = cy0 + Math.cos(blobT * 1.3) * 2;
    ctx.fillStyle = 'rgba(28,38,24,0.55)';
    ctx.beginPath(); ctx.ellipse(nx, ny, 6 + Math.sin(blobT * 2.2) * 0.9, 4.4, 0, 0, Math.PI * 2); ctx.fill();
  }
  if (snS.unlocked.includes('chemo')) {
    // пузырьки химии поднимаются внутри тела
    for (let i = 0; i < 3; i++) {
      const ph = (blobT * 0.45 + i / 3) % 1;
      ctx.fillStyle = `rgba(183,224,124,${(1 - ph) * 0.5})`;
      ctx.beginPath(); ctx.arc(px - 5 + i * 5 + Math.sin(blobT * 2 + i * 2) * 2, cy0 + 4 - ph * 9, 1.5, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (snS.unlocked.includes('thermal')) {
    // тёплое марево внутри жижи
    const g2 = ctx.createRadialGradient(px, cy0 + 2, 1, px, cy0 + 2, 13);
    g2.addColorStop(0, 'rgba(240,144,96,0.30)');
    g2.addColorStop(1, 'rgba(240,144,96,0)');
    ctx.fillStyle = g2;
    ctx.beginPath(); ctx.ellipse(px, cy0 + 2, 13, 8, 0, 0, Math.PI * 2); ctx.fill();
  }
  if (snS.unlocked.includes('light')) {
    // светящееся пятнышко-фоторецептор
    ctx.fillStyle = `rgba(245,240,176,${0.5 + Math.sin(blobT * 3) * 0.2})`;
    ctx.beginPath(); ctx.arc(px + Math.sin(blobT * 1.1) * 4, cy0 - 2 + Math.cos(blobT * 1.7) * 2, 2.2, 0, Math.PI * 2); ctx.fill();
  }
  if (snS.unlocked.includes('hearing')) {
    // ушные дуги сзади тела
    ctx.strokeStyle = 'rgba(208,168,240,0.5)';
    ctx.lineWidth = 1.2;
    const bx = px - ux * 10, by2 = cy0 - uy * 6;
    ctx.beginPath(); ctx.arc(bx, by2, 4, Math.PI * 0.2, Math.PI * 0.8); ctx.stroke();
  }
    }, // inner
  });
  ctx.restore();

  // капли регенерации — падают В тело (референс: капли над слизнем), с ореолом чтобы не терялись на фоне спор
  for (const d of regenDrops) {
    const fa = Math.max(0, 1 - d.life / d.max) * 0.9;
    const halo = ctx.createRadialGradient(d.ax, d.ay, 0.5, d.ax, d.ay, d.r * 2.8);
    halo.addColorStop(0, `rgba(255,226,150,${(fa * 0.5).toFixed(3)})`);
    halo.addColorStop(1, 'rgba(255,226,150,0)');
    ctx.fillStyle = halo;
    ctx.beginPath(); ctx.arc(d.ax, d.ay, d.r * 2.8, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = `rgba(255,224,140,${fa.toFixed(3)})`;
    ctx.beginPath(); ctx.arc(d.ax, d.ay, d.r, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = `rgba(255,250,225,${(fa * 0.9).toFixed(3)})`;
    ctx.beginPath(); ctx.arc(d.ax - d.r * 0.3, d.ay - d.r * 0.3, d.r * 0.36, 0, Math.PI * 2); ctx.fill();
  }
  // искры-звёздочки восстановления
  if (regenT > 0) {
    for (let i = 0; i < 4; i++) {
      const tw = Math.sin(time * 7 + i * 2.4);
      if (tw > 0.1) {
        const stx = sx + Math.sin(i * 2.1 + time * 0.7) * 17;
        const sty = sy - 24 - ((time * 13 + i * 11) % 12);
        const r0 = 2.1 * ((tw - 0.1) / 0.9);
        ctx.strokeStyle = `rgba(255,236,175,${(0.85 * regenT).toFixed(3)})`;
        ctx.lineWidth = 1.2; ctx.lineCap = 'round';
        ctx.beginPath();
        ctx.moveTo(stx - r0 * 2, sty); ctx.lineTo(stx + r0 * 2, sty);
        ctx.moveTo(stx, sty - r0 * 2); ctx.lineTo(stx, sty + r0 * 2);
        ctx.stroke();
      }
    }
  }

  if (!dying) {
  // визуальные гены (хвостик-капля и мордочка — в SlimeJelly)
  if (vis.has('physical')) {
    ctx.fillStyle = flash ? '#fff' : '#3a3a30';
    for (let i = -1; i <= 1; i++) {
      ctx.beginPath();
      ctx.ellipse(jx + i * 7, jy - 12 + bob * 0.4, 5, 3, 0, Math.PI, 0);
      ctx.fill();
    }
  }
  if (vis.has('crystal')) {
    ctx.fillStyle = flash ? '#fff' : '#63b3a1';
    ctx.beginPath();
    ctx.moveTo(jx - 4, jy - 13); ctx.lineTo(jx - 1, jy - 21); ctx.lineTo(jx + 2, jy - 12);
    ctx.moveTo(jx + 3, jy - 12); ctx.lineTo(jx + 7, jy - 18); ctx.lineTo(jx + 8, jy - 10);
    ctx.closePath(); ctx.fill();
  }
  if (vis.has('elemental')) {
    const gl = 0.5 + Math.sin(time * 7) * 0.4;
    ctx.fillStyle = `rgba(235,210,90,${gl})`;
    for (let i = 0; i < 3; i++) {
      ctx.beginPath(); ctx.arc(jx - 7 + i * 7, jy - 8 + Math.sin(time * 6 + i * 2) * 2, 1.8, 0, Math.PI * 2); ctx.fill();
    }
  }
  if (vis.has('electric_trap') || vis.has('battery')) {
    ctx.strokeStyle = `rgba(235,210,90,${0.5 + Math.sin(time * 9) * 0.4})`;
    ctx.lineWidth = 1.2;
    ctx.beginPath();
    ctx.moveTo(jx + 12, jy - 14); ctx.lineTo(jx + 17, jy - 18); ctx.lineTo(jx + 14, jy - 22);
    ctx.stroke();
  }
  if (vis.has('poison_glands')) {
    ctx.fillStyle = 'rgba(140,190,90,0.75)';
    ctx.beginPath(); ctx.arc(jx - p.facing * 12, jy - 6 + (time * 20 % 8), 1.6, 0, Math.PI * 2); ctx.fill();
  }
  // споровый ринг: облако/вуаль спор кружат вокруг тела (Регион 2)
  if (p.genome.installed.includes('spore_cloud') || p.genome.installed.includes('spore_veil')) {
    for (let i = 0; i < 7; i++) {
      const a2 = time * 1.6 + i * (Math.PI * 2 / 7);
      const rr = 20 + Math.sin(time * 2 + i) * 3;
      ctx.fillStyle = `rgba(150,220,150,${0.20 + Math.sin(time * 3 + i) * 0.09})`;
      ctx.beginPath(); ctx.arc(jx + Math.cos(a2) * rr, jy - 8 + Math.sin(a2) * rr * 0.5, 1.5, 0, Math.PI * 2); ctx.fill();
    }
  }

  // HP игрока
  const wBar = 30;
  ctx.fillStyle = 'rgba(0,0,0,0.55)';
  ctx.fillRect(sx - wBar / 2, sy - 28, wBar, 4);
  const hpFrac = Math.max(0, p.hp / Math.max(1, s.maxHp));
  ctx.fillStyle = hpFrac > 0.35 ? '#7a9a4a' : '#c9563d';
  ctx.fillRect(sx - wBar / 2, sy - 28, wBar * hpFrac, 4);
  } // !dying
}
