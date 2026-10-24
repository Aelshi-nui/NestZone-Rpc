// build-extension.mjs: builds Chrome and Firefox extension zips + unpacked chrome dir.
// Node built-ins only. Zips via Windows `tar -a -c -f out.zip -C dir .`.
import { readFileSync, writeFileSync, rmSync, mkdirSync, cpSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const SRC = join(here, 'extension');
const DIST = join(here, 'dist');
const KEY_FILE = join(here, 'extension-key.pub.txt');

const FILES = ['background.js', 'activity.js', 'content.js', 'popup.html',
  'popup.js', 'popup.css'];

function stage(dir) {
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  for (const f of FILES) cpSync(join(SRC, f), join(dir, f));
  cpSync(join(SRC, 'icons'), join(dir, 'icons'), { recursive: true });
}

function baseManifest() {
  return JSON.parse(readFileSync(join(SRC, 'manifest.json'), 'utf8'));
}

function zip(dir, out) {
  rmSync(out, { force: true });
  execFileSync('tar', ['-a', '-c', '-f', out, '-C', dir, '.'], { stdio: 'inherit' });
}


function buildChrome() {
  const dir = join(DIST, 'chrome');
  stage(dir);
  const m = baseManifest();
  const key = readFileSync(KEY_FILE, 'utf8').trim();
  if (!key) throw new Error('extension-key.pub.txt is empty');
  m.key = key;
  writeFileSync(join(dir, 'manifest.json'), JSON.stringify(m, null, 2));
  zip(dir, join(DIST, 'nestzone-discord-chrome.zip'));
  return dir;
}

function buildFirefox() {
  const dir = join(DIST, 'firefox');
  stage(dir);
  const m = baseManifest();
  delete m.key;
  // Firefox: ES-module background scripts.
  m.background = { scripts: ['background.js'], type: 'module' };
  m.browser_specific_settings = {
    gecko: { id: 'discord@nestzone.tech', strict_min_version: '128.0' }
  };
  writeFileSync(join(dir, 'manifest.json'), JSON.stringify(m, null, 2));
  zip(dir, join(DIST, 'nestzone-discord-firefox.zip'));
  return dir;
}

function main() {
  mkdirSync(DIST, { recursive: true });
  const c = buildChrome();
  const f = buildFirefox();
  console.log('Chrome unpacked:', c);
  console.log('Firefox unpacked:', f);
  console.log('Wrote:', join(DIST, 'nestzone-discord-chrome.zip'));
  console.log('Wrote:', join(DIST, 'nestzone-discord-firefox.zip'));
}

main();
