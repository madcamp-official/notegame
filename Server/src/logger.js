const LEVELS = Object.freeze({ debug: 10, info: 20, warn: 30, error: 40, silent: 100 });

export function createLogger(level = "info") {
  const threshold = LEVELS[level] ?? LEVELS.info;
  const write = (name, payload) => {
    if (LEVELS[name] < threshold) return;
    const record = {
      timestamp: new Date().toISOString(),
      level: name,
      ...(typeof payload === "string" ? { message: payload } : payload)
    };
    const line = JSON.stringify(record);
    if (name === "error") console.error(line);
    else console.log(line);
  };
  return {
    debug: (payload) => write("debug", payload),
    info: (payload) => write("info", payload),
    warn: (payload) => write("warn", payload),
    error: (payload) => write("error", payload)
  };
}
