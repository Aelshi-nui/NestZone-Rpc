// background.js: MV3 service worker (Chrome/Edge) and Firefox background module.
import { buildActivity } from './activity.js';

const api = (typeof globalThis.browser !== 'undefined' && globalThis.browser)
  ? globalThis.browser : chrome;

const VERSION = '1.0.1';
const SITE_ORIGIN = 'https://nestzone.tech';

// Only our own content script, running in a nestzone.tech tab, may talk to us.
function fromSite(sender) {
  if (!sender || sender.id !== api.runtime.id || !sender.tab) return false;
  const url = sender.origin || sender.url || '';
  return url === SITE_ORIGIN || url.startsWith(SITE_ORIGIN + '/');
}

// Only our own popup page (an extension page, not a tab) may change settings.
function fromPopup(sender) {
  if (!sender || sender.id !== api.runtime.id || sender.tab) return false;
  const base = api.runtime.getURL('');
  return typeof sender.url === 'string' && sender.url.startsWith(base);
}
const CLIENT_ID = '1558768405585203200';
const HOST = 'tech.nestzone.discord';

// ---- state ----
const ports = new Map();        // tabId -> runtime.Port (content scripts)
const popupPorts = new Set();   // popup message channels (via runtime.onMessage)
let current = null;             // { owner, presence, since }
let lastActivityJson = null;    // last activity sent, to dedupe
let nativePort = null;
let closeTimer = null;          // close native port after presence clears
let expireTimer = null;         // clear presence if no updates
let setTimer = null;            // throttle SET
let pendingActivity = undefined; // undefined=nothing; null=clear; obj=set
let lastSetAt = 0;

let status = { version: VERSION, enabled: true, helper: 'idle', user: null };

api.storage.local.get({ enabled: true }).then((r) => {
  status.enabled = r.enabled !== false;
  broadcast();
});

function broadcast(id) {
  const msg = { type: 'status', id, status: { ...status } };
  for (const p of ports.values()) {
    try { p.postMessage(msg); } catch (e) { /* ignore */ }
  }
  notifyPopup({ nzPopup: true, status: { ...status } });
}

// Tell an open popup about the new status. In MV3 sendMessage returns a
// promise that rejects ("Receiving end does not exist") when no popup is
// open, so the rejection has to be caught here or Chrome logs an error.
function notifyPopup(msg) {
  try {
    const p = api.runtime.sendMessage(msg);
    if (p && typeof p.catch === 'function') p.catch(() => { /* no popup open */ });
  } catch (e) { /* no popup open */ }
}

function setHelper(helper, user, error) {
  const changed = status.helper !== helper || status.user !== (user || null) ||
    status.error !== error;
  status.helper = helper;
  status.user = user || null;
  if (error) status.error = error; else delete status.error;
  if (changed) broadcast();
}


// ---- native messaging host ----
function openNative() {
  if (nativePort) return nativePort;
  try {
    nativePort = api.runtime.connectNative(HOST);
  } catch (e) {
    setHelper('missing', null);
    nativePort = null;
    return null;
  }
  nativePort.onMessage.addListener(onNative);
  nativePort.onDisconnect.addListener(() => {
    const err = (api.runtime.lastError && api.runtime.lastError.message) || '';
    nativePort = null;
    lastActivityJson = null;
    if (/not found|not be found|Specified native messaging host/i.test(err)) {
      setHelper('missing', null);
    } else if (err) {
      setHelper('error', null, err);
    } else {
      setHelper('idle', null);
    }
  });
  return nativePort;
}

function onNative(msg) {
  if (!msg || typeof msg !== 'object') return;
  if (msg.type === 'status') {
    if (msg.discord === 'connected') setHelper('connected', msg.user || null);
    else if (msg.discord === 'not-running') setHelper('no-discord', null);
    else if (msg.discord === 'error') setHelper('error', null, msg.error);
  } else if (msg.type === 'error') {
    setHelper(status.helper === 'connected' ? 'connected' : 'no-discord', status.user, msg.message);
  }
}

function sendNative(obj) {
  const port = openNative();
  if (!port) return false;
  try { port.postMessage(obj); return true; }
  catch (e) { setHelper('error', null, String(e)); return false; }
}

