'use client';

// Слой Presentation: HUD, панели генома/поселения/журнала, диалоги NPC.
// Мобильная схема (§13): крупные кнопки касания, минимум иконок, панели на весь экран на телефоне.

import { useEffect, useState } from 'react';
import type { Engine } from '@/game/engine';
import { GENES, SYNERGIES, nodeNextCost, canInstall } from '@/game/genes';
import { SPECIES } from '@/game/creatures';
import type { Creature, Npc } from '@/game/types';
import { DUNGEONS } from '@/game/dungeon';
import { questViews, giverLabel } from '@/game/quests';
import {
  ORGAN_INFO, ORE_CORE_THRESHOLD, nextOrganGoal, MAGIC_ENERGY_MAX, organProgress, DIET_LABELS,
  type SenseOrgan, type SensoryState,
} from '@/game/senses';
import {
  FOOD_KINDS, MODE_INFO, phaseOf, storedMassOf, capacityOf,
  type DigestMode, type StoredItem,
} from '@/game/digestion';
import { SURFACE_REGIONS, UNDERGROUND_REGIONS, DEPTH_REGIONS, worldRegionById } from '@/game/atlas';

export type PanelId = 'genome' | 'village' | 'journal' | string | null;

const btnCls =
  'inline-flex items-center justify-center gap-1.5 rounded-md border border-amber-900/50 bg-stone-900/85 px-3 py-2 text-sm text-amber-100/90 transition hover:border-amber-600/70 hover:bg-stone-800/90 active:scale-[0.98] min-h-[44px]';

