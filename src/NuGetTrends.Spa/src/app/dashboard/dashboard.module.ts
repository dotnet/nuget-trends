import {NgModule} from '@angular/core';
import {DashboardComponent} from './dashboard.component';
import {PackagesService} from './common/packages.service';
import {SharedModule} from '../shared/shared.module';

@NgModule({
  imports: [SharedModule],
  declarations: [
    DashboardComponent
  ],
  providers: [PackagesService],
  exports: [DashboardComponent]
})
export class DashboardModule {}
