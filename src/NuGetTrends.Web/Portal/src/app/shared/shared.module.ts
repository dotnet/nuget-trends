import { NgModule } from '@angular/core';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { ToastrModule } from 'ngx-toastr';

import {
  SearchInputComponent,
  PackageListComponent,
  SearchTypeComponent,
  SearchPeriodComponent,
  LoadingIndicatorComponent,
  LoadingIndicatorInterceptor
} from './components';

@NgModule({
  declarations: [
    SearchInputComponent,
    PackageListComponent,
    SearchTypeComponent,
    SearchPeriodComponent,
    LoadingIndicatorComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    MatAutocompleteModule,
    ToastrModule.forRoot({
      positionClass: 'toast-bottom-right',
      preventDuplicates: true,
    }),
    ReactiveFormsModule,
    BrowserAnimationsModule
  ],
  exports: [
    CommonModule,
    FormsModule,
    MatAutocompleteModule,
    ReactiveFormsModule,
    SearchInputComponent,
    PackageListComponent,
    SearchTypeComponent,
    SearchPeriodComponent,
    LoadingIndicatorComponent
  ],
  providers: [
    {
      provide: HTTP_INTERCEPTORS,
      useClass: LoadingIndicatorInterceptor,
      multi: true
    }
  ],
})
export class SharedModule {
}
