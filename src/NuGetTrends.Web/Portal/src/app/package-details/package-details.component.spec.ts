import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { PackageDetailsComponent } from './package-details.component';
import { IPackageDetails } from '../shared/models/package-models';
import { PackagesService } from '../core';
import { MockedActivatedRoute, MockedRouter, ToastrMock } from '../mocks';

class PackagesServiceMock {
  getPackageDetails(_id: string) {
    const details: IPackageDetails = {
      packageId: 'EntityFramework',
      title: 'Entity Framework',
      summary: 'A modern object-database mapper for .NET.',
      description: 'Entity Framework package details',
      authors: 'Microsoft',
      latestVersion: '6.4.4',
      latestVersionPublishedUtc: '2025-11-20T00:00:00Z',
      latestVersionAgeDays: 79,
      firstVersionPublishedUtc: '2013-10-10T00:00:00Z',
      lastCatalogCommitUtc: '2025-11-21T00:00:00Z',
      lastCatalogCommitAgeDays: 78,
      latestDownloadCount: 550000000,
      latestDownloadCountCheckedUtc: '2026-02-07T00:00:00Z',
      totalVersionCount: 120,
      stableVersionCount: 110,
      prereleaseVersionCount: 10,
      listedVersionCount: 110,
      unlistedVersionCount: 10,
      releasesInLast12Months: 4,
      supportedTargetFrameworkCount: 18,
      latestVersionTargetFrameworkCount: 3,
      distinctDependencyCount: 30,
      latestPackageSizeBytes: 830000,
      iconUrl: 'https://example.test/icon.png',
      projectUrl: 'https://github.com/dotnet/efcore',
      licenseUrl: 'https://licenses.nuget.org/MIT',
      nuGetUrl: 'https://www.nuget.org/packages/EntityFramework',
      nuGetInfoUrl: 'https://nuget.info/packages/EntityFramework/6.4.4',
      topTargetFrameworks: [{ framework: 'netstandard2.0', versionCount: 26 }],
      latestVersionTargetFrameworks: ['net6.0', 'net7.0', 'net8.0'],
      tags: ['orm', 'database']
    };

    return of(details);
  }
}

describe('PackageDetailsComponent', () => {
  let component: PackageDetailsComponent;
  let fixture: ComponentFixture<PackageDetailsComponent>;
  let router: MockedRouter;
  let activatedRoute: MockedActivatedRoute;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [PackageDetailsComponent],
      imports: [NoopAnimationsModule],
      providers: [
        { provide: PackagesService, useClass: PackagesServiceMock },
        { provide: Router, useClass: MockedRouter },
        { provide: ActivatedRoute, useClass: MockedActivatedRoute },
        { provide: ToastrService, useClass: ToastrMock }
      ]
    }).compileComponents();
  }));

  beforeEach(() => {
    router = MockedRouter.injectMockRouter();
    activatedRoute = MockedActivatedRoute.injectMockActivatedRoute();
    activatedRoute.testPathParamMap = { packageId: 'EntityFramework' };

    fixture = TestBed.createComponent(PackageDetailsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load package details on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    expect(component.isLoading).toBeFalse();
    expect(component.packageDetails?.packageId).toBe('EntityFramework');
  }));

  it('should navigate to trends page when adding package to graph', fakeAsync(() => {
    spyOn(router, 'navigate').and.callThrough();

    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    component.addToTrends();

    expect(router.navigate).toHaveBeenCalledWith(['/packages', 'EntityFramework']);
  }));
});
