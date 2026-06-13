export function requestPermission() {
  if ("Notification" in window && Notification.permission === "default") {
    Notification.requestPermission();
  }
}

export function notify(title, body) {
  if ("Notification" in window && Notification.permission === "granted") {
    new Notification(title, { body });
  }
}

// Positions the safety breakdown card with viewport-clamped fixed positioning so it can never
// clip off the top or bottom of the screen, regardless of which row or scroll position. Pure-CSS
// hover still controls visibility; this only sets coordinates on hover. Delegated, so it survives
// Blazor re-renders.
let _cardInited = false;
export function initSafetyCards() {
  if (_cardInited) return;
  _cardInited = true;
  const PAD = 8, GAP = 12;
  document.addEventListener("mouseover", (e) => {
    const cell = e.target.closest && e.target.closest(".cell-safety");
    if (!cell) return;
    const card = cell.querySelector(".tt-pop.card");
    if (!card) return;
    const r = cell.getBoundingClientRect();
    const ch = card.offsetHeight || 480;
    const cw = card.offsetWidth || 340;
    // Prefer opening to the left of the cell; fall back to the right if there's no room.
    let left = r.left - cw - GAP;
    if (left < PAD) left = Math.min(r.right + GAP, window.innerWidth - cw - PAD);
    // Centre on the row, then clamp fully inside the viewport.
    let top = r.top + r.height / 2 - ch / 2;
    top = Math.max(PAD, Math.min(top, window.innerHeight - ch - PAD));
    card.style.position = "fixed";
    card.style.left = left + "px";
    card.style.top = top + "px";
    card.style.right = "auto";
    card.style.bottom = "auto";
    card.style.transform = "none";
    card.style.margin = "0";
  });
}

// Per-device persistence of sort/filter/bankroll preferences.
export function savePrefs(p) {
  try { localStorage.setItem("arbPrefs", JSON.stringify(p)); } catch { /* storage blocked */ }
}
export function loadPrefs() {
  try { const s = localStorage.getItem("arbPrefs"); return s ? JSON.parse(s) : null; }
  catch { return null; }
}

// Returns whether the "how to use" panel has been seen before, then marks it seen.
// First-time visitors get it expanded; afterwards it stays collapsed.
export function seenHowto() {
  let seen = false;
  try {
    seen = localStorage.getItem("howtoSeen") === "1";
    localStorage.setItem("howtoSeen", "1");
  } catch { /* storage blocked — just show it */ }
  return seen;
}
