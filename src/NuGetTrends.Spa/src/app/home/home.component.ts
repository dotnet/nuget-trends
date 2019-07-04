import { Component, OnInit } from '@angular/core';
import {Chart, ChartOptions} from 'chart.js';
import {DatePipe} from '@angular/common';
import {AppAnimations} from '../shared';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  animations: [ AppAnimations.slideInOutAnimation ]
})
export class HomeComponent {

}
