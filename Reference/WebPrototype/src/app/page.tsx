'use client';

// SERVERCLOSED — прототип «Две жизни одного слизня».
// Единственный маршрут приложения: машина состояний title → life ⇄ chess → death → judgment → reincarnate.

import { useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { Engine } from '@/game/engine';
import GameCanvas from '@/components/game/GameCanvas';
import { Hud, CreatureInfo, type PanelId } from '@/components/game/Hud';
import ChessScreen from '@/components/game/ChessScreen';
import { TitleScreen, DeathOverlay, JudgmentScreen, ReincarnationScreen } from '@/components/game/LifeScreens';
import { Toasts } from '@/components/game/Toasts';
import type { Creature } from '@/game/types';
import { birthWeights, normalizeWeights, rollRegion, REGIONS, defaultTraits } from '@/game/regions';

export default function Page() {
  const engine = useMemo(() => new Engine(), []);
  const [, force] = useReducer((x: number) => x + 1, 0);
  const [panel, setPanel] = useState<PanelId>(null);
  const [infoCreature, setInfoCreature] = useState<Creature | null>(null);
  const mounted = useRef(false);

  useEffect(() => {
    const unsub = engine.subscribe(force);
    if (!mounted.current) {
      mounted.current = true;
      engine.notify();
    }
    // отладочный хук для автоматизированных проверок (не влияет на игру)
    (window as unknown as Record<string, unknown>).__sc = engine;
    // чистые функции «Резонанса души» — для проверки детерминизма и вероятностей (п.11 Дополнения)
    (window as unknown as Record<string, unknown>).__scRegions = {
      birthWeights, normalizeWeights, rollRegion, REGIONS, defaultTraits,
    };
    return unsub;
  }, [engine]);

  // Escape закрывает панели
  useEffect(() => {
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') setPanel(null); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);

  const phase = engine.phase;
  const inWorld = phase === 'life';

  return (
    <main className="relative h-[100dvh] w-full overflow-hidden bg-[#070907] text-stone-200 select-none">
      {engine.world && (inWorld || phase === 'chess') && (
        <div className={phase === 'chess' ? 'hidden' : 'contents'}>
          <GameCanvas
            engine={engine}
            onNpcInteract={(id) => setPanel(id)}
            onTapCreatureInfo={(c) => setInfoCreature(c)}
          />
          <Hud engine={engine} panel={panel} setPanel={setPanel} />
          {infoCreature && <CreatureInfo creature={infoCreature} onClose={() => setInfoCreature(null)} />}
        </div>
      )}

      {phase === 'chess' && engine.chess && <ChessScreen engine={engine} />}
      {phase === 'title' && <TitleScreen engine={engine} />}
      {phase === 'death' && <DeathOverlay engine={engine} />}
      {phase === 'judgment' && <JudgmentScreen engine={engine} />}
      {phase === 'reincarnate' && <ReincarnationScreen engine={engine} />}

      {inWorld && <Toasts engine={engine} />}
    </main>
  );
}
