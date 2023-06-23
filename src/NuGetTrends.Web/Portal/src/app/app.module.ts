import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { NgModule, ErrorHandler } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { Router } from '@angular/router';
import * as Sentry from '@sentry/angular-ivy';
import { Replay } from "@sentry/replay";

import { AppComponent } from './app.component';
import { AppRoutingModule } from './app-routes.module';
import { environment } from '../environments/environment';
import { PackagesModule } from './packages/packages.module';
import { SharedModule } from './shared/shared.module';
import { HomeModule } from './home/home.module';
import { CoreModule } from './core/core.module';

Sentry.init({
  dsn: environment.SENTRY_DSN,
  environment: environment.name,
  tunnel: environment.SENTRY_TUNNEL,
  enableTracing: true,
  replaysSessionSampleRate: 1.0,
  replaysOnErrorSampleRate: 1.0,

  integrations: [
    new Replay({
      // No PII here so lets get the texts
      maskAllText: false,
      blockAllMedia: false,
    }),
    new Sentry.BrowserTracing({
      routingInstrumentation: Sentry.instrumentAngularRouting,
      idleTimeout: 30000,
      heartbeatInterval:10000,
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
