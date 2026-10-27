import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  use: {
    baseURL: "http://127.0.0.1:5080",
    channel: process.env.CI ? undefined : "msedge",
    screenshot: "only-on-failure",
  },
  webServer: {
    command:
      "dotnet run --project ../src/ConsultReady.Api --no-launch-profile --urls http://127.0.0.1:5080",
    url: "http://127.0.0.1:5080/health",
    reuseExistingServer: !process.env.CI,
    timeout: 120000,
  },
});