export function Hud({
  engine, panel, setPanel,
}: {
  engine: Engine;
  panel: PanelId;
  setPanel: (p: PanelId) => void;
}) {
  const world = engine.world;
  if (!world) return null;
  const p = world.data.player;
  const s = world.stats;
  const v = world.data.village;
  const clock = world.clock;
  const hpFrac = Math.max(0, p.hp / Math.max(1, s.maxHp));
  const enFrac = Math.max(0, p.energy / Math.max(0.01, s.energyMax));
  const karma = engine.soul.karma + (world.data.flags.karmaTotal ?? 0);
  const path = world.karmaPath; // единый источник пути кармы (§12)
  const dun = world.dun;
  const inDun = !!(dun && dun.active);
  const ddef = inDun ? DUNGEONS[world.dungeonId!] : null;

  return (
    <>
      {/* Верхняя строка */}
      <div className="pointer-events-none absolute inset-x-0 top-0 z-20 flex items-start justify-between gap-2 p-2 sm:p-3">
        <div className="pointer-events-auto max-w-[62%] rounded-lg border border-amber-900/40 bg-stone-950/80 px-3 py-2 backdrop-blur-sm">
          <div className="font-serif text-[13px] leading-tight text-amber-100/90">{clock.label()}</div>
          <div className="mt-0.5 text-[11px] leading-tight text-stone-400">
            Воплощение №{engine.soul.life} · {s.stage} · {Math.floor(p.ageDays)} дн.
            {s.mortality > 0.02 && (
              <span className="ml-1 text-red-400">· риск угасания {Math.round(s.mortality * 100)}%/день</span>
            )}
          </div>
          {(() => {
            // «Чаша Отголосов»: где мы сейчас (мировой регион) + условия рождения этой жизни
            const rd = world.regionDef;
            const place = world.currentWorldRegionName();
            return (
              <div className="mt-0.5 text-[11px] leading-tight text-stone-400" title={`${rd.desc}\n\nПлюс: ${rd.perk}\nМинус: ${rd.minus}`}>
                Место: <span className="text-amber-100/80">{place}</span>
                <span className="text-stone-500"> · рождены: {rd.name} ({rd.difficulty})</span>
              </div>
            );
          })()}
          {(() => {
            // Сезонный бейдж (Этап 2 карты): экология дышит по календарю; подземелья — вне сезонов
            const inInst = world.data.activeDungeon != null;
            const season = world.season;
            const style = season === 'Пробуждение' ? 'border-emerald-700/60 text-emerald-300'
              : season === 'Цветение' ? 'border-lime-600/60 text-lime-300'
              : season === 'Увядание' ? 'border-amber-700/60 text-amber-400'
              : 'border-sky-800/60 text-sky-300';
            return (
              <div
                className={`mt-0.5 inline-block rounded border px-1.5 py-0.5 text-[10.5px] leading-tight ${inInst ? 'border-stone-700/60 text-stone-500' : style}`}
                title={world.seasonMods.hint}
              >
                {inInst ? `Сезон за стенами: ${season} · здесь — микроклимат` : `Сезон: ${season} — ${world.seasonMods.hint}`}
              </div>
            );
          })()}
          {inDun && ddef && (
            <div className="mt-1 text-[11px] text-emerald-300">
              {ddef.name} · глубина {dun!.depth}/{ddef.depths}
              {ddef.bossName && dun!.depth === ddef.depths && !(world.data.flags[ddef.bossFlag]) ? ` · здесь живёт ${ddef.bossName}` : ''}
            </div>
          )}
          <div className="mt-1.5 flex items-center gap-2">
            <Bar value={hpFrac} color={hpFrac > 0.35 ? '#7a9a4a' : '#c9563d'} label={`HP ${Math.ceil(p.hp)}/${s.maxHp}`} />
            <Bar value={enFrac} color="#c8a84b" label={`ЭН ${Math.floor(p.energy)}/${s.energyMax}`} />
          </div>
          {s.stability < 60 && (
            <div className={`mt-1 text-[11px] ${s.stability < 40 ? 'text-red-400' : 'text-amber-500/90'}`}>
              Стабильность генома: {s.stability}%{s.stability < 40 ? ' — вероятны мутации' : ''}
            </div>
          )}
          {(() => {
            // Дополнение к ТЗ: строка восприятия — какие органы пробуждены и что дальше (по диете)
            const sn = world.data.senses!;
            const mods = world.senseModsNow();
            const goal = nextOrganGoal(sn, mods.thr);
            return (
              <div className="mt-1 text-[11px] leading-snug text-teal-200/80">
                Чувства: {sn.unlocked.map((o) => ORGAN_INFO[o].short).join('·')}
                {world.coreActive && <span className="text-violet-300"> · Дух</span>}
                {goal && (
                  <span className="text-stone-400"> → {ORGAN_INFO[goal.organ].short} {goal.cur}/{goal.need}</span>
                )}
                {sn.focus && <span className="text-amber-300"> · фокус: {ORGAN_INFO[sn.focus].short}</span>}
                {sn.magicEnergy > 0 && (
                  <span className={sn.magicEnergy > 8 ? 'text-red-300' : 'text-violet-300'}> · энергия {sn.magicEnergy.toFixed(1)}</span>
                )}
              </div>
            );
          })()}
        </div>

        <div className="pointer-events-auto flex min-w-0 max-w-[46%] flex-col items-end gap-1.5 sm:max-w-[55%]">
          <div className="flex max-w-full flex-wrap items-center justify-end gap-1.5">
            <span className="rounded-md border border-emerald-900/50 bg-stone-950/80 px-2 py-1 text-xs text-emerald-200" title="Биомасса">
              {p.biomass} биомассы
            </span>
            {world.data.senses!.ore > 0 && (
              <span className="rounded-md border border-violet-800/60 bg-stone-950/80 px-2 py-1 text-xs text-violet-200" title="Магическая руда — отдельный счётчик усвоения (Дополнение к ТЗ п.8)">
                Руда {world.data.senses!.ore}
              </span>
            )}
            <span
              className={`rounded-md border px-2 py-1 text-xs ${karma >= 20 ? 'border-amber-700/60 bg-stone-950/80 text-amber-200' : karma <= -20 ? 'border-red-900/60 bg-stone-950/80 text-red-300' : 'border-stone-700/60 bg-stone-950/80 text-stone-300'}`}
              title={`Карма души · Путь: ${path.name} — ${path.desc}`}
            >
              Карма {karma > 0 ? '+' : ''}{karma} · {path.name}
            </span>
          </div>
          {v.raidPending && (
            <button className="animate-pulse rounded-md border border-red-800/70 bg-red-950/80 px-3 py-2 text-sm text-red-200" onClick={() => setPanel('village')}>
              ВОЛКИ У ОГРАДЫ!
            </button>
          )}
          <div className="flex flex-wrap justify-end gap-1.5">
            <button className={btnCls} onClick={() => setPanel(panel === 'map' ? null : 'map')}>Карта</button>
            <button className={btnCls} onClick={() => setPanel(panel === 'belly' ? null : 'belly')}>
              {(() => {
                // §5.5: давление содержимого — бейдж на кнопке
                const dig = world.data.digestion!;
                const cap = capacityOf(p.biomass, p.genome.slots);
                const stored = storedMassOf(dig);
                const pr = stored / Math.max(0.01, cap);
                return `Желудок${stored > 0 ? ` ${Math.round(pr * 100)}%` : ''}`;
              })()}
            </button>
            <button className={btnCls} onClick={() => setPanel(panel === 'genome' ? null : 'genome')}>Геном</button>
            <button className={btnCls} onClick={() => setPanel(panel === 'senses' ? null : 'senses')}>Чувства</button>
            <button className={btnCls} onClick={() => setPanel(panel === 'village' ? null : 'village')}>Поселение</button>
            <button className={btnCls} onClick={() => setPanel(panel === 'journal' ? null : 'journal')}>Журнал</button>
          </div>
        </div>
      </div>

      {/* Нижние кнопки действий */}
      <div className="pointer-events-none absolute inset-x-0 bottom-0 z-20 flex items-end justify-between p-3">
        <div className="pointer-events-auto rounded-md bg-stone-950/60 px-2 py-1 text-[11px] text-stone-400">
          {!world.data.senses!.unlocked.includes('vision')
            ? 'Вы слепы: осязание — единственный канал. Двигайтесь, чтобы щупать мир · [E] — поглотить'
            : 'WASD / тап — движение · тап по зверю — атака · [Пробел] — удар · [E] — поглотить'}
        </div>
        <div className="pointer-events-auto flex flex-col items-end gap-2">
          {world.nearGateId && !inDun && (
            <button className="animate-pulse rounded-full border border-emerald-500/70 bg-emerald-950/90 px-5 py-3 text-sm font-medium text-emerald-100 shadow-lg active:scale-95" onClick={() => engine.enterDungeon(world.nearGateId!)}>
              Войти: {DUNGEONS[world.nearGateId].name}
            </button>
          )}
          {inDun && world.nearStairsDown && (
            <button className="animate-pulse rounded-full border border-emerald-500/60 bg-emerald-950/85 px-5 py-3 text-sm font-medium text-emerald-100 shadow-lg active:scale-95" onClick={() => engine.descendStairs()}>
              Спуститься ниже
            </button>
          )}
          {inDun && world.nearStairsUp && (
            <button className="rounded-full border border-amber-500/60 bg-amber-950/85 px-5 py-3 text-sm font-medium text-amber-100 shadow-lg active:scale-95" onClick={() => engine.ascendStairs()}>
              {world.data.activeDungeon === 'birth_nest'
                ? (world.nestReady().ready ? 'Выйти в большой мир' : `Лаз тесен (${world.nestReady().cur}/${world.nestReady().need})`)
                : dun!.depth > 1 ? 'Подняться' : 'Выйти наверх'}
            </button>
          )}
          {inDun && world.nearCacheId != null && ddef && (
            <button
              className={`rounded-full border px-5 py-3 text-sm font-medium shadow-lg active:scale-95 ${ddef.cacheKind === 'crystal' ? 'border-cyan-500/60 bg-cyan-950/85 text-cyan-100' : 'border-lime-500/60 bg-lime-950/85 text-lime-100'}`}
              onClick={() => engine.takeCache()}
            >
              {ddef.cacheLabel}
            </button>
          )}
          {world.nearCorpseId != null && (
            <button className="rounded-full border border-emerald-500/60 bg-emerald-950/85 px-5 py-3 text-sm font-medium text-emerald-100 shadow-lg active:scale-95" onClick={() => engine.tryAbsorb()}>
              Поглотить
            </button>
          )}
          <div className="flex gap-2">
            <button
              className={`rounded-full border px-5 py-3 text-sm shadow-lg active:scale-95 ${engine.rest ? 'border-amber-500/80 bg-amber-900/85 text-amber-100' : 'border-stone-600/60 bg-stone-900/85 text-stone-200'}`}
              onClick={() => engine.setRest(!engine.rest)}
              title="Ускорить время и восстановиться (время мира пойдёт быстрее — §7)"
            >
              {engine.rest ? 'Отдых…' : 'Отдых'}
            </button>
            <button
              className="rounded-full border border-red-900/60 bg-stone-900/90 px-6 py-3 text-sm text-red-200 shadow-lg active:scale-95"
              onClick={() => engine.queueAttack()}
            >
              Атака
            </button>
          </div>
        </div>
      </div>

      {/* Панели */}
      {panel === 'map' && <WorldMapPanel engine={engine} close={() => setPanel(null)} />}
      {panel === 'belly' && <DigestionPanel engine={engine} close={() => setPanel(null)} />}
      {panel === 'genome' && <GenomePanel engine={engine} close={() => setPanel(null)} />}
      {panel === 'senses' && <SensesPanel engine={engine} close={() => setPanel(null)} />}
      {panel === 'village' && <VillagePanel engine={engine} close={() => setPanel(null)} />}
      {panel === 'journal' && <JournalPanel engine={engine} close={() => setPanel(null)} />}
      {panel && panel !== 'map' && panel !== 'belly' && panel !== 'genome' && panel !== 'senses' && panel !== 'village' && panel !== 'journal' && (
        <DialoguePanel engine={engine} npcId={panel} close={() => setPanel(null)} />
      )}
    </>
  );
}

