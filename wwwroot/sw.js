const CACHE_NAME = 'kanban-v1';
const STATIC_ASSETS = [
    '/',
    '/css/site.css',
    '/js/board.js',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js',
    '/icons/icon.svg',
    '/manifest.json'
];

// Install — cache static shell
self.addEventListener('install', e => {
    e.waitUntil(
        caches.open(CACHE_NAME).then(cache => cache.addAll(STATIC_ASSETS))
    );
    self.skipWaiting();
});

// Activate — clean old caches
self.addEventListener('activate', e => {
    e.waitUntil(
        caches.keys().then(keys =>
            Promise.all(keys.filter(k => k !== CACHE_NAME).map(k => caches.delete(k)))
        )
    );
    self.clients.claim();
});

// Fetch — network-first for API, cache-first for static assets
self.addEventListener('fetch', e => {
    const url = new URL(e.request.url);

    // API calls: always go to network (data must be fresh)
    if (url.pathname.startsWith('/api/')) {
        e.respondWith(fetch(e.request));
        return;
    }

    // Static assets: cache-first, fallback to network
    e.respondWith(
        caches.match(e.request).then(cached => {
            const fetched = fetch(e.request).then(response => {
                if (response.ok) {
                    const clone = response.clone();
                    caches.open(CACHE_NAME).then(cache => cache.put(e.request, clone));
                }
                return response;
            }).catch(() => cached);

            return cached || fetched;
        })
    );
});
