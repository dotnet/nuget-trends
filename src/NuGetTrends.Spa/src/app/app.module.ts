import {BrowserModule} from '@angular/platform-browser';
import {BrowserAnimationsModule} from '@angular/platform-browser/animations';
import {NgModule, ErrorHandler} from '@angular/core';
import {DatePipe} from '@angular/common';
import {init, captureException} from '@sentry/browser';
import {HttpClientModule} from '@angular/common/http';

import {AppComponent} from './app.component';
import {AppRoutingModule} from './app-routes.module';
import {environment} from '../environments/environment';
import {PackagesModule} from './packages/packages.module';
import {SharedModule} from './shared/shared.module';
import {HomeModule} from './home/home.module';
import {CoreModule} from './core/core.module';

init({dsn: environment.SENTRY_DSN});

export class SentryErrorHandler extends ErrorHandler {
  handleError(err: any): void {
    captureException(err.originalError || err);
    if (!environment.production) {
      super.handleError(err);
    }
  }
}

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
  providers: [DatePipe, {provide: ErrorHandler, useClass: SentryErrorHandler}],
  bootstrap: [AppComponent]
})
export class AppModule {
}
