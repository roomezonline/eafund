// MiniBank PWA Service Worker (improved caching to avoid stale CSS/JS)
// Bump CACHE_NAME on each release to force clients to fetch fresh assets
const CACHE_NAME = 'minibank-static-v4';
const OFFLINE_URL = '/offline.html';
const PRECACHE_URLS = [
  '/',
  '/offline.html',
  '/manifest.webmanifest',
  '/css/site.css',
  '/js/theme.js',
  '/js/drag-scroll.js',
  '/js/normalize-digits.js',
  '/icons/icon-192.svg',
  '/icons/icon-512.svg'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(PRECACHE_URLS)).then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.map((key) => key !== CACHE_NAME ? caches.delete(key) : undefined)))
      .then(() => self.clients.claim())
  );
});

// Support immediate activation on request
self.addEventListener('message', (event) => {
  if (event && event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting();
  }
});

// Network-first for navigation requests with offline fallback
self.addEventListener('fetch', (event) => {
  const req = event.request;

  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(async () => {
        const cache = await caches.open(CACHE_NAME);
        const cached = await cache.match(OFFLINE_URL);
        return cached || new Response('Offline', { status: 503, statusText: 'Offline' });
      })
    );
    return;
  }

  // Static assets: prefer network-first to avoid stale CSS/JS after deployments
  const url = new URL(req.url);
  if (url.origin === self.location.origin) {
    const isStatic = /(\/(css|js|icons|fonts)\/|\/lib\/fontawesome\/webfonts\/|\.(woff2?|ttf|otf)$|manifest\.webmanifest$)/.test(url.pathname);
    if (isStatic) {
      event.respondWith((async () => {
        try {
          const fresh = await fetch(req);
          const cache = await caches.open(CACHE_NAME);
          cache.put(req, fresh.clone());
          return fresh;
        } catch (err) {
          const cached = await caches.match(req);
          if (cached) return cached;
          throw err;
        }
      })());
      return;
    }
  }
});
