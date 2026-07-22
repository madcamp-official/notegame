#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";

function fail(message) {
  process.stderr.write(`${message}\n`);
  process.exit(1);
}

function parseArgs(argv) {
  const result = new Map();
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--self-test") {
      result.set("self-test", "1");
      continue;
    }
    if (!argument.startsWith("--") || index + 1 >= argv.length) {
      fail(`잘못된 인자입니다: ${argument}`);
    }
    result.set(argument.slice(2), argv[index + 1]);
    index += 1;
  }
  return result;
}

function xmlDecode(value) {
  return String(value ?? "")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'")
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&amp;", "&");
}

function attributeValue(attributes, name) {
  const match = new RegExp(`(?:^|\\s)${name}="([^"]*)"`).exec(attributes);
  return match ? xmlDecode(match[1]) : "";
}

function buildXmlReferenceMap(xml) {
  const references = new Map();
  const openingTagPattern = /<([a-z][a-z0-9-]*)\b([^>]*)>/gi;
  for (const match of xml.matchAll(openingTagPattern)) {
    const tag = match[1];
    const attributes = match[2];
    const id = attributeValue(attributes, "id");
    if (!id) {
      continue;
    }
    references.set(`${tag}:${id}`, {
      fmt: attributeValue(attributes, "fmt"),
      value: "",
    });
  }

  const simpleValuePattern = /<([a-z][a-z0-9-]*)\b([^>]*\bid="([0-9]+)"[^>]*)>([^<]*)<\/\1>/gi;
  for (const match of xml.matchAll(simpleValuePattern)) {
    const key = `${match[1]}:${match[3]}`;
    const existing = references.get(key) ?? { fmt: "", value: "" };
    existing.value = xmlDecode(match[4]).trim();
    if (!existing.fmt) {
      existing.fmt = attributeValue(match[2], "fmt");
    }
    references.set(key, existing);
  }
  return references;
}

function rootElements(rowBody) {
  const elements = [];
  const tagPattern = /<\/?[a-z][^>]*>/gi;
  let depth = 0;
  let elementStart = -1;
  for (const match of rowBody.matchAll(tagPattern)) {
    const token = match[0];
    const isClosing = token.startsWith("</");
    const isSelfClosing = token.endsWith("/>");
    if (!isClosing) {
      if (depth === 0) {
        elementStart = match.index;
      }
      if (isSelfClosing) {
        if (depth === 0) {
          elements.push(rowBody.slice(elementStart, match.index + token.length));
          elementStart = -1;
        }
      } else {
        depth += 1;
      }
      continue;
    }
    depth -= 1;
    if (depth === 0 && elementStart >= 0) {
      elements.push(rowBody.slice(elementStart, match.index + token.length));
      elementStart = -1;
    }
  }
  return elements;
}

function parseElement(element, references) {
  const opening = /^<([a-z][a-z0-9-]*)\b([^>]*)>/i.exec(element);
  if (!opening) {
    return { tag: "", value: "", fmt: "" };
  }
  const tag = opening[1];
  const attributes = opening[2];
  const ref = attributeValue(attributes, "ref");
  const id = attributeValue(attributes, "id");
  const reference = ref ? references.get(`${tag}:${ref}`) : id ? references.get(`${tag}:${id}`) : null;
  const directValueMatch = new RegExp(`^<${tag}\\b[^>]*>([^<]*)<\\/${tag}>$`, "i").exec(element);
  return {
    tag,
    value: directValueMatch ? xmlDecode(directValueMatch[1]).trim() : reference?.value ?? "",
    fmt: attributeValue(attributes, "fmt") || reference?.fmt || "",
  };
}

function rowsFromXml(xml) {
  const references = buildXmlReferenceMap(xml);
  const rows = [];
  for (const match of xml.matchAll(/<row>([\s\S]*?)<\/row>/gi)) {
    rows.push(rootElements(match[1]).map((element) => parseElement(element, references)));
  }
  return rows;
}

function numericElement(element) {
  const value = Number(element?.value);
  return Number.isFinite(value) ? value : null;
}

function percentile(values, percentileValue) {
  if (values.length === 0) {
    return null;
  }
  const sorted = [...values].sort((left, right) => left - right);
  const index = Math.max(0, Math.ceil((percentileValue / 100) * sorted.length) - 1);
  return sorted[index];
}

function mean(values) {
  return values.length === 0 ? null : values.reduce((sum, value) => sum + value, 0) / values.length;
}

function round(value, digits = 2) {
  if (!Number.isFinite(value)) {
    return null;
  }
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
}

function parseProcessPid(element) {
  const match = /\(([0-9]+)\)\s*$/.exec(element?.fmt ?? "");
  return match ? Number(match[1]) : null;
}

function mergeIntervals(intervals) {
  if (intervals.length === 0) {
    return [];
  }
  const sorted = [...intervals].sort((left, right) => left[0] - right[0]);
  const merged = [sorted[0].slice()];
  for (const [start, end] of sorted.slice(1)) {
    const current = merged[merged.length - 1];
    if (start <= current[1]) {
      current[1] = Math.max(current[1], end);
    } else {
      merged.push([start, end]);
    }
  }
  return merged;
}

function readOptional(filePath) {
  return filePath && fs.existsSync(filePath) ? fs.readFileSync(filePath, "utf8") : "";
}

function strictFiniteNumber(value) {
  const text = String(value ?? "").trim();
  if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(text)) {
    return null;
  }
  const number = Number(text);
  return Number.isFinite(number) ? number : null;
}

