const reconnectModal = document.getElementById("components-reconnect-modal");
reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

const retryButton = document.getElementById("components-reconnect-button");
retryButton.addEventListener("click", retry);

const resumeButton = document.getElementById("components-resume-button");
resumeButton.addEventListener("click", resume);

function handleReconnectStateChanged(event) {
  switch (event.detail.state) {
    case "show":
      reconnectModal.showModal();
      break;
    case "hide":
      reconnectModal.close();
      break;
    case "failed":
      scheduleRetryWhenVisible();
      break;
    case "rejected":
      location.reload();
      break;
  }
}

async function retry() {
  document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

  try {
    // Reconnect will asynchronously return:
    // - true to mean success
    // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
    // - exception to mean we didn't reach the server (this can be sync or async)
    const successful = await Blazor.reconnect();
    if (successful) {
      return;
    }

    // We have been able to reach the server, but the circuit is no longer available.
    // We'll reload the page so the user can continue using the app as quickly as possible.
    const resumeSuccessful = await Blazor.resumeCircuit();
    if (resumeSuccessful) {
      reconnectModal.close();
      return;
    }

    location.reload();
  } catch {
    // We got an exception, server is currently unavailable
    scheduleRetryWhenVisible();
  }
}

async function resume() {
  try {
    const successful = await Blazor.resumeCircuit();
    if (!successful) {
      location.reload();
    }
  } catch {
    reconnectModal.classList.replace(
      "components-reconnect-paused",
      "components-reconnect-resume-failed",
    );
  }
}

function scheduleRetryWhenVisible() {
  document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
  document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
}

async function retryWhenDocumentBecomesVisible() {
  if (document.visibilityState === "visible") {
    await retry();
  }
}
