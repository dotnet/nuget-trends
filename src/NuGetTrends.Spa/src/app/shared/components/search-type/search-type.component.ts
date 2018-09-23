import {Component, EventEmitter, Output} from '@angular/core';
import {SearchType} from '../../models/package-models';
import {PackageInteractionService} from '../../../core';

@Component({
  selector: 'app-search-type',
  templateUrl: './search-type.component.html',
  styleUrls: ['./search-type.component.scss']
})
export class SearchTypeComponent {
  @Output() packagedTypeChanged: EventEmitter<SearchType>;

  isNuGetPackage: boolean;

  constructor(private packageInteractionService: PackageInteractionService) {
    this.isNuGetPackage = true;
    this.packageInteractionService.searchType = SearchType.NuGetPackage;
  }

  /**
   * Fires an event with the appropriate SearchType
   * @param $event
   */
  changePackageType($event: any): void {
    if ($event.target.checked) {
      this.packageInteractionService.searchType = SearchType.NuGetPackage;
    } else {
      this.packageInteractionService.searchType = SearchType.Framework;
    }
  }
}
