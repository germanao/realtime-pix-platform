import { defineConfig, devices } from "@playwright/test";

const vercelBypassSecret = process.env.VERCEL_AUTOMATION_BYPASS_SECRET;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 420_000,
  reporter: "list",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:3000",
    extraHTTPHeaders: vercelBypassSecret
      ? {
          "x-vercel-protection-bypass": vercelBypassSecret,
          "x-vercel-set-bypass-cookie": "true"
        }
      : undefined,
    headless: true,
    trace: "retain-on-failure"
  }
});
