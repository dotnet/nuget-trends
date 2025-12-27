import { NgModule } from '@angular/core';
import { PackagesComponent } from './packages.component';
import { SharedModule } from '../shared/shared.module';

@NgModule({
  declarations: [PackagesComponent],
  imports: [SharedModule],
  exports: [PackagesComponent]
})
export class PackagesModule {
}
