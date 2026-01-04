import { Component } from '@angular/core';

@Component({
  selector: 'app-footer',
  templateUrl: './footer.component.html',
  styleUrls: ['./footer.component.scss'],
  standalone: false
})
export class FooterComponent {
  public year = new Date().getFullYear();
  public version = (window as any).SENTRY_RELEASE?.id ?? 'local';
}