function parseMetalHudRecord(line) {
  const prefix = "metal-HUD:";
  const prefixIndex = line.indexOf(prefix);
  if (prefixIndex < 0) {
    return null;
  }
  const tokens = line.slice(prefixIndex + prefix.length).split(",").map((token) => token.trim());
  if (tokens.length < 5 || (tokens.length - 3) % 2 !== 0 || tokens.some((token) => token === "")) {
    return { malformed: true };
  }
  if (!/^\d+$/.test(tokens[0])) {
    return { malformed: true };
  }
  const firstFrameNumber = Number(tokens[0]);
  const headerMetric = strictFiniteNumber(tokens[1]);
  const processMemory = strictFiniteNumber(tokens[2]);
  if (!Number.isSafeInteger(firstFrameNumber) || firstFrameNumber < 0 ||
      headerMetric === null || headerMetric < 0 || processMemory === null || processMemory < 0) {
    return { malformed: true };
  }
  const frames = [];
  for (let index = 3; index < tokens.length; index += 2) {
    const presentIntervalMs = strictFiniteNumber(tokens[index]);
    const gpuTimeMs = strictFiniteNumber(tokens[index + 1]);
    if (presentIntervalMs === null || presentIntervalMs <= 0 || gpuTimeMs === null || gpuTimeMs < 0) {
      return { malformed: true };
    }
    frames.push({
      frameNumber: firstFrameNumber + frames.length,
      presentIntervalMs,
      gpuTimeMs,
    });
  }
  return { malformed: false, firstFrameNumber, headerMetric, processMemory, frames };
}

function summarizeMetalHudText(logText, targetFps, requestedSchema = "auto") {
  const schema = ["current", "legacy"].includes(requestedSchema) ? requestedSchema : "auto";
  const result = {
    available: false,
    source: "metal-hud-log",
    status: "NOT_MEASURED",
    targetFps,
    frameBudgetMs: round(1000 / targetFps, 3),
    hitchThresholdMs: round((1000 / targetFps) * 1.5, 3),
    frameStatus: "NOT_MEASURED",
    schemaRequested: requestedSchema,
    schemaApplied: schema === "auto" ? "ambiguous" : schema,
    parserVersion: 1,
    logLineCount: 0,
    parsedLineCount: 0,
    malformedLineCount: 0,
    rawFrameSampleCount: 0,
    overlappingReportedFrameNumberCount: 0,
    identicalOverlapCount: 0,
    conflictingOverlapCount: 0,
    overlapResolution: "newest-log-line-wins",
    dataQualityStatus: "NOT_MEASURED",
    missingFrameNumberCount: null,
    firstFrameNumber: null,
    lastFrameNumber: null,
    frameCount: 0,
    presentedFps: null,
    frameIntervalP50Ms: null,
    frameIntervalP95Ms: null,
    frameIntervalP99Ms: null,
    frameIntervalMaxMs: null,
    frameObservedSeconds: null,
    hitchCount: null,
    hitchRatio: null,
    gpuTimeP50Ms: null,
    gpuTimeP95Ms: null,
    gpuTimeMaxMs: null,
    memorySampleCount: 0,
    graphicsMemoryStartMB: null,
    graphicsMemoryMaxMB: null,
    graphicsMemoryEndMB: null,
    processMemoryStartMB: null,
    processMemoryMaxMB: null,
    processMemoryEndMB: null,
    estimatedFrameMissesStart: null,
    estimatedFrameMissesMax: null,
    estimatedFrameMissesEnd: null,
    ambiguousHeaderMetricStart: null,
    ambiguousHeaderMetricMax: null,
    ambiguousHeaderMetricEnd: null,
  };
  if (!logText) {
    return result;
  }

  const records = [];
  const framesByNumber = new Map();
  for (const line of logText.split(/\r?\n/)) {
    if (!line.includes("metal-HUD:")) {
      continue;
    }
    result.logLineCount += 1;
    const record = parseMetalHudRecord(line);
    if (!record || record.malformed) {
      result.malformedLineCount += 1;
      continue;
    }
    result.parsedLineCount += 1;
    records.push(record);
    for (const frame of record.frames) {
      result.rawFrameSampleCount += 1;
      const previous = framesByNumber.get(frame.frameNumber);
      if (previous) {
        result.overlappingReportedFrameNumberCount += 1;
        if (previous.presentIntervalMs === frame.presentIntervalMs &&
            previous.gpuTimeMs === frame.gpuTimeMs) {
          result.identicalOverlapCount += 1;
        } else {
          result.conflictingOverlapCount += 1;
        }
      }
      // HUD windows can overlap. File order is append order, so the later line is
      // the deterministic newest observation for a repeated reported frame number.
      framesByNumber.set(frame.frameNumber, frame);
    }
  }
  if (result.logLineCount > 0 && result.parsedLineCount === 0) {
    result.status = "INSUFFICIENT_DATA";
    result.frameStatus = "INSUFFICIENT_DATA";
    result.dataQualityStatus = "INSUFFICIENT_DATA";
    return result;
  }

  const headerMetrics = records.map((record) => record.headerMetric);
  const processMemory = records.map((record) => record.processMemory);
  result.memorySampleCount = records.length;
  if (processMemory.length > 0) {
    result.processMemoryStartMB = round(processMemory[0], 3);
    result.processMemoryMaxMB = round(Math.max(...processMemory), 3);
    result.processMemoryEndMB = round(processMemory.at(-1), 3);
  }
  if (schema === "current" && headerMetrics.length > 0) {
    result.graphicsMemoryStartMB = round(headerMetrics[0], 3);
    result.graphicsMemoryMaxMB = round(Math.max(...headerMetrics), 3);
    result.graphicsMemoryEndMB = round(headerMetrics.at(-1), 3);
  } else if (schema === "legacy" && headerMetrics.length > 0) {
    result.estimatedFrameMissesStart = round(headerMetrics[0], 3);
    result.estimatedFrameMissesMax = round(Math.max(...headerMetrics), 3);
    result.estimatedFrameMissesEnd = round(headerMetrics.at(-1), 3);
  } else if (headerMetrics.length > 0) {
    result.ambiguousHeaderMetricStart = round(headerMetrics[0], 3);
    result.ambiguousHeaderMetricMax = round(Math.max(...headerMetrics), 3);
    result.ambiguousHeaderMetricEnd = round(headerMetrics.at(-1), 3);
  }

  const frames = [...framesByNumber.values()].sort(
    (left, right) => left.frameNumber - right.frameNumber,
  );
  result.frameCount = frames.length;
  if (frames.length === 0) {
    result.status = result.logLineCount > 0 ? "INSUFFICIENT_DATA" : "NOT_MEASURED";
    result.frameStatus = result.status;
    result.dataQualityStatus = result.status;
    return result;
  }
  result.firstFrameNumber = frames[0].frameNumber;
  result.lastFrameNumber = frames.at(-1).frameNumber;
  result.missingFrameNumberCount = Math.max(
    0,
    result.lastFrameNumber - result.firstFrameNumber + 1 - frames.length,
  );
  result.dataQualityStatus = result.conflictingOverlapCount > 0
    ? "CONFLICTING_OVERLAPS_RESOLVED"
    : result.identicalOverlapCount > 0
      ? "IDENTICAL_OVERLAPS_DEDUPLICATED"
      : "CLEAN";
  const intervalsMs = frames.map((frame) => frame.presentIntervalMs);
  const gpuTimesMs = frames.map((frame) => frame.gpuTimeMs);
  const observedSeconds = intervalsMs.reduce((sum, value) => sum + value, 0) / 1000;
  result.available = observedSeconds > 0;
  result.frameObservedSeconds = round(observedSeconds, 3);
  result.presentedFps = observedSeconds > 0 ? round(frames.length / observedSeconds, 3) : null;
  result.frameIntervalP50Ms = round(percentile(intervalsMs, 50), 3);
  result.frameIntervalP95Ms = round(percentile(intervalsMs, 95), 3);
  result.frameIntervalP99Ms = round(percentile(intervalsMs, 99), 3);
  result.frameIntervalMaxMs = round(Math.max(...intervalsMs), 3);
  const hitchThresholdMs = (1000 / targetFps) * 1.5;
  result.hitchCount = intervalsMs.filter((value) => value > hitchThresholdMs).length;
  result.hitchRatio = round(result.hitchCount / intervalsMs.length, 5);
  result.gpuTimeP50Ms = round(percentile(gpuTimesMs, 50), 3);
  result.gpuTimeP95Ms = round(percentile(gpuTimesMs, 95), 3);
  result.gpuTimeMaxMs = round(Math.max(...gpuTimesMs), 3);
  if (frames.length < 10 || observedSeconds < 2) {
    result.frameStatus = "INSUFFICIENT_DATA";
  } else {
    const fpsWithinTarget = result.presentedFps >= targetFps * 0.9 && result.presentedFps <= targetFps * 1.1;
    const p95WithinBudget = result.frameIntervalP95Ms <= (1000 / targetFps) * 1.25;
    const hitchesWithinBudget = result.hitchRatio <= 0.01;
    result.frameStatus = fpsWithinTarget && p95WithinBudget && hitchesWithinBudget ? "PASS" : "FAIL";
  }
  result.status = result.frameStatus;
  return result;
}

