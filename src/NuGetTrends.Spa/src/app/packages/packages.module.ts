import { NgModule } from '@angular/core';
import { PackagesComponent } from './packages.component';
import { SharedModule } from '../shared/shared.module';

@NgModule({
  imports: [SharedModule],
  declarations: [
    PackagesComponent
  ],
  exports: [PackagesComponent]
})
export class PackagesModule {
}
