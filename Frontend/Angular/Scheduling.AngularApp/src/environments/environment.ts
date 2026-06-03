// Dev API URLs are derived from the SPA's own port so each worktree slot talks to
// its own backend. The SPA is served on 7003 + 100*(slot-1); its APIs sit on the same
// slot (7001/7002 + the same offset). Slot 1 (the main checkout) resolves to 7001/7002
// unchanged. See docs/adr/0002-worktree-slots.md.
// NOTE: this repeats the +100*(slot-1) port formula that also lives in
// Aspire.AppHost/WorktreeSlot.Port and Identity.WebApi IdentityServerConfig.SlotPort —
// the three collapse into one source of truth once IdentityServer client URLs become
// config-driven (Phase 9 BFF).
const SPA_BASE = 7003
const SCHEDULING_BASE = 7001
const BILLING_BASE = 7002

const port = Number(window.location.port)
const slot = port >= SPA_BASE ? Math.round((port - SPA_BASE) / 100) + 1 : 1
const offset = 100 * (slot - 1)

export const environment = {
  production: false,
  schedulingApiUrl: `https://localhost:${SCHEDULING_BASE + offset}`,
  billingApiUrl: `https://localhost:${BILLING_BASE + offset}`
}
