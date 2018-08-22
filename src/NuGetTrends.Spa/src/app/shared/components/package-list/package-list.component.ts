import {Component, OnDestroy} from '@angular/core';
import {PackageInteractionService} from '../../common';
import {IPackageDownloadHistory} from '../../common/package-models';
import {IPackageColor, TagColor} from '../common/component-models';
import {Subscription} from 'rxjs/';

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

  constructor(private addPackageService: PackageInteractionService) {
    this.packageList = [];
    this.packageSelectedSubscription = this.packageSelectedSubscription = this.addPackageService.packageSelected$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.addPackageToList(packageHistory);
      });
  }

  ngOnDestroy(): void {
    this.packageSelectedSubscription.unsubscribe();
  }

  /**
   * Removes the package from the list and fires the event that removes it from the chart
   * @param packageColor
   */
  removePackage(packageColor: IPackageColor): void {
    if (packageColor) {
      const color = this.colorsList.find(p => p.code === packageColor.color);
      color.setUnused();
      this.addPackageService.removePackage(packageColor.id);
      this.packageList = this.packageList.filter(p => p.id !== packageColor.id);
    }
  }

  /**
   * Adds the package to the list and fires the event that plots it on the chart
   * @param packageHistory
   */
  private addPackageToList(packageHistory: IPackageDownloadHistory): void {
    if (packageHistory) {
      const color = this.colorsList.find(p => p.isInUse() === false);
      color.setUsed();
      packageHistory.color = color.code;
      this.packageList.push(<IPackageColor>{id: packageHistory.id, color: color.code});
      this.addPackageService.plotPackage(packageHistory);
    }
  }
}