function Bar({ value, color, label }: { value: number; color: string; label: string }) {
  return (
    <div className="w-24 sm:w-28">
      <div className="h-2.5 overflow-hidden rounded-full border border-stone-700/60 bg-stone-900">
        <div className="h-full rounded-full transition-all" style={{ width: `${Math.round(value * 100)}%`, background: color }} />
      </div>
      <div className="mt-0.5 text-[10px] text-stone-500">{label}</div>
    </div>
  );
}

// ===== «Чаша Отголосов»: карта-восприятие (Дополнение к ТЗ: карта мира, п.6) =====
// Правило п.9: карта показывает ТОЛЬКО то, что игрок ощутил — открытые регионы,
// их POI и врата. Неощущенное — тьма. Игрок всегда видит только себя.
const REGION_TINT: Record<string, string> = {
  echo_ridge: '108,122,154', ore_slopes: '127,168,160', black_ford: '138,111,90',
  root_thicket: '106,138,82', sunken_ruins: '122,134,168', forest_edge: '143,168,96',
  ash_fields: '154,144,136', warm_backwaters: '90,154,152', mud_bogs: '122,138,74',
  soul_rift: '154,106,154',
};

function WorldMapPanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const pois = world.data.pois;
  const discovered = (id: string) => world.isRegionDiscovered(id);
  const curId = world.currentWorldRegionId();
  const p = world.data.player;
  const pct = (v: number, max = 52) => `${(v / max) * 100}%`;

  const poiList: { x: number; y: number; label: string; region: string }[] = [
    { x: pois.village.pos.x, y: pois.village.pos.y, label: 'Опушка', region: 'forest_edge' },
    { x: pois.pond?.pos.x ?? 38, y: pois.pond?.pos.y ?? 36, label: 'Пруд', region: 'warm_backwaters' },
    { x: pois.mushroom_grove.pos.x, y: pois.mushroom_grove.pos.y, label: 'Грибная роща', region: 'root_thicket' },
    { x: pois.crystal_hollow.pos.x, y: pois.crystal_hollow.pos.y, label: 'Кристальная ложбинка', region: 'ore_slopes' },
    { x: pois.stone_circle.pos.x, y: pois.stone_circle.pos.y, label: 'Круг камней', region: 'black_ford' },
    { x: pois.burrow.pos.x, y: pois.burrow.pos.y, label: 'Волчьи норы', region: 'ash_fields' },
    ...(pois.rift ? [{ x: pois.rift.pos.x, y: pois.rift.pos.y, label: 'Разлом', region: 'soul_rift' }] : []),
    ...(pois.ruins ? [{ x: pois.ruins.pos.x, y: pois.ruins.pos.y, label: 'Затонувшие руины', region: 'sunken_ruins' }] : []),
    ...(pois.bogs ? [{ x: pois.bogs.pos.x, y: pois.bogs.pos.y, label: 'Илистые топи', region: 'mud_bogs' }] : []),
  ];
  const gates = Object.values(DUNGEONS).map((def) => {
    const g = def.gatePos(pois);
    return { x: g.x, y: g.y, def, region: def.id === 'catacombs' ? 'root_thicket' : def.id === 'crystal_caves' ? 'ore_slopes' : 'warm_backwaters' };
  });

  const underList = UNDERGROUND_REGIONS.map((r) => {
    const dunId = r.id === 'root_garden' ? 'catacombs' : r.id === 'forgotten_vein' ? 'crystal_caves' : r.id === 'underground_channel' ? 'underground_channel' : null;
    const dun = dunId ? world.data.dungeons?.[dunId as 'catacombs'] : null;
    return { r, known: discovered(r.id), visited: !!(dunId && (world.data.flags.dungeonEntered === 1 && dun)) };
  });

  return (
    <PanelShell title="Карта — то, что вы ощутили" close={close}>
      <div className="grid gap-4 sm:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        {/* Поверхность: открытые регионы + POI + врата + вы */}
        <div>
          <div className="relative aspect-square w-full overflow-hidden rounded-lg border border-stone-800 bg-[#0b0e0c]">
            {/* открытые регионы */}
            {SURFACE_REGIONS.map((r) => {
              if (!discovered(r.id)) return null;
              const tint = REGION_TINT[r.id] ?? '120,120,120';
              return (
                <div
                  key={r.id}
                  className="absolute -translate-x-1/2 -translate-y-1/2 rounded-full"
                  style={{
                    left: pct(r.center.x), top: pct(r.center.y),
                    width: pct(r.r * 2), height: pct(r.r * 2 * 0.58),
                    background: `radial-gradient(ellipse at center, rgba(${tint},0.30), rgba(${tint},0.05) 70%, transparent 75%)`,
                    border: `1px solid rgba(${tint},0.35)`,
                  }}
                />
              );
            })}
            {/* подписи открытых регионов */}
            {SURFACE_REGIONS.map((r) => discovered(r.id) && (
              <div
                key={`${r.id}-l`}
                className={`absolute -translate-x-1/2 -translate-y-1/2 whitespace-nowrap font-serif text-[9px] sm:text-[10px] ${r.id === curId ? 'text-amber-200' : 'text-stone-300'}`}
                style={{ left: pct(r.center.x), top: pct(r.center.y) }}
              >
                {r.name}
              </div>
            ))}
            {/* POI открытых регионов */}
            {poiList.map((poi) => discovered(poi.region) && (
              <div
                key={poi.label}
                title={poi.label}
                className="absolute h-1.5 w-1.5 -translate-x-1/2 -translate-y-1/2 rotate-45 border border-amber-200/70 bg-amber-900/80"
                style={{ left: pct(poi.x), top: pct(poi.y) }}
              />
            ))}
            {/* врата подземелий у открытых регионов */}
            {gates.map(({ x, y, def, region }) => discovered(region) && (
              <div
                key={def.id}
                title={`Врата: ${def.name}`}
                className="absolute h-2.5 w-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full border border-emerald-300/70 bg-emerald-950"
                style={{ left: pct(x), top: pct(y), boxShadow: '0 0 6px rgba(110,220,150,0.5)' }}
              />
            ))}
            {/* вы — всегда видите только себя (п.6: восприятие, не телепорт) */}
            <div
              className="absolute h-2 w-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-amber-300"
              style={{ left: pct(p.pos.x), top: pct(p.pos.y), boxShadow: '0 0 8px rgba(240,200,90,0.9)', animation: 'pulse 1.6s infinite' }}
            />
            {/* тьма по краям — мир больше того, что вы знаете */}
            <div className="pointer-events-none absolute inset-0" style={{ background: 'radial-gradient(ellipse at center, transparent 55%, rgba(4,6,5,0.55))' }} />
          </div>
          <div className="mt-1.5 text-[10px] leading-snug text-stone-500">
            Карта не существует сама по себе: мир проявляется только там, где вы побывали
            телом или душой. Неощущенное — тьма (Дополнение к ТЗ, п.6/9).
          </div>
        </div>

        {/* Подземный мир и Глубины: список */}
        <div className="min-w-0 space-y-3 text-[11px] leading-snug">
          <div>
            <div className="mb-1 font-serif text-[12px] text-emerald-200/90">Подземный мир — родина слизней</div>
            <div className="space-y-1">
              {underList.map(({ r, known }) => (
                <div key={r.id} className={`rounded border px-2 py-1 ${known ? 'border-stone-700/70 bg-stone-900/60' : 'border-stone-800/60 bg-stone-950/40'}`}>
                  {known ? (
                    <>
                      <span className="text-stone-200">{r.name}</span>
                      <span className="text-stone-500"> · {r.difficulty}</span>
                      <div className="text-[10px] text-stone-500">{r.ecology}</div>
                    </>
                  ) : (
                    <span className="text-stone-600">??? — вы там ещё не бывали</span>
                  )}
                </div>
              ))}
            </div>
          </div>
          <div>
            <div className="mb-1 font-serif text-[12px] text-violet-200/90">Глубины отголосков</div>
            {DEPTH_REGIONS.map((r) => (
              <div key={r.id} className="rounded border border-violet-900/40 bg-violet-950/20 px-2 py-1 text-violet-300/70">
                {discovered(r.id) ? r.name : 'Запечатано'} · путь лежит через Разлом забытых душ
              </div>
            ))}
          </div>
          <div className="text-[10px] text-stone-500">
            Открыто мест: {SURFACE_REGIONS.filter((r) => discovered(r.id)).length}/{SURFACE_REGIONS.length} поверхности ·
            {' '}{underList.filter((u) => u.known).length}/{underList.length} подземных
          </div>
        </div>
      </div>
    </PanelShell>
  );
}

