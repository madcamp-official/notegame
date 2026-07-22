import fs from "node:fs";
import path from "node:path";

const DEFAULT_BODY_LIMIT = 256 * 1024;

export class HttpInputError extends Error {
  constructor(statusCode, publicMessage) {
    super(publicMessage);
    this.name = "HttpInputError";
    this.statusCode = statusCode;
    this.publicMessage = publicMessage;
  }
}

export function parseBoolean(value, fallback = false) {
  if (value == null || value === "") return fallback;
  return /^(1|true|yes|on)$/i.test(String(value));
}

export async function readJsonBody(req, { limitBytes = DEFAULT_BODY_LIMIT } = {}) {
  const chunks = [];
  let total = 0;
  for await (const chunk of req) {
    total += chunk.length;
    if (total > limitBytes) {
      throw new HttpInputError(413, "Request body is too large");
    }
    chunks.push(chunk);
  }
  if (total === 0) return {};
  let value;
  try {
    value = JSON.parse(Buffer.concat(chunks).toString("utf8"));
  } catch {
    throw new HttpInputError(400, "Malformed JSON request body");
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    throw new HttpInputError(400, "JSON request body must be an object");
  }
  return value;
}

export function resolveStaticFile(staticRoot, requestPath) {
  let root;
  let decoded;
  try {
    root = fs.realpathSync(staticRoot);
    decoded = decodeURIComponent(String(requestPath || "/").split("?", 1)[0]);
  } catch (error) {
    if (error instanceof URIError) throw new HttpInputError(400, "Invalid path");
    throw new HttpInputError(404, "Static file not found");
  }
  if (decoded.includes("\0")) throw new HttpInputError(400, "Invalid path");
  const candidate = path.resolve(root, `.${decoded.startsWith("/") ? decoded : `/${decoded}`}`);
  const relative = path.relative(root, candidate);
  if (relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new HttpInputError(403, "Forbidden");
  }
  let realCandidate;
  try {
    realCandidate = fs.realpathSync(candidate);
  } catch {
    throw new HttpInputError(404, "Static file not found");
  }
  const realRelative = path.relative(root, realCandidate);
  if (realRelative.startsWith("..") || path.isAbsolute(realRelative)) {
    throw new HttpInputError(403, "Forbidden");
  }
  return realCandidate;
}

export function allowedCorsOrigin(requestOrigin, configuredOrigins = process.env.CORS_ORIGINS) {
  if (!requestOrigin) return null;
  const configured = String(configuredOrigins || "http://localhost:3000,http://127.0.0.1:3000")
    .split(",").map((v) => v.trim()).filter(Boolean);
  return configured.includes(requestOrigin) ? requestOrigin : null;
}

export function debugRoutesEnabled(env = process.env) {
  return parseBoolean(env.ENABLE_DEBUG_ROUTES, false) && (env.NODE_ENV || "development") === "development";
}

export function publicHttpError(error) {
  if (error instanceof HttpInputError) {
    return { statusCode: error.statusCode, body: { error: error.publicMessage } };
  }
  return { statusCode: 500, body: { error: "Internal server error" } };
}
