'use client';

// Слой Presentation: титульный экран, смерть, Суд души (§11.2), реинкарнация (§11.3).

import { useState } from 'react';
import type { Engine } from '@/game/engine';
import { SOUL_LEVELS } from '@/game/soul';

const SOUL_STAT_NAMES: Record<string, string> = {
  memory: 'Память', will: 'Воля', adaptation: 'Адаптация',
  perception: 'Восприятие', resonance: 'Резонанс', integrity: 'Целостность',
};

export function TitleScreen({ engine }: { engine: Engine }) {
  const [hasSave, setHasSave] = useState(() => engine.hasSaveGame());
  return (
    <div className="absolute inset-0 z-50 flex flex-col items-center justify-center overflow-y-auto bg-[radial-gradient(ellipse_at_center,#131a12_0%,#070907_70%)] p-6 text-center">
      <div className="mb-2 h-1 w-16 rounded bg-amber-700/60" />
      <h1 className="font-serif text-[1.65rem] tracking-[0.14em] text-amber-100/95 min-[420px]:text-4xl min-[420px]:tracking-[0.18em] sm:text-6xl">SERVERCLOSED</h1>
      <p className="mt-3 max-w-md font-serif text-lg italic text-stone-400">
        Прототип «Две жизни одного слизня»
      </p>
      <p className="mt-4 max-w-md text-sm leading-relaxed text-stone-500">
        Вы начнёте слабым слизнем у Леса Первого Дыхания. Поглощайте существ, вплетайте их гены,
        эволюционируйте, защитите поселение — и однажды угасните. Но смерть здесь не финал:
        душа предстанет перед Судом, а мир запомнит ваши поступки к следующему воплощению.
      </p>
      <div className="mt-8 flex w-full max-w-xs flex-col gap-2.5">
        <button
          className="rounded-lg border border-amber-600/70 bg-amber-900/50 px-4 py-3 font-serif text-lg text-amber-100 transition hover:bg-amber-800/60 active:scale-[0.99]"
          onClick={() => engine.startNewGame()}
        >
          Первая жизнь
        </button>
        {hasSave && (
          <button
            className="rounded-lg border border-stone-600/60 bg-stone-900/70 px-4 py-3 text-stone-200 transition hover:bg-stone-800/70"
            onClick={() => { if (engine.continueGame()) engine.notify(); }}
          >
            Продолжить путь
          </button>
        )}
        {hasSave && (
          <button
            className="rounded-lg border border-red-900/40 px-4 py-2 text-xs text-red-300/70 transition hover:bg-red-950/30"
            onClick={() => { engine.wipe(); setHasSave(false); }}
          >
            Стереть память мира (сброс сохранений)
          </button>
        )}
      </div>
      <div className="mt-8 max-w-md text-[11px] leading-relaxed text-stone-600">
        Управление: WASD или тап — движение · тап по существу — атака · Пробел — удар · E — поглотить ·
        на сенсорном экране — джойстик слева внизу. Игра сохраняется сама; мировое время течёт, только пока игра открыта.
      </div>
    </div>
  );
}

export function DeathOverlay({ engine }: { engine: Engine }) {
  const cause = engine.world!.data.deathCause!;
  const texts: Record<string, string> = {
    old_age: 'Тело угасло от старости. Оно честно отслужило свою жизнь.',
    killed: 'Тело разорвано хищником. Лес не прощает слабости — но душа помнит всё.',
    mutation: 'Геном дрогнул и разрушил своё тело. Эксперименты имеют цену.',
    starved: 'Силы иссякли.',
  };
  return (
    <div className="absolute inset-0 z-50 flex flex-col items-center justify-center bg-black/90 p-6 text-center">
      <div className="font-serif text-4xl text-stone-300">Жизнь окончена</div>
      <p className="mt-3 max-w-md text-sm leading-relaxed text-stone-500">{texts[cause]}</p>
      <button
        className="mt-8 rounded-lg border border-violet-700/60 bg-violet-950/50 px-6 py-3 font-serif text-lg text-violet-100 transition hover:bg-violet-900/50"
        onClick={() => engine.proceedToJudgment()}
      >
        Предстать перед Судом души
      </button>
    </div>
  );
}

