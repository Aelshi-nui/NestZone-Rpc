// activity.js: pure Discord IPC activity builder (ES module).
// buildActivity(presence, now) -> raw snake_case activity object, or null.

const SMALL_IMAGE = 'https://nestzone.tech/icon-192.png';
const SITE = 'https://nestzone.tech/';

// The "Watch/Read on NestZone" button may only point at nestzone.tech, so a
// script injected into the site cannot turn the button into a phishing link.
function siteUrl(value) {
  const u = goodUrl(value);
  return u && u.startsWith(SITE) ? u : null;
}

// AniList links are built from a positive whole-number id only.
function anilistUrl(kind, id) {
  if (typeof id !== 'number' || !Number.isInteger(id) || id <= 0 || id > 1e9) return null;
  return 'https://anilist.co/' + kind + '/' + id;
}

// Clip text to Discord's 2..128 char window. Pad short strings, cut long ones.
function clipText(value) {
  let s = typeof value === 'string' ? value : '';
  s = s.trim();
  if (!s) return null;
  if (s.length > 128) s = s.slice(0, 127) + '\u2026';
  while (s.length < 2) s = s + ' ';
  return s;
}

function clipLabel(value) {
  let s = typeof value === 'string' ? value.trim() : '';
  if (!s) return null;
  if (s.length > 32) s = s.slice(0, 31) + '\u2026';
  return s;
}

function goodUrl(value) {
  if (typeof value !== 'string') return null;
  const s = value.trim();
  if (!s.startsWith('https://')) return null;
  if (s.length > 512) return null;
  return s;
}

function makeButton(label, url) {
  const l = clipLabel(label);
  const u = goodUrl(url);
  if (!l || !u) return null;
  return { label: l, url: u };
}

function num(v) {
  return typeof v === 'number' && isFinite(v) ? v : null;
}


function buildWatch(p, now) {
  const anilist = anilistUrl('anime', p.anilistId);
  const cover = goodUrl(p.cover);
  const ep = 'Episode ' + p.episode;
  const total = num(p.totalEpisodes);
  const of = total ? 'Episode ' + p.episode + ' of ' + total : ep;
  let line = of;
  if (p.episodeTitle && !/^episode\s+\d+$/i.test(p.episodeTitle)) {
    line = 'Episode ' + p.episode + ': ' + p.episodeTitle;
  }
  let audio = '';
  if (p.audio === 'dub') audio = 'English dub';
  else if (p.audio === 'hindi') audio = 'Hindi dub';
  const state = audio ? line + ' \u00b7 ' + audio : line;

  const assets = { small_image: SMALL_IMAGE, small_text: 'NestZone' };
  if (!p.adult && cover) {
    assets.large_image = cover;
    assets.large_text = audio ? of + ' \u00b7 ' + audio : of;
    if (anilist) assets.large_url = anilist;
  } else {
    assets.large_image = SMALL_IMAGE;
    assets.large_text = audio ? of + ' \u00b7 ' + audio : of;
  }

  const activity = {
    type: 3,
    status_display_type: 2,
    details: clipText(p.title),
    state: clipText(state),
    assets,
    instance: false
  };
  if (anilist) activity.details_url = anilist;

  const dur = num(p.duration);
  const pos = num(p.position);
  if (!p.paused && dur && dur > 0) {
    const start = Math.round(now - (pos || 0) * 1000);
    activity.timestamps = { start, end: start + Math.round(dur * 1000) };
  }

  const buttons = [];
  const b1 = makeButton('Watch now', siteUrl(p.url));
  const b2 = makeButton('NestZone', SITE);
  if (b1) buttons.push(b1);
  if (b2) buttons.push(b2);
  if (buttons.length) activity.buttons = buttons.slice(0, 2);
  return activity;
}


function buildRead(p) {
  const anilist = anilistUrl('manga', p.anilistId);
  const cover = goodUrl(p.cover);
  const label = (typeof p.label === 'string' && p.label.trim()) ? p.label.trim() : 'Manga';
  const ch = p.chapter ? 'Chapter ' + p.chapter : null;
  let named = null;
  if (p.chapterTitle) {
    named = p.chapterTitle.replace(
      /^(?:chapter|ch\.?)\s*[\d.]+\s*[:.\-\u2013\u2014]?\s*/i, ''
    ).trim();
    if (!named) named = null;
  }
  let line;
  if (ch) line = named ? ch + ': ' + named : ch;
  else line = p.chapterTitle || label;

  const page = num(p.page);
  const pages = num(p.pages);
  let where = null;
  if (page && pages) where = 'Page ' + page + ' of ' + pages;
  else if (p.percent !== null && p.percent !== undefined && num(p.percent) !== null) {
    where = p.percent + '% read';
  }
  const state = where ? line + ' \u00b7 ' + where : line;

  const total = num(p.totalChapters);
  const of = (ch && total) ? ch + ' of ' + total : ch;

  const assets = { small_image: SMALL_IMAGE, small_text: 'NestZone' };
  if (!p.adult && cover) {
    assets.large_image = cover;
    if (anilist) assets.large_url = anilist;
  } else {
    assets.large_image = SMALL_IMAGE;
  }
  assets.large_text = of ? label + ' \u00b7 ' + of : label;

  const activity = {
    type: 0,
    status_display_type: 2,
    details: clipText('Reading ' + p.title),
    state: clipText(state),
    assets,
    instance: false
  };
  if (anilist) activity.details_url = anilist;

  const since = num(p.since);
  if (since) activity.timestamps = { start: Math.round(since) };

  const buttons = [];
  const b1 = makeButton('Read now', siteUrl(p.url));
  const b2 = makeButton('NestZone', SITE);
  if (b1) buttons.push(b1);
  if (b2) buttons.push(b2);
  if (buttons.length) activity.buttons = buttons.slice(0, 2);
  return activity;
}

export function buildActivity(presence, now) {
  if (!presence || typeof presence !== 'object') return null;
  const t = typeof now === 'number' && isFinite(now) ? now : Date.now();
  if (presence.kind === 'read') return buildRead(presence);
  return buildWatch(presence, t);
}
