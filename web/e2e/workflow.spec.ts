import { test, expect } from "@playwright/test";
test("review original evidence, add receipt, and export revised brief", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Explore Maya’s case" }).click();
  await page
    .getByRole("button", { name: "Sample walkthrough", exact: true })
    .click();
  await expect(
    page.getByText("Human-authored sample results. No model was called."),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Create reviewed brief" }),
  ).toBeDisabled();
  await page.getByRole("button", { name: "Project agreement · p.1" }).click();
  await expect(page.getByRole("dialog")).toContainText("$6,000.00");
  await page.getByRole("button", { name: "Close ×" }).click();
  for (let i = 0; i < 6; i++) {
    await page
      .getByRole("button", { name: "Confirm", exact: true })
      .nth(i)
      .click();
    await expect(page.getByRole("status")).toHaveCount(0);
  }
  await page.getByRole("button", { name: "Create reviewed brief" }).click();
  await expect(page.locator(".brief")).toContainText("$3,000");
  await page.getByRole("tab", { name: "01 Sources" }).click();
  await page.getByRole("button", { name: "Add October 2 receipt" }).click();
  await page.getByRole("tab", { name: "03 Consultation brief" }).click();
  await expect(
    page.getByRole("button", { name: "Print / save PDF" }),
  ).toBeDisabled();
  await page
    .getByRole("button", { name: "Sample walkthrough", exact: true })
    .click();
  await page.getByRole("tab", { name: "02 Review & clarify" }).click();
  await page
    .locator(".fact.pending")
    .getByRole("button", { name: "Confirm", exact: true })
    .click();
  await expect(page.getByRole("status")).toHaveCount(0);
  await page.getByRole("button", { name: "Create reviewed brief" }).click();
  await expect(page.locator(".changes")).toContainText(
    "Documented balance changed from $3,000.00 to $2,000.00.",
  );
  await expect(
    page.getByRole("button", { name: "Print / save PDF" }),
  ).toBeEnabled();
  await page.screenshot({
    path: ".artifacts/consultready-brief.png",
    fullPage: true,
  });
});
test("mobile workspace has no horizontal overflow", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");
  await page.getByRole("button", { name: "Explore Maya’s case" }).click();
  await page
    .getByRole("button", { name: "Sample walkthrough", exact: true })
    .click();
  await expect(page.locator(".fact")).toHaveCount(6);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: ".artifacts/consultready-mobile.png",
    fullPage: true,
  });
});

test("local corrections and clarifications remain labeled after reanalysis", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByRole("button", { name: "Explore Maya’s case" }).click();
  await page
    .getByRole("button", { name: "Sample walkthrough", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Correct", exact: true })
    .first()
    .click();
  await page
    .getByLabel("Corrected statement")
    .fill("I recall a revised project price of $5,000.");
  await page.getByLabel("Corrected amount (USD)").fill("5000");
  await page
    .getByRole("button", { name: "Save correction", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await page
    .getByRole("combobox", {
      name: "Clarification: Which version did the positive response refer to?",
    })
    .selectOption("earlier");
  await expect(
    page.getByText("Your clarification:", { exact: false }),
  ).toContainText("recollection");
  await page
    .getByRole("button", { name: "Sample walkthrough", exact: true })
    .click();
  await expect(page.locator(".fact").first()).toContainText(
    "I recall a revised project price of $5,000.",
  );
  await expect(page.locator(".fact").first()).toContainText("User correction");
  await page.getByRole("button", { name: "Project agreement · p.1" }).click();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
