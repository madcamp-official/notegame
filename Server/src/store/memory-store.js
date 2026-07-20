import { AppError, notFound } from "../errors.js";
import { clone } from "../domain/serialization.js";

export class MemoryStore {
  constructor() {
    this.campaigns = new Map();
    this.runs = new Map();
    this.turnsByIdempotency = new Map();
    this.turnsByNumber = new Map();
    this.navigationByIdempotency = new Map();
  }

  async health() {
    return { ok: true, storage: "memory" };
  }

  async createCampaign(campaign) {
    this.campaigns.set(campaign.id, clone(campaign));
    return clone(campaign);
  }

  async listCampaigns(ownerId) {
    return [...this.campaigns.values()]
      .filter((campaign) => campaign.ownerId === ownerId)
      .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
      .map(clone);
  }

  async getCampaign(ownerId, campaignId) {
    const campaign = this.campaigns.get(campaignId);
    if (!campaign || campaign.ownerId !== ownerId) throw notFound("Campaign");
    return clone(campaign);
  }

  async createRun(run) {
    this.runs.set(run.id, clone(run));
    return clone(run);
  }

  async getRun(ownerId, runId) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    return clone(run);
  }

  async commitAmbientWander({ ownerId, runId, expectedRunVersion, resolve }) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    if (run.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
    const committed = resolve(clone(run));
    if (committed.movedEntityIds.length > 0) this.runs.set(runId, clone(committed.run));
    return { run: clone(committed.run), movedEntityIds: [...committed.movedEntityIds] };
  }

  async findTurnByIdempotency(ownerId, runId, idempotencyKey) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    const turn = this.turnsByIdempotency.get(`${runId}:${idempotencyKey}`);
    return turn ? clone(turn) : null;
  }

  async findNavigationByIdempotency(ownerId, runId, idempotencyKey) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    const navigation = this.navigationByIdempotency.get(`${runId}:${idempotencyKey}`);
    return navigation ? clone(navigation) : null;
  }

  async commitNavigation({ ownerId, runId, idempotencyKey, requestFingerprint, expectedRunVersion, resolve }) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    const index = `${runId}:${idempotencyKey}`;
    const existing = this.navigationByIdempotency.get(index);
    if (existing) {
      if (existing.requestFingerprint !== requestFingerprint) throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The travel idempotency key was used with a different payload.");
      return { navigation: clone(existing), run: clone(run), fromIdempotencyCache: true };
    }
    if (run.status !== "active") throw new AppError(409, "RUN_NOT_ACTIVE", "The run does not accept travel.");
    if (run.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
    const committed = resolve(clone(run));
    if (!committed || committed.run.version !== run.version + 1 || committed.run.currentTurn !== run.currentTurn || committed.navigation.campaignTurnConsumed !== false) throw new AppError(500, "TRAVEL_INVARIANT_FAILED", "Safe travel changed a campaign turn or violated versioning.");
    this.runs.set(runId, clone(committed.run));
    this.navigationByIdempotency.set(index, clone(committed.navigation));
    return { ...committed, run: clone(committed.run), navigation: clone(committed.navigation), fromIdempotencyCache: false };
  }

  async commitTurn({ ownerId, runId, idempotencyKey, requestFingerprint, expectedRunVersion, resolve }) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    const idempotencyIndex = `${runId}:${idempotencyKey}`;
    const existing = this.turnsByIdempotency.get(idempotencyIndex);
    if (existing) {
      if (existing.requestFingerprint !== requestFingerprint) {
        throw new AppError(409, "IDEMPOTENCY_CONFLICT", "The idempotency key was already used with a different payload.");
      }
      return { turn: clone(existing), run: clone(this.runs.get(runId)), fromIdempotencyCache: true };
    }
    if (run.status !== "active") throw new AppError(409, "RUN_NOT_ACTIVE", "The run does not accept turns.");
    if (run.version !== expectedRunVersion) {
      throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
    }

    const committed = resolve(clone(run));
    if (!committed || committed.run.version !== run.version + 1 || committed.turn.turnNo !== run.currentTurn + 1) {
      throw new AppError(500, "TURN_INVARIANT_FAILED", "Turn resolver violated a commit invariant.");
    }
    this.runs.set(runId, clone(committed.run));
    this.turnsByIdempotency.set(idempotencyIndex, clone(committed.turn));
    this.turnsByNumber.set(`${runId}:${committed.turn.turnNo}`, clone(committed.turn));
    return { turn: clone(committed.turn), run: clone(committed.run), fromIdempotencyCache: false };
  }

  async getTurn(ownerId, runId, turnNo) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    const turn = this.turnsByNumber.get(`${runId}:${turnNo}`);
    if (!turn) throw notFound("Turn");
    return clone(turn);
  }

  async listTurns(ownerId, runId) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    return [...this.turnsByNumber.entries()]
      .filter(([index]) => index.startsWith(`${runId}:`))
      .map(([, turn]) => clone(turn))
      .sort((left, right) => left.turnNo - right.turnNo);
  }

  async abandonRun(ownerId, runId, expectedRunVersion, now) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    if (run.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
    if (run.status === "completed") throw new AppError(409, "RUN_COMPLETED", "A completed run cannot be abandoned.");
    if (run.status !== "abandoned") {
      run.status = "abandoned";
      run.version += 1;
      run.updatedAt = now;
      this.runs.set(run.id, clone(run));
    }
    return clone(run);
  }

  async resumeRun(ownerId, runId, expectedRunVersion, now) {
    const run = this.runs.get(runId);
    if (!run || run.ownerId !== ownerId) throw notFound("Run");
    if (run.version !== expectedRunVersion) throw new AppError(409, "RUN_VERSION_CONFLICT", "The run version is stale.", { currentVersion: run.version });
    if (run.status === "completed") throw new AppError(409, "RUN_COMPLETED", "A completed run cannot be resumed.");
    if (run.status !== "active") {
      run.status = "active";
      run.version += 1;
      run.updatedAt = now;
      this.runs.set(run.id, clone(run));
    }
    return clone(run);
  }

  async close() {}
}
