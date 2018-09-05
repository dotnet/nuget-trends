import {Injectable} from '@angular/core';
import {Subject} from 'rxjs';
import {IPackageDownloadHistory, SearchType} from './package-models';

@Injectable({
  providedIn: 'root'
})
export class PackageInteractionService {

  private packageAddedSource = new Subject<IPackageDownloadHistory>();
  private packagePlottedSource = new Subject<IPackageDownloadHistory>();
  private packageRemovedSource = new Subject<string>();
  private _searchType: SearchType;

  packageAdded$ = this.packageAddedSource.asObservable();
  packagePlotted$ = this.packagePlottedSource.asObservable();
  packageRemoved$ = this.packageRemovedSource.asObservable();

  /**
   * Fires the event that adds a package to the package list component
   * @param packageHistory
   */
  addPackage(packageHistory: IPackageDownloadHistory): void {
    this.packageAddedSource.next(packageHistory);
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

}
