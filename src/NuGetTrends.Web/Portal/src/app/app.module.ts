import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { NgModule, ErrorHandler } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { Router } from '@angular/router';
import * as Sentry from '@sentry/angular-ivy';
import { Replay } from "@sentry/replay";
import { HttpClient, CaptureConsole, ReportingObserver } from "@sentry/integrations";
import { Feedback } from '@sentry-internal/feedback';
import { getCanvasManager } from '@sentry-internal/rrweb';

import { AppComponent } from './app.component';
import { AppRoutingModule } from './app-routes.module';
import { environment } from '../environments/environment';
import { PackagesModule } from './packages/packages.module';
import { SharedModule } from './shared/shared.module';
import { HomeModule } from './home/home.module';
import { CoreModule } from './core/core.module';
import { getCanvasManager } from '@sentry-internal/rrweb';

Sentry.init({
  dsn: environment.SENTRY_DSN,
  environment: environment.name,
  tunnel: environment.SENTRY_TUNNEL,
  enableTracing: true,
  replaysSessionSampleRate: 1.0,
  replaysOnErrorSampleRate: 1.0,
  // @ts-ignore - TODO: Remove on next bump, not in types yet
  profilesSampleRate: 1.0,
  integrations: [
    new Replay({
      // No PII here so lets get the texts
      maskAllText: false,
      blockAllMedia: false,
      networkDetailAllowUrls: environment.NETWORK_DETAIL_ALLOW_URLS,
      networkRequestHeaders: ["referrer", "sentry-trace", "baggage"],
      networkResponseHeaders: ["Server"],
      _experiments: {
        canvas: {
          fps: 4,
          quality: 0.6,
          manager: getCanvasManager,
        },
      },
    }),
    new Sentry.BrowserTracing({
      routingInstrumentation: Sentry.instrumentAngularRouting,
      idleTimeout: 30000,
      heartbeatInterval:10000,
      _experiments: {
        enableInteractions: true,
        // If you want automatic route transactions in react or similar
        onStartRouteTransaction: Sentry.onProfilingStartRouteTransaction,
      }
    }),
    new Sentry.BrowserProfilingIntegration(),
    new HttpClient(),
    new CaptureConsole(),
    new ReportingObserver(),
    new Feedback({
      colorScheme: "light", // no dark theme yet
      themeLight: {
        submitBackground: '#215C84',
        submitBackgroundHover: '#A2BACB',
        submitBorder: '#153b54',
        inputBackground: '#ffffff',
        inputForeground: '#374151',
      },
    }),
  ],
});

@NgModule({
  declarations: [
    AppComponent
  ],
  imports: [
    Sentry.TraceModule,
    AppRoutingModule,
    BrowserModule,
    BrowserAnimationsModule,
    HttpClientModule,
    CoreModule,
    SharedModule,
    PackagesModule,
    HomeModule
  ],
  providers: [
    DatePipe,
    {
      provide: ErrorHandler,
      useValue: Sentry.createErrorHandler({
        showDialog: environment.production, // User Feedback enabled in production
        logErrors: !environment.production // log console errors in dev mode
      }),
    },
    {
      provide: Sentry.TraceService,
      deps: [Router],
    },
  ],
  bootstrap: [AppComponent]
})
export class AppModule {

  // force instantiating Sentry tracing
  // https://docs.sentry.io/platforms/javascript/guides/angular/#monitor-performance
  constructor(_: Sentry.TraceService) { }
}
