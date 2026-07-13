import { expect, test, type Page } from "@playwright/test";
import path from "node:path";

const primaryJourneyOrder = [
  "browser-start",
  "gateway",
  "transaction-start",
  "event-bus",
  "wallet",
  "transaction-confirm",
  "realtime",
  "browser-end"
] as const;

async function expectProceduralReplay(page: Page) {
  for (const [activeIndex, nodeId] of primaryJourneyOrder.entries()) {
    await expect(
      page.locator(`.serviceNode[data-flow-node="${nodeId}"][data-lane="primary"].active`)
    ).toBeVisible({ timeout: 3_000 });

    const states = await page
      .locator('.serviceNode[data-lane="primary"]')
      .evaluateAll((nodes) =>
        nodes.map((node) => ({
          id: node.getAttribute("data-flow-node"),
          status: ["idle", "active", "success", "failure"].find((status) =>
            node.classList.contains(status)
          )
        }))
      );

    expect(states.filter(({ status }) => status === "active").map(({ id }) => id)).toEqual([
      nodeId
    ]);
    expect(states.slice(0, activeIndex).every(({ status }) => status === "success")).toBe(true);
    expect(states.slice(activeIndex + 1).every(({ status }) => status === "idle")).toBe(true);
  }
}

test("simple mode sends one PIX, preserves context in Expert mode, and grants welcome money once", async ({
  page
}) => {
  await page.setViewportSize({ width: 1528, height: 760 });
  const consoleErrors: string[] = [];
  const notFoundResponses: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") {
      consoleErrors.push(`${message.text()} @ ${message.location().url}`);
    }
  });
  page.on("response", (response) => {
    if (response.status() === 404) {
      notFoundResponses.push(response.url());
    }
  });

  await page.goto("/");
  await expect(page.getByText("Live", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "$10,000.00" })).toBeVisible();
  await expect(page.locator(".mapCanvas").getByText("Front Door", { exact: true })).toBeVisible();
  await expect(page.getByText("api-gateway", { exact: true })).toHaveCount(0);
  await page.screenshot({
    fullPage: true,
    path: path.resolve(process.cwd(), "../../outputs/pix-layout-before.png")
  });

  await page.getByRole("button", { name: "Aurora Ledger Always available" }).click();
  await page.locator(".sendButton").click();

  await expect(page.getByRole("heading", { name: "Following $25.00" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "$9,975.00" })).toBeVisible();
  await expect(page.getByText("Sent to Aurora Ledger", { exact: true })).toBeVisible();
  await expect(page.locator('.serviceNode.primary.success')).toHaveCount(8, { timeout: 15_000 });
  await page.screenshot({
    fullPage: true,
    path: path.resolve(process.cwd(), "../../outputs/pix-layout-after.png")
  });

  await page.getByRole("button", { name: "Replay" }).click();
  await expectProceduralReplay(page);
  await expect(page.locator('.serviceNode.primary.success')).toHaveCount(8, { timeout: 3_000 });

  await page.getByRole("switch", { name: "Expert mode" }).click();
  await expect(page.getByRole("heading", { name: "Emit PIX transfer" })).toBeVisible();
  await expect(page.getByText("completed", { exact: true })).toBeVisible();
  await expect(page.getByRole("combobox", { name: "Recipient" })).toHaveValue("bot-aurora-ledger");
  await expect(page.getByRole("textbox", { name: "Amount" })).toHaveValue("25");

  await page.reload();
  await expect(page.getByText("Send it. See how it moves.", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "$9,975.00" })).toBeVisible();
  expect(notFoundResponses).toEqual([]);
  expect(consoleErrors).toEqual([]);
});