function PanelShell({ title, children, close }: { title: string; children: React.ReactNode; close: () => void }) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [close]);
  return (
    <div className="absolute inset-0 z-30 flex items-center justify-center bg-black/60 p-2 sm:p-6" onClick={close}>
      <div
        className="flex max-h-full w-full max-w-xl flex-col overflow-hidden rounded-xl border border-amber-900/50 bg-stone-950/95 shadow-2xl backdrop-blur"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-stone-800/80 px-4 py-3">
          <h2 className="font-serif text-lg text-amber-100">{title}</h2>
          <button className="rounded-md border border-stone-700 px-2.5 py-1 text-sm text-stone-300 hover:bg-stone-800" onClick={close} aria-label="Закрыть">✕</button>
        </div>
        <div className="max-h-[75vh] overflow-y-auto px-4 py-3">{children}</div>
      </div>
    </div>
  );
}

// ===== Панель генома (§9) =====
function GenomePanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const p = world.data.player;
  const s = world.stats;
  const [, force] = useState(0);
  const rerender = () => force((x) => x + 1);
  const nextNode = nodeNextCost(p.genome.nodes);

  const catColor: Record<string, string> = {
    physical: 'text-stone-200 border-stone-600/60',
    elemental: 'text-amber-200 border-amber-700/60',
    instinct: 'text-emerald-200 border-emerald-800/60',
    spirit: 'border-violet-700/60 text-violet-200',
    crystal: 'border-teal-700/60 text-teal-200',
  };

  return (
    <PanelShell title="Геном" close={close}>
      <div className="mb-3 grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
        <Stat label="Биомасса" value={String(p.biomass)} />
        <Stat label="Стабильность" value={`${s.stability}%`} warn={s.stability < 40} />
        <Stat label="Энергия генома" value={`${s.energyUsed}/${p.genome.capacity}`} />
        <Stat label="Слоты органов" value={`${p.genome.installed.length}/${p.genome.slots}`} />
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Установленные гены</h3>
      {p.genome.installed.length === 0 && (
        <p className="mb-2 text-xs text-stone-500">Тело чисто. Поглощайте существ и вплетайте их гены.</p>
      )}
      <div className="mb-3 flex flex-wrap gap-2">
        {p.genome.installed.map((id) => {
          const g = GENES[id];
          return (
            <div key={id} className={`rounded-lg border bg-stone-900/70 px-2.5 py-1.5 text-xs ${catColor[g.category]}`}>
              <div className="font-medium">{g.name}</div>
              <button className="mt-0.5 text-[11px] text-red-300/90 hover:text-red-200" onClick={() => { engine.world!.removeGene(id); rerender(); }}>
                распустить (+{Math.round(g.cost * 0.4)} биомассы)
              </button>
            </div>
          );
        })}
      </div>

      {s.synergies.length > 0 && (
        <>
          <h3 className="mb-1.5 text-sm font-medium text-emerald-300">Действующие синергии</h3>
          <div className="mb-3 space-y-1.5">
            {s.synergies.map((sy) => (
              <div key={sy.id} className="rounded-lg border border-emerald-800/50 bg-emerald-950/30 px-3 py-2 text-xs text-emerald-100/90">
                <span className="font-medium">{sy.name}.</span> {sy.desc}
              </div>
            ))}
          </div>
        </>
      )}

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Генофонд (получено поглощением)</h3>
      {p.knownGenes.filter((id) => !p.genome.installed.includes(id)).length === 0 && (
        <p className="mb-2 text-xs text-stone-500">Пусто. Ищите жуков, светляков, волков, жаб, ткачей, спорники, духов и кристальных слизней.</p>
      )}
      <div className="mb-3 space-y-2">
        {p.knownGenes.filter((id) => !p.genome.installed.includes(id)).map((id) => {
          const g = GENES[id];
          const afford = p.biomass >= g.cost;
          // причина запрета установки (§9.2): конфликт, слоты, энергия — видно в UI, а не молчанием
          const chk = canInstall(p.genome, id);
          const blocked = !chk.ok;
          const reason = !afford ? `Не хватает биомассы: нужно ${g.cost}` : blocked ? chk.reason! : null;
          const conflict = blocked && chk.reason?.startsWith('Конфликт');
          return (
            <div key={id} className={`rounded-lg border bg-stone-900/60 px-3 py-2 ${conflict ? 'border-red-900/50' : 'border-stone-800'}`}>
              <div className="flex items-center justify-between gap-2">
                <span className={`text-xs font-medium ${catColor[g.category].split(' ')[0]}`}>{g.name}</span>
                <button
                  disabled={blocked || !afford}
                  title={reason ?? ''}
                  className={`rounded-md border px-2.5 py-1 text-[11px] ${
                    blocked
                      ? 'cursor-not-allowed border-red-900/60 text-red-300/80'
                      : afford
                        ? 'border-emerald-700/60 text-emerald-200 hover:bg-emerald-950/50'
                        : 'cursor-not-allowed border-stone-700/60 text-stone-500'
                  }`}
                  onClick={() => { if (engine.world!.installGene(id)) rerender(); }}
                >
                  {blocked ? 'Несовместимо' : 'Вплести'}
                </button>
              </div>
              <p className="mt-0.5 text-[11px] leading-snug text-stone-400">{g.desc}</p>
              <p className="mt-0.5 text-[10px] text-stone-500">
                Энергия {g.energy} · нестабильность +{g.instability} · источник: {g.source ?? '—'}
              </p>
              {reason && (
                <p className={`mt-1 text-[10px] ${conflict ? 'text-red-300/90' : 'text-amber-300/80'}`}>
                  {conflict ? '⚔ ' : '• '}{reason}
                </p>
              )}
            </div>
          );
        })}
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Эволюционные узлы</h3>
      <p className="mb-2 text-xs text-stone-400">
        Узел растит тело: +1 слот органов и +1 к энергии генома. Следующий узел: {nextNode != null ? `${nextNode} биомассы` : 'достигнут предел прототипа'}.
      </p>
      <button
        className={btnCls}
        onClick={() => { engine.world!.buyNode(); rerender(); }}
      >
        Вырастить узел {nextNode != null ? `· ${nextNode}` : ''}
      </button>

      <h3 className="mb-1.5 mt-4 text-sm font-medium text-stone-300">Все синергии (открыто {p.synDiscovered.length}/{SYNERGIES.length})</h3>
      <div className="space-y-1">
        {SYNERGIES.map((sy) => {
          const known = p.synDiscovered.includes(sy.id);
          return (
            <div key={sy.id} className={`rounded-lg border px-3 py-2 text-xs ${known ? 'border-emerald-800/50 bg-emerald-950/20 text-emerald-100/80' : 'border-stone-800/60 bg-stone-900/40 text-stone-500'}`}>
              {known ? (
                <><span className="font-medium">{sy.name}.</span> {sy.desc}</>
              ) : (
                <span>??? — требуется «{GENES[sy.genes[0]].name}» + «{GENES[sy.genes[1]].name}»</span>
              )}
            </div>
          );
        })}
      </div>
    </PanelShell>
  );
}

