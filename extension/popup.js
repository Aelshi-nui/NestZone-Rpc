(function () {
  'use strict';
  var api = (typeof browser !== 'undefined' && browser) ? browser : chrome;
  var EXE_URL = 'https://github.com/Aelshi-nui/NestZone-Rpc/releases/latest/download/NestZoneDiscord.exe';

  var statusLine = document.getElementById('statusLine');
  var nowBox = document.getElementById('nowBox');
  var nowDetails = document.getElementById('nowDetails');
  var nowState = document.getElementById('nowState');
  var enabled = document.getElementById('enabled');

  function render(status, current) {
    enabled.checked = !!(status && status.enabled);
    statusLine.className = 'status';
    if (!status) { statusLine.textContent = 'Checking…'; statusLine.classList.add('muted'); return; }
    if (!status.enabled) {
      statusLine.textContent = 'Turned off.';
      statusLine.classList.add('muted');
    } else if (status.helper === 'connected') {
      statusLine.textContent = 'Connected to Discord as ' + (status.user || 'you');
      statusLine.classList.add('ok');
    } else if (status.helper === 'no-discord') {
      statusLine.textContent = 'Discord app is not open.';
      statusLine.classList.add('bad');
    } else if (status.helper === 'missing') {
      statusLine.textContent = 'Helper not installed. ';
      var link = document.createElement('a');
      link.href = EXE_URL;
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      link.textContent = 'Download the helper';
      statusLine.appendChild(link);
      statusLine.classList.add('bad');
    } else {
      statusLine.textContent = 'Not showing anything right now.';
      statusLine.classList.add('muted');
    }
    if (current && status.enabled && status.helper === 'connected') {
      var a = current.kind === 'read' ? 'Reading ' + (current.title || '') : (current.title || '');
      nowDetails.textContent = a;
      nowState.textContent = '';
      nowBox.hidden = false;
    } else {
      nowBox.hidden = true;
    }
  }

  function refresh() {
    api.runtime.sendMessage({ nzPopup: true, ask: 'status' }, function (r) {
      if (r) render(r.status, r.current);
    });
  }

  enabled.addEventListener('change', function () {
    api.runtime.sendMessage({ nzPopup: true, ask: 'setEnabled', enabled: enabled.checked }, function (r) {
      if (r) render(r.status, null);
    });
  });

  api.runtime.onMessage.addListener(function (msg) {
    if (msg && msg.nzPopup && msg.status) render(msg.status, null);
  });

  refresh();
  setInterval(refresh, 3000);
})();
