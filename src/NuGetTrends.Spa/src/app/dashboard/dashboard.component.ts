import {Component, OnInit} from '@angular/core';
import {Chart, ChartDataSets, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';

import {IPackageDownloadHistory, IDownloadStats, PackageToColorMap } from './common/package-models';
import {PackagesService} from "./common/packages.service";

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {

  constructor(
    private packagesService: PackagesService,
    private datePipe: DatePipe) {
  }

  trendChart: Chart;
  canvas: any;
  ctx: any;
  colorsMap: PackageToColorMap = {};

  ngOnInit() {
    this.colorsMap['entity-framework'] = '#B4F30D';
    this.colorsMap['dapper'] = '#0D45F3';
    this.colorsMap['ef-core'] = '#F30D30';

    this.populateChart();
  }

  addNextDataSet() {
    const nextPackage = this.getNextMockedData();
    const dataSet = this.parseDataSet(nextPackage);
    this.trendChart.config.data.datasets.push(dataSet);
    this.trendChart.update();
  }

  populateChart() {
    const data = this.getMockedData();
    const chart_data = {labels: [], datasets: []};

    data.map((packageDataPerPeriod: IPackageDownloadHistory, i: number) => {
      // create the labels
      if (i === 0) {
        chart_data.labels = packageDataPerPeriod.downloads.map((download: IDownloadStats) => {
          return this.datePipe.transform(download.date, 'MMM d');
        });
      }
      // parse the result into a ChartDataSets type
      chart_data.datasets.push(this.parseDataSet(packageDataPerPeriod));
    });

    const chart_options: ChartOptions = {
      responsive: true,
      maintainAspectRatio: false,
      scales: {
        xAxes: [{
          display: true,
          scaleLabel: {
            display: false,
            labelString: 'Month'
          }
        }],
        yAxes: [{
          display: true,
          scaleLabel: {
            display: true,
            labelString: 'Downloads'
          }
        }]
      }
    };

    this.canvas = document.getElementById('trend-chart');
    this.ctx = this.canvas.getContext('2d');

    this.trendChart = new Chart(this.ctx, {
      type: 'line',
      data: chart_data,
      options: chart_options
    });
  }

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
      pointHoverBackgroundColor: '#ffffff',
      pointHoverBorderColor: this.colorsMap[packageHistory.id],
      fill: false,
      data: totalDownloads,
    };
  }

  private getMockedData(): IPackageDownloadHistory[] {
    return [
      {
        id: 'entity-framework',
        downloads: [
          {
            date: new Date(2018, 2, 18),
            count: 1995642
          },
          {
            date: new Date(2018, 2, 25),
            count: 1950976
          },
          {
            date: new Date(2018, 3, 4),
            count: 1951476
          },
          {
            date: new Date(2018, 3, 11),
            count: 1952476
          },
          {
            date: new Date(2018, 3, 18),
            count: 1953476
          },
          {
            date: new Date(2018, 3, 25),
            count: 1954476
          },
          {
            date: new Date(2018, 4, 1),
            count: 1955476
          },
          {
            date: new Date(2018, 4, 8),
            count: 1956476
          },
          {
            date: new Date(2018, 4, 15),
            count: 1957476
          },
          {
            date: new Date(2018, 4, 22),
            count: 1958476
          },
          {
            date: new Date(2018, 4, 29),
            count: 1959476
          }
        ]
      },
      {
        id: 'dapper',
        downloads: [
          {
            date: new Date(2018, 2, 18),
            count: 1895642
          },
          {
            date: new Date(2018, 2, 25),
            count: 1850976
          },
          {
            date: new Date(2018, 3, 4),
            count: 1851476
          },
          {
            date: new Date(2018, 3, 11),
            count: 1852476
          },
          {
            date: new Date(2018, 3, 18),
            count: 1853476
          },
          {
            date: new Date(2018, 3, 25),
            count: 1854476
          },
          {
            date: new Date(2018, 4, 1),
            count: 1855476
          },
          {
            date: new Date(2018, 4, 8),
            count: 1856476
          },
          {
            date: new Date(2018, 4, 15),
            count: 1857476
          },
          {
            date: new Date(2018, 4, 22),
            count: 1858476
          },
          {
            date: new Date(2018, 4, 29),
            count: 1859476
          }
        ]
      }
    ];
  }

  private getNextMockedData(): IPackageDownloadHistory {
    return {
      id: 'ef-core',
      downloads: [
        {
          date: new Date(2018, 2, 18),
          count: 1795642
        },
        {
          date: new Date(2018, 2, 25),
          count: 1750976
        },
        {
          date: new Date(2018, 3, 4),
          count: 1751476
        },
        {
          date: new Date(2018, 3, 11),
          count: 1752476
        },
        {
          date: new Date(2018, 3, 18),
          count: 1753476
        },
        {
          date: new Date(2018, 3, 25),
          count: 1754476
        },
        {
          date: new Date(2018, 4, 1),
          count: 1852676
        },
        {
          date: new Date(2018, 4, 8),
          count: 2056476
        },
        {
          date: new Date(2018, 4, 15),
          count: 2157476
        },
        {
          date: new Date(2018, 4, 22),
          count: 2258476
        },
        {
          date: new Date(2018, 4, 29),
          count: 2299476
        }
      ]
    };
  }
}
