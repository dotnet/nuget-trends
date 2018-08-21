import {NgModule} from '@angular/core';
import {DashboardComponent} from './dashboard.component';
import {PackagesService, PackageInteractionService} from './common/';
import {SharedModule} from '../shared/shared.module';

@NgModule({
  imports: [SharedModule],
  declarations: [
    DashboardComponent
  ],
  providers: [PackagesService, PackageInteractionService],
  exports: [DashboardComponent]
})
export class DashboardModule {}
