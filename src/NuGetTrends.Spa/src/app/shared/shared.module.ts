import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {CommonModule} from '@angular/common';
import {SearchInputComponent} from './components/search-input/search-input.component';
import {PackageListComponent} from './components/package-list/package-list.component';
import { SearchTypeComponent } from './components/search-type/search-type.component';

@NgModule({
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  declarations: [SearchInputComponent, PackageListComponent, SearchTypeComponent],
  exports: [CommonModule, FormsModule, ReactiveFormsModule, SearchInputComponent, PackageListComponent, SearchTypeComponent]
})
export class SharedModule {
}