export function JudgmentScreen({ engine }: { engine: Engine }) {
  const j = engine.judgment!;
  const xpInto = engine.soul.xp;
  const nextLvl = SOUL_LEVELS[engine.soul.level] ?? null;
  return (
    <div className="absolute inset-0 z-50 overflow-y-auto bg-[radial-gradient(ellipse_at_top,#1a1420_0%,#070907_70%)] p-4 sm:p-8">
      <div className="mx-auto max-w-2xl">
        <h1 className="text-center font-serif text-3xl text-violet-200">Суд души</h1>
        <p className="mt-1 text-center text-xs uppercase tracking-[0.2em] text-violet-400/60">
          воплощение №{engine.soul.life} · вердикт: {j.verdict}
        </p>
        <p className="mt-4 text-center font-serif text-[15px] italic leading-relaxed text-stone-300">{j.summary}</p>

        <div className="mt-6 grid grid-cols-2 gap-2 sm:grid-cols-4">
          <Cell label="Прожито дней" value={String(Math.floor(j.ageDays))} />
          <Cell label="Карма" value={`${j.karma > 0 ? '+' : ''}${j.karma}`} tone={j.karma >= 0 ? 'good' : 'bad'} />
          <Cell label="Духовный опыт" value={`+${j.xpGained}`} tone="good" />
          <Cell label="Уровень души" value={`${j.newSoulLevel}${j.levelUp ? ' ↑' : ''}`} tone={j.levelUp ? 'good' : undefined} />
        </div>

        <Section title="Заслуги перед вечностью">
          {j.achievements.length === 0 && <Empty text="За эту жизнь новых заслуг не засчитано." />}
          <div className="space-y-1.5">
            {j.achievements.map((a) => (
              <div key={a.id} className="rounded-lg border border-amber-800/50 bg-amber-950/20 px-3 py-2 text-xs text-amber-100/90">
                <span className="font-medium">{a.title}</span> — {a.desc} <span className="text-amber-500">(+{a.soulXp} опыта)</span>
              </div>
            ))}
          </div>
        </Section>

        {/* Резонанс души (Дополнение к ТЗ п.10): изменения характеристик + вероятности рождения */}
        {j.traitDeltas && j.traitDeltas.length > 0 && (
          <Section title="Резонанс души — что изменила эта жизнь">
            <div className="space-y-1.5">
              {j.traitDeltas.map((d) => {
                const delta = d.to - d.from;
                const isKarma = d.key === 'karma';
                const frac = isKarma ? (d.to + 100) / 200 : Math.max(0, Math.min(100, d.to)) / 100;
                return (
                  <div key={d.key} className="rounded-lg border border-violet-900/40 bg-violet-950/15 px-3 py-2">
                    <div className="flex items-center justify-between text-xs">
                      <span className="font-medium text-violet-100/90">{d.name}</span>
                      <span className="text-stone-400">
                        {d.from} <span className="text-stone-600">→</span> <span className="text-stone-200">{d.to}</span>
                        {delta !== 0 && (
                          <span className={delta > 0 ? 'ml-1 text-emerald-400' : 'ml-1 text-red-400'}>
                            ({delta > 0 ? '+' : ''}{delta})
                          </span>
                        )}
                      </span>
                    </div>
                    <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-stone-800">
                      <div
                        className={`h-full rounded-full ${isKarma ? (d.to >= 0 ? 'bg-amber-600/80' : 'bg-red-700/80') : 'bg-violet-600/80'}`}
                        style={{ width: `${Math.round(frac * 100)}%` }}
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </Section>
        )}

        {j.regionOdds && j.regionOdds.length > 0 && (
          <Section title="Куда тянет вашу душу — вероятности рождения">
            <div className="space-y-1">
              {j.regionOdds.map((o, i) => (
                <div key={o.id} className={`rounded-md border px-3 py-1.5 text-xs ${i === 0 ? 'border-amber-700/60 bg-amber-950/25 text-amber-100' : 'border-stone-800 bg-stone-900/40 text-stone-300'}`}>
                  <div className="flex items-center justify-between">
                    <span className="font-medium">{o.name}{i === 0 && <span className="ml-1.5 text-[10px] uppercase tracking-wide text-amber-400">наиболее вероятно</span>}</span>
                    <span className="text-stone-400">{o.pct}% · {o.difficulty} сложность</span>
                  </div>
                  <div className="mt-1 h-1 overflow-hidden rounded-full bg-stone-800">
                    <div className="h-full rounded-full bg-violet-500/70" style={{ width: `${o.pct}%` }} />
                  </div>
                </div>
              ))}
            </div>
            {(j.regionReasons?.length ?? 0) > 0 && (
              <div className="mt-2 space-y-0.5">
                {j.regionReasons!.map((r, i) => (
                  <p key={i} className="text-[11px] leading-snug text-stone-500">• {r}</p>
                ))}
              </div>
            )}
            <p className="mt-2 text-[10px] italic leading-snug text-stone-600">
              Вероятности — склонности, не приговор: место рождения разыгрывается в момент воплощения,
              и живой мир вносит свою поправку.
            </p>
          </Section>
        )}

        <Section title="Взвешенные поступки">
          {j.decisions.length === 0 && <Empty text="Ни добра, ни зла: тихая, незаметная жизнь." />}
          <div className="space-y-1">
            {j.decisions.map((d, i) => (
              <div key={i} className={`rounded-md border px-2.5 py-1.5 text-xs ${d.tag === 'good' ? 'border-emerald-900/50 bg-emerald-950/20 text-emerald-200/90' : d.tag === 'bad' ? 'border-red-900/50 bg-red-950/20 text-red-200/90' : 'border-stone-800 bg-stone-900/40 text-stone-300'}`}>
                <span className="mr-1.5 text-stone-500">[{d.day} дн.]</span>
                {d.title} <span className={d.karma > 0 ? 'text-emerald-400' : d.karma < 0 ? 'text-red-400' : 'text-stone-500'}>({d.karma > 0 ? '+' : ''}{d.karma})</span>
              </div>
            ))}
          </div>
        </Section>

        {j.memoriesUnlocked.length > 0 && (
          <Section title="Приоткрытые воспоминания">
            <div className="space-y-1.5">
              {j.memoriesUnlocked.map((m) => (
                <div key={m.id} className="rounded-lg border border-violet-900/50 bg-violet-950/25 px-3 py-2 text-xs text-violet-100/90">
                  <div className="font-medium">{m.title}</div>
                  <div className="mt-0.5 italic leading-snug">{m.text}</div>
                </div>
              ))}
            </div>
          </Section>
        )}

        {j.levelUp && (
          <p className="mt-4 text-center font-serif text-lg text-amber-200">
            Душа окрепла: уровень {j.newSoulLevel}. В следующем воплощении вы сохраните больше знаний
            {nextLvl != null ? ` (до следующего уровня: ${Math.max(0, nextLvl - xpInto)} опыта)` : ''}.
          </p>
        )}

        <div className="mt-8 mb-4 text-center">
          <button
            className="rounded-lg border border-amber-600/70 bg-amber-900/50 px-8 py-3 font-serif text-lg text-amber-100 transition hover:bg-amber-800/60"
            onClick={() => engine.proceedToReincarnation()}
          >
            К новому воплощению
          </button>
        </div>
      </div>
    </div>
  );
}

export function ReincarnationScreen({ engine }: { engine: Engine }) {
  const opts = engine.reincarnateOptions();
  const soul = engine.soul;
  return (
    <div className="absolute inset-0 z-50 overflow-y-auto bg-[radial-gradient(ellipse_at_bottom,#0f1a14_0%,#070907_70%)] p-4 sm:p-8">
      <div className="mx-auto max-w-2xl">
        <h1 className="text-center font-serif text-3xl text-emerald-200">Реинкарнация</h1>
        <p className="mt-2 text-center text-sm text-stone-400">
          Душа уровня {soul.level} · карма {soul.karma > 0 ? '+' : ''}{soul.karma} · опыт {soul.xp}
        </p>
        <div className="mx-auto mt-4 grid max-w-md grid-cols-3 gap-2">
          {Object.entries(soul.stats).map(([k, v]) => (
            <div key={k} className="rounded-lg border border-stone-800 bg-stone-900/60 px-2 py-1.5 text-center">
              <div className="text-[10px] uppercase tracking-wide text-stone-500">{SOUL_STAT_NAMES[k]}</div>
              <div className="text-lg text-stone-200">{v}</div>
            </div>
          ))}
        </div>

        <p className="mt-6 text-center text-xs text-stone-500">
          Мир жил без вас: посёлок менялся, зверь плодился и умирал, старики уходили. Выберите тело:
        </p>
        {engine.judgment?.regionOdds && (
          <p className="mx-auto mt-2 max-w-md text-center text-[11px] leading-snug text-violet-300/70">
            Место рождения выберет резонанс души в момент воплощения:
            {engine.judgment.regionOdds.map((o) => ` ${o.name} ${o.pct}%`).join(' ·')}
          </p>
        )}
        <div className="mt-3 space-y-2.5">
          {opts.map((o) => (
            <button
              key={o.id}
              className="w-full rounded-xl border border-stone-700/70 bg-stone-900/70 p-4 text-left transition hover:border-emerald-700/60 hover:bg-stone-800/70 active:scale-[0.99]"
              onClick={() => engine.reincarnate(o.id)}
            >
              <div className="font-serif text-lg text-emerald-100">{o.name}</div>
              <div className="mt-1 text-xs leading-relaxed text-stone-400">{o.desc}</div>
            </button>
          ))}
        </div>

        {soul.pastLives.length > 0 && (
          <>
            <h2 className="mt-8 text-center font-serif text-lg text-stone-300">Нить жизней</h2>
            <div className="mx-auto mt-2 max-w-md space-y-1">
              {[...soul.pastLives].reverse().map((pl) => (
                <div key={pl.life} className="rounded-md border border-stone-800/70 bg-stone-950/60 px-3 py-1.5 text-[11px] text-stone-400">
                  <span className="text-stone-500">№{pl.life}</span> · {pl.species === 'slug' ? 'слизень' : pl.species} · {pl.ageDays} дн. · {pl.cause} · карма {pl.karma > 0 ? '+' : ''}{pl.karma} · +{pl.xpGained} опыта
                </div>
              ))}
            </div>
          </>
        )}
        <div className="h-6" />
      </div>
    </div>
  );
}

function Cell({ label, value, tone }: { label: string; value: string; tone?: 'good' | 'bad' }) {
  return (
    <div className={`rounded-lg border px-3 py-2 text-center ${tone === 'good' ? 'border-emerald-800/60 bg-emerald-950/20' : tone === 'bad' ? 'border-red-900/60 bg-red-950/20' : 'border-stone-800 bg-stone-900/50'}`}>
      <div className="text-[10px] uppercase tracking-wide text-stone-500">{label}</div>
      <div className="font-serif text-xl text-stone-100">{value}</div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="mt-6">
      <h2 className="mb-2 border-b border-stone-800/80 pb-1 font-serif text-base text-stone-300">{title}</h2>
      {children}
    </div>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="text-xs italic text-stone-600">{text}</p>;
}
