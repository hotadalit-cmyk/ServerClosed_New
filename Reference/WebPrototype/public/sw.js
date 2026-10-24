/* SERVERCLOSED — service worker: офлайн-кеш для установки на телефон (PWA).
   Стратегии: навигация — сеть с офлайн-фолбэком на «/»;
   /_next/static — кеш-первый (иммутабельные чанки); остальное — сеть с обновлением кеша. */
const CACHE = 'serverclosed-v1';
const PRECACHE = [
  '/',
  '/manifest.json',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/maskable-512.png',
  '/icons/apple-touch-icon.png',
];

self.addEventListener('install', (e) => {
  e.waitUntil((async () => {
    const c = await caches.open(CACHE);
    await Promise.allSettled(PRECACHE.map((u) => c.add(u)));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)));
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (e) => {
  const req = e.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== location.origin) return;

  // Навигация: сеть, при офлайне — кешированная «/»
  if (req.mode === 'navigate') {
    e.respondWith(
      fetch(req).catch(() => caches.match('/').then((r) => r || Response.error()))
    );
    return;
  }

  // Иммутабельные чанки Next.js — кеш-первый
  if (url.pathname.startsWith('/_next/static/')) {
    e.respondWith(
      caches.match(req).then((hit) => hit || fetch(req).then((res) => {
        if (res.ok) { const cl = res.clone(); caches.open(CACHE).then((c) => c.put(req, cl)); }
        return res;
      }))
    );
    return;
  }

  // Остальное — сеть с фоновым обновлением кеша, офлайн — кеш
  e.respondWith(
    fetch(req).then((res) => {
      if (res.ok) { const cl = res.clone(); caches.open(CACHE).then((c) => c.put(req, cl)); }
      return res;
    }).catch(() => caches.match(req).then((r) => r || Response.error()))
  );
});
