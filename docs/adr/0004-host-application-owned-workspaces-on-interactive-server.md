---
status: accepted
---

# Host Application-owned Workspaces on Interactive Server

Logic Lab uses a Blazor Web App with Static SSR pages and a per-page Interactive Server editor. Application owns each Editor Workspace, one controlling attachment, zero-or-one active Simulation Session, background operations, detach recovery, and typed CPU work lanes. The browser owns frame-rate scene and waveform interaction through collocated JavaScript; a Blazor circuit observes a Workspace but does not own it.

WebAssembly or Auto would add a `.Client` project, runtime download, duplicated DI, and an HTTP data layer while removing direct access to server Modules. Circuit-owned state would be simpler but would lose explicit fencing and operation continuity across reconnect. Server authority is therefore the clean V1 seam; custom SignalR, Web Workers, or worker processes remain later adapters justified by measurement.
