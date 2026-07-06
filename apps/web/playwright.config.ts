import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  reporter: "list",
  use: {
    ...devices["Desktop Edge"],
    baseURL: "http://localhost:3000",
    channel: "msedge",
    headless: true,
    trace: "retain-on-failure"
  }
});
