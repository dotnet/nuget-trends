import { Component } from '@angular/core';
import { ThemeService } from './core/theme/theme.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: false
})
export class AppComponent {
  /**
   * ThemeService is injected to initialize theming on app startup.
   * The service constructor sets up theme detection and applies the initial theme.
   */
  constructor(themeService: ThemeService) {
    // Access service to prevent tree-shaking
    void themeService.preference;
  }
}
