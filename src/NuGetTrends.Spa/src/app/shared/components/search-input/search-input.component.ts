import {AfterViewInit, Component, ElementRef, HostListener, ViewChild} from '@angular/core';
import {FormControl} from '@angular/forms';
import {Router} from '@angular/router';
import {catchError, debounceTime, distinctUntilChanged, filter, map, switchMap, tap} from 'rxjs/operators';
import {EMPTY, fromEvent, Observable} from 'rxjs';

import {PackagesService, PackageInteractionService} from '../../common/';
import {IPackageDownloadHistory, IPackageSearchResult} from '../../common/package-models';

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
    private router: Router,
    private packagesService: PackagesService,
    private addPackageService: PackageInteractionService,
    private element: ElementRef) {
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
  packageSelected(packageId: string): void {
    if (this.router.url.includes('/packages')) {
      this.packagesService.getPackageDownloadHistory(packageId)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
          this.selectPackage(packageHistory);
        });
    } else {
      this.packagesService.getPackageDownloadHistory(packageId)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
          this.router.navigate(['/packages']).then(() => {
            this.selectPackage(packageHistory);
          });
        });
    }
  }

  focusElementAndCheckForResults(): void {
    this.searchBox.nativeElement.focus();
    this.showResults = !!this.results$;
  }

  private selectPackage(packageHistory: IPackageDownloadHistory): void {
    this.addPackageService.selectPackage(packageHistory);
    this.showResults = false;
    this.queryField.setValue('');
    this.searchBox.nativeElement.focus();
  }

}
