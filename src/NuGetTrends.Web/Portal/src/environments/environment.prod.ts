export const environment = {
  name: 'production',
  production: true,
  API_URL: '',
  MAX_CHART_ITEMS: 6,
  SENTRY_DSN: 'https://85a592e835c64ca3a97d93776c12e947@sentry.io/1266321',
  // https://docs.sentry.io/platforms/javascript/troubleshooting/#using-the-tunnel-option
  SENTRY_TUNNEL: '/t',
  NETWORK_DETAIL_ALLOW_URLS: [window.location.origin],
};
