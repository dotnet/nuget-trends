import {AfterViewInit, Component, ElementRef, ViewChild, ViewEncapsulation} from '@angular/core';
import {FormControl} from '@angular/forms';
import {Router} from '@angular/router';
import {MatAutocomplete, MatAutocompleteSelectedEvent} from '@angular/material';
import {catchError, debounceTime, distinctUntilChanged, filter, map, startWith, switchMap, tap} from 'rxjs/operators';
import {EMPTY, Observable, pipe} from 'rxjs';
import {ToastrService} from 'ngx-toastr';

import {IPackageDownloadHistory, IPackageSearchResult, SearchType} from '../../models/package-models';
import {PackagesService, PackageInteractionService} from '../../../core';

@Component({
  selector: 'app-search-input',
  templateUrl: './search-input.component.html',
  styleUrls: ['./search-input.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SearchInputComponent implements AfterViewInit {
  @ViewChild(MatAutocomplete, {static: false}) autoComplete: MatAutocomplete;
  @ViewChild('searchBox', {static: false}) searchBox: ElementRef;

  queryField: FormControl = new FormControl('');
  results$: Observable<IPackageSearchResult[]>;
  isSearching = false;
  showResults = true;

  private readonly searchComponentNode: any;

  private handleApiError = pipe(
    catchError((err, caught) => {
      this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      return EMPTY;
    })
  );

  constructor(
    private router: Router,
    private packagesService: PackagesService,
    private packageInteractionService: PackageInteractionService,
    private element: ElementRef,
    private toastr: ToastrService) {
    this.searchComponentNode = this.element.nativeElement.parentNode;
  }

  ngAfterViewInit(): void {
    // Disables wrapping when navigating using the arrows
    this.autoComplete._keyManager.withWrap(false);
    this.searchBox.nativeElement.focus();

    this.results$ = this.queryField.valueChanges.pipe(
        startWith(''),
        map((value: string) => value),
        debounceTime(300),
        distinctUntilChanged(),
        filter((value: string) => value && !!value.trim()),
        tap(() => this.isSearching = true),
        switchMap((term: string) => this.searchNuGet(term, this.packageInteractionService.searchType)),
        catchError((err, caught) => {
          this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
          this.isSearching = false;
          return caught;
        }),
        tap(() => this.isSearching = false));
  }

  /**
   * Triggered when the user selects a result from the search.
   * Calls the api to get the historical data for the selected package
   * @param event
   */
  searchItemSelected(event: MatAutocompleteSelectedEvent): void {
    // get a hold of the current selected value and clear the autocomplete
    const packageId = event.option.value;
    this.queryField.setValue('');
    event.option.deselect();

    switch (this.packageInteractionService.searchType) {
      case SearchType.NuGetPackage:
        this.getNuGetPackageHistory(packageId, this.packageInteractionService.searchPeriod);
        break;
      case SearchType.Framework:
        this.getFrameworkHistory(packageId, this.packageInteractionService.searchPeriod);
        break;
    }
  }

  /**
   * Call the endpoint to search for either packages or frameworks
   * @param term
   * @param searchType
   */
  private searchNuGet(term: string, searchType: SearchType): Observable<IPackageSearchResult[]> {
    switch (searchType) {
      case SearchType.NuGetPackage:
        return this.packagesService.searchPackage(term);
      case SearchType.Framework:
        return this.packagesService.searchFramework(term);
      default:
        return EMPTY;
    }
  }

  /**
   * Get the download history for the selected NuGet Package
   * If not in the packages page, navigate when results are back.
   * @param packageId
   * @param period
   */
  private getNuGetPackageHistory(packageId: string, period: number): void {
    if (this.router.url.includes('/packages')) {
      this.packagesService.getPackageDownloadHistory(packageId, period)
        .pipe(this.handleApiError)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
          this.feedPackageHistoryResults(packageHistory);
        });
    } else {
      this.packagesService.getPackageDownloadHistory(packageId, period)
        .pipe(this.handleApiError)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
        this.router.navigate(['/packages']).then(() => {
          this.feedPackageHistoryResults(packageHistory);
        });
      });
    }
  }

  /**
   * Get the download history for the selected target Framework
   * If not in the frameworks page, navigate when results are back.
   * @param packageId
   * @param period
   */
  private getFrameworkHistory(packageId: string, period: number): void {
    if (this.router.url.includes('/frameworks')) {
      this.packagesService.getFrameworkDownloadHistory(packageId, period)
        .pipe(this.handleApiError)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
          this.feedPackageHistoryResults(packageHistory);
        });
    } else {
      this.packagesService.getFrameworkDownloadHistory(packageId, period)
        .pipe(this.handleApiError)
        .subscribe((frameworkHistory: IPackageDownloadHistory) => {
          this.router.navigate(['/frameworks']).then(() => {
            this.feedPackageHistoryResults(frameworkHistory);
          });
        });
    }
  }

  /**
   * Marks the item as selected and feed the results to the chart
   * @param packageHistory
   */
  private feedPackageHistoryResults(packageHistory: IPackageDownloadHistory): void {
    this.packageInteractionService.addPackage(packageHistory);
    this.showResults = false;
    this.queryField.setValue('');
  }
}
