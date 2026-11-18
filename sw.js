/* Service worker de Maths 4e : rend l'application utilisable hors connexion.
   Les pages sont toujours rechargées depuis le réseau quand il y en a.
   Augmenter VERSION seulement si les icônes ou le manifeste changent. */
const VERSION = "maths4e-v4";
const SHELL = [
  "./",
  "./index.html",
  "./config.js",
  "./manifest.webmanifest",
  "./icons/icon-192.png",
  "./icons/icon-512.png",
  "./icons/icon-maskable-512.png",
  "./icons/apple-touch-icon.png",
  "./icons/icon.svg"
];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);

  // Pages et config.js : réseau d'abord (pour avoir la dernière version), cache si hors ligne.
  // Chaque page est mise en cache sous sa propre adresse (prof.html ne remplace pas l'application élève).
  const sameOrigin = url.origin === self.location.origin;
  if (req.mode === "navigate" || (sameOrigin && url.pathname.endsWith("/config.js"))) {
    const cle = new URL(url.pathname.endsWith("/") ? url.pathname + "index.html" : url.pathname, url.origin).href;
    e.respondWith(
      fetch(req)
        .then((res) => {
          if (res.ok) { const copy = res.clone(); caches.open(VERSION).then((c) => c.put(cle, copy)); }
          return res;
        })
        .catch(() => caches.match(cle).then((hit) => hit || caches.match("./index.html")))
    );
    return;
  }

  // Polices Google et fichiers de l'application : cache d'abord, mise à jour en arrière-plan
  const isFont = url.hostname === "fonts.googleapis.com" || url.hostname === "fonts.gstatic.com";
  if (isFont || sameOrigin) {
    e.respondWith(
      caches.open(VERSION).then((c) =>
        c.match(req).then((hit) => {
          const net = fetch(req).then((res) => { if (res.ok || res.type === "opaque") c.put(req, res.clone()); return res; }).catch(() => hit);
          return hit || net;
        })
      )
    );
  }
});
