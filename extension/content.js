// content.js: page <-> extension bridge on https://nestzone.tech (document_start).
(function () {
  'use strict';
  var api = (typeof browser !== 'undefined' && browser) ? browser : chrome;
  var VERSION = '1.0.2';
  document.documentElement.dataset.nzDiscord = VERSION;

  var port = null;

  function connect() {
    try {
      port = api.runtime.connect({ name: 'nestzone' });
      port.onMessage.addListener(onBg);
      port.onDisconnect.addListener(function () { port = null; });
    } catch (e) { port = null; }
  }
  connect();

  // Background -> page: relay status to the page.
  function onBg(msg) {
    if (!msg || msg.type !== 'status') return;
    window.postMessage(
      { nz: 'nestzone-discord', dir: 'ext', type: 'status', id: msg.id, status: msg.status },
      location.origin
    );
  }

  function clip(v) {
    if (typeof v !== 'string') return v;
    return v.length > 300 ? v.slice(0, 300) : v;
  }
  function fin(v) { return (typeof v === 'number' && isFinite(v)) ? v : null; }

  // Validate + normalize a presence object before forwarding.
  function cleanPresence(p) {
    if (!p || typeof p !== 'object') return null;
    var out = {};
    out.kind = p.kind === 'read' ? 'read' : 'watch';
    out.tab = typeof p.tab === 'string' ? clip(p.tab) : '';
    out.url = typeof p.url === 'string' ? clip(p.url) : '';
    out.anilistId = fin(p.anilistId);
    out.malId = fin(p.malId);
    out.title = clip(typeof p.title === 'string' ? p.title : '');
    out.cover = typeof p.cover === 'string' ? clip(p.cover) : null;
    out.adult = !!p.adult;
    if (out.kind === 'watch') {
      out.episode = fin(p.episode);
      out.episodeTitle = typeof p.episodeTitle === 'string' ? clip(p.episodeTitle) : null;
      out.totalEpisodes = fin(p.totalEpisodes);
      out.position = fin(p.position);
      out.duration = fin(p.duration);
      out.paused = !!p.paused;
      out.audio = (p.audio === 'dub' || p.audio === 'hindi') ? p.audio : 'sub';
    } else {
      out.label = typeof p.label === 'string' ? clip(p.label) : 'Manga';
      out.chapter = typeof p.chapter === 'string' ? clip(p.chapter) : null;
      out.chapterTitle = typeof p.chapterTitle === 'string' ? clip(p.chapterTitle) : null;
      out.page = fin(p.page);
      out.pages = fin(p.pages);
      out.percent = fin(p.percent);
      out.totalChapters = fin(p.totalChapters);
    }
    return out;
  }

  window.addEventListener('message', function (ev) {
    if (ev.source !== window) return;
    if (ev.origin !== location.origin) return;
    var d = ev.data;
    if (!d || d.nz !== 'nestzone-discord' || d.dir !== 'page') return;
    if (!port) connect();
    if (!port) return;
    try {
      if (d.type === 'hello') {
        port.postMessage({ type: 'hello', id: typeof d.id === 'string' ? clip(d.id) : '' });
      } else if (d.type === 'presence') {
        var p = cleanPresence(d.presence);
        if (p) port.postMessage({ type: 'presence', presence: p });
      } else if (d.type === 'stop') {
        port.postMessage({ type: 'stop', tab: typeof d.tab === 'string' ? clip(d.tab) : '' });
      }
    } catch (e) { port = null; }
  }, false);
})();
