export function mount(toolbar) {
  return new SceneToolStripHandle(toolbar);
}

class SceneToolStripHandle {
  constructor(toolbar) {
    if (!(toolbar instanceof HTMLElement)) {
      throw new TypeError("The Scene toolbar host is invalid.");
    }

    this.toolbar = toolbar;
    this.abortController = new AbortController();
    toolbar.addEventListener("keydown", (event) => this.keyDown(event), {
      signal: this.abortController.signal,
    });
  }

  keyDown(event) {
    if (!(event.target instanceof HTMLElement)
        || event.target instanceof HTMLSelectElement) {
      return;
    }

    const controls = [...this.toolbar.querySelectorAll("[data-scene-tool]")]
      .filter((control) => !control.matches(":disabled"));
    const currentIndex = controls.indexOf(event.target);
    if (currentIndex < 0) {
      return;
    }

    let nextIndex;
    if (event.key === "ArrowLeft") {
      nextIndex = (currentIndex - 1 + controls.length) % controls.length;
    } else if (event.key === "ArrowRight") {
      nextIndex = (currentIndex + 1) % controls.length;
    } else if (event.key === "Home") {
      nextIndex = 0;
    } else if (event.key === "End") {
      nextIndex = controls.length - 1;
    } else {
      return;
    }

    event.preventDefault();
    for (const control of this.toolbar.querySelectorAll("[data-scene-tool]")) {
      control.tabIndex = control === controls[nextIndex] ? 0 : -1;
    }
    controls[nextIndex].focus();
  }

  destroy() {
    this.abortController.abort();
  }
}
