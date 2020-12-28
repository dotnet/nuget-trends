/* tslint:disable:variable-name */
import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';
import { IPackageDownloadHistory, SearchType } from '../../shared/models/package-models';

@Injectable({
  providedIn: 'root'
})
export class PackageInteractionService {

  private packageAddedSource = new Subject<IPackageDownloadHistory>();
  private packageUpdatedSource = new Subject<IPackageDownloadHistory>();
  private packagePlottedSource = new Subject<IPackageDownloadHistory>();
  private packageRemovedSource = new Subject<string>();
  private searchPeriodChangedSource = new Subject<number>();
  private _searchType!: SearchType;
  private _searchPeriod!: number;

  packageAdded$ = this.packageAddedSource.asObservable();
  packageUpdated$ = this.packageUpdatedSource.asObservable();
  packagePlotted$ = this.packagePlottedSource.asObservable();
  packageRemoved$ = this.packageRemovedSource.asObservable();
  searchPeriodChanged$ = this.searchPeriodChangedSource.asObservable();

  constructor() {
    this.searchType = SearchType.NuGetPackage;
    this.searchPeriod = 12;
  }

  /**
   * Fires the event that adds a package to the package list component
   */
  addPackage(packageHistory: IPackageDownloadHistory): void {
    this.packageAddedSource.next(packageHistory);
  }

  /**
   * Fires the event that updates the package on the chart with new data
   */
  updatePackage(updatedPackageHistory: IPackageDownloadHistory): void {
    this.packageUpdatedSource.next(updatedPackageHistory);
  }

  /**
   * Fires the event that plots the package on the chart
   */
  plotPackage(packageHistory: IPackageDownloadHistory): void {
    this.packagePlottedSource.next(packageHistory);
  }

  /**
   * Fires the event that removes a package from the chart
   */
  removePackage(packageId: string) {
    this.packageRemovedSource.next(packageId);
  }

  changeSearchPeriod(searchPeriod: number) {
    if (this._searchPeriod === searchPeriod) {
      return;
    }
    this._searchPeriod = searchPeriod;
    this.searchPeriodChangedSource.next(searchPeriod);
  }

  set searchType(searchType: SearchType) {
    this._searchType = searchType;
  }

  get searchType(): SearchType {
    return this._searchType;
  }

  set searchPeriod(searchPeriod: number) {
    this._searchPeriod = searchPeriod;
  }

  get searchPeriod(): number {
    return this._searchPeriod;
  }
}