function pingHelper() {
  if (status.helper === 'missing') { openNative(); }
  sendNative({ type: 'ping' });
}

function closeNativeSoon() {
  if (closeTimer) clearTimeout(closeTimer);
  closeTimer = setTimeout(() => {
    if (nativePort) { try { nativePort.disconnect(); } catch (e) {} nativePort = null; }
    setHelper('idle', null);
    lastActivityJson = null;
  }, 120000);
}


// ---- activity push (throttled, trailing) ----
function scheduleSet(activity) {
  pendingActivity = activity;
  if (setTimer) return;
  const wait = Math.max(0, 4000 - (Date.now() - lastSetAt));
  setTimer = setTimeout(flushSet, wait);
}

function flushSet() {
  setTimer = null;
  if (pendingActivity === undefined) return;
  const activity = pendingActivity;
  pendingActivity = undefined;
  lastSetAt = Date.now();
  if (activity === null) {
    lastActivityJson = null;
    sendNative({ type: 'clear' });
    return;
  }
  const json = JSON.stringify(activity);
  if (json === lastActivityJson) return; // Discord keeps it; no re-send
  lastActivityJson = json;
  sendNative({ type: 'set', activity, clientId: CLIENT_ID });
}

function pushCurrent() {
  if (!status.enabled || !current) { scheduleSet(null); return; }
  const presence = { ...current.presence };
  if (presence.kind === 'read') presence.since = current.since;
  const activity = buildActivity(presence, Date.now());
  if (activity) scheduleSet(activity);
}

function armExpiry() {
  if (expireTimer) clearTimeout(expireTimer);
  if (!current) return;
  const paused = current.presence.kind === 'watch' && current.presence.paused;
  const ms = paused ? 600000 : 45000;
  expireTimer = setTimeout(() => { clearPresence(); }, ms);
}

function clearPresence() {
  current = null;
  if (expireTimer) { clearTimeout(expireTimer); expireTimer = null; }
  scheduleSet(null);
  closeNativeSoon();
}


// ---- presence intake ----
function ownerKey(tabId, tab) { return tabId + ':' + (tab || ''); }

function onPresence(tabId, presence) {
  if (!status.enabled) return;
  if (closeTimer) { clearTimeout(closeTimer); closeTimer = null; }
  const key = ownerKey(tabId, presence.tab);
  let since = Date.now();
  if (current && current.presence.kind === 'read' && presence.kind === 'read' &&
      current.presence.anilistId === presence.anilistId) {
    since = current.since; // same title still being read
  }
  current = { owner: key, presence, since };
  armExpiry();
  if (!nativePort) openNative();
  pushCurrent();
}

function onStop(tabId, tab) {
  if (current && current.owner === ownerKey(tabId, tab)) clearPresence();
}

function wirePort(port) {
  if (port.name !== 'nestzone') return;
  if (!fromSite(port.sender)) { try { port.disconnect(); } catch (e) {} return; }
  const tabId = port.sender.tab.id;
  ports.set(tabId, port);
  port.onMessage.addListener((msg) => {
    if (!msg) return;
    if (msg.type === 'hello') { pingHelper(); broadcast(msg.id); }
    else if (msg.type === 'presence') onPresence(tabId, msg.presence);
    else if (msg.type === 'stop') onStop(tabId, msg.tab);
  });
  port.onDisconnect.addListener(() => {
    ports.delete(tabId);
    if (current && current.owner.startsWith(tabId + ':')) clearPresence();
  });
  // greet with current status
  try { port.postMessage({ type: 'status', status: { ...status } }); } catch (e) {}
}

api.runtime.onConnect.addListener(wirePort);

// ---- popup messaging ----
api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg || !msg.nzPopup) return;
  if (!fromPopup(sender)) return false;
  if (msg.ask === 'status') { pingHelper(); sendResponse({ status: { ...status }, current: current ? current.presence : null }); return true; }
  if (msg.ask === 'setEnabled') {
    status.enabled = !!msg.enabled;
    api.storage.local.set({ enabled: status.enabled });
    if (!status.enabled) clearPresence(); else broadcast();
    sendResponse({ status: { ...status } });
    return true;
  }
  return false;
});