function summarizeMetalHudLog(logPath, targetFps, requestedSchema = "auto") {
  return summarizeMetalHudText(readOptional(logPath), targetFps, requestedSchema);
}

function summarizeTrace(traceExportDirectory, pid, targetFps) {
  const result = {
    available: false,
    source: "xctrace",
    status: "NOT_MEASURED",
    targetFps,
    frameBudgetMs: round(1000 / targetFps, 3),
    hitchThresholdMs: round((1000 / targetFps) * 1.5, 3),
    frameStatus: "NOT_MEASURED",
    presentCount: 0,
    presentedFps: null,
    frameIntervalP50Ms: null,
    frameIntervalP95Ms: null,
    frameIntervalP99Ms: null,
    frameIntervalMaxMs: null,
    frameObservedSeconds: null,
    hitchCount: null,
    hitchRatio: null,
    targetGpuBusyMs: null,
    targetGpuBusyPercentOfTrace: null,
    targetGpuIntervalP50Ms: null,
    targetGpuIntervalP95Ms: null,
    targetGpuIntervalMaxMs: null,
    targetMetalAllocatedMaxMiB: null,
    potentialHangCount: null,
    thermalStates: [],
    thermalWorst: null,
    traceDurationSeconds: null,
  };
  if (!traceExportDirectory || !fs.existsSync(traceExportDirectory)) {
    return result;
  }

  const tocXml = readOptional(path.join(traceExportDirectory, "toc.xml"));
  const durationMatch = /<duration>([0-9]+(?:\.[0-9]+)?)<\/duration>/.exec(tocXml);
  if (durationMatch) {
    result.traceDurationSeconds = Number(durationMatch[1]);
  }

  const presentXml = readOptional(path.join(traceExportDirectory, "ca-client-present-request.xml"));
  const presentTimesNs = [...new Set(rowsFromXml(presentXml)
    .filter((row) => parseProcessPid(row[6]) === pid)
    .map((row) => numericElement(row[0]))
    .filter((value) => Number.isFinite(value)))]
    .sort((left, right) => left - right);
  result.presentCount = presentTimesNs.length;
  if (presentTimesNs.length >= 2) {
    const intervalsMs = [];
    for (let index = 1; index < presentTimesNs.length; index += 1) {
      const intervalMs = (presentTimesNs[index] - presentTimesNs[index - 1]) / 1_000_000;
      if (intervalMs > 0) {
        intervalsMs.push(intervalMs);
      }
    }
    const spanSeconds = (presentTimesNs.at(-1) - presentTimesNs[0]) / 1_000_000_000;
    result.frameObservedSeconds = round(spanSeconds, 3);
    result.available = intervalsMs.length > 0 && spanSeconds > 0;
    result.presentedFps = spanSeconds > 0 ? round((presentTimesNs.length - 1) / spanSeconds, 3) : null;
    result.frameIntervalP50Ms = round(percentile(intervalsMs, 50), 3);
    result.frameIntervalP95Ms = round(percentile(intervalsMs, 95), 3);
    result.frameIntervalP99Ms = round(percentile(intervalsMs, 99), 3);
    result.frameIntervalMaxMs = round(Math.max(...intervalsMs), 3);
    const hitchThresholdMs = (1000 / targetFps) * 1.5;
    result.hitchCount = intervalsMs.filter((value) => value > hitchThresholdMs).length;
    result.hitchRatio = round(result.hitchCount / intervalsMs.length, 5);
    if (presentTimesNs.length < 10 || spanSeconds < 2) {
      result.frameStatus = "INSUFFICIENT_DATA";
    } else {
      const fpsWithinTarget = result.presentedFps >= targetFps * 0.9 && result.presentedFps <= targetFps * 1.1;
      const p95WithinBudget = result.frameIntervalP95Ms <= (1000 / targetFps) * 1.25;
      const hitchesWithinBudget = result.hitchRatio <= 0.01;
      result.frameStatus = fpsWithinTarget && p95WithinBudget && hitchesWithinBudget ? "PASS" : "FAIL";
    }
    result.status = result.frameStatus;
  }

  const gpuXml = readOptional(path.join(traceExportDirectory, "metal-gpu-intervals.xml"));
  const gpuIntervals = [];
  const gpuDurationsMs = [];
  for (const row of rowsFromXml(gpuXml)) {
    const startNs = numericElement(row[0]);
    const durationNs = numericElement(row[1]);
    if (startNs === null || durationNs === null || parseProcessPid(row[10]) !== pid) {
      continue;
    }
    gpuIntervals.push([startNs, startNs + durationNs]);
    gpuDurationsMs.push(durationNs / 1_000_000);
  }
  if (gpuIntervals.length > 0) {
    const mergedBusyNs = mergeIntervals(gpuIntervals)
      .reduce((sum, [start, end]) => sum + (end - start), 0);
    result.targetGpuBusyMs = round(mergedBusyNs / 1_000_000, 3);
    result.targetGpuIntervalP50Ms = round(percentile(gpuDurationsMs, 50), 3);
    result.targetGpuIntervalP95Ms = round(percentile(gpuDurationsMs, 95), 3);
    result.targetGpuIntervalMaxMs = round(Math.max(...gpuDurationsMs), 3);
    if (result.traceDurationSeconds > 0) {
      result.targetGpuBusyPercentOfTrace = round(
        (mergedBusyNs / 1_000_000_000 / result.traceDurationSeconds) * 100,
        3,
      );
    }
  }

  const metalMemoryXml = readOptional(path.join(traceExportDirectory, "metal-current-allocated-size.xml"));
  const metalAllocationBytes = rowsFromXml(metalMemoryXml)
    .filter((row) => parseProcessPid(row[3]) === pid)
    .map((row) => numericElement(row[4]))
    .filter((value) => Number.isFinite(value));
  if (metalAllocationBytes.length > 0) {
    result.targetMetalAllocatedMaxMiB = round(Math.max(...metalAllocationBytes) / 1024 / 1024, 3);
  }

  const hangsXml = readOptional(path.join(traceExportDirectory, "potential-hangs.xml"));
  const hangRows = rowsFromXml(hangsXml);
  result.potentialHangCount = hangRows.filter((row) => {
    const processElement = row.find((element) => element.tag === "process");
    return !processElement || parseProcessPid(processElement) === pid;
  }).length;

  const thermalXml = readOptional(path.join(traceExportDirectory, "device-thermal-state-intervals.xml"));
  const thermalStates = rowsFromXml(thermalXml)
    .map((row) => row[3]?.fmt)
    .filter(Boolean);
  result.thermalStates = [...new Set(thermalStates)];
  if (result.thermalStates.length > 0) {
    const thermalSeverity = new Map([
      ["Nominal", 0],
      ["Fair", 1],
      ["Serious", 2],
      ["Critical", 3],
    ]);
    result.thermalWorst = [...result.thermalStates]
      .sort((left, right) => (thermalSeverity.get(right) ?? -1) - (thermalSeverity.get(left) ?? -1))[0];
  }
  return result;
}

