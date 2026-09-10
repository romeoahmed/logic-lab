const decoder = new TextDecoder("utf-8", { fatal: true });
export const interopEnvelopeBytes = 512;

/** Stages bounded binary candidates until their digest and complete JSON are verified. */
export class CandidateTransfers {
  #pending = new Map();

  constructor(policy, kinds) {
    this.maximumBytes = policy.candidateTransferBytes;
    this.maximumChunkBytes = policy.interopBatchBytes - interopEnvelopeBytes;
    this.kinds = kinds;
  }

  begin(id, kind, byteLength, digest) {
    if (
      typeof id !== "string" ||
      !/^[A-Za-z0-9._-]+$/.test(id) ||
      !this.kinds.includes(kind) ||
      !Number.isSafeInteger(byteLength) ||
      byteLength <= 0 ||
      byteLength > this.maximumBytes ||
      typeof digest !== "string" ||
      !/^[a-f0-9]{64}$/.test(digest) ||
      this.#pending.has(id)
    ) {
      throw new Error("invalid candidate transfer envelope");
    }

    this.#pending.set(id, {
      kind,
      digest,
      bytes: new Uint8Array(byteLength),
      received: 0,
      ordinal: 0,
      committing: false,
    });
  }

  append(id, ordinal, chunk) {
    const transfer = this.#pending.get(id);
    if (
      !transfer ||
      transfer.committing ||
      ordinal !== transfer.ordinal ||
      !(chunk instanceof Uint8Array) ||
      chunk.byteLength === 0 ||
      chunk.byteLength > this.maximumChunkBytes ||
      chunk.byteLength > transfer.bytes.byteLength - transfer.received
    ) {
      this.abort(id);
      throw new Error("invalid candidate transfer chunk");
    }

    // Blazor may supply a view into a shared buffer; own the bytes before interop returns.
    transfer.bytes.set(chunk, transfer.received);
    transfer.received += chunk.byteLength;
    transfer.ordinal++;
  }

  async commit(id, publish) {
    const transfer = this.#pending.get(id);
    if (transfer?.committing) return false;
    if (!transfer || transfer.received !== transfer.bytes.byteLength) {
      this.abort(id);
      throw new Error("incomplete candidate transfer");
    }
    transfer.committing = true;

    try {
      const digest = await sha256(transfer.bytes);
      // Abort and teardown must still win while Web Crypto is awaiting its result.
      if (this.#pending.get(id) !== transfer) return false;
      if (digest !== transfer.digest) throw new Error("candidate digest mismatch");
      return publish(transfer.kind, JSON.parse(decoder.decode(transfer.bytes)));
    } finally {
      if (this.#pending.get(id) === transfer) this.abort(id);
    }
  }

  abort(id) {
    this.#pending.delete(id);
  }

  clear() {
    this.#pending.clear();
  }
}

export async function sha256(bytes) {
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return new Uint8Array(digest).toHex();
}
