import { createApplication } from "./app.js";
import { loadLocalEnv } from "./load-env.js";

loadLocalEnv();
const application = await createApplication();
application.server.listen(application.config.port, application.config.host, () => {
  application.logger.info({
    event: "server_started",
    host: application.config.host,
    port: application.config.port,
    storage: application.config.storage,
    llmProvider: application.config.llmProvider,
    narrationModel: application.config.llmProvider === "vllm"
      ? application.config.vllmModel
      : application.config.geminiFastModel,
    llmConfigured: application.config.llmProvider === "vllm"
      ? Boolean(application.config.vllmBaseUrl)
      : Boolean(application.config.geminiApiKey)
  });
});

let closing = false;
async function shutdown(signal) {
  if (closing) return;
  closing = true;
  application.logger.info({ event: "server_stopping", signal });
  await application.close();
  process.exitCode = 0;
}

process.on("SIGINT", () => void shutdown("SIGINT"));
process.on("SIGTERM", () => void shutdown("SIGTERM"));