function selectGraphicsSummary(trace, metalHud, targetFps) {
  let source = "none";
  let selected = null;
  if (trace.available) {
    source = "xctrace";
    selected = trace;
  } else if (metalHud.available) {
    source = "metal-hud-log";
    selected = metalHud;
  } else if (trace.traceDurationSeconds !== null) {
    source = "xctrace";
    selected = trace;
  } else if (metalHud.logLineCount > 0) {
    source = "metal-hud-log";
    selected = metalHud;
  }

  const isTrace = source === "xctrace";
  const isMetalHud = source === "metal-hud-log";
  return {
    available: Boolean(selected?.available),
    source,
    status: selected?.frameStatus ?? "NOT_MEASURED",
    preferredSource: "xctrace",
    fallbackUsed: isMetalHud,
    targetFps,
    frameBudgetMs: round(1000 / targetFps, 3),
    hitchThresholdMs: selected?.hitchThresholdMs ?? round((1000 / targetFps) * 1.5, 3),
    frameStatus: selected?.frameStatus ?? "NOT_MEASURED",
    frameCount: isTrace ? trace.presentCount : isMetalHud ? metalHud.frameCount : 0,
    presentedFps: selected?.presentedFps ?? null,
    frameIntervalP50Ms: selected?.frameIntervalP50Ms ?? null,
    frameIntervalP95Ms: selected?.frameIntervalP95Ms ?? null,
    frameIntervalP99Ms: selected?.frameIntervalP99Ms ?? null,
    frameIntervalMaxMs: selected?.frameIntervalMaxMs ?? null,
    frameObservedSeconds: selected?.frameObservedSeconds ?? null,
    hitchCount: selected?.hitchCount ?? null,
    hitchRatio: selected?.hitchRatio ?? null,
    gpuTimeP50Ms: isTrace ? trace.targetGpuIntervalP50Ms : isMetalHud ? metalHud.gpuTimeP50Ms : null,
    gpuTimeP95Ms: isTrace ? trace.targetGpuIntervalP95Ms : isMetalHud ? metalHud.gpuTimeP95Ms : null,
    gpuTimeMaxMs: isTrace ? trace.targetGpuIntervalMaxMs : isMetalHud ? metalHud.gpuTimeMaxMs : null,
    graphicsMemoryStartMB: isMetalHud ? metalHud.graphicsMemoryStartMB : null,
    graphicsMemoryMaxMB: isMetalHud ? metalHud.graphicsMemoryMaxMB : null,
    graphicsMemoryEndMB: isMetalHud ? metalHud.graphicsMemoryEndMB : null,
    processMemoryStartMB: isMetalHud ? metalHud.processMemoryStartMB : null,
    processMemoryMaxMB: isMetalHud ? metalHud.processMemoryMaxMB : null,
    processMemoryEndMB: isMetalHud ? metalHud.processMemoryEndMB : null,
    targetGpuBusyMs: isTrace ? trace.targetGpuBusyMs : null,
    targetGpuBusyPercentOfTrace: isTrace ? trace.targetGpuBusyPercentOfTrace : null,
    targetMetalAllocatedMaxMiB: isTrace ? trace.targetMetalAllocatedMaxMiB : null,
    potentialHangCount: isTrace ? trace.potentialHangCount : null,
    thermalStates: isTrace ? trace.thermalStates : [],
    thermalWorst: isTrace ? trace.thermalWorst : null,
    traceDurationSeconds: isTrace ? trace.traceDurationSeconds : null,
    metalHudSchema: metalHud.schemaApplied,
    metalHudLogLineCount: metalHud.logLineCount,
    metalHudParsedLineCount: metalHud.parsedLineCount,
    metalHudMalformedLineCount: metalHud.malformedLineCount,
    metalHudRawFrameSampleCount: metalHud.rawFrameSampleCount,
    metalHudOverlappingFrameCount: metalHud.overlappingReportedFrameNumberCount,
    metalHudIdenticalOverlapCount: metalHud.identicalOverlapCount,
    metalHudConflictingOverlapCount: metalHud.conflictingOverlapCount,
    metalHudOverlapResolution: metalHud.overlapResolution,
    metalHudDataQualityStatus: metalHud.dataQualityStatus,
  };
}

