import { Component, ErrorHandler, OnInit } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import * as Sentry from '@sentry/angular';

import { AppAnimations } from '../shared';
import { PackageInteractionService, PackagesService } from '../core';
import { IPackageDetails, SearchType } from '../shared/models/package-models';

@Component({
  selector: 'app-package-details',
  templateUrl: './package-details.component.html',
  styleUrls: ['./package-details.component.scss'],
  animations: [AppAnimations.slideInOutAnimation],
  standalone: false
})
@Sentry.TraceClass({ name: 'PackageDetailsComponent' })
export class PackageDetailsComponent implements OnInit {
  packageId = '';
  packageDetails: IPackageDetails | null = null;
  isLoading = true;
  isNotFound = false;

  constructor(
    private packagesService: PackagesService,
    private packageInteractionService: PackageInteractionService,
    private activatedRoute: ActivatedRoute,
    private router: Router,
    private toastr: ToastrService,
    private errorHandler: ErrorHandler
  ) {}

  ngOnInit(): void {
    void this.loadPackageDetails();
  }

  addToTrends(): void {
    const targetPackageId = this.packageDetails?.packageId ?? this.packageId;
    if (!targetPackageId) {
      return;
    }

    this.router.navigate(['/packages', targetPackageId]);
  }

  getFreshnessClass(ageInDays: number | null): string {
    if (ageInDays === null) {
      return 'freshness-unknown';
    }

    if (ageInDays <= 90) {
      return 'freshness-recent';
    }

    if (ageInDays <= 365) {
      return 'freshness-steady';
    }

    return 'freshness-stale';
  }

  formatCount(value: number | null): string {
    if (value === null) {
      return 'N/A';
    }

    return value.toLocaleString();
  }

  formatDate(value: string | null): string {
    if (!value) {
      return 'N/A';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return 'N/A';
    }

    return date.toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    });
  }

  formatAge(ageInDays: number | null): string {
    if (ageInDays === null) {
      return 'Unknown';
    }

    if (ageInDays <= 0) {
      return 'Today';
    }

    if (ageInDays < 30) {
      return `${ageInDays} days`;
    }

    const months = Math.round(ageInDays / 30);
    if (months < 12) {
      return `${months} months`;
    }

    const years = Math.round(months / 12);
    return `${years} years`;
  }

  formatFileSize(bytes: number | null): string {
    if (bytes === null || bytes <= 0) {
      return 'Unknown';
    }

    const units = ['B', 'KB', 'MB', 'GB'];
    let value = bytes;
    let unitIndex = 0;

    while (value >= 1024 && unitIndex < units.length - 1) {
      value /= 1024;
      unitIndex += 1;
    }

    return `${value.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
  }

  getHeadline(details: IPackageDetails): string | null {
    if (details.summary) {
      return details.summary;
    }

    if (details.description) {
      return details.description;
    }

    return null;
  }

  private async loadPackageDetails(): Promise<void> {
    this.packageId = this.activatedRoute.snapshot.paramMap.get('packageId') ?? '';

    if (!this.packageId) {
      await this.router.navigate(['/']);
      return;
    }

    this.isLoading = true;
    this.isNotFound = false;

    try {
      this.packageDetails = await firstValueFrom(this.packagesService.getPackageDetails(this.packageId));
      this.packageInteractionService.searchType = SearchType.NuGetPackage;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.isNotFound = true;
      } else {
        this.errorHandler.handleError(error);
        this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      }
    } finally {
      this.isLoading = false;
    }
  }
}
