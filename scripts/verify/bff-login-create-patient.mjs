// scripts/verify/bff-login-create-patient.mjs
//
// Reusable Playwright driver for the BFF (cookie + OIDC) auth flow, so an agent
// (or a human) can log in and exercise an authenticated Scheduling API endpoint
// headlessly — no manual clicking.
//
// What it does:
//   1. Hits the Scheduling BFF login endpoint (`/auth/login`), which triggers the
//      OIDC challenge and redirects to the Duende IdentityServer login page.
//   2. Fills the Identity Razor login form (Input.Email / Input.Password) and submits.
//   3. Follows the redirect back to the BFF, which sets the auth cookie.
//   4. Confirms the session via `/auth/current-user`.
//   5. Optionally POSTs a new Patient to `/api/patients` (carrying the cookie +
//      X-Requested-With, which the Angular client also sends) and prints the result.
//
// The patient create is what publishes `PatientCreatedIntegrationEvent`, so this is
// the trigger used to verify the cross-context messaging flow (MT→MT or W→W).
//
// Usage (Windows / PowerShell), pointing NODE at the globally-cached Playwright pkg:
//   $env:NODE_PATH = "$env:LOCALAPPDATA\npm-cache\_npx\<hash>\node_modules"
//   $env:BASE_URL="https://localhost:7101"; $env:EMAIL="admin@test.com"; $env:PASSWORD="Admin123!"
//   $env:HEADED="1"; $env:CREATE_PATIENT="1"
//   node scripts/verify/bff-login-create-patient.mjs
//
// Env vars:
//   BASE_URL        Scheduling API base (default https://localhost:7101)
//   EMAIL/PASSWORD  Identity credentials (seeded admin: admin@test.com / Admin123!)
//   HEADED          "1" to show the browser (watch it happen), else headless
//   CREATE_PATIENT  "1" to POST a patient after login
//   SLOWMO          ms to slow each action when HEADED (default 250 when headed)
//   STATE_OUT       path to save storageState JSON (cookie) for reuse (optional)
//
// Exit code 0 on success; non-zero on any failure (so it is CI/agent friendly).

// Resolve Playwright. ESM `import` ignores NODE_PATH, so when Playwright isn't
// installed locally (e.g. it lives in the global npx cache), point PLAYWRIGHT_MODULE
// at the package's entry file (absolute path or file:// URL), e.g.
//   $env:PLAYWRIGHT_MODULE="$env:LOCALAPPDATA\npm-cache\_npx\<hash>\node_modules\playwright\index.js"
import { pathToFileURL } from 'node:url';
import { isAbsolute } from 'node:path';
const pwSpec = process.env.PLAYWRIGHT_MODULE
  ? (isAbsolute(process.env.PLAYWRIGHT_MODULE) ? pathToFileURL(process.env.PLAYWRIGHT_MODULE).href : process.env.PLAYWRIGHT_MODULE)
  : 'playwright';
const pwMod = await import(pwSpec);
const chromium = pwMod.chromium ?? pwMod.default?.chromium;
if (!chromium) throw new Error('could not load Playwright chromium from ' + pwSpec);

const BASE = process.env.BASE_URL ?? 'https://localhost:7101';
const EMAIL = process.env.EMAIL ?? 'admin@test.com';
const PASSWORD = process.env.PASSWORD ?? 'Admin123!';
const HEADED = process.env.HEADED === '1';
const CREATE_PATIENT = process.env.CREATE_PATIENT === '1';
const SLOWMO = Number(process.env.SLOWMO ?? (HEADED ? 250 : 0));

const log = (...a) => console.log('[bff-login]', ...a);

const browser = await chromium.launch({ headless: !HEADED, slowMo: SLOWMO });
const context = await browser.newContext({ ignoreHTTPSErrors: true });
const page = await context.newPage();

let exitCode = 0;
try {
  // 1–3. Trigger the OIDC challenge and complete the Identity login form.
  log(`navigating to ${BASE}/auth/login`);
  await page.goto(`${BASE}/auth/login?returnUrl=/`, { waitUntil: 'domcontentloaded' });

  // We are now on the IdentityServer login page (different origin/port).
  await page.waitForSelector('#Input_Email, input[name="Input.Email"]', { timeout: 20000 });
  log(`login page: ${page.url()}`);
  await page.fill('#Input_Email, input[name="Input.Email"]', EMAIL);
  await page.fill('#Input_Password, input[name="Input.Password"]', PASSWORD);
  await Promise.all([
    page.waitForURL((u) => u.toString().startsWith(BASE), { timeout: 20000 }).catch(() => {}),
    page.click('button[value="login"], button[type="submit"]'),
  ]);
  // Some Duende setups land on a consent screen first; approve if present.
  if (/consent/i.test(page.url())) {
    log('consent screen — approving');
    await page.click('button[value="yes"], button[name="button"][value="yes"]').catch(() => {});
    await page.waitForURL((u) => u.toString().startsWith(BASE), { timeout: 20000 }).catch(() => {});
  }
  log(`back at app: ${page.url()}`);

  // 4. Confirm the authenticated session through the BFF.
  const who = await page.request.get(`${BASE}/auth/current-user`, { failOnStatusCode: false });
  log(`/auth/current-user -> ${who.status()}`);
  const whoBody = await who.text();
  log(`current-user body: ${whoBody.slice(0, 300)}`);
  if (who.status() !== 200) throw new Error(`login did not establish a session (current-user=${who.status()})`);

  if (process.env.STATE_OUT) {
    await context.storageState({ path: process.env.STATE_OUT });
    log(`saved storageState -> ${process.env.STATE_OUT}`);
  }

  // 5. Optionally create a Patient (publishes PatientCreatedIntegrationEvent).
  if (CREATE_PATIENT) {
    const stamp = process.env.PATIENT_STAMP ?? String(Date.now());
    const patient = {
      firstName: 'Verify',
      lastName: `WtoW-${stamp}`,
      email: `verify.wtow.${stamp}@test.com`,
      dateOfBirth: '1990-01-01T00:00:00Z',
      phoneNumber: '+3212345678',
      status: process.env.PATIENT_STATUS ?? 'Active',
    };
    log(`POST ${BASE}/api/patients ${JSON.stringify(patient)}`);
    const res = await page.request.post(`${BASE}/api/patients`, {
      headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
      data: patient,
      failOnStatusCode: false,
    });
    const body = await res.text();
    log(`create patient -> ${res.status()}`);
    log(`response: ${body.slice(0, 500)}`);
    // Print a machine-greppable marker for the orchestrator.
    console.log(`RESULT patient_status=${res.status()} body=${body.replace(/\s+/g, ' ').slice(0, 300)}`);
    if (res.status() >= 400) throw new Error(`create patient failed (${res.status()})`);
  }

  log('done');
} catch (err) {
  exitCode = 1;
  console.error('[bff-login] ERROR', err?.message ?? err);
} finally {
  if (HEADED) await page.waitForTimeout(2500); // brief pause so you can see the end state
  await browser.close();
  process.exit(exitCode);
}
