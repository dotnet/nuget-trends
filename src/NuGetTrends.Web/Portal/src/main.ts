import { enableProdMode } from '@angular/core';
import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';
import * as Sentry from "@sentry/angular-ivy";

import { AppModule } from './app/app.module';
import { environment } from './environments/environment';

if (environment.production) {
  enableProdMode();
}

const activeTransaction = Sentry.getActiveTransaction();
const bootstrapSpan =
  activeTransaction &&
  activeTransaction.startChild({
    description: "platform-browser-dynamic",
    op: "ui.angular.bootstrap",
  });

platformBrowserDynamic()
  .bootstrapModule(AppModule)
  .then(() => console.log(`Bootstrap success`))
  .catch(err => console.error(err))
  .finally(() => {
    if (bootstrapSpan) {
      bootstrapSpan.finish();
    }
  });
