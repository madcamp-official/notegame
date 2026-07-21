import { randomUUID } from "node:crypto";
import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { AppError } from "../errors.js";

const MAX_BODY_BYTES = 64 * 1024;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const REQUEST_ID_PATTERN = /^[A-Za-z0-9_.:-]{1,96}$/;

export function createRequestHandler({ service, config, logger = console }) {
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

      // Serve static files in public directory
      if (request.method === "GET" && path.startsWith("/public/")) {
        const relativePath = path.substring(8); // remove "/public/"
        const safePath = relativePath.replace(/\.\./g, ""); // basic path traversal protection
        let filePath = join(process.cwd(), "public", safePath);
        if (!existsSync(filePath)) {
          filePath = join(process.cwd(), "..", "public", safePath);
        }
        if (existsSync(filePath)) {
          const content = readFileSync(filePath);
          let contentType = "text/plain";
          if (filePath.endsWith(".html")) contentType = "text/html";
          else if (filePath.endsWith(".css")) contentType = "text/css";
          else if (filePath.endsWith(".js")) contentType = "application/javascript";
          else if (filePath.endsWith(".png")) contentType = "image/png";

          response.writeHead(200, { "content-type": contentType });
          response.end(content);
          return;
        } else {
          throw new AppError(404, "FILE_NOT_FOUND", "Static file not found.");
        }
      }

      if (request.method === "GET" && path === "/health") {
        sendJson(response, 200, await service.health());
        return;
      }

      const ownerId = resolveOwner(request, config);
      let match;
      if (path === "/v1/campaigns" && request.method === "POST") {
        sendJson(response, 201, { campaign: await service.createCampaign(ownerId, await readJson(request)) });
      } else if (path === "/v1/campaigns" && request.method === "GET") {
        sendJson(response, 200, { campaigns: await service.listCampaigns(ownerId) });
      } else if ((match = path.match(/^\/v1\/campaigns\/([^/]+)$/)) && request.method === "GET") {
        sendJson(response, 200, { campaign: await service.getCampaign(ownerId, decodeURIComponent(match[1])) });
      } else if ((match = path.match(/^\/v1\/campaigns\/([^/]+)\/runs$/)) && request.method === "POST") {
        sendJson(response, 201, { run: await service.createRun(ownerId, decodeURIComponent(match[1]), await readJson(request)) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)$/)) && request.method === "GET") {
        sendJson(response, 200, { run: await service.getRun(ownerId, decodeURIComponent(match[1])) });
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/debug$/)) && request.method === "GET") {
        sendJson(response, 200, await service.getRunDebug(ownerId, decodeURIComponent(match[1])));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/inventory$/)) && request.method === "POST") {
        sendJson(response, 200, await service.mutateInventory(ownerId, decodeURIComponent(match[1]), await readJson(request)));
      } else if ((match = path.match(/^\/v1\/runs\/([^/]+)\/ambient-wander$/)) && request.method === "POST") {
        sendJson(response, 200, await service.ambientWander(ownerId, decodeURIComponent(match[1]), await readJson(request)));
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
  if (!isOriginAllowed(origin, config.corsOrigins)) throw new AppError(403, "ORIGIN_FORBIDDEN", "Origin is not allowed.");
  response.setHeader("access-control-allow-origin", origin);
  response.setHeader("vary", "Origin");
  response.setHeader("access-control-allow-methods", "GET,POST,OPTIONS");
  response.setHeader("access-control-allow-headers", "content-type,x-user-id,x-request-id");
  response.setHeader("access-control-max-age", "600");
}

function isOriginAllowed(origin, configuredOrigins) {
  if (origin === "null" || origin === "") return true;
  if (configuredOrigins.includes(origin)) return true;
  try {
    const parsed = new URL(origin);
    if (parsed.protocol === "file:") return true;
    return ["http:", "https:"].includes(parsed.protocol) && ["localhost", "127.0.0.1", "[::1]"].includes(parsed.hostname);
  } catch {
    return false;
  }
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
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > MAX_BODY_BYTES) throw new AppError(413, "BODY_TOO_LARGE", "JSON body exceeds 64 KiB.");
    chunks.push(chunk);
  }
  if (chunks.length === 0) return {};
  try {
    return JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw new AppError(400, "JSON_INVALID", "Request body contains invalid JSON.");
  }
}

function sendJson(response, status, body) {
  const payload = JSON.stringify(body);
  response.statusCode = status;
  response.setHeader("content-length", Buffer.byteLength(payload));
  response.end(payload);
}

function handleError(response, error, requestId, logger) {
  const appError = error instanceof AppError ? error : new AppError(500, "INTERNAL_ERROR", "An internal error occurred.");
  if (!(error instanceof AppError)) logger?.error?.({ event: "unhandled_error", requestId, category: error?.name || "unknown" });
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

function safePath(rawUrl) {
  try {
    return new URL(rawUrl || "/", "http://localhost").pathname;
  } catch {
    return "/invalid-url";
  }
}
