// SlimeJelly — процедурный жидкий слизень (Canvas 2D).
// Референсы игрока: «Лист анимаций изометрического героя-слизи» + «Лист анимации милого голубого слайма»:
//   • полупрозрачное светящееся желе: светлое ядро → тон → насыщенный край, внешнее свечение;
//   • двойной глянцевый блик, пузырьки воздуха и капельки-включения внутри;
//   • мордочка: глянцевые глаза с искрами (моргает, смотрит по движению), улыбка, румянец —
//     появляются по мере пробуждения органов (лужа бесформенна);
//   • хвостик-капля сверху с пружинным запаздыванием;
//   • hop-цикл при движении: stretch в полёте, squash при приземлении + брызги;
//   • контур из N точек с пружинами: колыхамент, натяжение, облизывание стен (не входит в непроходимое);
//   • палитра от диеты (§2.3): органика — изумруд, минерал — лазурь, магия — фиолет, токсин — мрак;
//   • смерть-распад (референс «Смерть / Распад»): тело растекается лужей и рассыпается на капли,
//     глаза закрываются — спокойный уход перед экраном смерти.

export interface SlimePalette {
  core: string; // светлый центр желе
  mid: string;  // основной тон
  deep: string; // насыщенный край
  glow: string; // 'r,g,b' — внешнее свечение
  rim: string;  // 'r,g,b' — верхняя кромка / брызги
}

const PALS: Record<string, SlimePalette> = {
  emerald:  { core: '#e9ffdb', mid: '#8ed46e', deep: '#3c8a4c', glow: '150,235,140', rim: '240,255,225' },
  azure:    { core: '#e2f7ff', mid: '#6fcbef', deep: '#2a7fc0', glow: '120,205,245', rim: '235,250,255' },
  violet:   { core: '#f0e4ff', mid: '#b48ce9', deep: '#7752b5', glow: '180,140,235', rim: '245,238,255' },
  rose:     { core: '#ffe6f0', mid: '#f392c1', deep: '#c05a90', glow: '243,146,193', rim: '255,240,248' },
  amber:    { core: '#fff3d2', mid: '#f0c66e', deep: '#bd8530', glow: '240,200,110', rim: '255,248,225' },
  obsidian: { core: '#d6dce2', mid: '#69737f', deep: '#262c35', glow: '130,140,155', rim: '230,238,245' },
};

