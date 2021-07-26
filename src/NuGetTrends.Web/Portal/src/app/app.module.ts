import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { NgModule, ErrorHandler } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { Router } from '@angular/router';
import * as Sentry from '@sentry/angular';
import { Integrations } from '@sentry/tracing';

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
  integrations: [
    new Integrations.BrowserTracing({
      routingInstrumentation: Sentry.routingInstrumentation,
    }),
  ],
  tracesSampleRate: 1.0,
  tunnel: environment.SENTRY_TUNNEL,
});

@NgModule({
  declarations: [
    AppComponent
  ],
  imports: [
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
