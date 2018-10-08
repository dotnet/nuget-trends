import {AfterViewInit, Component, ElementRef, HostListener, ViewChild} from '@angular/core';
import {FormControl} from '@angular/forms';
import {Router} from '@angular/router';
import {catchError, debounceTime, distinctUntilChanged, filter, map, switchMap, tap} from 'rxjs/operators';
import {EMPTY, fromEvent, Observable, pipe} from 'rxjs';
import {ToastrService} from 'ngx-toastr';

import {IPackageDownloadHistory, IPackageSearchResult, SearchType} from '../../models/package-models';
import {PackagesService, PackageInteractionService} from '../../../core';

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
    this.results$ = fromEvent(this.searchBox.nativeElement, 'keyup')
      .pipe(
        map((event: KeyboardEvent) => (event.target as HTMLInputElement).value),
        debounceTime(200),
        tap((value: string) => {
          this.showResults = !!value;
        }),
        distinctUntilChanged(),
        filter((value: string) => value && !!value.trim()),
        tap(() => this.isSearching = true),
        switchMap((term: string) => this.searchNuget(term, this.packageInteractionService.searchType)),
        catchError((err, caught) => {
          this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
          this.isSearching = false;
          return caught;
        }),
        tap(() => this.isSearching = false));
  }

  @HostListener('document:click', ['$event'])
  checkIfInputWasClicked(event) {
    this.showResults = this.searchBox.nativeElement.contains(event.target);
  }

  /**
   * Triggered when the user selects a result from the search.
   * Calls the api to get the historical data for the selected package
   * @param packageId
   */
  searchItemSelected(packageId: string): void {
    switch (this.packageInteractionService.searchType) {
      case SearchType.NuGetPackage:
        this.getNuGetPackageHistory(packageId, this.packageInteractionService.searchPeriod);
        break;
      case SearchType.Framework:
        this.getFrameworkHistory(packageId, this.packageInteractionService.searchPeriod);
        break;
    }
  }

  focusElementAndCheckForResults(): void {
    this.searchBox.nativeElement.focus();
    this.showResults = !!this.results$;
  }

  /**
   * Call the endpoint to search for either packages or frameworks
   * @param term
   * @param searchType
   */
  private searchNuget(term: string, searchType: SearchType): Observable<IPackageSearchResult[]> {
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
    this.searchBox.nativeElement.focus();
  }

}
