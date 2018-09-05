import {Component, OnDestroy, OnInit} from '@angular/core';
import {Chart, ChartDataSets, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';

import {IPackageDownloadHistory, IDownloadStats} from '../shared/common/package-models';
import {PackagesService, PackageInteractionService, AppAnimations} from '../shared/common/';
import {Subscription} from 'rxjs';
import {ActivatedRoute, Params, Router} from '@angular/router';

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
  private loaded = false;
  private plotPackageSubscription: Subscription;
  private removePackageSubscription: Subscription;
  private urlParamName = 'ids';

  constructor(
    private packagesService: PackagesService,
    private route: Router,
    private activatedRoute: ActivatedRoute,
    private addPackageService: PackageInteractionService,
    private datePipe: DatePipe) {
    this.plotPackageSubscription = this.addPackageService.packagePlotted$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.plotPackage(packageHistory);
      });

    this.removePackageSubscription = this.addPackageService.packageRemoved$.subscribe(
      (packageId: string) => this.removePackage(packageId));
  }

  ngOnInit(): void {
    const packageIds: string[] = this.activatedRoute.snapshot.queryParamMap.getAll('ids');
    if (packageIds.length) {
      this.loadPackagesFromUrl(packageIds);
    }
  }

  ngOnDestroy(): void {
    this.plotPackageSubscription.unsubscribe();
    this.removePackageSubscription.unsubscribe();
  }

  /**
   * Handles the plotPackage event
   * @param packageHistory
   */
  private plotPackage(packageHistory: IPackageDownloadHistory): void {
    if (packageHistory) {
      this.loaded = true;
      const dataSet = this.parseDataSet(packageHistory);

      setTimeout(() => {
        if (this.chartData.datasets.length === 0) {
          this.initializeChart(packageHistory);
        } else {
          this.chartData.datasets.push(dataSet);
        }
        this.trendChart.update();
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
    this.loaded = this.chartData.datasets.length > 0;
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
      return this.datePipe.transform(download.date, 'MMM d');
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

  private loadPackagesFromUrl(packageIds: string[]) {
    packageIds.forEach((packageId: string) => {
      this.packagesService.getPackageDownloadHistory(packageId)
        .subscribe((packageHistory: IPackageDownloadHistory) => {
          this.addPackageService.addPackage(packageHistory);
        });
    });
  }

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
      queryParams: queryParams,
      queryParamsHandling: 'merge'
    });
  }

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