function parseCsv(csvText) {
  const lines = csvText.trim().split(/\r?\n/).filter(Boolean);
  if (lines.length < 2) {
    return [];
  }
  const headers = lines[0].split(",");
  return lines.slice(1).map((line) => {
    const values = line.split(",");
    return Object.fromEntries(headers.map((header, index) => [header, values[index] ?? ""]));
  });
}

function finiteColumn(rows, name) {
  return rows.map((row) => Number(row[name])).filter((value) => Number.isFinite(value));
}

function summarizeSamples(rows) {
  const intervalCpu = finiteColumn(rows.filter((row) => row.interval_cpu_percent !== ""), "interval_cpu_percent");
  const rssKiB = finiteColumn(rows, "rss_kib");
  const threads = finiteColumn(rows, "threads");
  const gpuDevice = finiteColumn(rows.filter((row) => row.system_gpu_device_percent !== ""), "system_gpu_device_percent");
  const gpuRenderer = finiteColumn(rows.filter((row) => row.system_gpu_renderer_percent !== ""), "system_gpu_renderer_percent");
  const gpuTiler = finiteColumn(rows.filter((row) => row.system_gpu_tiler_percent !== ""), "system_gpu_tiler_percent");
  const elapsed = finiteColumn(rows, "elapsed_seconds");
  const rssGrowthMiB = rssKiB.length >= 2 ? (rssKiB.at(-1) - rssKiB[0]) / 1024 : null;
  const elapsedMinutes = elapsed.length >= 2 ? (elapsed.at(-1) - elapsed[0]) / 60 : null;
  return {
    sampleCount: rows.length,
    observedSeconds: elapsed.length ? round(Math.max(...elapsed), 3) : null,
    cpuMeanPercent: round(mean(intervalCpu), 3),
    cpuP95Percent: round(percentile(intervalCpu, 95), 3),
    cpuMaxPercent: intervalCpu.length ? round(Math.max(...intervalCpu), 3) : null,
    rssStartMiB: rssKiB.length ? round(rssKiB[0] / 1024, 3) : null,
    rssMaxMiB: rssKiB.length ? round(Math.max(...rssKiB) / 1024, 3) : null,
    rssGrowthMiB: round(rssGrowthMiB, 3),
    rssGrowthMiBPerMinute: elapsedMinutes > 0 ? round(rssGrowthMiB / elapsedMinutes, 3) : null,
    threadMin: threads.length ? Math.min(...threads) : null,
    threadMax: threads.length ? Math.max(...threads) : null,
    systemGpuDeviceMeanPercent: round(mean(gpuDevice), 3),
    systemGpuDeviceMaxPercent: gpuDevice.length ? Math.max(...gpuDevice) : null,
    systemGpuRendererMeanPercent: round(mean(gpuRenderer), 3),
    systemGpuTilerMeanPercent: round(mean(gpuTiler), 3),
  };
}

function display(value, suffix = "") {
  return value === null || value === undefined ? "측정 안 됨" : `${value}${suffix}`;
}

function markdownEscape(value) {
  return String(value ?? "").replaceAll("|", "\\|").replaceAll("\n", " ");
}

