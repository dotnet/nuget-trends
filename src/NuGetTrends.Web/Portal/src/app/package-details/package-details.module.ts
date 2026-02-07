import { NgModule } from '@angular/core';
import { SharedModule } from '../shared/shared.module';
import { PackageDetailsComponent } from './package-details.component';

@NgModule({
  declarations: [PackageDetailsComponent],
  imports: [SharedModule],
  exports: [PackageDetailsComponent]
})
export class PackageDetailsModule {
}
