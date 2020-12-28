import { Component, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs/';
import { IPackageDownloadHistory, IPackageColor, TagColor } from '../../models/package-models';
import { PackageInteractionService } from '../../../core';
import { environment } from '../../../../environments/environment.prod';
import { ToastrService } from 'ngx-toastr';

@Component({
  selector: 'app-package-list',
  templateUrl: './package-list.component.html',
  styleUrls: ['./package-list.component.scss']
})
export class PackageListComponent implements OnDestroy {
  packageList: Array<IPackageColor>;

  private colorsList: Array<TagColor> = [
    new TagColor('#055499'),
    new TagColor('#ff5a00'),
    new TagColor('#9bca3c'),
    new TagColor('#e91365'),
    new TagColor('#9B5094'),
    new TagColor('#DB9D47')
  ];

  private packageSelectedSubscription: Subscription;
  private packageUpdatedSubscription: Subscription;

  constructor(
    private packageInteractionService: PackageInteractionService,
    private toastr: ToastrService) {
    this.packageList = [];

    this.packageSelectedSubscription = this.packageInteractionService.packageAdded$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.addPackageToList(packageHistory);
      });

    this.packageUpdatedSubscription = this.packageInteractionService.packageUpdated$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.updatePackageOnList(packageHistory);
      });
  }

  ngOnDestroy(): void {
    this.packageSelectedSubscription.unsubscribe();
    this.packageUpdatedSubscription.unsubscribe();
  }

  /**
   * Removes the package from the list and fires the event that removes it from the chart
   */
  removePackage(packageColor: IPackageColor): void {
    if (packageColor) {
      const color = this.colorsList.find(p => p.code === packageColor.color);
      color?.setUnused();
      this.packageInteractionService.removePackage(packageColor.id);
      this.packageList = this.packageList.filter(p => p.id !== packageColor.id);
    }
  }

  /**
   * Adds the package to the list and fires the event that plots it on the chart
   */
  private addPackageToList(packageHistory: IPackageDownloadHistory): void {

    if (this.packageList.length === environment.MAX_CHART_ITEMS) {
      this.toastr.warning('Insert bitcoin to add another item to the chart.');
      return;
    }

    if (packageHistory && !this.packageList.some(p => p.id === packageHistory.id)) {
      const color = this.colorsList.find(p => p.isInUse() === false)!;
      color.setUsed();
      packageHistory.color = color.code;
      this.packageList.push({id: packageHistory.id, color: color.code} as IPackageColor);
      this.packageInteractionService.plotPackage(packageHistory);
    }
  }

  /**
   * Re-trigger the event that plots the package on the chart with new data
   * @param updatedPackageHistory Updated package download history
   */
  private updatePackageOnList(updatedPackageHistory: IPackageDownloadHistory): void {
    const existingPackage = this.packageList.find(p => p.id === updatedPackageHistory.id);

    if (updatedPackageHistory && existingPackage) {
      updatedPackageHistory.color = existingPackage.color;
      this.packageInteractionService.plotPackage(updatedPackageHistory);
    }
  }

}
