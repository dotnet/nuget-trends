import {NgModule} from '@angular/core';
import {FormsModule, ReactiveFormsModule} from '@angular/forms';
import {CommonModule} from '@angular/common';
import {SearchInputComponent} from './components/search-input/search-input.component';

@NgModule({
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  declarations: [SearchInputComponent],
  providers: [],
  exports: [CommonModule, FormsModule, ReactiveFormsModule, SearchInputComponent]
})
export class SharedModule {
}
