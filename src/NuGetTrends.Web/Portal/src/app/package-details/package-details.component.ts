import { Component, ErrorHandler, OnDestroy, OnInit } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
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
export class PackageDetailsComponent implements OnInit, OnDestroy {
  private readonly fallbackIconUrl = 'https://nuget.org/Content/Images/packageDefaultIcon-50x50.png';
  private routeParamSubscription: Subscription | null = null;
  private loadRequestId = 0;

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
    this.routeParamSubscription = this.activatedRoute.paramMap.subscribe(params => {
      const packageId = params.get('packageId')?.trim() ?? '';
      void this.loadPackageDetails(packageId);
    });
  }

  ngOnDestroy(): void {
    this.routeParamSubscription?.unsubscribe();
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

  setPackageIconFallback(event: Event): void {
    const image = event.target as HTMLImageElement | null;
    if (!image || image.src.includes(this.fallbackIconUrl)) {
      return;
    }

    image.src = this.fallbackIconUrl;
  }

  private async loadPackageDetails(packageId: string): Promise<void> {
    this.packageId = packageId;

    if (!packageId) {
      await this.router.navigate(['/']);
      return;
    }

    const currentRequestId = ++this.loadRequestId;
    this.isLoading = true;
    this.isNotFound = false;
    this.packageDetails = null;

    try {
      const packageDetails = await firstValueFrom(this.packagesService.getPackageDetails(packageId));

      if (currentRequestId !== this.loadRequestId) {
        return;
      }

      this.packageDetails = packageDetails;
      this.packageInteractionService.searchType = SearchType.NuGetPackage;
    } catch (error) {
      if (currentRequestId !== this.loadRequestId) {
        return;
      }

      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.isNotFound = true;
      } else {
        this.errorHandler.handleError(error);
        this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      }
    } finally {
      if (currentRequestId === this.loadRequestId) {
        this.isLoading = false;
      }
    }
  }
}
