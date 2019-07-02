import {Injectable} from '@angular/core';
import {Subject} from 'rxjs';
import {
  IPackageDownloadHistory,
  SearchType,
  SearchPeriod,
  InitialSearchPeriod
} from '../../shared/models/package-models';

@Injectable({
  providedIn: 'root'
})
export class PackageInteractionService {

  private packageAddedSource = new Subject<IPackageDownloadHistory>();
  private packageUpdatedSource = new Subject<IPackageDownloadHistory>();
  private packagePlottedSource = new Subject<IPackageDownloadHistory>();
  private packageRemovedSource = new Subject<string>();
  private _searchType: SearchType;
  private _searchPeriod: number;

  packageAdded$ = this.packageAddedSource.asObservable();
  packageUpdated$ = this.packageUpdatedSource.asObservable();
  packagePlotted$ = this.packagePlottedSource.asObservable();
  packageRemoved$ = this.packageRemovedSource.asObservable();

  constructor() {
    this.searchType = SearchType.NuGetPackage;
  }

  /**
   * Fires the event that adds a package to the package list component
   * @param packageHistory
   */
  addPackage(packageHistory: IPackageDownloadHistory): void {
    this.packageAddedSource.next(packageHistory);
  }

    /**
   * Fires the event that updates the package on the chart with new data
   * @param updatedPackageHistory
   */
  updatePackage(updatedPackageHistory: IPackageDownloadHistory): void {
    this.packageUpdatedSource.next(updatedPackageHistory);
  }

  /**
   * Fires the event that plots the package on the chart
   * @param packageHistory
   */
  plotPackage(packageHistory: IPackageDownloadHistory): void {
    this.packagePlottedSource.next(packageHistory);
  }

  /**
   * Fires the event that removes a package from the chart
   * @param packageId
   */
  removePackage(packageId: string) {
    this.packageRemovedSource.next(packageId);
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