// ===== Панель восприятия (Дополнение к ТЗ + диета/выбор развития) =====
function SensesPanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const sn: SensoryState = world.data.senses!;
  const mods = world.senseModsNow();
  const goal = nextOrganGoal(sn, mods.thr);
  const cap = 10 + mods.overloadAdd; // ORE_OVERLOAD + Призматическое ядро

  const organs: SenseOrgan[] = ['touch', 'chemo', 'vibration', 'thermal', 'light', 'vision', 'hearing'];

  return (
    <PanelShell title="Восприятие" close={close}>
      <div className="mb-3 grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
        <Stat label="Усвоено биомассы" value={String(sn.biomassTotal)} />
        <Stat label="Биомасса тела" value={String(Math.floor(world.data.player.biomass))} />
        <Stat label="Магическая руда" value={String(sn.ore)} />
        <Stat label="Магическая энергия" value={`${sn.magicEnergy.toFixed(1)} / ${MAGIC_ENERGY_MAX}`} warn={sn.magicEnergy > cap} />
      </div>

      {sn.magicEnergy > cap && (
        <div className="mb-3 rounded-lg border border-red-800/60 bg-red-950/30 px-3 py-2 text-xs text-red-200">
          ПЕРЕГРУЗКА: энергия выше запаса ({cap}) — тело обжигает изнутри, −2 HP каждые 6 секунд.
          Энергия медленно оседает сама; Призматическое ядро повышает запас прочности.
        </div>
      )}

      <div className="mb-3 rounded-lg border border-amber-900/50 bg-amber-950/20 px-3 py-2 text-[11px] leading-relaxed text-amber-100/80">
        <b className="text-amber-200">Диета решает:</b> каждый вид пищи питает «свои» органы сильнее.
        Выберите <b>фокус</b> — и выбранный орган растёт быстрее от любой пищи. Биомассу тела можно
        вложить напрямую (кнопка «+5») — но она же тратится на гены.
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Телесные рецепторы</h3>
      <p className="mb-2 text-[11px] text-stone-500">
        Каждое усвоенное существо питает рецепторы по своему составу. Тело умрёт — рецепторы разовьются заново с нуля.
      </p>
      <div className="mb-3 max-h-72 space-y-1.5 overflow-y-auto pr-1">
        {organs.map((o) => {
          const on = sn.unlocked.includes(o);
          const isTouch = o === 'touch';
          const prog = isTouch ? null : organProgress(sn, o as Exclude<SenseOrgan, 'touch' | 'spirit'>, mods.thr);
          const frac = prog ? Math.min(100, (prog.cur / Math.max(1, prog.need)) * 100) : on ? 100 : 0;
          return (
            <div key={o} className={`rounded-lg border px-3 py-2 text-xs ${on ? 'border-teal-800/50 bg-teal-950/20 text-teal-100/90' : 'border-stone-800/60 bg-stone-900/40 text-stone-500'} ${sn.focus === o ? 'ring-1 ring-amber-500/60' : ''}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium">{ORGAN_INFO[o].name}{sn.focus === o && <span className="ml-1 text-[10px] text-amber-300">фокус ×1.75</span>}</span>
                <span className="text-[10px] uppercase tracking-wide">{on ? 'пробуждено' : prog ? `${prog.cur}/${prog.need} питания` : ''}</span>
              </div>
              {!on && prog && (
                <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-stone-800">
                  <div className="h-full rounded-full bg-teal-600/80 transition-all" style={{ width: `${frac}%` }} />
                </div>
              )}
              <p className="mt-1 leading-snug">{ORGAN_INFO[o].desc}</p>
              {!on && !isTouch && (
                <p className="mt-0.5 text-[10px] italic leading-snug text-amber-200/60">Питает: {DIET_LABELS[o]}</p>
              )}
              {!on && !isTouch && (
                <div className="mt-1.5 flex gap-1.5">
                  <button
                    className={`rounded border px-2 py-1 text-[10px] ${world.data.player.biomass >= 5 ? 'border-lime-700/60 bg-lime-950/40 text-lime-200 hover:bg-lime-900/40' : 'border-stone-800 text-stone-600'}`}
                    onClick={() => engine.investOrgan(o)}
                    disabled={world.data.player.biomass < 5}
                  >
                    +5 биомассы
                  </button>
                  <button
                    className={`rounded border px-2 py-1 text-[10px] ${sn.focus === o ? 'border-amber-600/70 bg-amber-950/50 text-amber-200' : 'border-stone-700 text-stone-400 hover:bg-stone-800/50'}`}
                    onClick={() => engine.focusOrgan(sn.focus === o ? null : o)}
                  >
                    {sn.focus === o ? 'снять фокус' : 'фокус развития'}
                  </button>
                </div>
              )}
            </div>
          );
        })}
        {goal && (
          <p className="text-[11px] text-stone-400">
            Ближайшее чувство: {ORGAN_INFO[goal.organ].name} — питания ещё {Math.max(0, Math.ceil(goal.need - goal.cur))}.
          </p>
        )}
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-violet-300">Духовное чутьё (свойство души)</h3>
      <div className={`mb-2 rounded-lg border px-3 py-2 text-xs ${world.coreActive ? 'border-violet-800/50 bg-violet-950/20 text-violet-100/90' : 'border-stone-800/60 bg-stone-900/40 text-stone-500'}`}>
        {world.coreActive ? (
          <>
            <div className="font-medium">Активно · душа жила с ядром {engine.soul.spiritMemory ?? 0}× — чутьё усиливается с каждой жизнью</div>
            <p className="mt-1 leading-snug">Вы видите следы душ, магические поля и отпечатки недавних событий — даже сквозь тьму и стены.</p>
          </>
        ) : (
          <>
            <div className="font-medium">Ядро не сформировано — чутьё недоступно</div>
            <p className="mt-1 leading-snug">
              Усвойте магическую руду: кристальные слизни, осколыши, стражи, Титан, лесные духи и кристальные жилы
              в подземельях. Прогресс: {sn.ore}/{ORE_CORE_THRESHOLD}. До ядра духовное чутьё невозможно.
            </p>
          </>
        )}
      </div>
      <p className="text-[11px] leading-relaxed text-stone-500">
        Правила души (§11): физические рецепторы принадлежат телу и после смерти развиваются заново — каждая новая
        жизнь снова начинается слепой. Резонансное ядро принадлежит душе: оно переживает смерть и открывает духовное
        чутьё сразу, но его сила растёт лишь от руды, усвоенной текущим телом.
      </p>
    </PanelShell>
  );
}

// ===== Панель пищеварения (ТЗ §5–§7) =====
// Поглощение ≠ переваривание: груз лежит в полостях и перерабатывается во времени.
function DigestionPanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const p = world.data.player;
  const dig = world.data.digestion!;
  const [, force] = useState(0);
  const rerender = () => force((x) => x + 1);
  const cap = capacityOf(p.biomass, p.genome.slots);
  const stored = storedMassOf(dig);
  const pr = stored / Math.max(0.01, cap);
  const enzyme = world.digestiveEnzyme();
  const load = 1 + pr * 0.6;

  const kindColor: Record<string, string> = {
    organic: 'border-lime-800/60 text-lime-300',
    mineral: 'border-sky-800/60 text-sky-300',
    magic: 'border-violet-800/60 text-violet-300',
    toxic: 'border-yellow-800/70 text-yellow-400',
  };

  return (
    <PanelShell title="Пищеварение — внутренняя система" close={close}>
      <div className="mb-3 grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
        <Stat label="Содержимое" value={`${stored.toFixed(1)} / ${cap.toFixed(0)}`} warn={pr > 0.85} />
        <Stat label="Давление" value={`${Math.round(pr * 100)}%`} warn={pr > 0.6} />
        <Stat label="Ферменты" value={`×${enzyme.toFixed(2)}`} />
        <Stat label="Остаток" value={dig.insoluble.toFixed(1)} />
      </div>

      {pr > 0.85 && (
        <div className="mb-3 rounded-lg border border-red-800/60 bg-red-950/30 px-3 py-2 text-xs text-red-200">
          Натяжение мембраны опасно: новые захваты заблокированы, тело движется медленнее.
          Переварите содержимое или извергните его.
        </div>
      )}

      <div className="mb-3 rounded-lg border border-amber-900/50 bg-amber-950/20 px-3 py-2 text-[11px] leading-relaxed text-amber-100/80">
        <b className="text-amber-200">Поглощение не равно перевариванию.</b> Захваченное остаётся видимым внутри
        тела, перерабатывается в полостях по времени — время растёт с твёрдостью и падает с силой ферментов
        (Время = База × Твёрдость / (Ферменты × Совместимость) × Нагрузка). Ресурсы появляются только по завершении.
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Пищеварительные полости</h3>
      <div className="mb-3 space-y-2">
        {dig.chambers.map((item, i) => (
          <ChamberCard
            key={i} idx={i} item={item} enzyme={enzyme} mode={dig.mode} load={load}
            kindColor={kindColor}
            onEgest={() => { engine.egestChamberAt(i); rerender(); }}
          />
        ))}
      </div>

      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Режим пищеварения</h3>
      <p className="mb-2 text-[11px] text-stone-500">
        Открываемые функции: ускоренный — при 25 усвоенной биомассы, бережный — при 45 (считается за текущее тело).
      </p>
      <div className="mb-3 space-y-1.5">
        {(['balanced', 'fast', 'gentle'] as DigestMode[]).map((m) => {
          const info = MODE_INFO[m];
          const unlocked = m === 'balanced' || dig.modeUnlocked.includes(m);
          const active = dig.mode === m;
          return (
            <div key={m} className={`rounded-lg border px-3 py-2 text-xs ${active ? 'border-amber-600/70 bg-amber-950/30 text-amber-100' : unlocked ? 'border-stone-800/60 bg-stone-900/40 text-stone-400' : 'border-stone-800/40 bg-stone-900/20 text-stone-600'}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium">{info.name}{active && <span className="ml-1.5 text-[10px] text-amber-300">активен</span>}</span>
                {unlocked ? (
                  !active && (
                    <button className="rounded border border-stone-700 px-2 py-0.5 text-[10px] text-stone-300 hover:bg-stone-800" onClick={() => { engine.setDigestMode(m); rerender(); }}>
                      включить
                    </button>
                  )
                ) : (
                  <span className="text-[10px]">закрыт: усвоить {info.unlockAt} биомассы</span>
                )}
              </div>
              <p className="mt-0.5 leading-snug">{info.desc}</p>
              {!unlocked && <p className="mt-0.5 text-[10px] italic text-stone-600">Тело ещё не умеет так работать.</p>}
            </div>
          );
        })}
      </div>

      {dig.insoluble > 0 && (
        <button
          className="rounded-md border border-stone-700 bg-stone-900/70 px-3 py-1.5 text-xs text-stone-300 hover:bg-stone-800"
          onClick={() => { engine.clearInsoluble(); rerender(); }}
        >
          Выбросить нерастворимый остаток ({dig.insoluble.toFixed(1)})
        </button>
      )}
    </PanelShell>
  );
}

