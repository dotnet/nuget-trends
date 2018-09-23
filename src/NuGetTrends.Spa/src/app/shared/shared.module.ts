import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {CommonModule} from '@angular/common';
import {SearchInputComponent} from './components/search-input/search-input.component';
import {PackageListComponent} from './components/package-list/package-list.component';
import { SearchTypeComponent } from './components/search-type/search-type.component';
import {BrowserAnimationsModule} from '@angular/platform-browser/animations';
import { SearchPeriodComponent } from './components/search-period/search-period.component';

@NgModule({
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    BrowserAnimationsModule],

  declarations: [
    SearchInputComponent,
    PackageListComponent,
    SearchTypeComponent,
    SearchPeriodComponent],

  exports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    SearchInputComponent,
    PackageListComponent,
    SearchTypeComponent,
    SearchPeriodComponent]
})
export class SharedModule {
}
