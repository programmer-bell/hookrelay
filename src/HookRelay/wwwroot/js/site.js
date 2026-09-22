/* HookRelay UI micro-interactions — plain vanilla JS, no dependencies. */
(function () {
  'use strict';

  var reducedMotion =
    window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------- Theme toggle ---------- */
  var root = document.documentElement;
  var themeToggle = document.getElementById('theme-toggle');
  if (themeToggle) {
    themeToggle.addEventListener('click', function () {
      var next = root.getAttribute('data-theme') === 'light' ? 'dark' : 'light';
      root.setAttribute('data-theme', next);
      try { localStorage.setItem('hr-theme', next); } catch (e) {}
    });
  }

  /* ---------- Toast ---------- */
  var toastEl = document.getElementById('toast');
  var toastTimer = null;
  function toast(message) {
    if (!toastEl) return;
    toastEl.textContent = message;
    toastEl.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toastEl.classList.remove('show'); }, 2200);
  }

  /* ---------- Copy to clipboard ---------- */
  function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).then(function () { toast('Copied to clipboard'); });
    }
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); toast('Copied to clipboard'); } catch (e) {}
    document.body.removeChild(ta);
    return Promise.resolve();
  }

  document.addEventListener('click', function (event) {
    var copyBtn = event.target.closest('[data-copy], [data-copy-value], [data-copy-ingest]');
    if (!copyBtn) return;
    var text;
    if (copyBtn.hasAttribute('data-copy-ingest')) {
      text = location.origin + '/h/' + copyBtn.getAttribute('data-copy-ingest');
    } else if (copyBtn.hasAttribute('data-copy-value')) {
      text = copyBtn.getAttribute('data-copy-value');
    } else {
      var src = copyBtn.getAttribute('data-copy');
      var input = src ? document.querySelector(src) : null;
      text = input ? input.value : '';
    }
    if (text.length > 0) copyText(text);
  });

  /* ---------- Detail rows (collapse via delegation) ---------- */
  document.addEventListener('click', function (event) {
    var toggle = event.target.closest('[data-collapse]');
    if (!toggle) return;
    var target = document.querySelector(toggle.getAttribute('data-collapse'));
    if (!target) return;
    var isOpen = target.classList.toggle('open');
    toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
  });

  /* ---------- Reveal-on-scroll ---------- */
  var revealEls = Array.prototype.slice.call(document.querySelectorAll('.reveal'));
  if ('IntersectionObserver' in window && !reducedMotion) {
    var revealObserver = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('in');
        revealObserver.unobserve(entry.target);
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.05 });
    revealEls.forEach(function (el, index) {
      el.style.setProperty('--reveal-delay', (index * 90) + 'ms');
      revealObserver.observe(el);
    });
    if (!revealEls.length) { /* nothing */ }
  } else {
    revealEls.forEach(function (el) { el.classList.add('in'); });
  }

  /* ---------- Ambient glow parallax ---------- */
  if (!reducedMotion && 'matchMedia' in window) {
    var raf = null;
    window.addEventListener('pointermove', function (event) {
      if (raf) return;
      raf = requestAnimationFrame(function () {
        raf = null;
        var x = (event.clientX / window.innerWidth - 0.5) * 60;
        root.style.setProperty('--glow-x', x.toFixed(1) + 'px');
      });
    }, { passive: true });
  }

  /* ---------- System status chip (polls /readyz) ---------- */
  var statusEl = document.getElementById('system-status');
  var dotEl = document.getElementById('system-dot');
  function checkStatus() {
    fetch('/readyz', { cache: 'no-store' })
      .then(function (res) {
        if (!statusEl || !dotEl) return;
        if (res.ok) {
          statusEl.textContent = 'All systems operational';
          dotEl.className = 'status-dot ok';
        } else {
          statusEl.textContent = 'Degraded';
          dotEl.className = 'status-dot warn';
        }
      })
      .catch(function () {
        if (!statusEl || !dotEl) return;
        statusEl.textContent = 'Status unavailable';
        dotEl.className = 'status-dot bad';
      });
  }
  if (statusEl && dotEl) {
    checkStatus();
    setInterval(checkStatus, 45000);
  }

  /* ---------- Live feed: animate rows, sync delivery badges ---------- */
  var requestRows = document.getElementById('request-rows');
  if (requestRows) {
    var burst = 0;
    var mutantDetected = false;
    var flushBurst = null;
    var observer = new MutationObserver(function (mutations) {
      var added = [];
      mutations.forEach(function (mutation) {
        mutation.addedNodes.forEach(function (node) {
          if (node.nodeType !== 1) return;
          if (node.matches && node.matches('tr[data-request-id]')) {
            added.push(node);
          } else if (node.querySelectorAll) {
            node.querySelectorAll('tr[data-request-id]').forEach(function (row) { added.push(row); });
          }
        });
        if (mutation.removedNodes.length > 0 && mutation.type === 'childList') {
          mutantDetected = true;
        }
      });
      if (!added.length) return;

      /* Filter tab swaps replace many rows; SSE appends one at a time. */
      var many = added.length > 1;
      added.forEach(function (row, index) {
        row.classList.remove('row-flash');
        row.classList.add('row-enter');
        if (many) {
          row.style.animationDelay = Math.min(index * 40, 240) + 'ms';
        }
      });

      if (mutantDetected && !many) {
        /* A row vanished (sidebar cleanup); re-add entrance to the fresh one. */
        mutantDetected = false;
      }
      if (flushBurst) clearTimeout(flushBurst);
      flushBurst = setTimeout(function () {
        added.forEach(function (row) { row.classList.add('row-flash'); });
      }, 650);
    });
    observer.observe(requestRows, { childList: true });
  }

  document.body.addEventListener('htmx:sseMessage', function (event) {
    var detail = event.detail;
    if (!detail || !detail.data || !requestRows) return;

    if (detail.type === 'delivery-status') {
      var parsed = document.createElement('div');
      parsed.innerHTML = detail.data;
      var wrapper = parsed.querySelector('span[data-request-id]');
      if (wrapper) {
        var row = requestRows.querySelector('tr[data-request-id="' + wrapper.getAttribute('data-request-id') + '"]');
        if (row) {
          var badge = wrapper.querySelector('[data-badge]');
          var statusCell = row.querySelector('.request-status-cell');
          if (badge && statusCell) {
            statusCell.replaceChildren(badge);
          }
        }
      }
      return;
    }

    /* request-row frames: dedupe and bound the table to 50 rows. */
    if (detail.type !== 'request-row') return;
    var seen = new Set();
    requestRows.querySelectorAll('tr[data-request-id]').forEach(function (row) {
      var id = row.getAttribute('data-request-id');
      if (seen.has(id)) { row.remove(); } else { seen.add(id); }
    });
    while (requestRows.querySelectorAll('tr[data-request-id]').length > 50) {
      var last = requestRows.querySelectorAll('tr[data-request-id]');
      last[last.length - 1].remove();
    }
  });

  document.body.addEventListener('htmx:afterSwap', function () {
    document.querySelectorAll('.reveal:not(.in)').forEach(function (el) { el.classList.add('in'); });
  });
})();