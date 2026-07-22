import { randomUUID } from "node:crypto";
import { readFileSync, existsSync, statSync } from "node:fs";
import { join } from "node:path";
import { AppError } from "../errors.js";
import {
  HttpInputError,
  allowedCorsOrigin,
  publicHttpError,
  readJsonBody,
  resolveStaticFile
} from "./httpGuards.js";

import { addUnityContractAliases } from "../compat/unityContract.js";
const MAX_BODY_BYTES = 64 * 1024;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const REQUEST_ID_PATTERN = /^[A-Za-z0-9_.:-]{1,96}$/;

export function createRequestHandler({ service, config, logger = console }) {
  const developmentToolsEnabled = config.enableDebugRoutes === true && config.environment === "development";
  return async (request, response) => {
    const startedAt = Date.now();
    const suppliedRequestId = request.headers["x-request-id"];
    const requestId = typeof suppliedRequestId === "string" && REQUEST_ID_PATTERN.test(suppliedRequestId) ? suppliedRequestId : randomUUID();
    setBaseHeaders(response, requestId);

    try {
      applyCors(request, response, config);
      if (request.method === "OPTIONS") {
        response.writeHead(204);
        response.end();
        return;
      }

      const url = new URL(request.url || "/", "http://localhost");
      const path = url.pathname.replace(/\/+$/, "") || "/";

      // These pages expose development-only test clients and architecture assets.
      if (request.method === "GET" && path.startsWith("/public/")) {
        if (!developmentToolsEnabled) throw new AppError(404, "ROUTE_NOT_FOUND", "Route was not found.");
        const relativePath = path.substring(8); // remove "/public/"
        const filePath = resolvePublicFile(relativePath);
        const content = readFileSync(filePath);
        response.writeHead(200, { "content-type": contentTypeFor(filePath) });
        response.end(content);
        return;
      }

      if (request.method === "GET" && path === "/health") {
        sendJson(response, 200, await service.health());
        return;
      }

      const ownerId = resolveOwner(request, config);
      let match;
      if (path === "/v1/campaigns" && request.method === "POST") {
        const campaign = await service.createCampaign(ownerId, await readCreationJson(request));
        sendJson(response, campaign.fromIdempotencyCache ? 200 : 201, { campaign });
      } else if (path === "/v1/campaigns" && request.method === "GET") {
        sendJson(response, 200, { campaigns: await service.listCampaigns(ownerId) });
      } else if ((match = path.match(/^\/v1\/campaigns\/([^/]+)$/)) && request.method === "GET") {
        sendJson(response, 200, { campaign: await service.getCampaign(ownerId, decodeURIComponent(match[1])) });
      } else if ((match = path.match(/^\/v1\/campaigns\/([^/]+)\/runs$/)) && request.method === "POST") {
        const run = await service.createRun(ownerId, decodeURIComponent(match[1]), await readCreationJson(request));
        sendJson(response, run.fromIdempotencyCache ? 200 : 201, { run });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)$/)) && request.method === "GET") {
        sendJson(response, 200, { run: await service.getRun(ownerId, decodeURIComponent(match[1])) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/debug$/)) && request.method === "GET") {
        if (!developmentToolsEnabled) throw new AppError(404, "ROUTE_NOT_FOUND", "Route was not found.");
        sendJson(response, 200, await service.getRunDebug(ownerId, decodeURIComponent(match[1])));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/inventory$/)) && request.method === "POST") {
        sendJson(response, 200, await service.mutateInventory(ownerId, decodeURIComponent(match[1]), await readJson(request)));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/ambient-wander$/)) && request.method === "POST") {
        sendJson(response, 200, await service.ambientWander(ownerId, decodeURIComponent(match[1]), await readJson(request)));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/dice$/)) && request.method === "POST") {
        sendJson(response, 200, await service.prepareD20(ownerId, decodeURIComponent(match[1]), await readJson(request)));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/(?:actions|turns)$/)) && request.method === "POST") {
        const result = await service.submitTurn(ownerId, decodeURIComponent(match[1]), await readJson(request));
        sendJson(response, result.fromIdempotencyCache ? 200 : 201, result);
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/choices$/)) && request.method === "POST") {
        const result = await service.submitChoice(ownerId, decodeURIComponent(match[1]), await readJson(request));
        sendJson(response, result.fromIdempotencyCache ? 200 : 201, result);
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/messages$/)) && request.method === "POST") {
        const result = await service.submitPlayerMessage(ownerId, decodeURIComponent(match[1]), await readJson(request));
        sendJson(response, result.fromIdempotencyCache ? 200 : 201, result);
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/(?:travel|navigation)$/)) && request.method === "POST") {
        const result = await service.travel(ownerId, decodeURIComponent(match[1]), await readJson(request));
        sendJson(response, result.fromIdempotencyCache ? 200 : 201, result);
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/turns$/)) && request.method === "GET") {
        sendJson(response, 200, { turns: await service.listTurns(ownerId, decodeURIComponent(match[1])) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/turns\/(\d+)$/)) && request.method === "GET") {
        const turnNo = Number(match[2]);
        if (!Number.isSafeInteger(turnNo) || turnNo < 1) throw new AppError(400, "TURN_NUMBER_INVALID", "turnNo must be a positive integer.");
        sendJson(response, 200, { turn: await service.getTurn(ownerId, decodeURIComponent(match[1]), turnNo) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/abandon$/)) && request.method === "POST") {
        sendJson(response, 200, { run: await service.abandonRun(ownerId, decodeURIComponent(match[1]), await readJson(request)) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/resume$/)) && request.method === "POST") {
        sendJson(response, 200, { run: await service.resumeRun(ownerId, decodeURIComponent(match[1]), await readJson(request)) });
      } else if (path === "/v1/gm/narrate" && request.method === "POST") {
        sendJson(response, 200, await service.narrate(await readJson(request)));
      } else if (path === "/v1/gm/scene-transitions" && request.method === "POST") {
        sendJson(response, 200, await service.planSceneTransition(await readJson(request)));
      } else {
        throw new AppError(404, "ROUTE_NOT_FOUND", "Route was not found.");
      }
    } catch (error) {
      handleError(response, error, requestId, logger);
    } finally {
      logger?.info?.({
        event: "http_request",
        requestId,
        method: request.method,
        path: safePath(request.url),
        status: response.statusCode,
        durationMs: Date.now() - startedAt
      });
    }
  };
}