function ChamberCard({
  idx, item, enzyme, mode, load, kindColor, onEgest,
}: {
  idx: number;
  item: StoredItem | null;
  enzyme: number;
  mode: DigestMode;
  load: number;
  kindColor: Record<string, string>;
  onEgest: () => void;
}) {
  if (!item) {
    return (
      <div className="rounded-lg border border-dashed border-stone-800 bg-stone-900/30 px-3 py-3 text-xs text-stone-600">
        Полость {idx + 1} — свободна. Пустая полость обрабатывает следующий захват.
      </div>
    );
  }
  const kind = FOOD_KINDS[item.kind];
  const phase = phaseOf(item.progress);
  // §6.4: полное время — для отображения остатка
  const total = 7 * item.hardness / (enzyme * kind.compat) * load / MODE_INFO[mode].speedMul;
  const remain = Math.max(0, (1 - item.progress) * Math.max(1.5, total));
  const frac = Math.round(item.progress * 100);
  return (
    <div className="rounded-lg border border-stone-800 bg-stone-900/60 px-3 py-2 text-xs">
      <div className="flex items-center justify-between gap-2">
        <span className={`rounded border px-1.5 py-0.5 text-[10px] ${kindColor[item.kind]}`}>{kind.name}</span>
        <span className="text-[10px] text-stone-500">полость {idx + 1} · масса {item.mass}</span>
      </div>
      <div className="mt-1 font-medium text-stone-200">{item.name}</div>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-stone-800">
        <div className={`h-full rounded-full transition-all ${item.kind === 'toxic' ? 'bg-yellow-700/80' : item.kind === 'magic' ? 'bg-violet-600/80' : item.kind === 'mineral' ? 'bg-sky-600/80' : 'bg-lime-600/80'}`} style={{ width: `${frac}%` }} />
      </div>
      <div className="mt-1 flex items-center justify-between text-[10px] text-stone-500">
        <span>Фаза {phase}/12 — {PHASE_LABEL[phase]}</span>
        <span>~{remain.toFixed(0)} сек</span>
      </div>
      {item.toxicity > 0 && (
        <p className="mt-0.5 text-[10px] italic text-yellow-500/80">Токсичность {Math.round(item.toxicity * 100)}% — возможно отравление.</p>
      )}
      <button className="mt-1.5 rounded border border-stone-700 px-2 py-0.5 text-[10px] text-stone-400 hover:bg-stone-800" onClick={onEgest}>
        вывести содержимое (−1.5 энергии)
      </button>
    </div>
  );
}

