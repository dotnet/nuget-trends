import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {CommonModule} from '@angular/common';
import {SearchInputComponent} from './components/search-input/search-input.component';
import { PackageListComponent } from './components/package-list/package-list.component';

@NgModule({
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  declarations: [SearchInputComponent, PackageListComponent],
  providers: [],
  exports: [CommonModule, FormsModule, ReactiveFormsModule, SearchInputComponent, PackageListComponent]
})
export class SharedModule {
}
