import {Component, OnInit} from '@angular/core';
import {PackagesService} from '../../../dashboard/common/packages.service';
import {IPackageSearchResult} from '../../../dashboard/common/package-models';
import {FormControl} from '@angular/forms';

import {debounceTime, distinctUntilChanged, filter, switchMap} from 'rxjs/operators';

@Component({
  selector: 'app-search-input',
  templateUrl: './search-input.component.html',
  styleUrls: ['./search-input.component.scss']
})
export class SearchInputComponent implements OnInit {

  results: IPackageSearchResult[] = [];
  queryField: FormControl = new FormControl();

  constructor(private packagesService: PackagesService) {
  }

  ngOnInit() {
    this.queryField.valueChanges
      .pipe(
        debounceTime(300),
        filter((value: string) => value !== null),
        distinctUntilChanged(),
        switchMap((query: string) => this.packagesService.searchPackage(query))
      ).subscribe(result => this.results = result);
  }
  packageSelected(packageId: string) {
    console.log(packageId);
    this.queryField.setValue(null);
    this.results = [];
  }
}