const PHASE_LABEL: Record<number, string> = {
  1: 'выделение ферментов', 2: 'пузырьки на поверхности', 3: 'контуры становятся неровными',
  4: 'частицы отделяются', 5: 'жидкость меняет цвет', 6: 'интенсивная циркуляция',
  7: 'структура разрушается', 8: 'частицы расходятся', 9: 'извлекаются компоненты',
  10: 'остаток уменьшается', 11: 'вещества входят в массу', 12: 'полость стабилизируется',
};

function Stat({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <div className={`rounded-lg border px-2.5 py-1.5 ${warn ? 'border-red-800/60 bg-red-950/20' : 'border-stone-800 bg-stone-900/60'}`}>
      <div className="text-[10px] uppercase tracking-wide text-stone-500">{label}</div>
      <div className={`text-sm font-medium ${warn ? 'text-red-300' : 'text-stone-200'}`}>{value}</div>
    </div>
  );
}

// ===== Панель поселения (§15) =====
function VillagePanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const v = world.data.village;
  return (
    <PanelShell title={`Поселение «${v.name}»`} close={close}>
      <div className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat label="Статус" value={v.stage} />
        <Stat label="Население" value={String(v.population)} />
        <Stat label="Запасы еды" value={String(Math.round(v.food))} warn={v.food < 20} />
        <Stat label="Стабильность" value={`${Math.round(v.stability)}%`} warn={v.stability < 35} />
      </div>
      {v.raidPending && (
        <div className="mb-3 rounded-lg border border-red-800/60 bg-red-950/30 px-3 py-2 text-xs text-red-200">
          Волчья стая у ограды! Поговорите с Охотником у домов, чтобы начать оборону. Если промедлить день — посёлок пострадает.
        </div>
      )}
      <h3 className="mb-1.5 text-sm font-medium text-stone-300">Жители</h3>
      <div className="space-y-1.5">
        {world.data.npcs.map((n) => (
          <div key={n.id} className={`rounded-lg border px-3 py-2 text-xs ${n.alive ? 'border-stone-800 bg-stone-900/60' : 'border-stone-800/40 bg-stone-950/60 text-stone-500'}`}>
            <div className="flex items-center justify-between">
              <span className="font-medium text-stone-200">{n.name}</span>
              <span className="text-stone-500">{n.role}{n.alive ? ` · ${Math.floor(n.ageDays / 24 * 4)} лет` : ' · покойник(ца)'}</span>
            </div>
            <div className="mt-0.5 h-1.5 overflow-hidden rounded-full bg-stone-800">
              <div className="h-full bg-amber-700/70" style={{ width: `${n.attitude}%` }} />
            </div>
            <div className="mt-0.5 text-[10px] text-stone-500">расположение к вам: {n.attitude > 65 ? 'тёплое' : n.attitude > 35 ? 'настороженное' : 'враждебное'}</div>
          </div>
        ))}
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-stone-500">
        Посёлок живёт своей жизнью даже без вас: еда родится и съедается, дети рождаются, старики уходят.
        С ростом населения Хутор становится Деревней, а Деревня — Посадом: поля кормят больше ртов.
        Ваши поступки сегодня изменят его к вашему следующему воплощению.
      </p>
    </PanelShell>
  );
}

