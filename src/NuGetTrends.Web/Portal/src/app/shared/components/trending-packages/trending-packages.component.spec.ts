import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CommonModule } from '@angular/common';

import { TrendingPackagesComponent } from './trending-packages.component';
import { PackagesService } from '../../../core/services/packages.service';
import { ITrendingPackage } from '../../models/package-models';

describe('TrendingPackagesComponent', () => {
  let component: TrendingPackagesComponent;
  let fixture: ComponentFixture<TrendingPackagesComponent>;
  let packagesServiceSpy: jasmine.SpyObj<PackagesService>;
  let routerSpy: jasmine.SpyObj<Router>;

  const mockTrendingPackages: ITrendingPackage[] = [
    {
      packageId: 'Newtonsoft.Json',
      downloadCount: 150000,
      growthRate: 0.25,
      iconUrl: 'https://example.com/icon1.png',
      gitHubUrl: 'https://github.com/JamesNK/Newtonsoft.Json'
    },
    {
      packageId: 'Sentry',
      downloadCount: 50000,
      growthRate: 0.75,
      iconUrl: 'https://example.com/icon2.png',
      gitHubUrl: 'https://github.com/getsentry/sentry-dotnet'
    },
    {
      packageId: 'NoGitHub.Package',
      downloadCount: 10000,
      growthRate: -0.1,
      iconUrl: 'https://example.com/icon3.png',
      gitHubUrl: null
    }
  ];

  beforeEach(waitForAsync(() => {
    packagesServiceSpy = jasmine.createSpyObj('PackagesService', ['getTrendingPackages']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [CommonModule],
      declarations: [TrendingPackagesComponent],
      providers: [
        { provide: PackagesService, useValue: packagesServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    }).compileComponents();
  }));

  beforeEach(() => {
    packagesServiceSpy.getTrendingPackages.and.returnValue(of(mockTrendingPackages));
    fixture = TestBed.createComponent(TrendingPackagesComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load trending packages on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();

    expect(packagesServiceSpy.getTrendingPackages).toHaveBeenCalledWith(10);
    expect(component.trendingPackages).toEqual(mockTrendingPackages);
    expect(component.isLoading).toBeFalse();
    expect(component.errorMessage).toBeNull();
  }));

  it('should show error message when loading fails', fakeAsync(() => {
    packagesServiceSpy.getTrendingPackages.and.returnValue(throwError(() => new Error('Network error')));

    fixture.detectChanges();
    tick();

    expect(component.isLoading).toBeFalse();
    expect(component.errorMessage).toBeTruthy();
    expect(component.trendingPackages).toEqual([]);
  }));

  it('should navigate to package when clicked', () => {
    fixture.detectChanges();

    component.navigateToPackage('Newtonsoft.Json');

    expect(routerSpy.navigate).toHaveBeenCalledWith(['/packages', 'Newtonsoft.Json']);
  });

  it('should open NuGet.org page in new tab', () => {
    spyOn(window, 'open');
    fixture.detectChanges();

    component.openNuGetPage('Newtonsoft.Json');

    expect(window.open).toHaveBeenCalledWith(
      'https://www.nuget.org/packages/Newtonsoft.Json',
      '_blank',
      'noopener,noreferrer'
    );
  });

  describe('formatGrowthRate', () => {
    it('should format positive growth rate with plus sign', () => {
      expect(component.formatGrowthRate(0.25)).toBe('+25%');
      expect(component.formatGrowthRate(1.0)).toBe('+100%');
      expect(component.formatGrowthRate(0.5)).toBe('+50%');
    });

    it('should format negative growth rate', () => {
      expect(component.formatGrowthRate(-0.1)).toBe('-10%');
      expect(component.formatGrowthRate(-0.5)).toBe('-50%');
    });

    it('should format zero growth rate', () => {
      expect(component.formatGrowthRate(0)).toBe('0%');
    });

    it('should return N/A for null growth rate', () => {
      expect(component.formatGrowthRate(null)).toBe('N/A');
    });
  });

  describe('getGrowthClass', () => {
    it('should return growth-positive for positive rates', () => {
      expect(component.getGrowthClass(0.25)).toBe('growth-positive');
      expect(component.getGrowthClass(0.01)).toBe('growth-positive');
    });

    it('should return growth-negative for negative rates', () => {
      expect(component.getGrowthClass(-0.1)).toBe('growth-negative');
      expect(component.getGrowthClass(-0.5)).toBe('growth-negative');
    });

    it('should return growth-neutral for zero', () => {
      expect(component.getGrowthClass(0)).toBe('growth-neutral');
    });

    it('should return growth-neutral for null', () => {
      expect(component.getGrowthClass(null)).toBe('growth-neutral');
    });
  });

  describe('formatDownloadCount', () => {
    it('should format millions with M suffix', () => {
      expect(component.formatDownloadCount(1500000)).toBe('1.5M');
      expect(component.formatDownloadCount(1000000)).toBe('1.0M');
      expect(component.formatDownloadCount(25000000)).toBe('25.0M');
    });

    it('should format thousands with K suffix', () => {
      expect(component.formatDownloadCount(1500)).toBe('1.5K');
      expect(component.formatDownloadCount(1000)).toBe('1.0K');
      expect(component.formatDownloadCount(50000)).toBe('50.0K');
    });

    it('should show raw number for small counts', () => {
      expect(component.formatDownloadCount(999)).toBe('999');
      expect(component.formatDownloadCount(100)).toBe('100');
      expect(component.formatDownloadCount(0)).toBe('0');
    });
  });
});
