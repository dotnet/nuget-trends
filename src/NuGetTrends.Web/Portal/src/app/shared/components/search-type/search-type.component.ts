import { Component, EventEmitter, Output, OnInit } from '@angular/core';
import { SearchType } from '../../models/package-models';
import { PackageInteractionService } from '../../../core';

@Component({
  selector: 'app-search-type',
  templateUrl: './search-type.component.html',
  styleUrls: ['./search-type.component.scss']
})
export class SearchTypeComponent implements OnInit {
  @Output() packagedTypeChanged: EventEmitter<SearchType> = new EventEmitter<SearchType>();

  isNuGetPackage: boolean;

  constructor(private packageInteractionService: PackageInteractionService) {
    this.isNuGetPackage = true;
  }

  ngOnInit(): void {
    this.packageInteractionService.searchType = SearchType.NuGetPackage;
  }

  /**
   * Fires an event with the appropriate SearchType
   */
  changePackageType($event: any): void {
    if ($event.target.checked) {
      this.packageInteractionService.searchType = SearchType.NuGetPackage;
    } else {
      this.packageInteractionService.searchType = SearchType.Framework;
    }
  }
}
