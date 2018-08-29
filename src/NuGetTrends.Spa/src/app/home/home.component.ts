import { Component, OnInit } from '@angular/core';
import {Chart, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';
import {AppAnimations} from '../shared/common';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  animations: [ AppAnimations.slideInOutAnimation ]
})
export class HomeComponent implements OnInit {

  constructor(private datePipe: DatePipe) { }

  ngOnInit() {
    this.fillPublishedChart();
  }

  private fillPublishedChart(): void {
    const packageData = {
      id: 'Packages Published',
      color: '#e91365',
      downloads: [
        {date: '2017-08-20T00:00:00', count: 2878142.5},
        {date: '2017-08-27T00:00:00', count: 2929586.1428571427},
        {date: '2017-09-03T00:00:00', count: 2985325.5714285714},
        {date: '2017-09-10T00:00:00', count: 3048006.2857142859},
        {date: '2017-09-17T00:00:00', count: 3110683.8333333335},
        {date: '2017-09-24T00:00:00', count: 3176684.0},
        {date: '2017-10-01T00:00:00', count: 3214292.0}
      ]
    };

    const tempData = {labels: [], datasets: []};

    tempData.labels = packageData.downloads.map((download: any) => {
      return this.datePipe.transform(download.date, 'MMM d');
    });

    const totalDownloads = packageData.downloads.map((data: any) => {
      return data.count;
    });

    const dataSet = {
      label: packageData.id,
      backgroundColor: packageData.color,
      borderColor: packageData.color,
      pointRadius: 6,
      pointHoverRadius: 8,
      pointBackgroundColor: packageData.color,
      pointBorderColor: '#fff',
      pointBorderWidth: 1,
      pointHoverBackgroundColor: packageData.color,
      pointHoverBorderColor: packageData.color,
      fill: false,
      data: totalDownloads,
    };
    tempData.datasets.push(dataSet);

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
          gridLines: {
            display: false
          },
          ticks: {
            fontColor: '#CCC'
          },
          scaleLabel: {
            display: false,
            labelString: 'Month'
          }
        }],
        yAxes: [{
          display: false
        }]
      }
    };

    const ctx1 = (<any>document.getElementById('published-chart1')).getContext('2d');
    const ctx2 = (<any>document.getElementById('published-chart2')).getContext('2d');
    const ctx3 = (<any>document.getElementById('published-chart3')).getContext('2d');

    const chart1 = new Chart(ctx1, {
      type: 'line',
      data: tempData,
      options: chartOptions
    });
    const chart2 = new Chart(ctx2, {
      type: 'line',
      data: tempData,
      options: chartOptions
    });
    const chart3 = new Chart(ctx3, {
      type: 'line',
      data: tempData,
      options: chartOptions
    });

  }

}
