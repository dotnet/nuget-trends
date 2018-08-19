import { AfterViewInit, Component, ElementRef, HostListener, ViewChild } from '@angular/core';
import { FormControl } from '@angular/forms';
import { catchError, debounceTime, distinctUntilChanged, filter, map, switchMap, tap } from 'rxjs/operators';
import { EMPTY, fromEvent, Observable } from 'rxjs';
import { PackagesService, AddPackageService } from '../../../dashboard/common/';
import { IPackageDownloadHistory, IPackageSearchResult } from '../../../dashboard/common/package-models';

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
        debounceTime(100),
        tap((value: string) => {
          this.showResults = !!value;
        }),
        distinctUntilChanged(),
        filter((value: string) => value && !!value.trim()),
        tap(() => this.isSearching = true),
        switchMap((query: string) => this.packagesService.searchPackage(query)),
        catchError<IPackageSearchResult[], never>((err, caught) => {
          // TODO: Show some message to the user that the search failed.
          return EMPTY;
        }),
        tap(() => this.isSearching = false));
  }

  @HostListener('document:click', ['$event'])
  checkIfInputWasClicked(event) {
    this.showResults = this.searchBox.nativeElement.contains(event.target);
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

  focusElementAndCheckForResults() {
    this.searchBox.nativeElement.focus();
    this.showResults = !!this.results$;
  }

}
