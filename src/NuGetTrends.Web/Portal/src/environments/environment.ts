// This file can be replaced during build by using the `fileReplacements` array.
// `ng build ---prod` replaces `environment.ts` with `environment.prod.ts`.
// The list of file replacements can be found in `angular.json`.

export const environment = {
  name: 'local',
  production: false,
  API_URL: 'https://localhost:5001',
  MAX_CHART_ITEMS: 6,
  SENTRY_DSN: 'https://85a592e835c64ca3a97d93776c12e947@sentry.io/1266321',
  SENTRY_TUNNEL: 'https://localhost:5001/t'
};

/*
 * In development mode, to ignore zone related error stack frames such as
 * `zone.run`, `zoneDelegate.invokeTask` for easier debugging, you can
 * import the following file, but please comment it out in production mode
 * because it will have performance impact when throw error
 */
// import 'zone.js/plugins/zone-error';  // Included with Angular CLI.
