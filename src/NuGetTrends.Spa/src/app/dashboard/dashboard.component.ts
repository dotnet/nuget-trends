import {Component, OnInit} from '@angular/core';
import {Chart, ChartDataSets, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';

import {IPackageDownloadHistory, IDownloadPeriod, PackageToColorMap, IPackageSearchResult} from './common/package-models';
import {PackagesService} from './common/packages.service';

@Component({
  selector: 'app-dashboard',
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {

  constructor(private datePipe: DatePipe, private packageService: PackagesService) {
  }

  trendChart: Chart;
  canvas: any;
  ctx: any;
  colorsMap: PackageToColorMap = {};
  packages: IPackageSearchResult[];

  ngOnInit() {
    this.colorsMap['entity-framework'] = '#B4F30D';
    this.colorsMap['dapper'] = '#0D45F3';
    this.colorsMap['ef-core'] = '#F30D30';

    this.populateChart();
    this.packageService.searchPackage('a').subscribe((result: IPackageSearchResult[]) => {
      this.packages = result;
    });
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
        chart_data.labels = packageDataPerPeriod.data.map((download: IDownloadPeriod) => {
          return this.datePipe.transform(download.period, 'MMM d');
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
    const totalDownloads = packageHistory.data.map((data: IDownloadPeriod) => {
      return data.downloads;
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
        data: [
          {
            period: new Date(2018, 2, 18),
            downloads: 1995642
          },
          {
            period: new Date(2018, 2, 25),
            downloads: 1950976
          },
          {
            period: new Date(2018, 3, 4),
            downloads: 1951476
          },
          {
            period: new Date(2018, 3, 11),
            downloads: 1952476
          },
          {
            period: new Date(2018, 3, 18),
            downloads: 1953476
          },
          {
            period: new Date(2018, 3, 25),
            downloads: 1954476
          },
          {
            period: new Date(2018, 4, 1),
            downloads: 1955476
          },
          {
            period: new Date(2018, 4, 8),
            downloads: 1956476
          },
          {
            period: new Date(2018, 4, 15),
            downloads: 1957476
          },
          {
            period: new Date(2018, 4, 22),
            downloads: 1958476
          },
          {
            period: new Date(2018, 4, 29),
            downloads: 1959476
          }
        ]
      },
      {
        id: 'dapper',
        data: [
          {
            period: new Date(2018, 2, 18),
            downloads: 1895642
          },
          {
            period: new Date(2018, 2, 25),
            downloads: 1850976
          },
          {
            period: new Date(2018, 3, 4),
            downloads: 1851476
          },
          {
            period: new Date(2018, 3, 11),
            downloads: 1852476
          },
          {
            period: new Date(2018, 3, 18),
            downloads: 1853476
          },
          {
            period: new Date(2018, 3, 25),
            downloads: 1854476
          },
          {
            period: new Date(2018, 4, 1),
            downloads: 1855476
          },
          {
            period: new Date(2018, 4, 8),
            downloads: 1856476
          },
          {
            period: new Date(2018, 4, 15),
            downloads: 1857476
          },
          {
            period: new Date(2018, 4, 22),
            downloads: 1858476
          },
          {
            period: new Date(2018, 4, 29),
            downloads: 1859476
          }
        ]
      }
    ];
  }

  private getNextMockedData(): IPackageDownloadHistory {
    return {
      id: 'ef-core',
      data: [
        {
          period: new Date(2018, 2, 18),
          downloads: 1795642
        },
        {
          period: new Date(2018, 2, 25),
          downloads: 1750976
        },
        {
          period: new Date(2018, 3, 4),
          downloads: 1751476
        },
        {
          period: new Date(2018, 3, 11),
          downloads: 1752476
        },
        {
          period: new Date(2018, 3, 18),
          downloads: 1753476
        },
        {
          period: new Date(2018, 3, 25),
          downloads: 1754476
        },
        {
          period: new Date(2018, 4, 1),
          downloads: 1852676
        },
        {
          period: new Date(2018, 4, 8),
          downloads: 2056476
        },
        {
          period: new Date(2018, 4, 15),
          downloads: 2157476
        },
        {
          period: new Date(2018, 4, 22),
          downloads: 2258476
        },
        {
          period: new Date(2018, 4, 29),
          downloads: 2299476
        }
      ]
    };
  }
}
