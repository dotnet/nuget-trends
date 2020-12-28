import { Component, ErrorHandler, OnDestroy, OnInit } from '@angular/core';
import { Chart, ChartData, ChartDataSets, ChartOptions } from 'chart.js';
import { DatePipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { AppAnimations } from '../shared';
import { ToastrService } from 'ngx-toastr';

import { PackagesService, PackageInteractionService } from '../core';
import { IPackageDownloadHistory, IDownloadStats } from '../shared/models/package-models';

@Component({
  selector: 'app-dashboard',
  templateUrl: './packages.component.html',
  styleUrls: ['./packages.component.scss'],
  animations: [AppAnimations.slideInOutAnimation]
})
export class PackagesComponent implements OnInit, OnDestroy {

  private trendChart!: Chart;
  private canvas: any;
  private ctx: any;
  private chartData: ChartData = { labels: [], datasets: [] };
  private plotPackageSubscription: Subscription;
  private removePackageSubscription: Subscription;
  private searchPeriodSubscription: Subscription;
  private urlParamName = 'ids';

  constructor(
    private packagesService: PackagesService,
    private route: Router,
    private activatedRoute: ActivatedRoute,
    private packageInteractionService: PackageInteractionService,
    private datePipe: DatePipe,
    private toastr: ToastrService,
    private errorHandler: ErrorHandler) {

    this.plotPackageSubscription = this.packageInteractionService.packagePlotted$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.plotPackage(packageHistory);
      });
    this.removePackageSubscription = this.packageInteractionService.packageRemoved$.subscribe(
      (packageId: string) => this.removePackage(packageId));

    this.searchPeriodSubscription = this.packageInteractionService.searchPeriodChanged$.subscribe(
      (searchPeriod: number) => this.periodChanged(searchPeriod));
  }

  ngOnInit() {
    this.loadPackagesFromUrl();
  }

  ngOnDestroy(): void {
    this.plotPackageSubscription.unsubscribe();
    this.removePackageSubscription.unsubscribe();
    this.searchPeriodSubscription.unsubscribe();

    if (this.trendChart) {
      this.trendChart.destroy();
    }
  }

  /**
   * Re-loads the chart with data for the new period
   */
  private async periodChanged(period: number): Promise<void> {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll('ids');
    if (!packageIds.length) {
      return;
    }

    this.chartData.datasets = [];

    packageIds.forEach(async (packageId: string) => {
      try {
        const downloadHistory = await this.packagesService.getPackageDownloadHistory(packageId, period).toPromise();
        this.packageInteractionService.updatePackage(downloadHistory);
      } catch (error) {
        this.errorHandler.handleError(error);
        this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      }
    });
  }

  /**
   * Handles the plotPackage event
   */
  private plotPackage(packageHistory: IPackageDownloadHistory): void {
    if (packageHistory) {
      const dataset = this.parseDataSet(packageHistory);

      setTimeout(() => {
        if (this.chartData.datasets!.length === 0) {
          this.initializeChart(packageHistory);
        } else {
          this.chartData.datasets!.push(dataset);
          this.trendChart.update();
        }
        this.addPackageToUrl(packageHistory.id);
      }, 0);
    }
  }

  /**
   * Handles the removePackage event
   */
  private removePackage(packageId: string): void {
    this.chartData.datasets = this.chartData.datasets!.filter(d => d.label !== packageId);
    this.trendChart.update();
    this.removePackageFromUrl(packageId);

    if (!this.chartData.datasets.length) {
      this.route.navigate(['/']);
    }
  }

  /**
   * Initializes the chart with the first added package
   */
  private initializeChart(firstPackageData: IPackageDownloadHistory): void {
    this.chartData.labels = firstPackageData.downloads.map((download: IDownloadStats) => {
      return this.datePipe.transform(download.week, 'MMM d')!;
    });

    this.chartData.datasets!.push(this.parseDataSet(firstPackageData));
    Chart.defaults.global.defaultFontSize = 13;

    const chartOptions: ChartOptions = {
      responsive: true,
      maintainAspectRatio: false,
      legend: {
        display: false
      },
      tooltips: {
        callbacks: {
          label: (tooltipItem: any, data: any) => {
            let label = data.datasets[tooltipItem.datasetIndex].label || 'NuGet Package';
            if (label) {
              label += ': ';
            }
            label += tooltipItem.yLabel.toLocaleString();
            return label;
          }
        }
      },
      scales: {
        gridLines: {
          display: true
        },
        xAxes: [{
          display: true,
          scaleLabel: {
            display: false,
            labelString: 'Month'
          }
        }],
        yAxes: [{
          display: true,
          ticks: {
            callback: (value: string) => value.toLocaleString(),
          },
          scaleLabel: {
            display: false,
            labelString: 'Downloads'
          }
        }]
      }
    };

    this.canvas = document.getElementById('trend-chart');
    this.ctx = this.canvas.getContext('2d');

    this.trendChart = new Chart(this.ctx, {
      type: 'line',
      data: this.chartData,
      options: chartOptions
    });
  }

  /**
   * Parses an IPackageDownloadHistory to a Chart.js type
   */
  private parseDataSet(packageHistory: IPackageDownloadHistory): ChartDataSets {
    const totalDownloads = packageHistory.downloads.map((data: IDownloadStats) => {
      return data.count;
    });

    return {
      label: packageHistory.id,
      backgroundColor: packageHistory.color,
      borderColor: packageHistory.color,
      pointRadius: 6,
      pointHoverRadius: 8,
      pointBackgroundColor: packageHistory.color,
      pointBorderColor: '#fff',
      pointBorderWidth: 1,
      pointHoverBackgroundColor: packageHistory.color,
      pointHoverBorderColor: packageHistory.color,
      fill: false,
      data: totalDownloads,
    };
  }

  /**
   * Reads the packages from the URL and initialize the chart
   * Useful when sharing the URL with others
   */
  private async loadPackagesFromUrl(): Promise<void> {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll(this.urlParamName);
    if (!packageIds.length) {
      return;
    }

    packageIds.forEach(async (packageId: string) => {
      try {
        const downloadHistory = await this.packagesService.getPackageDownloadHistory(
          packageId, this.packageInteractionService.searchPeriod).toPromise();

        this.packageInteractionService.addPackage(downloadHistory);
      } catch (error) {
        this.errorHandler.handleError(error);
        this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      }
    });
  }

  /**
   * Add the selected packageId to the URL making it shareable
   */
  private addPackageToUrl(packageId: string) {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll(this.urlParamName);

    if (packageIds.includes(packageId)) {
      return;
    }
    const queryParams: Params = { ...this.activatedRoute.snapshot.queryParams };

    // if packageIds exists, append the new package to the URL
    // otherwise initialize the param
    if (packageIds.length) {
      queryParams[this.urlParamName] = [...packageIds, packageId];
    } else {
      queryParams[this.urlParamName] = packageId;
    }
    this.route.navigate([], {
      replaceUrl: true,
      relativeTo: this.activatedRoute,
      queryParams,
      queryParamsHandling: 'merge'
    });
  }

  /**
   * Removes the package from the URL when removing the "tag" from the list
   */
  private removePackageFromUrl(packageId: string) {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll(this.urlParamName);

    if (!packageIds.includes(packageId)) {
      return;
    }
    const queryParams: Params = { ...this.activatedRoute.snapshot.queryParams };
    queryParams[this.urlParamName] = packageIds.filter(p => p !== packageId);

    this.route.navigate([], {
      queryParams,
      queryParamsHandling: 'merge'
    });
  }
}
