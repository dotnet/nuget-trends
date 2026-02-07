import { NgModule, Optional, SkipSelf } from '@angular/core';
import { CommonModule } from '@angular/common';

import { throwIfAlreadyLoaded } from './module-import-guard';
import { PackagesService, PackageInteractionService } from './';
import { FooterComponent } from './footer/footer.component';
import { NavigationComponent } from './navigation/navigation.component';
import { ThemeToggleComponent } from './theme/theme-toggle.component';
import { ThemeService } from './theme/theme.service';

@NgModule({
  imports: [CommonModule],
  declarations: [FooterComponent, NavigationComponent, ThemeToggleComponent],
  exports: [FooterComponent, NavigationComponent, ThemeToggleComponent],
  providers: [PackagesService, PackageInteractionService, ThemeService]
})
export class CoreModule {
  constructor(@Optional() @SkipSelf() parentModule: CoreModule) {
    throwIfAlreadyLoaded(parentModule, 'CoreModule');
  }
}
