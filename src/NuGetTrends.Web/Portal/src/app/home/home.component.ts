import { Component } from '@angular/core';
import { AppAnimations } from '../shared';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.scss'],
  animations: [AppAnimations.slideInOutAnimation],
  standalone: false
})
export class HomeComponent {

}
