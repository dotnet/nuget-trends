import { AfterViewInit, Component, ElementRef, ErrorHandler, ViewChild, ViewEncapsulation } from '@angular/core';
import { FormControl } from '@angular/forms';
import { Router } from '@angular/router';
import { MatAutocomplete, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { catchError, debounceTime, distinctUntilChanged, filter, map, mapTo, startWith, switchMap, tap } from 'rxjs/operators';
import { EMPTY, Observable, Subject, merge } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import * as Sentry from '@sentry/angular';

import { IPackageDownloadHistory, IPackageSearchResult, SearchType } from '../../models/package-models';
import { PackagesService, PackageInteractionService } from '../../../core';

@Component({
  selector: 'app-search-input',
  templateUrl: './search-input.component.html',
  styleUrls: ['./search-input.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SearchInputComponent implements AfterViewInit {
  @ViewChild(MatAutocomplete) autoComplete!: MatAutocomplete;
  @ViewChild('searchBox') searchBox!: ElementRef;

  queryField: FormControl = new FormControl('');
  results$!: Observable<IPackageSearchResult[]>;
  isSearching = false;
  showResults = true;
  private searchClear$ = new Subject<IPackageSearchResult[]>();

  constructor(
    private router: Router,
    private packagesService: PackagesService,
    private packageInteractionService: PackageInteractionService,
    private toastr: ToastrService,
    private errorHandler: ErrorHandler) {
  }

  ngAfterViewInit(): void {
    // Disables wrapping when navigating using the arrows
    this.autoComplete._keyManager.withWrap(false);
    this.searchBox.nativeElement.focus();

    this.results$ = merge(
       this.queryField.valueChanges.pipe(
        startWith(''),
        map((value: string) => value),
        debounceTime(300),
        distinctUntilChanged(),
        filter((value: string, _: number) => !!value.trim()),
        tap(() => this.isSearching = true),
        switchMap((term: string) => this.searchNuGet(term.trim(), this.packageInteractionService.searchType)),
        catchError((error, caught) => {
          this.errorHandler.handleError(error);
          this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
          this.isSearching = false;
          return caught;
        }),
        tap((value: IPackageSearchResult[]) => {
          this.showInfoIfResultIsEmpty(value);
          this.isSearching = false;
        })), this.searchClear$.pipe(mapTo([])));
  }

  /**
   * Triggered when the user selects a result from the search.
   * Calls the api to get the historical data for the selected package
   */
  async searchItemSelected(event: MatAutocompleteSelectedEvent): Promise<void> {
    // get a hold of the current selected value and clear the autocomplete
    const packageId = event.option.value;
    this.queryField.setValue('');
    event.option.deselect();
    if (this.packageInteractionService.searchType === SearchType.NuGetPackage) {
      await this.getNuGetPackageHistory(packageId, this.packageInteractionService.searchPeriod);
    }
  }

  clear() {
    if (!this.queryField.value) {
      this.searchClear$.next([]);
    }
  }

  /**
   * Call the endpoint to search for either packages or frameworks
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
   * Show an info if the result is empty.
   */
  private showInfoIfResultIsEmpty(value: IPackageSearchResult[]): void {
    if (value && value.length === 0) {
      let message = 'Nothing found!';
      switch (this.packageInteractionService.searchType) {
        case SearchType.NuGetPackage:
          message = 'No packages found!';
          break;
        case SearchType.Framework:
          message = 'No framework found!';
          break;
      }
      Sentry.addBreadcrumb({
        category: 'search.result',
        message,
        level: Sentry.Severity.Info,
      });
      this.toastr.info(message);
    }
  }

  /**
   * Get the download history for the selected NuGet Package
   * If not in the packages page, navigate when results are back.
   */
  private async getNuGetPackageHistory(packageId: string, period: number): Promise<void> {
    try {
      const downloadHistory = await this.packagesService.getPackageDownloadHistory(packageId, period).toPromise();

      if (this.router.url.includes('/packages')) {
        this.feedPackageHistoryResults(downloadHistory);
      } else {
        this.router.navigate(['/packages']).then(() => {
          this.feedPackageHistoryResults(downloadHistory);
        });
      }
    } catch (error) {
      this.errorHandler.handleError(error);
      this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
    }
  }

  /**
   * Marks the item as selected and feed the results to the chart
   */
  private feedPackageHistoryResults(packageHistory: IPackageDownloadHistory): void {
    this.packageInteractionService.addPackage(packageHistory);
    this.showResults = false;
    this.queryField.setValue('');
  }
}
