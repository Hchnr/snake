// Bounded request/response bookkeeping for an already-open DevTools socket.
class CdpRequests {
  constructor(socket, timeoutMs = 15000) {
    this.socket = socket; this.timeoutMs = timeoutMs;
    this.next = 0; this.pending = new Map(); this.failure = null;
  }
  call(method, params = {}) {
    if (this.failure) return Promise.reject(this.failure);
    const id = ++this.next;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(Error('CDP request timed out: ' + method));
      }, this.timeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      try { this.socket.send(JSON.stringify({ id, method, params })); }
      catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }
  receive(message) {
    const item = this.pending.get(message.id);
    if (!item) return;
    clearTimeout(item.timer); this.pending.delete(message.id);
    if (message.error) item.reject(Error(message.error.message));
    else item.resolve(message.result);
  }
  fail(error) {
    if (this.failure) return;
    this.failure = error instanceof Error ? error : Error(String(error));
    for (const item of this.pending.values()) { clearTimeout(item.timer); item.reject(this.failure); }
    this.pending.clear();
  }
}
module.exports = { CdpRequests };
