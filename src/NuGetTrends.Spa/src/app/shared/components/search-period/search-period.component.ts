import { Component, OnDestroy, OnInit } from '@angular/core';
import { Router, ActivatedRoute, Params } from '@angular/router';
import { FormControl } from '@angular/forms';

import { SearchPeriod, DefaultSearchPeriods, InitialSearchPeriod } from '../../models/package-models';
import { PackageInteractionService } from '../../../core';

@Component({
  selector: 'app-search-period',
  templateUrl: './search-period.component.html',
  styleUrls: ['./search-period.component.scss']
})
export class SearchPeriodComponent implements OnInit, OnDestroy {
  periodControl: FormControl;
  periodValues: Array<SearchPeriod>;

  private urlPeriodName = 'months';

  constructor(
    private route: Router,
    private activatedRoute: ActivatedRoute,
    private packageInterationService: PackageInteractionService
  ) {
  }

  ngOnInit(): void {
    this.periodValues = DefaultSearchPeriods;
    this.addDefaultOrCurrentPeriodToUrl(InitialSearchPeriod.value);
  }

  ngOnDestroy(): void {
    // reset the value to avoid left overs, since it's shared
    this.packageInterationService.searchPeriod = InitialSearchPeriod.value;
  }

  /**
   * Invokes the EventEmitter passing the new period value
   * and updates the URL accordingly
   */
  changePeriod(): void {
    const newSearchPeriod = this.periodControl.value;

    const queryParams: Params = {...this.activatedRoute.snapshot.queryParams};
    queryParams[this.urlPeriodName] = newSearchPeriod;

    this.packageInterationService.changeSearchPeriod(newSearchPeriod);

    this.route.navigate([], {
      replaceUrl: true,
      relativeTo: this.activatedRoute,
      queryParams
    });
  }

  /**
   * Adds the default or current period value to the URL
   * @param defaultPeriod The default period in months
   */
  private addDefaultOrCurrentPeriodToUrl(defaultPeriod: number = 12) {
    const currentUrlValue = Number(
      this.activatedRoute.snapshot.queryParamMap.get(this.urlPeriodName)
    );
    let valueToUse: number;

    // if not in the URL, use the default
    if (!currentUrlValue || isNaN(currentUrlValue)) {
      valueToUse = defaultPeriod;
    } else {
      // otherwise use what we have (in case of URL sharing, for instance)
      valueToUse = currentUrlValue;
    }

    const queryParams: Params = {...this.activatedRoute.snapshot.queryParams};
    queryParams[this.urlPeriodName] = valueToUse;

    this.periodControl = new FormControl(valueToUse);
    this.packageInterationService.searchPeriod = valueToUse;

    this.route.navigate([], {
      replaceUrl: true,
      relativeTo: this.activatedRoute,
      queryParams
    });
  }
}
