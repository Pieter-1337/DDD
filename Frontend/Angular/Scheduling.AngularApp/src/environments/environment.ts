// Dev: each worktree slot serves the SPA at 7003 + offset and runs its APIs at the same
// offset above 7001/7002 (see docs/adr/0002-worktree-slots.md). Derive the offset from our
// own port so the SPA always talks to its own slot's backend. Slot 1 (main) → 7003 → offset 0.
const SPA_BASE = 7003
const SCHEDULING_BASE = 7001
const BILLING_BASE = 7002

const port = Number(window.location.port)
const offset = port >= SPA_BASE ? port - SPA_BASE : 0

export const environment = {
  production: false,
  schedulingApiUrl: `https://localhost:${SCHEDULING_BASE + offset}`,
  billingApiUrl: `https://localhost:${BILLING_BASE + offset}`
}