function buildSummaryMarkdown(metadata, samples, graphics, trace, metalHud, runtimeErrorCount) {
  const instrumentation = graphics.source === "xctrace"
    ? "Xcode Game Performance trace 부착(우선 소스; CPU 수치에 계측 오버헤드 포함 가능)"
    : graphics.source === "metal-hud-log"
      ? "Apple Metal Performance HUD 측정-window 로그(공식 앱 귀속 fallback) + ps/ioreg"
      : "ps/ioreg 저오버헤드 샘플링 패스(프레임·앱 귀속 GPU 자료 없음)";
  return `# Ninja Adventure 성능 측정 — ${markdownEscape(metadata.scenario)}

- 측정 시각(UTC): ${markdownEscape(metadata.startedAtUtc)}
- 앱: \`${markdownEscape(metadata.appPath)}\`
- 검증 PID: \`${metadata.pid}\` (실행 파일: \`${markdownEscape(metadata.executablePath)}\`)
- 측정 구간: ${display(samples.observedSeconds, "초")}, ${samples.sampleCount} samples
- 화면/플레이 조건: ${markdownEscape(metadata.notes || "별도 메모 없음")}
- 계측 방식: ${instrumentation}
- 프레임·GPU 선택 소스 / 판정: **${graphics.source} / ${graphics.status}**

## 프로세스 자원

| 지표 | 결과 |
|---|---:|
| interval CPU 평균 / p95 / 최대 | ${display(samples.cpuMeanPercent, "%")} / ${display(samples.cpuP95Percent, "%")} / ${display(samples.cpuMaxPercent, "%")} |
| RSS 시작 / 최대 / 변화 | ${display(samples.rssStartMiB, " MiB")} / ${display(samples.rssMaxMiB, " MiB")} / ${display(samples.rssGrowthMiB, " MiB")} |
| RSS 변화율 | ${display(samples.rssGrowthMiBPerMinute, " MiB/min")} |
| thread 최소 / 최대 | ${display(samples.threadMin)} / ${display(samples.threadMax)} |
| 시스템 전체 GPU Device 평균 / 최대 | ${display(samples.systemGpuDeviceMeanPercent, "%")} / ${display(samples.systemGpuDeviceMaxPercent, "%")} |
| 시스템 전체 GPU Renderer / Tiler 평균 | ${display(samples.systemGpuRendererMeanPercent, "%")} / ${display(samples.systemGpuTilerMeanPercent, "%")} |

CPU는 프로세스 누적 CPU time의 샘플 간 차이이므로 멀티코어 사용 시 100%를 넘을 수 있습니다.
\`ioreg\` GPU 수치는 해당 순간 시스템 전체 부하이며 앱 단독 수치가 아닙니다.

## 프레임·앱 귀속 Metal 지표

| 지표 | 결과 |
|---|---:|
| 소스 / ${graphics.targetFps}fps 프레임 판정 | **${graphics.source} / ${graphics.frameStatus}** |
| frame 수 / 관측 FPS | ${graphics.frameCount} / ${display(graphics.presentedFps, " fps")} |
| 프레임 관측 구간 | ${display(graphics.frameObservedSeconds, "초")} |
| 프레임 간격 p50 / p95 / p99 / 최대 | ${display(graphics.frameIntervalP50Ms, " ms")} / ${display(graphics.frameIntervalP95Ms, " ms")} / ${display(graphics.frameIntervalP99Ms, " ms")} / ${display(graphics.frameIntervalMaxMs, " ms")} |
| ${graphics.hitchThresholdMs}ms 초과 hitch 수 / 비율 | ${display(graphics.hitchCount)} / ${display(graphics.hitchRatio)} |
| 선택 소스 GPU time/interval p50 / p95 / 최대 | ${display(graphics.gpuTimeP50Ms, " ms")} / ${display(graphics.gpuTimeP95Ms, " ms")} / ${display(graphics.gpuTimeMaxMs, " ms")} |
| xctrace 대상 앱 GPU busy union / trace 비율 | ${display(trace.targetGpuBusyMs, " ms")} / ${display(trace.targetGpuBusyPercentOfTrace, "%")} |
| xctrace 대상 앱 Metal 최대 할당 | ${display(trace.targetMetalAllocatedMaxMiB, " MiB")} |
| Metal HUD schema / 유효·전체·malformed line | ${metalHud.schemaApplied} / ${metalHud.parsedLineCount} / ${metalHud.logLineCount} / ${metalHud.malformedLineCount} |
| Metal HUD raw sample / unique frame | ${metalHud.rawFrameSampleCount} / ${metalHud.frameCount} |
| Metal HUD overlap 전체 / 동일 / 충돌 | ${metalHud.overlappingReportedFrameNumberCount} / ${metalHud.identicalOverlapCount} / ${metalHud.conflictingOverlapCount} |
| Metal HUD overlap 처리 / 품질 / 누락 번호 | ${metalHud.overlapResolution} / ${metalHud.dataQualityStatus} / ${display(metalHud.missingFrameNumberCount)} |
| Metal HUD 앱 graphics memory 시작 / 최대 / 끝 | ${display(metalHud.graphicsMemoryStartMB, " MB")} / ${display(metalHud.graphicsMemoryMaxMB, " MB")} / ${display(metalHud.graphicsMemoryEndMB, " MB")} |
| Metal HUD process memory 시작 / 최대 / 끝 | ${display(metalHud.processMemoryStartMB, " MB")} / ${display(metalHud.processMemoryMaxMB, " MB")} / ${display(metalHud.processMemoryEndMB, " MB")} |
| xctrace 잠재 hang(100ms+) | ${display(trace.potentialHangCount)} |
| xctrace thermal state / 최악 | ${trace.thermalStates.length ? trace.thermalStates.join(", ") : "측정 안 됨"} / ${display(trace.thermalWorst)} |
| xctrace 구간 | ${display(trace.traceDurationSeconds, "초")} |

프레임 PASS는 최소 2초 분량에서 관측 FPS가 목표의 90–110%, p95 간격이 frame budget의
125% 이하, frame budget의 150%를 초과한 간격 비율이 1% 이하인 경우입니다. 정지 화면에서도 Player가 프레임 제출을
의도적으로 생략한다면 낮은 FPS를 오류로 단정하지 말고 같은 상태의 CPU/GPU 감소와 함께 해석합니다.
사용 가능한 xctrace frame 자료를 항상 우선하고, 없을 때만 측정 창의 \`metal-HUD:\` line을 fallback으로 사용합니다.
Metal HUD의 frame interval·GPU time은 ms, memory 값은 HUD가 보고한 MB입니다. \`auto\` schema에서는
과거 HUD와 의미가 충돌하는 두 번째 header 값을 graphics memory로 추정하지 않습니다.
겹치는 HUD window는 reported frame number로 중복 제거하며, 같은 번호가 다시 나오면 captured log의
뒤쪽 line을 최신 값으로 채택합니다. 동일 overlap과 값이 다른 conflict overlap 수를 별도로 남깁니다.

## 런타임 로그

- 측정 창의 Error/Exception/Assert 후보: **${runtimeErrorCount}건**
- 원시 자료: \`samples.csv\`, \`metadata.json\`, \`thermal-start.txt\`, \`thermal-end.txt\`${metadata.xctraceRecorded ? ", `game-performance.trace`, `xctrace-export/`" : ""}${metadata.playerLogCaptured ? ", `player-log-window.txt`" : ""}
`;
}

