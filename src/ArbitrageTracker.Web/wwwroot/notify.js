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
