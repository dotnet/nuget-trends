import {FormsModule} from '@angular/forms';
import {NgModule} from '@angular/core';
import {DashboardComponent} from './dashboard.component';
import {PackagesService} from './common/packages.service';

@NgModule({
  imports: [FormsModule],
  declarations: [
    DashboardComponent
  ],
  providers: [PackagesService],
  exports: [DashboardComponent]
})
export class DashboardModule {}
