# Blazor Migration Rollout Plan

## Environment Variable Changes

### Remove `ASPNETCORE_WEBROOT`

The Angular SPA was served from `Portal/dist`, requiring:

```yaml
- name: ASPNETCORE_WEBROOT
  value: "/App/Portal/dist"
```

Blazor serves static files from `wwwroot/` by default. **Remove this variable entirely** — ASP.NET Core will default to `{ContentRoot}/wwwroot`, which is `/App/wwwroot` in the published output.

### Keep unchanged

```yaml
- name: ASPNETCORE_URLS
  value: "http://+:8080"         # No change needed
- name: ASPNETCORE_CONTENTROOT
  value: "/App"                  # No change needed
```

## What Changed

| Aspect | Before (Angular) | After (Blazor) |
|--------|-----------------|----------------|
| Static files | `/App/Portal/dist/` | `/App/wwwroot/` (default) |
| JS bundles | `main.js`, `runtime.js`, `polyfills.js` | `_framework/blazor.web.js` + WASM assemblies |
| CSS | `styles.css` (built by Angular CLI) | `css/app.css` + `lib/bulma/...` (vendored, no CDN) |
| Rendering | Client-side SPA | SSR + WebAssembly hybrid |
| Interop | None (pure Angular) | `js/themeInterop.js` (charts use Blazor-ApexCharts) |

## Deployment Checklist

- [ ] Remove `ASPNETCORE_WEBROOT` env var from deployment config
- [ ] Deploy new build
- [ ] Verify static assets load: `/css/app.css`, `/images/logo-inverted-400.png`, `/favicon.ico`
- [ ] Verify WASM loads: `/_framework/blazor.web.js` returns 200
- [ ] Verify SSR works: initial page load returns full HTML (not empty `<app-root>`)
- [ ] Verify interactivity: search dropdown appears when typing (requires WASM hydration)
- [ ] Verify chart rendering: selecting a package renders the download chart (Blazor-ApexCharts)
- [ ] Verify API endpoints: `/api/package/search?q=sentry`, `/api/package/trending`
- [ ] Verify Swagger: `/swagger/index.html`
- [ ] Monitor Sentry for new errors post-deploy

## Post-Migration Cleanup

Done — the `Portal/` directory, Angular CI steps, and Playwright Angular e2e workflow have been removed.
