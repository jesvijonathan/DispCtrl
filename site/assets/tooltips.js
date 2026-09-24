(() => {
  'use strict';

  const tip = document.createElement('div');
  tip.id = 'site-tooltip';
  tip.className = 'site-tooltip';
  tip.setAttribute('role', 'tooltip');
  tip.hidden = true;
  document.body.append(tip);

  // Short on purpose. A control whose own words already say what it does gets
  // no tooltip at all; one that repeats its label is noise.
  const hints = {
    wingetCmd: 'Click to copy',
    dlSetup: 'Latest release on GitHub',
    dlZip: 'Latest release on GitHub',
    upiInlineCopy: 'Copy UPI ID',
    upiCopy: 'Copy UPI ID',
    qr: 'Scan with any UPI app',
    previewClose: 'Esc',
    navstar: 'Star on GitHub'
  };

  function text(el) {
    if (el.dataset.tooltip !== undefined) return el.dataset.tooltip;
    if (el.id === 'theme') {
      return document.documentElement.dataset.theme === 'light' ? 'Dark theme' : 'Light theme';
    }
    if (hints[el.id]) return hints[el.id];
    if (el.classList.contains('navstar')) return hints.navstar;
    if (el.matches('[data-preview]')) return 'Click to enlarge';
    if (el.matches('a[href]')) {
      const href = el.getAttribute('href');
      if (href.startsWith('mailto:')) return href.slice(7);
      const url = new URL(el.href, location.href);
      // Where an outside link goes, which its label rarely says.
      if (url.protocol.startsWith('http') && url.origin !== location.origin) {
        return url.hostname.replace(/^www\./, '') + ' ↗';
      }
    }
    return '';
  }

  function trigger(node) {
    if (!(node instanceof Element)) return null;
    const el = node.closest('[data-tooltip], a[href], button, [data-preview]');
    if (!el || el.closest('[hidden], [inert], [aria-hidden="true"]') || el.matches(':disabled')) return null;
    return text(el) ? el : null;
  }

  let current = null, pending = null, timer = 0, warmUntil = 0;
  // Clicked or dismissed: stays quiet until the pointer leaves it.
  let dismissed = null;

  function hide() {
    clearTimeout(timer);
    timer = 0; pending = null;
    if (!current) return;
    const ids = (current.getAttribute('aria-describedby') || '').split(/\s+/).filter(id => id && id !== tip.id);
    if (ids.length) current.setAttribute('aria-describedby', ids.join(' '));
    else current.removeAttribute('aria-describedby');
    current = null;
    tip.classList.remove('on');
    tip.hidden = true;
    // Moving straight on to the next control shows its label at once.
    warmUntil = performance.now() + 300;
  }

  function place(el) {
    const r = el.getBoundingClientRect(), b = tip.getBoundingClientRect(), m = 8, gap = 6;
    const left = Math.max(m, Math.min(r.left + (r.width - b.width) / 2, innerWidth - b.width - m));
    const top = r.top - b.height - gap >= m ? r.top - b.height - gap : r.bottom + gap;
    tip.style.left = left + 'px';
    tip.style.top = Math.min(top, innerHeight - b.height - m) + 'px';
  }

  function open(el) {
    timer = 0; pending = null;
    const words = text(el);
    if (!words || !el.isConnected || !el.getClientRects().length) return;
    current = el;
    tip.textContent = words;
    tip.hidden = false;
    place(el);
    requestAnimationFrame(() => { if (current === el) tip.classList.add('on'); });
    const ids = new Set((el.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
    ids.add(tip.id);
    el.setAttribute('aria-describedby', [...ids].join(' '));
  }

  function schedule(el, delay) {
    if (el && (el === current || el === pending)) return;
    hide();
    if (!el || el === dismissed) return;
    pending = el;
    if (!delay || performance.now() < warmUntil) open(el);
    else timer = setTimeout(() => open(el), delay);
  }

  // Delegated, so gallery tabs and library rows drawn later are covered too.
  document.addEventListener('pointerover', e => {
    if (e.pointerType === 'mouse' && !e.buttons) schedule(trigger(e.target), 500);
  });
  // A scroll cancels the label; the pointer resting where it landed has had no
  // new pointerover, so the next small movement brings it back.
  document.addEventListener('pointermove', e => {
    if (!current && !pending && e.pointerType === 'mouse' && !e.buttons) schedule(trigger(e.target), 500);
  }, { passive: true });
  document.addEventListener('pointerout', e => {
    if (e.pointerType !== 'mouse') return;
    const next = trigger(e.relatedTarget);
    if (next !== dismissed) dismissed = null;
    if (!next || (next !== current && next !== pending)) hide();
  });

  let keyboard = false;
  document.addEventListener('keydown', e => {
    keyboard = true;
    if (e.key === 'Escape' && (current || pending)) {
      dismissed = current || pending;
      hide();
      e.stopImmediatePropagation();
    }
  }, true);
  document.addEventListener('pointerdown', e => { keyboard = false; dismissed = trigger(e.target); hide(); }, true);
  document.addEventListener('focusin', e => { if (keyboard) schedule(trigger(e.target), 0); });
  document.addEventListener('focusout', hide);
  document.addEventListener('click', hide, true);
  // Tabbing scrolls the focused control into view; keep its label with it.
  document.addEventListener('scroll', () => {
    if (current && current === document.activeElement && keyboard) place(current);
    else hide();
  }, { capture: true, passive: true });
  addEventListener('resize', hide);
  addEventListener('blur', hide);
})();
