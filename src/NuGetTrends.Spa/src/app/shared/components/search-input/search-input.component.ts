import {AfterViewInit, Component, ElementRef, HostListener, ViewChild} from '@angular/core';
import {FormControl} from '@angular/forms';
import {debounceTime, distinctUntilChanged, filter, map, switchMap, tap} from 'rxjs/operators';
import {fromEvent, Observable} from 'rxjs';

import {PackagesService, AddPackageService} from '../../../dashboard/common/';
import {IPackageDownloadHistory, IPackageSearchResult} from '../../../dashboard/common/package-models';

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
  showResults = true;

  private readonly searchComponentNode: any;

  constructor(
    private packagesService: PackagesService,
    private addPackageService: AddPackageService,
    private element: ElementRef) {
    this.searchComponentNode = this.element.nativeElement.parentNode;
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

  @HostListener('document:click', ['$event.path'])
  @HostListener('document:touchstart', ['$event.path'])
  onClickOutside($event: Array<any>) {
    // showResults is true when the click happens inside the parentComponent <app-search-input>
    // anywhere else it will be false, meaning should be closed
    this.showResults = $event.find(node => node === this.searchComponentNode);
  }

  /**
   * Calls api to get the historical data for the selected package
   * @param packageId
   */
  packageSelected(packageId: string) {
    this.packagesService.getPackageDownloadHistory(packageId)
      .subscribe((packageHistory: IPackageDownloadHistory) => {
        this.addPackageService.addPackage(packageHistory);
        this.showResults = false;
        this.queryField.setValue('');
        this.searchBox.nativeElement.focus();
      });
  }
}
