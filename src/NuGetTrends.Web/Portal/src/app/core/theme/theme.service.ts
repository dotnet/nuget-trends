import { Injectable, signal, effect, computed } from '@angular/core';
import * as Sentry from '@sentry/angular';

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY = 'nuget-trends-theme';

@Injectable({
  providedIn: 'root'
})
export class ThemeService {
  private readonly preferenceSignal = signal<ThemePreference>('system');
  private readonly systemThemeSignal = signal<ResolvedTheme>('light');
  private mediaQuery: MediaQueryList | null = null;
  private lastSentryTheme: ResolvedTheme | null = null;

  readonly preference = this.preferenceSignal.asReadonly();

  readonly resolvedTheme = computed<ResolvedTheme>(() => {
    const pref = this.preferenceSignal();
    if (pref === 'system') {
      return this.systemThemeSignal();
    }
    return pref;
  });

  readonly isDark = computed(() => this.resolvedTheme() === 'dark');

  constructor() {
    this.initSystemThemeListener();
    this.loadSavedPreference();

    effect(() => {
      const theme = this.resolvedTheme();
      this.applyTheme(theme);
      this.updateSentryTheme(theme);
    });
  }

  private initSystemThemeListener(): void {
    if (typeof window === 'undefined') return;

    this.mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    this.systemThemeSignal.set(this.mediaQuery.matches ? 'dark' : 'light');

    const handler = (e: MediaQueryListEvent) => {
      this.systemThemeSignal.set(e.matches ? 'dark' : 'light');
    };

    this.mediaQuery.addEventListener('change', handler);
  }

  private loadSavedPreference(): void {
    if (typeof localStorage === 'undefined') return;

    const saved = localStorage.getItem(STORAGE_KEY) as ThemePreference | null;
    if (saved && ['system', 'light', 'dark'].includes(saved)) {
      this.preferenceSignal.set(saved);
    }
  }

  private savePreference(preference: ThemePreference): void {
    if (typeof localStorage === 'undefined') return;
    localStorage.setItem(STORAGE_KEY, preference);
  }

  private applyTheme(theme: ResolvedTheme): void {
    if (typeof document === 'undefined') return;

    const body = document.body;
    if (theme === 'dark') {
      body.classList.add('dark-theme');
      body.classList.remove('light-theme');
    } else {
      body.classList.add('light-theme');
      body.classList.remove('dark-theme');
    }
  }

  private updateSentryTheme(theme: ResolvedTheme): void {
    if (typeof document === 'undefined') return;

    // Only update if theme has changed to avoid unnecessary operations
    if (this.lastSentryTheme === theme) return;

    // Get Sentry feedback integration
    const feedback = Sentry.getFeedback();
    if (!feedback) return;

    try {
      // Remove the existing widget
      feedback.remove();

      // Re-create the widget with the new theme
      feedback.createWidget({
        colorScheme: theme,
      });

      // The widget auto-attaches itself, no need to manually attach
      this.lastSentryTheme = theme;
    } catch (error) {
      // Widget operations might fail if feedback is not properly initialized
      Sentry.captureException(error, {
        level: 'debug',
        tags: { component: 'theme-service', operation: 'update-sentry-theme' },
        extra: { theme, message: 'Sentry feedback widget could not be updated: feedback integration may not be initialized or configured' }
      });
    }
  }

  setPreference(preference: ThemePreference): void {
    this.preferenceSignal.set(preference);
    this.savePreference(preference);
  }

  cycleTheme(): void {
    const current = this.preferenceSignal();
    const next: ThemePreference =
      current === 'system' ? 'light' :
      current === 'light' ? 'dark' : 'system';
    this.setPreference(next);
  }
}