function writeIndex(outputRoot) {
  const runs = [];
  if (fs.existsSync(outputRoot)) {
    for (const entry of fs.readdirSync(outputRoot, { withFileTypes: true })) {
      if (!entry.isDirectory()) {
        continue;
      }
      const metricsPath = path.join(outputRoot, entry.name, "metrics.json");
      if (!fs.existsSync(metricsPath)) {
        continue;
      }
      const metrics = JSON.parse(fs.readFileSync(metricsPath, "utf8"));
      runs.push({ directory: entry.name, ...metrics });
    }
  }
  runs.sort((left, right) => String(left.metadata.startedAtUtc).localeCompare(String(right.metadata.startedAtUtc)));
  const lines = [
    "# Ninja Adventure 성능 측정 인덱스",
    "",
    "| 시나리오 | UTC | 구간 | CPU 평균/p95 | RSS 최대/변화 | thread 최대 | 시스템 GPU 평균 | 프레임 소스 | FPS/p95 | GPU p95 | HUD graphics memory 최대 | 판정 | 로그 오류 후보 |",
    "|---|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|",
  ];
  for (const run of runs) {
    const graphics = run.graphics ?? {
      source: run.trace?.available ? "xctrace" : "none",
      presentedFps: run.trace?.presentedFps,
      frameIntervalP95Ms: run.trace?.frameIntervalP95Ms,
      gpuTimeP95Ms: run.trace?.targetGpuIntervalP95Ms,
      frameStatus: run.trace?.frameStatus ?? "NOT_MEASURED",
    };
    const graphicsMemoryMaxMB = run.metalHud?.graphicsMemoryMaxMB ?? graphics.graphicsMemoryMaxMB;
    lines.push(
      `| [${markdownEscape(run.metadata.scenario)}](${run.directory}/summary.md) | ${markdownEscape(run.metadata.startedAtUtc)} | ${display(run.samples.observedSeconds, "s")} | ${display(run.samples.cpuMeanPercent, "%")}/${display(run.samples.cpuP95Percent, "%")} | ${display(run.samples.rssMaxMiB, " MiB")}/${display(run.samples.rssGrowthMiB, " MiB")} | ${display(run.samples.threadMax)} | ${display(run.samples.systemGpuDeviceMeanPercent, "%")} | ${graphics.source} | ${display(graphics.presentedFps, " fps")}/${display(graphics.frameIntervalP95Ms, " ms")} | ${display(graphics.gpuTimeP95Ms, " ms")} | ${display(graphicsMemoryMaxMB, " MB")} | ${graphics.frameStatus} | ${run.runtimeErrorCount} |`,
    );
  }
  lines.push("", `총 ${runs.length}개 측정 구간. 각 행의 링크에서 원시 자료와 판정 근거를 확인합니다.`, "");
  fs.writeFileSync(path.join(outputRoot, "Performance-Index.md"), lines.join("\n"));
}

