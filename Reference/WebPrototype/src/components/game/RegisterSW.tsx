'use client';

// Регистрация service worker только на https-хостинге (превью/прод).
// localhost (dev) и file:// (офлайн-сборка SERVERCLOSED.html) не трогаем,
// чтобы не ломать разработку кешированием и не сыпать ошибками в файловой сборке.

import { useEffect } from 'react';

export default function RegisterSW() {
  useEffect(() => {
    if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return;
    if (location.protocol !== 'https:') return;
    const t = setTimeout(() => {
      navigator.serviceWorker.register('/sw.js').catch(() => {});
    }, 1500);
    return () => clearTimeout(t);
  }, []);
  return null;
}