function setBaseHeaders(response, requestId) {
  response.setHeader("content-type", "application/json; charset=utf-8");
  response.setHeader("cache-control", "no-store");
  response.setHeader("x-content-type-options", "nosniff");
  response.setHeader("x-frame-options", "DENY");
  response.setHeader("referrer-policy", "no-referrer");
  response.setHeader("x-request-id", requestId);
}

function applyCors(request, response, config) {
  const origin = request.headers.origin;
  if (!origin) return;
  const allowedOrigin = allowedCorsOrigin(origin, config.corsOrigins);
  if (!allowedOrigin) throw new AppError(403, "ORIGIN_FORBIDDEN", "Origin is not allowed.");
  response.setHeader("access-control-allow-origin", allowedOrigin);
  response.setHeader("vary", "Origin");
  response.setHeader("access-control-allow-methods", "GET,POST,OPTIONS");
  response.setHeader("access-control-allow-headers", "content-type,idempotency-key,x-user-id,x-request-id");
  response.setHeader("access-control-max-age", "600");
}

function resolveOwner(request, config) {
  const supplied = request.headers["x-user-id"];
  const ownerId = typeof supplied === "string" && supplied ? supplied : config.authMode === "local" ? config.defaultUserId : "";
  if (!ownerId) throw new AppError(401, "AUTH_REQUIRED", "x-user-id is required.");
  if (!UUID_PATTERN.test(ownerId)) throw new AppError(401, "AUTH_INVALID", "Authenticated user id must be a UUID.");
  return ownerId.toLowerCase();
}

async function readJson(request) {
  const contentType = request.headers["content-type"];
  if (contentType && !String(contentType).toLowerCase().startsWith("application/json")) {
    throw new AppError(415, "CONTENT_TYPE_INVALID", "Content-Type must be application/json.");
  }
  try {
    return await readJsonBody(request, { limitBytes: MAX_BODY_BYTES });
  } catch (error) {
    if (!(error instanceof HttpInputError)) throw error;
    const code = error.statusCode === 413 ? "BODY_TOO_LARGE" : "JSON_INVALID";
    throw new AppError(error.statusCode, code, error.publicMessage);
  }
}

async function readCreationJson(request) {
  const body = await readJson(request);
  const header = request.headers["idempotency-key"];
  if (header === undefined) return body;
  if (typeof header !== "string" || (body.idempotencyKey !== undefined && body.idempotencyKey !== header)) {
    throw new AppError(400, "IDEMPOTENCY_KEY_INVALID", "Idempotency-Key must be a single value matching body.idempotencyKey when both are supplied.");
  }
  return { ...body, idempotencyKey: header };
}

function sendJson(response, status, body) {
  body = addUnityContractAliases(body);
  const payload = JSON.stringify(body);
  response.statusCode = status;
  response.setHeader("content-length", Buffer.byteLength(payload));
  response.end(payload);
}

function handleError(response, error, requestId, logger) {
  const publicError = publicHttpError(error);
  const appError = error instanceof AppError
    ? error
    : error instanceof HttpInputError
      ? new AppError(publicError.statusCode, "HTTP_INPUT_INVALID", publicError.body.error)
      : new AppError(publicError.statusCode, "INTERNAL_ERROR", "An internal error occurred.");
  if (!(error instanceof AppError)) {
    logger?.error?.({ event: "unhandled_error", requestId, category: error?.name || "unknown", error: error?.message, stack: error?.stack });
  }
  const body = {
    error: {
      code: appError.code,
      message: appError.message,
      ...(appError.details === undefined ? {} : { details: appError.details })
    },
    requestId
  };
  sendJson(response, appError.status, body);
}

function resolvePublicFile(relativePath) {
  const roots = [join(process.cwd(), "public"), join(process.cwd(), "..", "public")];
  for (const root of roots) {
    if (!existsSync(root)) continue;
    try {
      const candidate = resolveStaticFile(root, relativePath);
      if (statSync(candidate).isFile()) return candidate;
    } catch (error) {
      if (error instanceof HttpInputError && error.statusCode === 404) continue;
      throw error;
    }
  }
  throw new AppError(404, "FILE_NOT_FOUND", "Static file not found.");
}

function contentTypeFor(filePath) {
  if (filePath.endsWith(".html")) return "text/html; charset=utf-8";
  if (filePath.endsWith(".css")) return "text/css; charset=utf-8";
  if (filePath.endsWith(".js")) return "application/javascript; charset=utf-8";
  if (filePath.endsWith(".png")) return "image/png";
  return "application/octet-stream";
}

function safePath(rawUrl) {
  try {
    return new URL(rawUrl || "/", "http://localhost").pathname;
  } catch {
    return "/invalid-url";
  }
}
