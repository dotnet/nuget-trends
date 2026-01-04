import { Component } from '@angular/core';

@Component({
  selector: 'app-footer',
  templateUrl: './footer.component.html',
  styleUrls: ['./footer.component.scss'],
  standalone: false
})
export class FooterComponent {
  public year = new Date().getFullYear();

  private readonly devVersion = 'NuGetTrends.Spa@1.0.0+dev';

  get fullVersion(): string {
    return (window as any).SENTRY_RELEASE?.id ?? this.devVersion;
  }

  get shortSha(): string {
    const sha = this.fullVersion.split('+')[1];
    return sha?.substring(0, 8) ?? 'unknown';
  }

  copyVersion(): void {
    navigator.clipboard.writeText(this.fullVersion);
  }
}
