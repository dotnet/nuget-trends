import {Component, OnDestroy, OnInit} from '@angular/core';
import {Chart, ChartDataSets, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';
import {Subscription, Observable, forkJoin, pipe, EMPTY, empty, of} from 'rxjs';
import {catchError} from 'rxjs/operators';
import {ActivatedRoute, Params, Router} from '@angular/router';
import {AppAnimations} from '../shared';
import {ToastrService} from 'ngx-toastr';

import {PackagesService, PackageInteractionService} from '../core';
import {IPackageDownloadHistory, IDownloadStats} from '../shared/models/package-models';

@Component({
  selector: 'app-dashboard',
  templateUrl: './packages.component.html',
  styleUrls: ['./packages.component.scss'],
  animations: [AppAnimations.slideInOutAnimation]
})
export class PackagesComponent implements OnInit, OnDestroy {

  private trendChart: Chart;
  private canvas: any;
  private ctx: any;
  private chartData = {labels: [], datasets: []};
  private plotPackageSubscription: Subscription;
  private removePackageSubscription: Subscription;
  private urlParamName = 'ids';

  private handleApiError =
    catchError<IPackageDownloadHistory, Observable<IPackageDownloadHistory>>(() => {
      this.toastr.error('Our servers are too cool (or not) to handle your request at the moment.');
      return of<IPackageDownloadHistory>();
    });

  constructor(
    private packagesService: PackagesService,
    private route: Router,
    private activatedRoute: ActivatedRoute,
    private packageInterationService: PackageInteractionService,
    private datePipe: DatePipe,
    private toastr: ToastrService) {

    this.plotPackageSubscription = this.packageInterationService.packagePlotted$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.plotPackage(packageHistory);
      });
    this.removePackageSubscription = this.packageInterationService.packageRemoved$.subscribe(
      (packageId: string) => this.removePackage(packageId));
  }

  ngOnInit(): void {
    this.loadPackagesFromUrl();
  }

  ngOnDestroy(): void {
    this.plotPackageSubscription.unsubscribe();
    this.removePackageSubscription.unsubscribe();
  }

  /**
   * Re-loads the chart with data for the new period
   */
  periodChanged(period: number): void {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll('ids');

    if (!packageIds.length) {
      return;
    }

    this.chartData.datasets = [];
    const requests: Array<Observable<IPackageDownloadHistory>> = [];

    // create the observables
    packageIds.forEach((packageId: string) => {
      requests.push(
        this.packagesService
          .getPackageDownloadHistory(packageId, period)
          .pipe(this.handleApiError)
      );
    });

    forkJoin(requests).subscribe((results: Array<IPackageDownloadHistory>) => {
      results.forEach((packageHistory: IPackageDownloadHistory) => this.packageInterationService.updatePackage(packageHistory));
    });
  }

  /**
   * Handles the plotPackage event
   * @param packageHistory
   */
  private plotPackage(packageHistory: IPackageDownloadHistory): void {
    if (packageHistory) {
      const dataset = this.parseDataSet(packageHistory);

      setTimeout(() => {
        if (this.chartData.datasets.length === 0) {
          this.initializeChart(packageHistory);
        } else {
          this.chartData.datasets.push(dataset);
          this.trendChart.update();
        }
        this.addPackageToUrl(packageHistory.id);
      }, 0);
    }
  }

  /**
   * Handles the removePackage event
   * @param packageId
   */
  private removePackage(packageId: string): void {
    this.chartData.datasets = this.chartData.datasets.filter(d => d.label !== packageId);
    this.trendChart.update();
    this.removePackageFromUrl(packageId);

    if (!this.chartData.datasets.length) {
      this.route.navigate(['/']);
    }
  }

  /**
   * Initializes the chart with the first added package
   * @param firstPackageData
   */
  private initializeChart(firstPackageData: IPackageDownloadHistory): void {
    this.chartData.labels = firstPackageData.downloads.map((download: IDownloadStats) => {
      return this.datePipe.transform(download.week, 'MMM d');
    });

    this.chartData.datasets.push(this.parseDataSet(firstPackageData));
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
            callback: (value: string, index: number, values: any) => value.toLocaleString(),
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
   * @param packageHistory
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
  private loadPackagesFromUrl() {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll('ids');
    if (!packageIds.length) {
      return;
    }

    const requests: Array<Observable<IPackageDownloadHistory>> = [];

    packageIds.forEach((packageId: string) => {
      requests.push(this.packagesService.getPackageDownloadHistory(
        packageId,
        this.packageInterationService.searchPeriod).pipe(this.handleApiError));
    });

    forkJoin(requests).subscribe((results: Array<IPackageDownloadHistory>) => {
      results.forEach((packageHistory: IPackageDownloadHistory) =>
      this.packageInterationService.addPackage(packageHistory));
    });
  }

  /**
   * Add the selected packageId to the URL making it shareable
   * @param packageId
   */
  private addPackageToUrl(packageId: string) {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll(this.urlParamName);

    if (packageIds.includes(packageId)) {
      return;
    }
    const queryParams: Params = {...this.activatedRoute.snapshot.queryParams};

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
      queryParams: queryParams,
      queryParamsHandling: 'merge'
    });
  }

  /**
   * Removes the package from the URL when removing the "tag" from the list
   * @param packageId
   */
  private removePackageFromUrl(packageId: string) {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll(this.urlParamName);

    if (!packageIds.includes(packageId)) {
      return;
    }
    const queryParams: Params = {...this.activatedRoute.snapshot.queryParams};
    queryParams[this.urlParamName] = packageIds.filter(p => p !== packageId);

    this.route.navigate([], {
      queryParams: queryParams,
      queryParamsHandling: 'merge'
    });
  }

}
