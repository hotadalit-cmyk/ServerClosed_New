'use client';

// Слой Presentation: экран Magic Chess (ТЗ §14, §20).
// Расстановка — время мира остановлено; бой — автобой тиками; результат меняет мир.

import { useEffect } from 'react';
import type { Engine } from '@/game/engine';
import { CHESS_ENEMY_UI } from '@/game/chess';

const ROLE_COLOR: Record<string, string> = {
  'Защитник': 'border-stone-500/60 bg-stone-800/80 text-stone-100',
  'Боец': 'border-red-800/60 bg-red-950/60 text-red-100',
  'Стрелок': 'border-emerald-800/60 bg-emerald-950/50 text-emerald-100',
  'Маг': 'border-violet-800/60 bg-violet-950/50 text-violet-100',
  'Поддержка': 'border-amber-800/60 bg-amber-950/40 text-amber-100',
  'Командир': 'border-amber-600/70 bg-amber-900/50 text-amber-100',
};

export default function ChessScreen({ engine }: { engine: Engine }) {
  const st = engine.chess!;

  // тик автобоя — движок вызывается из этого экрана, пока открыт бой
  useEffect(() => {
    let raf = 0;
    let last = performance.now();
    const loop = (now: number) => {
      const dt = Math.min(0.05, (now - last) / 1000);
      last = now;
      engine.update(dt);
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, [engine]);

  const canPlaceMore = st.placed.length < st.reserveLimit;

  return (
    <div className="absolute inset-0 z-40 flex flex-col bg-stone-950/95">
      <div className="border-b border-stone-800 px-4 py-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <div>
            <h2 className="font-serif text-lg text-amber-100">{st.scenario}</h2>
            <p className="text-xs text-stone-400">{st.stake}</p>
          </div>
          <div className="text-xs text-stone-400">
            Юнитов: {st.placed.length}/{st.reserveLimit} · время мира: {st.phase === 'setup' ? 'остановлено' : `+${Math.round(st.worldMinutesSpent)} мин`}
          </div>
        </div>
      </div>

      <div className="flex min-h-0 flex-1 flex-col-reverse items-center justify-center gap-3 overflow-y-auto p-3 lg:flex-row lg:items-start lg:justify-center">
        {/* Поле */}
        <div className="shrink-0">
          <div
            className="grid gap-[3px] rounded-lg border border-amber-900/40 bg-stone-900/60 p-2"
            style={{ gridTemplateColumns: `repeat(${st.boardC}, minmax(0, 1fr))`, width: 'min(92vw, 560px)' }}
          >
            {Array.from({ length: st.boardR * st.boardC }).map((_, i) => {
              const r = Math.floor(i / st.boardC);
              const c = i % st.boardC;
              const unit = [...st.placed, ...st.enemies].find((u) => u.alive && u.r === r && u.c === c);
              const isEnemyZone = r < 2;
              const isPlayerZone = r >= 3;
              return (
                <button
                  key={i}
                  disabled={st.phase !== 'setup' || !isPlayerZone}
                  onClick={() => {
                    const def = st.playerRoster.find((d) => !st.placed.some((u) => u.defId === d.id));
                    if (def && canPlaceMore) engine.placeChessUnit(def.id, r, c);
                  }}
                  className={`relative aspect-square rounded border text-[9px] leading-none transition sm:text-[11px] ${
                    isPlayerZone ? 'border-emerald-900/50 bg-emerald-950/30 hover:bg-emerald-900/40' : isEnemyZone ? 'border-red-950/60 bg-red-950/20' : 'border-stone-800 bg-stone-900/40'
                  }`}
                >
                  {unit && (
                    <span
                      className={`absolute inset-0.5 flex flex-col items-center justify-center overflow-hidden rounded ${unit.side === 'player' ? 'border bg-stone-800/90' : 'border border-red-900/70 bg-red-950/80'} ${unit.side === 'player' ? ROLE_COLOR[st.playerRoster.find((d) => d.id === unit.defId)?.role ?? 'Боец'] : 'text-red-200'}`}
                    >
                      <span className="truncate px-0.5 font-medium">
                        {unit.side === 'player'
                          ? st.playerRoster.find((d) => d.id === unit.defId)?.name ?? '?'
                          : CHESS_ENEMY_UI[unit.defId]?.short ?? 'Волк'}
                      </span>
                      <span className="mt-0.5 h-1 w-[80%] overflow-hidden rounded-full bg-black/60">
                        <span
                          className="block h-full"
                          style={{
                            width: `${Math.max(0, Math.min(100, (unit.hp / (unit.side === 'player' ? st.playerRoster.find((d) => d.id === unit.defId)!.hp : CHESS_ENEMY_UI[unit.defId]?.hp ?? 50)) * 100))}%`,
                            background: unit.side === 'player' ? '#7a9a4a' : '#c9563d',
                          }}
                        />
                      </span>
                    </span>
                  )}
                </button>
              );
            })}
          </div>
          <p className="mt-1 text-center text-[11px] text-stone-500">
            {st.phase === 'setup'
              ? 'Выберите юнита справа и коснитесь зелёной клетки (ваши ряды — нижние два).'
              : 'Бой идёт автоматически. Вмешиваться нельзя — командуйте до боя.'}
          </p>
        </div>

        {/* Состав и журнал */}
        <div className="w-full max-w-sm shrink-0 space-y-2">
          {st.phase === 'setup' && (
            <div className="rounded-lg border border-stone-800 bg-stone-900/70 p-2.5">
              <h3 className="mb-1.5 text-sm font-medium text-stone-200">Рота обороны</h3>
              <div className="space-y-1.5">
                {st.playerRoster.map((d) => {
                  const placed = st.placed.some((u) => u.defId === d.id);
                  return (
                    <div key={d.id} className={`rounded-md border px-2.5 py-1.5 text-xs ${placed ? 'border-emerald-800/50 bg-emerald-950/20' : 'border-stone-700/60 bg-stone-900/60'}`}>
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-medium text-stone-100">{d.name}</span>
                        <span className="text-[10px] text-stone-400">{d.role}</span>
                      </div>
                      <div className="text-[10px] text-stone-400">HP {d.hp} · атака {d.atk} · броня {d.def} · дальность {d.range} · {d.synergies.join(', ')}</div>
                      <div className="mt-0.5 text-[10px] italic text-stone-500">{d.trait}</div>
                      {placed && (
                        <button className="mt-1 text-[10px] text-red-300/90 hover:text-red-200" onClick={() => engine.removeChessUnit(st.placed.find((u) => u.defId === d.id)!.uid)}>
                          убрать с поля
                        </button>
                      )}
                    </div>
                  );
                })}
              </div>
              <button
                className="mt-2 w-full rounded-md border border-amber-600/70 bg-amber-900/60 px-3 py-2.5 text-sm font-medium text-amber-100 hover:bg-amber-800/60 disabled:opacity-40"
                disabled={st.placed.length === 0}
                onClick={() => engine.startChessBattle()}
              >
                Трубить сбор (начать бой)
              </button>
            </div>
          )}

          {st.phase === 'battle' && (
            <div className="rounded-lg border border-stone-800 bg-stone-900/70 p-2.5">
              <h3 className="mb-1 text-sm font-medium text-stone-200">Ход сражения</h3>
              <div className="max-h-44 space-y-1 overflow-y-auto">
                {[...st.battleLog].reverse().slice(0, 10).map((l, i) => (
                  <div key={i} className="rounded bg-stone-950/70 px-2 py-1 text-[11px] text-stone-300">{l}</div>
                ))}
              </div>
            </div>
          )}

          {st.phase === 'result' && (
            <div className={`rounded-lg border p-3 text-center ${st.result === 'win' ? 'border-emerald-700/60 bg-emerald-950/40' : 'border-red-800/60 bg-red-950/40'}`}>
              <div className="font-serif text-xl text-stone-100">{st.result === 'win' ? 'Стая отбита!' : 'Оборона пала…'}</div>
              <p className="mt-1 text-xs text-stone-300">
                {st.result === 'win'
                  ? 'Посёлок спасён. Ополченцы запомнят командира-слизня. Волчьи ряды поредели — их биомасса растворилась в лесу.'
                  : 'Волки ушли с добычей. Посёлок осиротел, но жив. Ваши союзники пали — их копии рассеялись.'}
              </p>
              <p className="mt-1 text-[11px] text-stone-500">В мире прошло {Math.round(st.worldMinutesSpent)} минут (§7: бой съедает малый интервал времени).</p>
              <button
                className="mt-2 w-full rounded-md border border-amber-600/70 bg-amber-900/60 px-3 py-2.5 text-sm text-amber-100 hover:bg-amber-800/60"
                onClick={() => engine.closeChess()}
              >
                Вернуться в мир
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
