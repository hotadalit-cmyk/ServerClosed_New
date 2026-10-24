'use client';

import type { Engine } from '@/game/engine';

export function Toasts({ engine }: { engine: Engine }) {
  return (
    <div className="pointer-events-none absolute inset-x-0 top-24 z-30 flex flex-col items-center gap-1.5 px-4">
      {engine.toasts.map((t) => (
        <div
          key={t.id}
          className={`max-w-md rounded-lg border px-3.5 py-2 text-center text-xs shadow-xl backdrop-blur-sm ${
            t.kind === 'epic'
              ? 'border-amber-600/60 bg-amber-950/85 text-amber-100'
              : t.kind === 'good'
                ? 'border-emerald-700/60 bg-emerald-950/85 text-emerald-100'
                : t.kind === 'bad'
                  ? 'border-red-800/60 bg-red-950/85 text-red-100'
                  : 'border-stone-700/60 bg-stone-950/85 text-stone-200'
          }`}
        >
          {t.text}
        </div>
      ))}
    </div>
  );
}