function runSelfTest() {
  const presentRows = [];
  for (let index = 0; index < 90; index += 1) {
    const timestamp = index * 33_333_333;
    presentRows.push(`<row><start-time id="${index + 1}" fmt="x">${timestamp}</start-time></row>`);
  }
  const xml = `<trace-query-result>${presentRows.join("")}</trace-query-result>`;
  const parsed = rowsFromXml(xml).map((row) => numericElement(row[0]));
  if (parsed.length !== 90 || parsed[1] !== 33_333_333) {
    throw new Error("frame XML parser self-test failed");
  }
  const merged = mergeIntervals([[0, 10], [5, 20], [30, 35]]);
  if (JSON.stringify(merged) !== JSON.stringify([[0, 20], [30, 35]])) {
    throw new Error("GPU interval merge self-test failed");
  }
  const refs = rowsFromXml(
    '<trace-query-result><row><duration id="1" fmt="1 ms">1000000</duration><process id="2" fmt="Game (42)"><pid>42</pid></process></row><row><duration ref="1"/><process ref="2"/></row></trace-query-result>',
  );
  if (numericElement(refs[1][0]) !== 1_000_000 || parseProcessPid(refs[1][1]) !== 42) {
    throw new Error("XML reference self-test failed");
  }
  const mixedProcesses = rowsFromXml(
    '<trace-query-result><row><start-time id="1">0</start-time><sentinel/><sentinel/><sentinel/><sentinel/><sentinel/><process id="2" fmt="Other (7)"><pid>7</pid></process></row><row><start-time id="3">33333333</start-time><sentinel/><sentinel/><sentinel/><sentinel/><sentinel/><process id="4" fmt="Target (42)"><pid>42</pid></process></row><row><start-time id="5">66666666</start-time><sentinel/><sentinel/><sentinel/><sentinel/><sentinel/><process ref="4"/></row></trace-query-result>',
  );
  const targetFrames = mixedProcesses.filter((row) => parseProcessPid(row[6]) === 42);
  if (targetFrames.length !== 2) {
    throw new Error("target PID frame filter self-test failed");
  }
  const hudPairs = Array.from({ length: 90 }, (_, index) =>
    `${index === 89 ? "51" : "33.333"},${index % 3 === 0 ? "6.5" : "5"}`,
  ).join(",");
  const hudLog = [
    `2026-07-22 10:00:00.000 Game[42:7] metal-HUD: 100,376.27,932.19,${hudPairs}`,
    "[com.apple.metal.hud:Logging] CompileShader: name: Test compilation-time: 100 cached: 0",
    "metal-HUD: 190,376.27,932.19,33.333",
    "metal-HUD: 191,NaN,932.19,33.333,5",
  ].join("\n");
  const currentHud = summarizeMetalHudText(hudLog, 30, "current");
  if (currentHud.logLineCount !== 3 || currentHud.parsedLineCount !== 1 ||
      currentHud.malformedLineCount !== 2 || currentHud.frameCount !== 90 ||
      currentHud.hitchCount !== 1 || currentHud.frameIntervalP50Ms !== 33.333 ||
      currentHud.frameIntervalP99Ms !== 51 || currentHud.gpuTimeP95Ms !== 6.5 ||
      currentHud.graphicsMemoryMaxMB !== 376.27 || currentHud.processMemoryMaxMB !== 932.19 ||
      currentHud.frameStatus !== "FAIL") {
    throw new Error("Metal HUD current-schema parser self-test failed");
  }
  const legacyHud = summarizeMetalHudText(
    "metal-HUD: 10,2,56.05,33.333,4.5",
    30,
    "legacy",
  );
  if (legacyHud.graphicsMemoryMaxMB !== null || legacyHud.estimatedFrameMissesMax !== 2 ||
      legacyHud.processMemoryMaxMB !== 56.05) {
    throw new Error("Metal HUD legacy-schema parser self-test failed");
  }
  const autoHud = summarizeMetalHudText("metal-HUD: 10,2,56.05,33.333,4.5", 30, "auto");
  if (autoHud.graphicsMemoryMaxMB !== null || autoHud.estimatedFrameMissesMax !== null ||
      autoHud.ambiguousHeaderMetricMax !== 2) {
    throw new Error("Metal HUD ambiguous-schema parser self-test failed");
  }
  const overlappingHud = summarizeMetalHudText(
    [
      "metal-HUD: 10,1,2,33.333,4,33.333,4,33.333,5",
      "metal-HUD: 11,1,2,33.333,4,50,9,33.333,6",
    ].join("\n"),
    30,
    "current",
  );
  if (overlappingHud.frameCount !== 4 ||
      overlappingHud.rawFrameSampleCount !== 6 ||
      overlappingHud.overlappingReportedFrameNumberCount !== 2 ||
      overlappingHud.identicalOverlapCount !== 1 ||
      overlappingHud.conflictingOverlapCount !== 1 ||
      overlappingHud.frameIntervalMaxMs !== 50 || overlappingHud.gpuTimeMaxMs !== 9 ||
      overlappingHud.missingFrameNumberCount !== 0 ||
      overlappingHud.dataQualityStatus !== "CONFLICTING_OVERLAPS_RESOLVED") {
    throw new Error("Metal HUD overlapping-range self-test failed");
  }
  const rollingWindowLines = [];
  let rollingFirstFrame = 9635;
  for (let lineIndex = 0; lineIndex < 119; lineIndex += 1) {
    const framePairs = lineIndex < 65 || lineIndex === 118 ? 31 : 30;
    const pairs = [];
    for (let frameIndex = 0; frameIndex < framePairs; frameIndex += 1) {
      const gpuTime = 2 + lineIndex / 1000 + (Math.floor(frameIndex / 2) % 3) / 10;
      pairs.push("33", gpuTime.toFixed(3));
    }
    rollingWindowLines.push(
      `metal-HUD: ${rollingFirstFrame},195.36,611.97,${pairs.join(",")}`,
    );
    if (lineIndex < 118) {
      rollingFirstFrame += lineIndex < 32 ? 16 : 15;
    }
  }
  const rollingHud = summarizeMetalHudText(rollingWindowLines.join("\n"), 30, "current");
  if (rollingHud.logLineCount !== 119 || rollingHud.rawFrameSampleCount !== 3636 ||
      rollingHud.frameCount !== 1833 || rollingHud.overlappingReportedFrameNumberCount !== 1803 ||
      rollingHud.firstFrameNumber !== 9635 || rollingHud.lastFrameNumber !== 11467 ||
      rollingHud.frameObservedSeconds !== 60.489 || rollingHud.presentedFps !== 30.303 ||
      rollingHud.conflictingOverlapCount <= 0 || rollingHud.frameStatus !== "PASS") {
    throw new Error("Metal HUD rolling-window deduplication self-test failed");
  }
  const noTrace = summarizeTrace("", 42, 30);
  if (selectGraphicsSummary(noTrace, currentHud, 30).source !== "metal-hud-log") {
    throw new Error("Metal HUD fallback selection self-test failed");
  }
  const usableTrace = {
    ...noTrace,
    available: true,
    frameStatus: "PASS",
    presentCount: 90,
  };
  if (selectGraphicsSummary(usableTrace, currentHud, 30).source !== "xctrace") {
    throw new Error("xctrace preference self-test failed");
  }
  process.stdout.write("성능 요약기 self-test 통과\n");
}

const args = parseArgs(process.argv.slice(2));
if (args.has("self-test")) {
  runSelfTest();
  process.exit(0);
}
if (args.has("index")) {
  const outputRoot = path.resolve(args.get("index"));
  writeIndex(outputRoot);
  process.exit(0);
}

for (const required of ["metadata", "samples", "output"]) {
  if (!args.has(required)) {
    fail(`--${required} 인자가 필요합니다.`);
  }
}

const metadata = JSON.parse(fs.readFileSync(args.get("metadata"), "utf8"));
const sampleRows = parseCsv(fs.readFileSync(args.get("samples"), "utf8"));
if (sampleRows.length < 2) {
  fail("유효한 성능 sample이 2개 이상 필요합니다.");
}
const targetFps = Number(metadata.targetFps || 30);
const samples = summarizeSamples(sampleRows);
const trace = summarizeTrace(args.get("trace-export"), Number(metadata.pid), targetFps);
const metalHudSchema = args.get("metal-hud-schema") ?? metadata.metalHudSchema ?? "auto";
if (!["auto", "current", "legacy"].includes(metalHudSchema)) {
  fail(`잘못된 Metal HUD schema입니다: ${metalHudSchema}`);
}
const metalHud = summarizeMetalHudLog(args.get("metal-hud-log"), targetFps, metalHudSchema);
const graphics = selectGraphicsSummary(trace, metalHud, targetFps);
const runtimeErrorCount = Number(metadata.runtimeErrorCount || 0);
const metrics = { metadata, samples, graphics, trace, metalHud, runtimeErrorCount };
const outputDirectory = path.resolve(args.get("output"));
fs.mkdirSync(outputDirectory, { recursive: true });
fs.writeFileSync(path.join(outputDirectory, "metrics.json"), `${JSON.stringify(metrics, null, 2)}\n`);
fs.writeFileSync(
  path.join(outputDirectory, "summary.md"),
  buildSummaryMarkdown(metadata, samples, graphics, trace, metalHud, runtimeErrorCount),
);
