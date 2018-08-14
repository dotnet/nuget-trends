import {AfterViewInit, Component, ElementRef, ViewChild} from '@angular/core';
import {PackagesService} from '../../../dashboard/common/packages.service';
import {IPackageSearchResult} from '../../../dashboard/common/package-models';
import {FormControl} from '@angular/forms';

import {debounceTime, distinctUntilChanged, filter, switchMap, tap, map} from 'rxjs/operators';
import {Observable, fromEvent} from 'rxjs';

@Component({
  selector: 'app-search-input',
  templateUrl: './search-input.component.html',
  styleUrls: ['./search-input.component.scss']
})
export class SearchInputComponent implements AfterViewInit {
  @ViewChild('searchBox') searchBox: ElementRef;

  queryField: FormControl = new FormControl('');
  results$: Observable<IPackageSearchResult[]>;
  isSearching = false;

  constructor(private packagesService: PackagesService) {
  }

  ngAfterViewInit(): void {
    this.results$ = fromEvent(this.searchBox.nativeElement, 'keyup')
      .pipe(
        map((event: KeyboardEvent) => (event.target as HTMLInputElement).value),
        filter((value: string) => value && !!value.trim()),
        debounceTime(300),
        distinctUntilChanged(),
        tap(() => this.isSearching = true),
        switchMap((query: string) => this.packagesService.searchPackage(query)),
        tap(() => this.isSearching = false));
  }

  packageSelected(packageId: string) {
    console.log(packageId);
  }
}
