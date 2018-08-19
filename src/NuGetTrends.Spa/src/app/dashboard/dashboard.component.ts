import {Component, OnInit} from '@angular/core';
import {Chart, ChartDataSets, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';

import {IPackageDownloadHistory, IDownloadStats, PackageToColorMap} from './common/package-models';
import {PackagesService, AddPackageService} from './common/';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss'],
  providers: [AddPackageService]
})
export class DashboardComponent implements OnInit {

  constructor(
    private packagesService: PackagesService,
    private addPackageService: AddPackageService,
    private datePipe: DatePipe) {

    this.addPackageService.packageAdded$.subscribe(
      (packageHistory: IPackageDownloadHistory) => {
        this.addPackageToChart(packageHistory);
      });
  }

  trendChart: Chart;
  canvas: any;
  ctx: any;
  colorsMap: PackageToColorMap = {};

  private chartData = {labels: [], datasets: []};

  ngOnInit() {
    this.colorsMap['EntityFramework'] = '#B4F30D';
    this.colorsMap['Dapper'] = '#0D45F3';
    this.colorsMap['ef-core'] = '#F30D30';
  }

  /**
   * Added a new dataset to the chart
   * @param packageHistory
   */
  private addPackageToChart(packageHistory: IPackageDownloadHistory) {
    const dataSet = this.parseDataSet(packageHistory);

    if (this.chartData.datasets.length === 0) {
      this.initializeChart(packageHistory);
    } else {
      this.chartData.datasets.push(dataSet);
    }
    this.trendChart.update();
  }


  /**
   * Initializes the chart with the first added package
   * @param firstPackageData
   */
  private initializeChart(firstPackageData: IPackageDownloadHistory) {
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
      backgroundColor: this.colorsMap[packageHistory.id],
      borderColor: this.colorsMap[packageHistory.id],
      pointRadius: 6,
      pointHoverRadius: 8,
      pointBackgroundColor: this.colorsMap[packageHistory.id],
      pointBorderColor: '#fff',
      pointBorderWidth: 1,
      pointHoverBackgroundColor: this.colorsMap[packageHistory.id],
      pointHoverBorderColor: this.colorsMap[packageHistory.id],
      fill: false,
      data: totalDownloads,
    };
  }
}
