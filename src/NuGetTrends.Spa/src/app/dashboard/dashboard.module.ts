import {NgModule} from '@angular/core';
import {DashboardComponent} from './dashboard.component';
import {PackagesService, AddPackageService} from './common/';
import {SharedModule} from '../shared/shared.module';

@NgModule({
  imports: [SharedModule],
  declarations: [
    DashboardComponent
  ],
  providers: [PackagesService, AddPackageService],
  exports: [DashboardComponent]
})
export class DashboardModule {}
