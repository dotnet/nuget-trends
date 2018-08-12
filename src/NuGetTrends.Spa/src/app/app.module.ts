import { BrowserModule } from '@angular/platform-browser';
import { NgModule, ErrorHandler } from '@angular/core';
import { DatePipe } from '@angular/common';
import { init, captureException } from '@sentry/browser';
import {HttpClientModule} from '@angular/common/http';

import { AppComponent } from './app.component';
import { AppRoutingModule } from './app-routes.module';
import { environment } from '../environments/environment';
import {DashboardModule} from './dashboard/dashboard.module';

init({ dsn: 'https://99693a201d194623afee8262c4499e46@sentry.io/1260895', });
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
    HttpClientModule,
    DashboardModule
  ],
  providers: [ DatePipe, { provide: ErrorHandler, useClass: SentryErrorHandler } ],
  bootstrap: [AppComponent]
})
export class AppModule { }