// ===== Журнал =====
function JournalPanel({ engine, close }: { engine: Engine; close: () => void }) {
  const world = engine.world!;
  const log = [...world.data.log].reverse().slice(0, 60);
  const mems = engine.soul.memories.filter((m) => m.unlocked);
  return (
    <PanelShell title="Журнал жизни" close={close}>
      <h3 className="mb-1.5 text-sm font-medium text-amber-200">Задания посёлка</h3>
      <div className="mb-3 space-y-1.5">
        {questViews(world.data.flags).map(({ q, status, progress }) => {
          if (status === 'available') {
            return (
              <div key={q.id} className="rounded-lg border border-stone-800/60 bg-stone-900/40 px-3 py-2 text-xs text-stone-500">
                <div className="font-medium">??? — поговорите с {giverLabel(q.giver).toLowerCase()}ом</div>
              </div>
            );
          }
          const cls = status === 'done'
            ? 'border-stone-800/60 bg-stone-950/40 text-stone-500'
            : status === 'ready'
              ? 'border-amber-600/60 bg-amber-950/20 text-amber-100'
              : 'border-stone-700/60 bg-stone-900/60 text-stone-300';
          return (
            <div key={q.id} className={`rounded-lg border px-3 py-2 text-xs ${cls}`}>
              <div className="flex items-center justify-between">
                <span className="font-medium">{q.title}</span>
                <span className="text-[10px] uppercase tracking-wide text-stone-500">{giverLabel(q.giver)}</span>
              </div>
              <div className="mt-0.5 leading-snug">{q.desc}</div>
              {status === 'active' && <div className="mt-1 text-[11px] text-stone-400">Прогресс: {progress}/{q.need}</div>}
              {status === 'ready' && <div className="mt-1 text-[11px] text-amber-300">Готово к сдаче — вернитесь к жителю</div>}
              {status === 'done' && <div className="mt-1 text-[11px]">Выполнено</div>}
            </div>
          );
        })}
      </div>
      <h3 className="mb-1.5 text-sm font-medium text-violet-300">Воспоминания души</h3>
      <div className="mb-3 space-y-1.5">
        {mems.map((m) => (
          <div key={m.id} className="rounded-lg border border-violet-900/50 bg-violet-950/20 px-3 py-2 text-xs text-violet-100/85">
            <div className="font-medium">{m.title}</div>
            <div className="mt-0.5 italic leading-snug">{m.text}</div>
          </div>
        ))}
        {mems.length === 0 && <p className="text-xs text-stone-500">Душа пока не помнит ничего, кроме тепла.</p>}
      </div>
      <h3 className="mb-1.5 text-sm font-medium text-stone-300">События</h3>
      <div className="space-y-1">
        {log.map((e, i) => (
          <div key={i} className="rounded-md border border-stone-800/70 bg-stone-900/40 px-2.5 py-1.5 text-[11px] leading-snug text-stone-300">
            <span className="mr-1.5 text-stone-500">[{e.day} дн.]</span>{e.text}
          </div>
        ))}
      </div>
    </PanelShell>
  );
}

// ===== Диалог NPC (решения с последствиями — §3.2) =====
function DialoguePanel({ engine, npcId, close }: { engine: Engine; npcId: string; close: () => void }) {
  const world = engine.world!;
  const npc: Npc | undefined = world.data.npcs.find((n) => n.id === npcId);
  const [, force] = useState(0);
  if (!npc || !npc.alive) return null;
  const d = world.npcDialogue(npcId);
  const act = (opt: string) => {
    if (opt === 'leave') { close(); return; }
    if (opt === 'start_raid') {
      close();
      engine.openChess();
      return;
    }
    world.npcAction(npcId, opt);
    force((x) => x + 1);
  };
  return (
    <PanelShell title={d.name} close={close}>
      <p className="mb-1 text-[11px] uppercase tracking-wide text-stone-500">{d.role} · расположение: {npc.attitude}</p>
      <p className="mb-3 font-serif text-[15px] leading-relaxed text-stone-200">{d.text}</p>
      <div className="flex flex-col gap-1.5">
        {d.options.map((o) => (
          <button key={o.id} className={btnCls + ' justify-start text-left'} onClick={() => act(o.id)}>
            {o.label}
          </button>
        ))}
      </div>
    </PanelShell>
  );
}

/** Всплывающая карточка существа по тапу */
export function CreatureInfo({ creature, onClose }: { creature: Creature; onClose: () => void }) {
  const def = SPECIES[creature.species];
  return (
    <div className="absolute bottom-24 left-1/2 z-20 w-64 -translate-x-1/2 rounded-lg border border-stone-700/60 bg-stone-950/90 px-3 py-2 text-xs text-stone-200 shadow-xl">
      <div className="flex items-center justify-between">
        <span className="font-medium">{creature.name ?? def.name}</span>
        <button className="text-stone-500 hover:text-stone-300" onClick={onClose} aria-label="Закрыть">✕</button>
      </div>
      <div className="mt-0.5 text-stone-400">
        HP {Math.ceil(creature.hp)}/{def.base.maxHp} · атака {def.base.atk} · броня {def.base.def}
      </div>
      <div className="text-stone-500">
        {def.sentient ? 'Разумное существо — убийство и поглощение отягчат карму' : 'Неразумное'} · биомасса {def.biomass[0]}–{def.biomass[1]}
        {def.geneDrop.length > 0 && creature.gen > 1 ? ` · поколение ${creature.gen}` : ''}
      </div>
    </div>
  );
}
