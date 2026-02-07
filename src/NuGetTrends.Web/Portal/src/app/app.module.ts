import { BrowserModule } from "@angular/platform-browser";
import { BrowserAnimationsModule } from "@angular/platform-browser/animations";
import { NgModule, ErrorHandler } from "@angular/core";
import { DatePipe } from "@angular/common";
import { HttpClientModule } from "@angular/common/http";
import { Router } from "@angular/router";
import * as Sentry from "@sentry/angular";

import { AppComponent } from "./app.component";
import { AppRoutingModule } from "./app-routes.module";
import { environment } from "../environments/environment";
import { PackagesModule } from "./packages/packages.module";
import { SharedModule } from "./shared/shared.module";
import { HomeModule } from "./home/home.module";
import { CoreModule } from "./core/core.module";
import { filterNoisyErrors } from "./core/sentry-error-filter";

Sentry.init({
  dsn: environment.SENTRY_DSN,
  environment: environment.name,
  tunnel: environment.SENTRY_TUNNEL,
  tracesSampleRate: 1.0,
  enableLogs: true,
  replaysSessionSampleRate: 1.0,
  replaysOnErrorSampleRate: 1.0,
  profileSessionSampleRate: 1.0,
  beforeSend: filterNoisyErrors,
  integrations: [
    Sentry.replayIntegration({
      // No PII here so lets get the texts
      maskAllText: false,
      blockAllMedia: false,
      networkDetailAllowUrls: environment.NETWORK_DETAIL_ALLOW_URLS,
      networkRequestHeaders: ["referrer", "sentry-trace", "baggage"],
      networkResponseHeaders: ["Server"],
    }),
    Sentry.replayCanvasIntegration(),
    Sentry.feedbackIntegration({
      colorScheme: "system",
      themeLight: {
        accentBackground: "#215C84",
      },
      themeDark: {
        accentBackground: "#4a9fd4",
      },
    }),
    Sentry.browserTracingIntegration({
      idleTimeout: 30000,
      beforeStartSpan: (context) => {
        return {
          ...context,
          // Parameterize the /packages/:packageId route to avoid high-cardinality transaction names
          name: location.pathname.replace(/^\/packages\/[^/?]+$/, '/packages/:packageId'),
        };
      },
    }),
    Sentry.browserProfilingIntegration(),
  ],
});

@NgModule({
  declarations: [AppComponent],
  imports: [
    Sentry.TraceModule,
    AppRoutingModule,
    BrowserModule,
    BrowserAnimationsModule,
    HttpClientModule,
    CoreModule,
    SharedModule,
    PackagesModule,
    HomeModule,
  ],
  providers: [
    DatePipe,
    {
      provide: ErrorHandler,
      useValue: Sentry.createErrorHandler({
        showDialog: environment.production, // User Feedback enabled in production
        logErrors: !environment.production, // log console errors in dev mode
      }),
    },
    {
      provide: Sentry.TraceService,
      deps: [Router],
    },
  ],
  bootstrap: [AppComponent],
})
export class AppModule {
  // force instantiating Sentry tracing
  // https://docs.sentry.io/platforms/javascript/guides/angular/#monitor-performance
  constructor(_: Sentry.TraceService) {}
}
