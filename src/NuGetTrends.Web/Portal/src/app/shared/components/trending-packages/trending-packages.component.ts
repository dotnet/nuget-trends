import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { PackagesService } from '../../../core/services/packages.service';
import { ITrendingPackage } from '../../models/package-models';

@Component({
  selector: 'app-trending-packages',
  templateUrl: './trending-packages.component.html',
  styleUrls: ['./trending-packages.component.scss'],
  standalone: false
})
export class TrendingPackagesComponent implements OnInit {
  trendingPackages: ITrendingPackage[] = [];
  isLoading = true;
  errorMessage: string | null = null;

  constructor(
    private packagesService: PackagesService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.loadTrendingPackages();
  }

  private loadTrendingPackages(): void {
    this.isLoading = true;
    this.errorMessage = null;

    this.packagesService.getTrendingPackages(10).subscribe({
      next: (packages) => {
        this.trendingPackages = packages;
        this.isLoading = false;
      },
      error: (error) => {
        console.error('Failed to load trending packages', error);
        this.errorMessage = 'Unable to load trending packages. Please try again later.';
        this.isLoading = false;
      }
    });
  }

  navigateToPackage(packageId: string): void {
    this.router.navigate(['/packages', packageId]);
  }

  openNuGetPage(packageId: string): void {
    window.open(`https://www.nuget.org/packages/${packageId}`, '_blank', 'noopener,noreferrer');
  }

  /**
   * Formats the growth rate as a percentage string.
   * Returns '+25%' for positive, '-10%' for negative, or '0%' for zero.
   */
  formatGrowthRate(growthRate: number | null): string {
    if (growthRate === null) {
      return 'N/A';
    }
    const percentage = Math.round(growthRate * 100);
    if (percentage > 0) {
      return `+${percentage}%`;
    }
    return `${percentage}%`;
  }

  /**
   * Returns a CSS class based on the growth rate.
   */
  getGrowthClass(growthRate: number | null): string {
    if (growthRate === null || growthRate === 0) {
      return 'growth-neutral';
    }
    return growthRate > 0 ? 'growth-positive' : 'growth-negative';
  }

  /**
   * Formats download count with K/M suffix.
   */
  formatDownloadCount(count: number): string {
    if (count >= 1000000) {
      return (count / 1000000).toFixed(1) + 'M';
    }
    if (count >= 1000) {
      return (count / 1000).toFixed(1) + 'K';
    }
    return count.toString();
  }
}