test("desktop balance and journey geometry stay separated at the reported viewport", async ({ page }) => {
  await page.setViewportSize({ width: 1528, height: 760 });
  await page.goto("/");
  await expect(page.getByText("Live", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "$10,000.00" })).toBeVisible();

  const heroGeometry = await page.evaluate(() => {
    const balance = document.querySelector(".heroBalance")?.getBoundingClientRect();
    const composer = document.querySelector(".sendComposer")?.getBoundingClientRect();
    if (!balance || !composer) {
      throw new Error("Expected hero elements.");
    }
    return {
      balance: { left: balance.left, right: balance.right, top: balance.top, bottom: balance.bottom },
      composer: { left: composer.left, right: composer.right, top: composer.top, bottom: composer.bottom },
      viewport: window.innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      balanceWraps:
        document.querySelector(".heroBalance")?.getClientRects().length !== 1
    };
  });

  expect(heroGeometry.balance.right).toBeLessThan(heroGeometry.composer.left);
  expect(heroGeometry.balanceWraps).toBe(false);
  expect(heroGeometry.documentWidth).toBeLessThanOrEqual(heroGeometry.viewport);

  const primaryNodes = page.locator('.serviceNode[data-lane="primary"]');
  await expect(primaryNodes).toHaveCount(8);
  const primaryBoxes = await primaryNodes.evaluateAll((nodes) =>
    nodes.map((node) => {
      const box = node.getBoundingClientRect();
      return { left: box.left, right: box.right, top: box.top, bottom: box.bottom };
    })
  );

  for (let index = 1; index < primaryBoxes.length; index += 1) {
    expect(primaryBoxes[index].left).toBeGreaterThan(primaryBoxes[index - 1].right);
    expect(Math.abs(primaryBoxes[index].top - primaryBoxes[0].top)).toBeLessThan(2);
  }

  const supportingBoxes = await page
    .locator('.serviceNode[data-lane="supporting"]')
    .evaluateAll((nodes) =>
      nodes.map((node) => {
        const box = node.getBoundingClientRect();
        return { top: box.top, bottom: box.bottom };
      })
    );
  expect(supportingBoxes).toHaveLength(2);
  expect(supportingBoxes.every((box) => box.top > primaryBoxes[0].bottom)).toBe(true);

  const edgeGeometry = await page.evaluate(() => {
    const boxes = (selector: string) =>
      Array.from(document.querySelectorAll<SVGGraphicsElement>(selector)).map((edge) => {
        const box = edge.getBBox();
        return { width: box.width, height: box.height };
      });
    return {
      primary: boxes(".react-flow__edge:not(.supporting) .react-flow__edge-path"),
      supporting: boxes(".react-flow__edge.supporting .react-flow__edge-path")
    };
  });

  expect(edgeGeometry.primary).toHaveLength(7);
  expect(edgeGeometry.primary.every((edge) => edge.height < 2 && edge.width > 10)).toBe(true);
  expect(edgeGeometry.supporting).toHaveLength(2);
  expect(edgeGeometry.supporting.every((edge) => edge.width < 2 && edge.height > 10)).toBe(true);
});

test("mobile mode uses the vertical journey without horizontal overflow", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");
  await expect(page.getByText("Live", { exact: true })).toBeVisible();
  await expect(page.locator(".mobileJourney")).toBeVisible();
  await expect(page.locator(".mapCanvas")).toBeHidden();

  const dimensions = await page.evaluate(() => ({
    viewport: window.innerWidth,
    document: document.documentElement.scrollWidth
  }));
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport);
});

test("presence appears and disappears immediately across two browser sessions", async ({ browser }) => {
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const firstPage = await firstContext.newPage();
  const secondPage = await secondContext.newPage();

  await firstPage.goto("/");
  await expect(firstPage.getByText("Live", { exact: true })).toBeVisible();
  await secondPage.goto("/");
  await expect(secondPage.getByText("Live", { exact: true })).toBeVisible();

  const secondIdentity = secondPage.locator(".identityDetail").first().locator("strong");
  await expect(secondIdentity).not.toHaveText("Joining...");
  const secondName = await secondIdentity.innerText();
  await expect(
    firstPage.getByRole("button", { name: `${secondName} Online now` })
  ).toBeVisible();

  await secondContext.close();
  await expect(
    firstPage.getByRole("button", { name: `${secondName} Online now` })
  ).toHaveCount(0);

  await firstContext.close();
});

