import { NgModule, Optional, SkipSelf } from '@angular/core';

import {throwIfAlreadyLoaded} from './module-import-guard';
import {PackagesService, PackageInteractionService} from './';
import {FooterComponent} from './footer/footer.component';

@NgModule({
  imports: [ ],
  exports: [FooterComponent],
  declarations: [FooterComponent],
  providers: [PackagesService, PackageInteractionService]
})
export class CoreModule {
  constructor( @Optional() @SkipSelf() parentModule: CoreModule) {
    throwIfAlreadyLoaded(parentModule, 'CoreModule');
  }
}
