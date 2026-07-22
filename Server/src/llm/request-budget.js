import { AsyncLocalStorage } from "node:async_hooks";
import { AppError } from "../errors.js";

const turnBudgetStorage = new AsyncLocalStorage();

export function withLlmTurnBudget({ timeoutMs, maxCalls }, work) {
  if (turnBudgetStorage.getStore()) return work();
  const controller = new AbortController();
  const budget = {
    deadlineAt: Date.now() + timeoutMs,
    maxCalls,
    calls: 0,
    controller
  };
  const timeout = setTimeout(() => {
    controller.abort(new AppError(504, "LLM_TURN_DEADLINE", "The turn-level LLM deadline was reached."));
  }, timeoutMs);
  timeout.unref?.();
  return turnBudgetStorage.run(budget, async () => {
    try {
      return await work();
    } finally {
      clearTimeout(timeout);
    }
  });
}

export function createProviderRequestScope({ timeoutMs, timeoutCode, timeoutMessage }) {
  const budget = turnBudgetStorage.getStore();
  if (budget) {
    const remaining = budget.deadlineAt - Date.now();
    if (budget.controller.signal.aborted || remaining <= 0) {
      throw budget.controller.signal.reason instanceof Error
        ? budget.controller.signal.reason
        : new AppError(504, "LLM_TURN_DEADLINE", "The turn-level LLM deadline was reached.");
    }
    if (budget.calls >= budget.maxCalls) {
      throw new AppError(503, "LLM_CALL_BUDGET_EXHAUSTED", "The turn-level LLM call budget was exhausted.");
    }
    budget.calls += 1;
  }

  const controller = new AbortController();
  const remaining = budget ? Math.max(1, budget.deadlineAt - Date.now()) : timeoutMs;
  const effectiveTimeout = Math.max(1, Math.min(timeoutMs, remaining));
  const onBudgetAbort = () => controller.abort(budget.controller.signal.reason);
  budget?.controller.signal.addEventListener("abort", onBudgetAbort, { once: true });
  const timeout = setTimeout(() => {
    controller.abort(new AppError(504, timeoutCode, timeoutMessage));
  }, effectiveTimeout);
  timeout.unref?.();
  return {
    signal: controller.signal,
    throwIfAborted(error) {
      if (controller.signal.aborted && controller.signal.reason instanceof Error) throw controller.signal.reason;
      throw error;
    },
    cleanup() {
      clearTimeout(timeout);
      budget?.controller.signal.removeEventListener("abort", onBudgetAbort);
    }
  };
}

export class ProviderConcurrencyGate {
  constructor(maxConcurrent = 2) {
    this.maxConcurrent = maxConcurrent;
    this.active = 0;
    this.waiters = [];
  }

  async run(signal, work) {
    const release = await this.acquire(signal);
    try {
      return await work();
    } finally {
      release();
    }
  }

  acquire(signal) {
    if (signal?.aborted) return Promise.reject(signal.reason || new AppError(504, "LLM_REQUEST_ABORTED", "The LLM request was aborted."));
    if (this.active < this.maxConcurrent) {
      this.active += 1;
      return Promise.resolve(() => this.release());
    }
    return new Promise((resolve, reject) => {
      const waiter = { resolve, reject, signal, onAbort: null };
      waiter.onAbort = () => {
        const index = this.waiters.indexOf(waiter);
        if (index >= 0) this.waiters.splice(index, 1);
        reject(signal.reason || new AppError(504, "LLM_REQUEST_ABORTED", "The LLM request was aborted."));
      };
      signal?.addEventListener("abort", waiter.onAbort, { once: true });
      this.waiters.push(waiter);
    });
  }

  release() {
    while (this.waiters.length > 0) {
      const waiter = this.waiters.shift();
      waiter.signal?.removeEventListener("abort", waiter.onAbort);
      if (waiter.signal?.aborted) continue;
      waiter.resolve(() => this.release());
      return;
    }
    this.active = Math.max(0, this.active - 1);
  }
}
