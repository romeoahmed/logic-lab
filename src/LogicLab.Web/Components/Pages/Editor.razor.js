const attachmentStorageKey = "logiclab.workspace-attachment";

export function readHistoryEntryState(localUrl) {
  const userState = history.state?.userState;
  if (typeof userState === "string") {
    return userState;
  }

  try {
    const stored = JSON.parse(sessionStorage.getItem(attachmentStorageKey));
    return stored?.localUrl === localUrl && typeof stored.userState === "string"
      ? stored.userState
      : null;
  } catch {
    return null;
  }
}

export function replaceHistoryEntry(localUrl, attachmentFence) {
  const currentState = history.state;
  const nextState =
    currentState !== null && typeof currentState === "object"
      ? { ...currentState, userState: attachmentFence }
      : { _index: 0, userState: attachmentFence };

  history.replaceState(nextState, "", localUrl);

  try {
    sessionStorage.setItem(
      attachmentStorageKey,
      JSON.stringify({ localUrl, userState: attachmentFence }),
    );
  } catch {
    // The history entry remains sufficient for ordinary reloads when storage is unavailable.
  }

  const cultureReturnUrl = document.querySelector('[data-culture-form] input[name="returnUrl"]');
  if (cultureReturnUrl instanceof HTMLInputElement) {
    cultureReturnUrl.value = `${location.pathname}${location.search}${location.hash}`;
  }
}
