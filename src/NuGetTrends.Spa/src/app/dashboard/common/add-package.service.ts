import {Injectable} from '@angular/core';
import {Subject} from 'rxjs';
import {IPackageDownloadHistory} from './package-models';

@Injectable()
export class AddPackageService {

  private packageAddedSource = new Subject<IPackageDownloadHistory>();
  private packageRemovedSource = new Subject<string>();

  packageAdded$ = this.packageAddedSource.asObservable();
  packageRemoved$ = this.packageRemovedSource.asObservable();

  /**
   * Fires the event that adds a package download history to the chart
   * @param packageHistory
   */
  addPackage(packageHistory: IPackageDownloadHistory) {
    this.packageAddedSource.next(packageHistory);
  }

  /**
   * Fires the event that removes a package from the chart
   * @param astronaut
   */
  removePackage(astronaut: string) {
    this.packageRemovedSource.next(astronaut);
  }
}
