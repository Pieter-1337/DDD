// scripts/verify/aspire-trace-capture.mjs
//
// Opens the Aspire dashboard (with its one-time login token), navigates to the
// Traces view, and screenshots it — evidence that a cross-service OpenTelemetry
// trace exists (e.g. scheduling-webapi -> billing-webapi for a Patient create).
//
// Env:
//   DASHBOARD_URL    e.g. https://localhost:17354
//   DASHBOARD_TOKEN  the t=... value from the AppHost "Login to the dashboard" line
//   OUT              screenshot path (default ./aspire-traces.png)
//   HEADED           "1" to show the browser
//   PLAYWRIGHT_MODULE  abs path / file URL to playwright pkg if not locally installed
//
// Usage: see bff-login-create-patient.mjs header for the PLAYWRIGHT_MODULE pattern.

import { pathToFileURL } from 'node:url';
import { isAbsolute } from 'node:path';
const pwSpec = process.env.PLAYWRIGHT_MODULE
  ? (isAbsolute(process.env.PLAYWRIGHT_MODULE) ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : process.env.PLAYWRIGHT_MODULE)
  : 'playwright';
const pwMod = await import(pwSpec);
const chromium = pwMod.chromium ?? pwMod.default?.chromium;

const URL = process.env.DASHBOARD_URL ?? 'https://localhost:17354';
const TOKEN = process.env.DASHBOARD_TOKEN ?? '';
const OUT = process.env.OUT ?? 'aspire-traces.png';
const HEADED = process.env.HEADED === '1';
const log = (...a) => console.log('[aspire-trace]', ...a);

const browser = await chromium.launch({ headless: !HEADED, slowMo: HEADED ? 200 : 0 });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
const page = await ctx.newPage();
let code = 0;
try {
  log(`login ${URL}/login?t=…`);
  await page.goto(`${URL}/login?t=${TOKEN}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);
  log('open Traces');
  await page.goto(`${URL}/traces`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(3000);
  if (process.env.FILTER) {
    log(`filter: ${process.env.FILTER}`);
    const box = page.getByPlaceholder('Filter...').first();
    await box.fill(process.env.FILTER).catch(() => {});
    await page.waitForTimeout(2500);
  }
  await page.screenshot({ path: OUT, fullPage: true });
  log(`screenshot -> ${OUT}`);
  // Open a specific trace by clicking the link whose text matches CLICK_TEXT.
  const clickText = process.env.CLICK_TEXT;
  if (clickText) {
    const link = page.getByText(clickText, { exact: false }).first();
    if (await link.count()) {
      await link.click().catch(() => {});
      await page.waitForTimeout(3000);
      await page.screenshot({ path: OUT.replace(/\.png$/, '-detail.png'), fullPage: true });
      log(`detail screenshot -> ${OUT.replace(/\.png$/, '-detail.png')}`);
    } else {
      log(`CLICK_TEXT "${clickText}" not found`);
    }
  }
} catch (e) {
  code = 1;
  console.error('[aspire-trace] ERROR', e?.message ?? e);
} finally {
  if (HEADED) await page.waitForTimeout(2000);
  await browser.close();
  process.exit(code);
}
