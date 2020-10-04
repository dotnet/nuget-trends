import { AfterViewInit, Component, ElementRef, ErrorHandler, ViewChild, ViewEncapsulation } from '@angular/core';
import { FormControl } from '@angular/forms';
import { Router } from '@angular/router';
import { MatAutocomplete, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { catchError, debounceTime, distinctUntilChanged, filter, map, startWith, switchMap, tap } from 'rxjs/operators';
import { EMPTY, Observable } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { IPackageDownloadHistory, IPackageSearchResult, SearchType } from '../../models/package-models';
import { PackagesService, PackageInteractionService } from '../../../core';

@Component({
  selector: 'app-search-input',
  templateUrl: './search-input.component.html',
  styleUrls: ['./search-input.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SearchInputComponent implements AfterViewInit {
  @ViewChild(MatAutocomplete) autoComplete: MatAutocomplete;
  @ViewChild('searchBox') searchBox: ElementRef;

  queryField: FormControl = new FormControl('');
  results$: Observable<IPackageSearchResult[]>;
  isSearching = false;
  showResults = true;

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

    this.results$ = this.queryField.valueChanges.pipe(
      startWith(''),
      map((value: string) => value),
      debounceTime(300),
      distinctUntilChanged(),
      filter((value: string) => value && !!value.trim()),
      tap(() => this.isSearching = true),
      switchMap((term: string) => this.searchNuGet(term.trim(), this.packageInteractionService.searchType)),
      catchError((_, caught) => {
        this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
        this.isSearching = false;
        return caught;
      }),
      tap((value: IPackageSearchResult[]) => {
        this.showInfoIfResultIsEmpty(value);
        this.isSearching = false;
      }));
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
      switch (this.packageInteractionService.searchType) {
        case SearchType.NuGetPackage:
          this.toastr.info('No packages found!');
          break;
        case SearchType.Framework:
          this.toastr.info('No frameworks found!');
          break;
        default:
          this.toastr.info('Nothing found!');
          break;
      }
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
