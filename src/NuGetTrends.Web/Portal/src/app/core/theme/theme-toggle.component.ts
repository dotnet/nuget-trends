import { Component } from '@angular/core';
import { ThemeService } from './theme.service';

@Component({
  selector: 'app-theme-toggle',
  templateUrl: './theme-toggle.component.html',
  styleUrls: ['./theme-toggle.component.scss'],
  standalone: false
})
export class ThemeToggleComponent {
  constructor(public themeService: ThemeService) {}

  get currentIcon(): string {
    const pref = this.themeService.preference();
    switch (pref) {
      case 'system': return 'fa-desktop';
      case 'light': return 'fa-sun-o';
      case 'dark': return 'fa-moon-o';
    }
  }

  get tooltip(): string {
    const pref = this.themeService.preference();
    switch (pref) {
      case 'system': return 'Theme: System (click to change)';
      case 'light': return 'Theme: Light (click to change)';
      case 'dark': return 'Theme: Dark (click to change)';
    }
  }

  toggle(): void {
    this.themeService.cycleTheme();
  }
}