function hexRgb(h: string): [number, number, number] {
  return [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
}
function lerpHex(a: string, b: string, t: number): string {
  const A = hexRgb(a), B = hexRgb(b);
  const m = (i: number) => Math.round(A[i] + (B[i] - A[i]) * t).toString(16).padStart(2, '0');
  return `#${m(0)}${m(1)}${m(2)}`;
}
function blendPal(base: SlimePalette, target: SlimePalette, t: number): SlimePalette {
  return {
    core: lerpHex(base.core, target.core, t),
    mid: lerpHex(base.mid, target.mid, t),
    deep: lerpHex(base.deep, target.deep, t),
    glow: t > 0.5 ? target.glow : base.glow,
    rim: t > 0.5 ? target.rim : base.rim,
  };
}

// §2.3: доминирующее содержимое полостей окрашивает желе
export function resolvePalette(kind: 'organic' | 'mineral' | 'magic' | 'toxic' | null): SlimePalette {
  const base = PALS.emerald;
  if (!kind || kind === 'organic') return base;
  if (kind === 'mineral') return blendPal(base, PALS.azure, 0.55);
  if (kind === 'magic') return blendPal(base, PALS.violet, 0.6);
  return blendPal(base, PALS.obsidian, 0.65); // toxic — мутный мрак
}

interface Pt { ox: number; oy: number; vx: number; vy: number; }
interface Drop { x: number; y: number; vx: number; vy: number; life: number; max: number; r: number; }

const N = 22;

export interface SlimeUpdateEnv {
  k: number;          // фактор скорости 0..1
  ux: number; uy: number; // экранный единичный вектор движения
  pulse: number;      // пульс насыщения при поглощении 0..1
  flowT: number;      // обтекание стены (world.ts)
  pressure: number;   // давление пищеварения 0..1 (§5.5)
  young: number;      // 3..0 — бесформенность лужи
  R: number;          // базовый радиус (рост, дыхание)
  widthMul: number; heightMul: number;
  cx: number; cy: number; // точка опоры на экране (центр низа тела)
  wx: number; wy: number; // мировая позиция (для проверки стен контуром)
  moving: boolean;
  dissolve?: number;     // 0..1 — прогресс распада (смерть-растекание)
  s2wX: (sx: number, sy: number) => number;
  s2wY: (sx: number, sy: number) => number;
  blocked: (wx: number, wy: number) => boolean;
}

export interface SlimeInnerGeo { cx: number; cy: number; rAlong: number; rAcross: number; t: number; }

export interface SlimeDrawEnv {
  pal: SlimePalette;
  time: number; pulse: number; flash: boolean; toxicIn: boolean;
  organsN: number; chemo: boolean; thermal: boolean; vision: boolean;
  prismatic?: boolean; // призматическое ядро (Регион 3): контур переливается всеми цветами
  dissolve?: number;   // 0..1 — распад: тело бледнеет/растекается, глаза закрываются
  inner?: (g: SlimeInnerGeo) => void;
}

export class SlimeJelly {
  private pts: Pt[] = [];
  private t = 0;
  private hopPhase = 0;
  private squashT = 0;
  private yLift = 0;
  private blinkT = -1;
  private nextBlink = 2.4;
  private bumpX = 0; private bumpVX = 0;
  private drops: Drop[] = [];
  private trailT = 0;
  private lastUx = 1; private lastUy = 0;
  private lastDissolve = 0;
  geo = { cx: 0, cy: 0, groundY: 0, rAlong: 14, rAcross: 10 };

  update(dt: number, e: SlimeUpdateEnv) {
    this.t += dt;
    this.lastUx = e.ux; this.lastUy = e.uy;
    const moving = e.moving && e.k > 0.12 && !(e.dissolve && e.dissolve > 0);

    // ---- смерть-распад: тело растекается и рассыпается на капли (референс «Death», 8 кадров) ----
    const dis = e.dissolve ?? 0;
    if (dis > 0) {
      if (this.lastDissolve === 0) {
        // первичный выброс: тело лопается каплями наружу по всем направлениям
        this.splash(e, 14, 2.1);
      } else if (dis < 0.55 && Math.random() < dt * 14) {
        this.splash(e, 1, 0.7 + dis);
      }
      this.lastDissolve = dis;
    } else if (this.lastDissolve > 0) {
      this.lastDissolve = 0;
    }

    // ---- hop-цикл: полёт → приземление (squash + брызги) ----
    if (moving) {
      this.hopPhase += dt * (2.9 + e.k * 1.9);
      if (this.hopPhase >= 1) {
        this.hopPhase %= 1;
        this.squashT = 1;
        this.splash(e, 4, 1);
      }
      this.yLift = Math.sin(Math.PI * this.hopPhase) * (1.8 + e.k * 2.8);
    } else {
      this.hopPhase = 0;
      this.yLift = Math.max(0, this.yLift - dt * 14);
    }
    this.squashT = Math.max(0, this.squashT - dt * 3.4);

    // ---- геометрия покоя ----
    const R = e.R;
    const sAlong = 1 + 0.22 * e.k + e.pulse * 0.14 + (e.flowT > 0 ? 0.16 : 0);
    let rAlong = R * sAlong * e.widthMul;
    let rAcross = R * (1 - 0.15 * e.k) * e.heightMul * 0.74;
    if (dis > 0) {
      // распад: лужа расползается вширь и оседает, колыхамент затухает
      rAlong *= 1 + dis * 1.35;
      rAcross *= Math.max(0.16, 1 - dis * 0.86);
    }
    if (moving) {
      const hs = Math.sin(Math.PI * this.hopPhase);
      rAcross *= 1 + 0.10 * hs * e.k; // вытянулся в полёте
      rAlong *= 1 - 0.05 * hs * e.k;
    }
    rAcross *= 1 - 0.17 * this.squashT; // расплющился при приземлении
    rAlong *= 1 + 0.11 * this.squashT;

    const idleBob = !moving ? Math.sin(this.t * 1.8) * 0.5 : 0;
    this.geo = { cx: e.cx, groundY: e.cy, rAlong, rAcross, cy: e.cy - 5 + this.yLift + idleBob };

    // ---- физика контура: пружины к эллипсу покоя + натяжение к соседям + колыхамент ----
    if (this.pts.length !== N) {
      this.pts = Array.from({ length: N }, (_, i) => {
        const a = (i / N) * Math.PI * 2;
        return { ox: Math.cos(a) * rAcross, oy: Math.sin(a) * rAcross * 0.8, vx: 0, vy: 0 };
      });
    }
    const wobA = ((0.8 + 1.0 * e.k) * (0.85 + e.young * 0.18) * (1 + e.pressure * 0.9) + this.squashT * 0.8) * (1 - dis * 0.75);
    for (let i = 0; i < N; i++) {
      const pt = this.pts[i];
      const a = (i / N) * Math.PI * 2;
      const ex = Math.cos(a) * rAlong;
      const ey = Math.sin(a) * rAcross;
      const rx = ex * e.ux - ey * e.uy;
      const ry = ex * e.uy + ey * e.ux;
      const prev = this.pts[(i + N - 1) % N], nxt = this.pts[(i + 1) % N];
      let ax = (rx - pt.ox) * 46 + ((prev.ox + nxt.ox) / 2 - pt.ox) * 10;
      let ay = (ry - pt.oy) * 46 + ((prev.oy + nxt.oy) / 2 - pt.oy) * 10;
      ax += Math.cos(a * 3 + this.t * 4.1) * wobA * 12;
      ay += Math.sin(a * 3 + this.t * 4.6) * wobA * 9;
      pt.vx += ax * dt; pt.vy += ay * dt;
      const damp = Math.pow(0.0008, dt); // вязкое затухание — поведение жидкости
      pt.vx *= damp; pt.vy *= damp;
      pt.ox += pt.vx * dt; pt.oy += pt.vy * dt;
      // контур не выходит в непроходимое: тело облизывает стену изнутри
      const wpx = e.wx + e.s2wX(pt.ox, pt.oy);
      const wpy = e.wy + e.s2wY(pt.ox, pt.oy);
      if (e.blocked(wpx, wpy)) {
        pt.ox *= 0.5; pt.oy *= 0.5;
        pt.vx *= 0.25; pt.vy *= 0.25;
      }
      const dl = Math.hypot(pt.ox, pt.oy) || 0.001;
      if (dl > R * 2.0) { pt.ox *= R * 2.0 / dl; pt.oy *= R * 2.0 / dl; }
      if (dl < R * 0.4) { pt.ox *= R * 0.4 / dl; pt.oy *= R * 0.4 / dl; }
    }

    // ---- хвостик-капля: пружинное запаздывание за движением ----
    this.bumpVX += ((-e.ux * 2.2 * e.k - this.bumpX) * 26 - this.bumpVX * 7) * dt;
    this.bumpX += this.bumpVX * dt;

    // ---- моргание ----
    if (this.blinkT >= 0) {
      this.blinkT -= dt;
      if (this.blinkT < 0) this.nextBlink = 2.2 + Math.random() * 2.8;
    } else {
      this.nextBlink -= dt;
      if (this.nextBlink <= 0) this.blinkT = 0.12;
    }

    // ---- брызги: след на ходу, всплеск при обтекании стены ----
    if (e.k > 0.45) {
      this.trailT -= dt;
      if (this.trailT <= 0) {
        this.trailT = 0.22;
        this.drops.push({
          x: this.geo.cx - e.ux * this.geo.rAlong * 0.7,
          y: this.geo.groundY - 2 - e.uy * 4,
          vx: -e.ux * 26 + (Math.random() - 0.5) * 14,
          vy: -12 - Math.random() * 16,
          life: 0.34, max: 0.34, r: 0.9 + Math.random() * 0.8,
        });
      }
      if (e.flowT > 0 && Math.random() < dt * 6) this.splash(e, 1, 0.6);
    }

    for (let i = this.drops.length - 1; i >= 0; i--) {
      const d = this.drops[i];
      d.life -= dt;
      d.vy += 90 * dt;
      d.x += d.vx * dt; d.y += d.vy * dt;
      if (d.life <= 0) this.drops.splice(i, 1);
    }
  }

  private splash(e: SlimeUpdateEnv, n: number, power: number) {
    for (let i = 0; i < n; i++) {
      this.drops.push({
        x: this.geo.cx + (Math.random() - 0.5) * this.geo.rAlong * 1.2,
        y: this.geo.groundY - 1,
        vx: (Math.random() - 0.5) * 46 * power,
        vy: -(22 + Math.random() * 40) * power,
        life: 0.45, max: 0.45, r: 1 + Math.random() * 1.1,
      });
    }
  }

  draw(ctx: CanvasRenderingContext2D, e: SlimeDrawEnv) {
    const { cx, cy, groundY, rAlong, rAcross } = this.geo;
    const pal = e.pal;
    const airK = Math.min(1, this.yLift / 4);
    const dis = e.dissolve ?? 0;
    // при распаде тело растворяется — капли при этом живут собственной жизнью (alpha сброшена ниже)
    const bodyA = 1 - dis * 0.9;
    ctx.globalAlpha = bodyA;

    // тень (меньше и бледнее в полёте)
    ctx.fillStyle = `rgba(0,0,0,${(0.42 - airK * 0.18).toFixed(3)})`;
    ctx.beginPath();
    ctx.ellipse(cx, groundY + 1, rAlong * (1 - airK * 0.22), rAlong * (1 - airK * 0.22) * 0.45, 0, 0, Math.PI * 2);
    ctx.fill();

    // внешнее свечение желе
    const gA = 0.20 + Math.sin(this.t * 2.6) * 0.05 + e.pulse * 0.12;
    const gg = ctx.createRadialGradient(cx, cy, 2, cx, cy, rAlong * 1.9);
    gg.addColorStop(0, `rgba(${pal.glow},${gA.toFixed(3)})`);
    gg.addColorStop(1, `rgba(${pal.glow},0)`);
    ctx.fillStyle = gg;
    ctx.beginPath(); ctx.arc(cx, cy, rAlong * 1.9, 0, Math.PI * 2); ctx.fill();

    // хвостик-капля сверху (с эволюции органов) — рисуется до тела, низ скрыт под желе.
    // Привязан к ФАКТИЧЕСКОМУ верху контура: когда тело сплющено о стену (облизывание),
    // хвостик опускается вместе с желе, а не повисает в воздухе
    if (e.organsN >= 1) {
      const bs = Math.min(1, 0.35 + e.organsN * 0.14);
      let topOy = this.pts.length ? 0 : -rAcross;
      for (const pt of this.pts) if (pt.oy < topOy) topOy = pt.oy;
      const bx = cx + this.bumpX;
      const by = cy + topOy - 1.0;
      const br = (2.0 + e.organsN * 0.30) * bs;
      const tip = this.bumpX * 1.6 + Math.sin(this.t * 3.1) * 0.6;
      ctx.fillStyle = e.flash ? '#fff' : pal.mid;
      ctx.beginPath();
      ctx.arc(bx, by, br, 0, Math.PI * 2);
      ctx.moveTo(bx - br * 0.55, by - br * 0.35);
      ctx.quadraticCurveTo(bx + tip, by - br * 2.4, bx + br * 0.55, by - br * 0.35);
      ctx.fill();
    }

    // ---- гладкий жидкий контур: квадратичные кривые через середины сегментов ----
    const path = new Path2D();
    {
      const midX = (this.pts[0].ox + this.pts[1].ox) / 2;
      const midY = (this.pts[0].oy + this.pts[1].oy) / 2;
      path.moveTo(cx + midX, cy + midY);
      for (let i = 1; i <= N; i++) {
        const cur = this.pts[i % N], nxt2 = this.pts[(i + 1) % N];
        path.quadraticCurveTo(cx + cur.ox, cy + cur.oy, cx + (cur.ox + nxt2.ox) / 2, cy + (cur.oy + nxt2.oy) / 2);
      }
      path.closePath();
    }

    // желе: радиальный градиент — светлое ядро → тон → насыщенный край
    const grad = ctx.createRadialGradient(cx - rAlong * 0.22, cy - rAcross * 0.42, 1, cx, cy, rAlong * 1.35);
    grad.addColorStop(0, e.flash ? '#ffffff' : pal.core);
    grad.addColorStop(0.52, e.flash ? '#ffffff' : pal.mid);
    grad.addColorStop(1, e.flash ? '#ffffff' : pal.deep);
    ctx.fillStyle = grad;
    ctx.fill(path);

    // токсичные прожилки (§2.3)
    if (e.toxicIn) {
      ctx.strokeStyle = `rgba(26,30,14,${(0.35 + Math.sin(this.t * 6) * 0.15).toFixed(3)})`;
      ctx.lineWidth = 1.4;
      for (let v = 0; v < 3; v++) {
        ctx.beginPath();
        const vx0 = cx - 8 + v * 8;
        ctx.moveTo(vx0, cy - rAcross * 0.5);
        ctx.quadraticCurveTo(vx0 + Math.sin(this.t * 5 + v * 2) * 4, cy, vx0, cy + rAcross * 0.5);
        ctx.stroke();
      }
    }

    // внутренности: содержимое полостей, органы — сквозь мембрану
    e.inner?.({ cx, cy, rAlong, rAcross, t: this.t });

    // ленивые пузырьки воздуха внутри желе (стиль референса)
    for (let i = 0; i < 4; i++) {
      const ph = (this.t * 0.30 + i / 4) % 1;
      const bx2 = cx + Math.sin(i * 2.7 + this.t * 0.7) * rAlong * 0.45;
      const by2 = cy + rAcross * 0.55 - ph * rAcross * 1.25;
      ctx.fillStyle = `rgba(255,255,255,${((1 - ph) * 0.28).toFixed(3)})`;
      ctx.beginPath(); ctx.arc(bx2, by2, 0.9 + (i % 2) * 0.5, 0, Math.PI * 2); ctx.fill();
    }
    // капельки-включения (вытянутые блики внутри, как на референсе)
    const dr = [[-0.45, 0.3, 2.4, 3.8, -0.5], [0.34, 0.12, 1.9, 3.1, 0.6], [-0.08, 0.5, 1.5, 2.4, 0.2]] as const;
    for (let i = 0; i < dr.length; i++) {
      const [dx, dy, w1, h1, rot] = dr[i];
      ctx.fillStyle = `rgba(255,255,255,${(0.14 + Math.sin(this.t * 1.4 + i * 2) * 0.05).toFixed(3)})`;
      ctx.beginPath();
      ctx.ellipse(cx + rAlong * dx, cy + rAcross * dy, w1, h1, rot, 0, Math.PI * 2);
      ctx.fill();
    }

    // светлая кромка сверху (rim light) — желе собирает свет верхом
    ctx.save();
    ctx.clip(path);
    const rg = ctx.createRadialGradient(cx, cy - rAcross * 0.9, 1, cx, cy - rAcross * 0.9, rAcross * 1.5);
    rg.addColorStop(0, `rgba(${pal.rim},0.5)`);
    rg.addColorStop(0.55, `rgba(${pal.rim},0.12)`);
    rg.addColorStop(1, `rgba(${pal.rim},0)`);
    ctx.fillStyle = rg;
    ctx.fillRect(cx - rAlong * 1.3, cy - rAcross * 2.2, rAlong * 2.6, rAcross * 2.2);
    ctx.restore();

    // контур (призматическое ядро переливается всеми цветами)
    ctx.strokeStyle = e.prismatic
      ? `hsla(${(this.t * 90) % 360},72%,74%,0.9)`
      : 'rgba(14,24,14,0.55)';
    ctx.lineWidth = e.prismatic ? 2 : 1.2;
    ctx.stroke(path);

    // двойной глянцевый блик (экранно-фиксированный свет сверху-слева)
    const hx = cx - rAlong * 0.34, hy = cy - rAcross * 0.42;
    ctx.fillStyle = 'rgba(255,255,255,0.72)';
    ctx.beginPath(); ctx.ellipse(hx, hy, rAlong * 0.22, rAcross * 0.16, -0.55, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = 'rgba(255,255,255,0.45)';
    ctx.beginPath(); ctx.arc(hx + rAlong * 0.16, hy + rAcross * 0.14, rAlong * 0.07, 0, Math.PI * 2); ctx.fill();

    this.face(ctx, e, cx, cy, rAcross);

    // капли живут после растворения тела — их альфа независимая
    ctx.globalAlpha = 1;
    for (const d of this.drops) {
      const a = Math.max(0, d.life / d.max);
      ctx.fillStyle = `rgba(${pal.rim},${(a * 0.8).toFixed(3)})`;
      ctx.beginPath(); ctx.arc(d.x, d.y, d.r, 0, Math.PI * 2); ctx.fill();
    }
  }

  // мордочка: глаза/улыбка/румянец пробуждаются вместе с органами
  private face(ctx: CanvasRenderingContext2D, e: SlimeDrawEnv, cx: number, cy: number, rAcross: number) {
    if (e.organsN < 1) return; // бесформенная лужа лица ещё не имеет
    const fx = cx + this.lastUx * 2.4;
    const fy = cy - rAcross * 0.16 + this.lastUy * 1.6;
    const gap = e.vision ? 4.0 : 3.2;
    const exL = fx - gap, exR = fx + gap;
    const eyeY = fy - 1.2;
    const blink = this.blinkT >= 0 ? 0.14 : 1;
    const happy = e.pulse > 0.55 && !e.flash;

    // румянец (тепловое чувство)
    if (e.thermal) {
      ctx.fillStyle = 'rgba(238,120,145,0.32)';
      ctx.beginPath(); ctx.ellipse(fx - gap - 2.4, fy + 1.8, 2.5, 1.4, 0, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.ellipse(fx + gap + 2.4, fy + 1.8, 2.5, 1.4, 0, 0, Math.PI * 2); ctx.fill();
    }

    if (e.dissolve != null && e.dissolve > 0) {
      // уход: глаза тихо закрываются (референс «Смерть / Распад» — мирные закрытые глаза)
      const close = Math.min(1, e.dissolve * 4);
      ctx.strokeStyle = '#10180f'; ctx.lineWidth = 1.3; ctx.lineCap = 'round';
      ctx.beginPath(); ctx.arc(exL, eyeY - 0.6, 1.6, Math.PI * 0.12, Math.PI * 0.88); ctx.stroke();
      ctx.beginPath(); ctx.arc(exR, eyeY - 0.6, 1.6, Math.PI * 0.12, Math.PI * 0.88); ctx.stroke();
      if (e.chemo && close > 0.4) {
        // улыбка медленно гаснет — переворачивается вниз
        ctx.beginPath(); ctx.arc(fx, fy + 3.6, 1.9, Math.PI * 1.15, Math.PI * 1.85); ctx.stroke();
      }
    } else if (happy) {
      // довольные глаза-дуги (только что поел)
      ctx.strokeStyle = '#10180f'; ctx.lineWidth = 1.4; ctx.lineCap = 'round';
      ctx.beginPath(); ctx.arc(exL, eyeY + 0.8, 1.7, Math.PI * 1.15, Math.PI * 1.85); ctx.stroke();
      ctx.beginPath(); ctx.arc(exR, eyeY + 0.8, 1.7, Math.PI * 1.15, Math.PI * 1.85); ctx.stroke();
    } else if (e.flash) {
      // зажмурился от боли
      ctx.strokeStyle = '#10180f'; ctx.lineWidth = 1.3; ctx.lineCap = 'round';
      ctx.beginPath(); ctx.moveTo(exL - 1.5, eyeY); ctx.lineTo(exL + 1.5, eyeY); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(exR - 1.5, eyeY); ctx.lineTo(exR + 1.5, eyeY); ctx.stroke();
    } else {
      const er = e.vision ? 2.5 : 1.5;
      const eh = (e.vision ? 3.4 : 1.9) * blink;
      ctx.fillStyle = '#0e170f';
      ctx.beginPath(); ctx.ellipse(exL, eyeY, er, Math.max(0.4, eh), 0, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.ellipse(exR, eyeY, er, Math.max(0.4, eh), 0, 0, Math.PI * 2); ctx.fill();
      if (blink > 0.5) {
        if (e.vision) {
          // двойные искры в глянцевых глазах (референс)
          ctx.fillStyle = 'rgba(255,255,255,0.9)';
          ctx.beginPath(); ctx.arc(exL - 0.8, eyeY - 1.1, 0.95, 0, Math.PI * 2); ctx.fill();
          ctx.beginPath(); ctx.arc(exR - 0.8, eyeY - 1.1, 0.95, 0, Math.PI * 2); ctx.fill();
          ctx.fillStyle = 'rgba(255,255,255,0.6)';
          ctx.beginPath(); ctx.arc(exL + 0.7, eyeY + 1.0, 0.5, 0, Math.PI * 2); ctx.fill();
          ctx.beginPath(); ctx.arc(exR + 0.7, eyeY + 1.0, 0.5, 0, Math.PI * 2); ctx.fill();
        } else {
          ctx.fillStyle = 'rgba(255,255,255,0.7)';
          ctx.beginPath(); ctx.arc(exL - 0.5, eyeY - 0.5, 0.5, 0, Math.PI * 2); ctx.fill();
          ctx.beginPath(); ctx.arc(exR - 0.5, eyeY - 0.5, 0.5, 0, Math.PI * 2); ctx.fill();
        }
      }
    }

    // улыбка (химическое чувство)
    if (e.chemo) {
      if (e.flash) {
        ctx.strokeStyle = '#10180f'; ctx.lineWidth = 1.1;
        ctx.beginPath(); ctx.arc(fx, fy + 3.2, 1.3, 0, Math.PI * 2); ctx.stroke();
      } else {
        ctx.strokeStyle = '#10180f'; ctx.lineWidth = 1.1; ctx.lineCap = 'round';
        ctx.beginPath(); ctx.arc(fx, fy + 1.6, 1.9, Math.PI * 0.18, Math.PI * 0.82); ctx.stroke();
      }
    }
  }
}
