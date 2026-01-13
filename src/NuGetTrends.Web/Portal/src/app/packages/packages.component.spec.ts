import { HttpClientModule, HttpErrorResponse } from '@angular/common/http';
import { CommonModule, DatePipe } from '@angular/common';
import { Router, ActivatedRoute } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { ToastrModule, ToastrService } from 'ngx-toastr';
import { of, Observable, throwError } from 'rxjs';

import { PackagesService, PackageInteractionService } from '../core';
import { MockedRouter, ToastrMock, MockedActivatedRoute } from '../mocks';
import { PackageListComponent, SearchPeriodComponent } from '../shared/components';
import { IPackageDownloadHistory } from '../shared/models/package-models';
import { PackagesComponent } from './packages.component';

class PackagesServiceMock {

  public static mockedDownloadHistory: IPackageDownloadHistory[] = [
    {
      id: 'EntityFramework',
      downloads: [
        { week: new Date('2018-10-28T00:00:00'), count: 51756066 },
        { week: new Date('2018-11-04T00:00:00'), count: 52022309 },
        { week: new Date('2018-11-11T00:00:00'), count: 52394207 },
      ]
    },
    {
      id: 'Dapper',
      downloads: [
        { week: new Date('2018-10-28T00:00:00'), count: 11659886 },
        { week: new Date('2018-11-04T00:00:00'), count: 11816356 },
        { week: new Date('2018-11-11T00:00:00'), count: 12043389 },
      ]
    }];

  getPackageDownloadHistory(packageId: string, _months: number = 12): Observable<IPackageDownloadHistory> {
    return of(PackagesServiceMock.mockedDownloadHistory.find(p => p.id === packageId)!);
  }

  checkPackageExistsOnNuGet(_packageId: string): Observable<boolean> {
    // Default mock returns false (package doesn't exist on nuget.org)
    return of(false);
  }
}

describe('PackagesComponent', () => {
  let component: PackagesComponent;
  let fixture: ComponentFixture<PackagesComponent>;
  let activatedRoute: MockedActivatedRoute;
  let mockedPackageService: PackagesServiceMock;
  let router: MockedRouter;
  let mockedToastr: ToastrMock;
  let packageInteractionService: PackageInteractionService;
  const queryParamName = 'ids';

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [
        PackagesComponent, PackageListComponent, SearchPeriodComponent
      ],
      imports: [
        CommonModule,
        FormsModule,
        ReactiveFormsModule,
        NoopAnimationsModule,
        RouterTestingModule.withRoutes([]),
        HttpClientModule,
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
        }),
      ],
      providers: [
        DatePipe,
        { provide: PackagesService, useClass: PackagesServiceMock },
        { provide: ToastrService, useClass: ToastrMock },
        { provide: Router, useClass: MockedRouter },
        { provide: ActivatedRoute, useClass: MockedActivatedRoute },
      ],
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(PackagesComponent);
    component = fixture.componentInstance;
    activatedRoute = MockedActivatedRoute.injectMockActivatedRoute();
    mockedPackageService = TestBed.inject(PackagesService);
    packageInteractionService = TestBed.inject(PackageInteractionService);
    router = MockedRouter.injectMockRouter();
    mockedToastr = TestBed.inject(ToastrService);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('Load Packages from the URL', () => {

    it('should skip loading packages if the URL does not contain params', () => {
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
      spyOn(packageInteractionService, 'addPackage').and.callThrough();

      fixture.detectChanges();

      expect(mockedPackageService.getPackageDownloadHistory).not.toHaveBeenCalled();
      expect(packageInteractionService.addPackage).not.toHaveBeenCalledTimes(1);
    });

    it('should load package history when ids are present in the URL', fakeAsync(() => {
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
      spyOn(packageInteractionService, 'addPackage').and.callThrough();
      spyOn(packageInteractionService, 'plotPackage').and.callThrough();

      const packages = ['EntityFramework'];

      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      const actualPackageElements: HTMLElement[] = fixture.nativeElement.querySelectorAll('.tags span');

      expect(actualPackageElements.length).toBe(packages.length);
      expect(mockedPackageService.getPackageDownloadHistory).toHaveBeenCalledTimes(packages.length);
      expect(packageInteractionService.addPackage).toHaveBeenCalledTimes(packages.length);
      expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(packages.length);
    }));

    it('should show error message when request fails during initial history load', fakeAsync(() => {
      // Fake the service returning a 500 error
      const response = new HttpErrorResponse({
        error: '{ "error": ""}',
        status: 500,
        statusText: 'Internal Server Error',
        url: 'http://test'
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      spyOn(mockedToastr, 'error').and.callThrough();

      const packages = ['EntityFramework'];

      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      expect(mockedToastr.error).toHaveBeenCalled();
    }));

    it('should show empty state when package does not exist and 404 returned', fakeAsync(() => {
      // Fake the service returning a 404 error
      const response = new HttpErrorResponse({
        error: 'Not Found',
        status: 404,
        statusText: 'Not Found',
        url: 'http://test/api/package/history/InvalidPackage'
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      // Package doesn't exist on nuget.org
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(false));
      spyOn(mockedToastr, 'warning').and.callThrough();
      spyOn(mockedToastr, 'error').and.callThrough();
      spyOn(router, 'navigate').and.callThrough();

      const packages = ['InvalidPackage'];

      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // No toast during initial load - empty state handles the messaging
      expect(mockedToastr.warning).not.toHaveBeenCalled();
      expect(mockedToastr.error).not.toHaveBeenCalled();
      // Component should track the not-found package
      expect(component.notFoundPackages.length).toBe(1);
      expect(component.notFoundPackages[0]).toEqual({ packageId: 'InvalidPackage', existsOnNuGet: false });
      expect(component.showEmptyState).toBeTrue();
    }));

    it('should show empty state when package exists on nuget.org but not tracked', fakeAsync(() => {
      // Fake the service returning a 404 error
      const response = new HttpErrorResponse({
        error: 'Not Found',
        status: 404,
        statusText: 'Not Found',
        url: 'http://test/api/package/history/System.Diagnostics'
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      // Package exists on nuget.org but NuGet Trends doesn't track it
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(true));
      spyOn(mockedToastr, 'warning').and.callThrough();
      spyOn(mockedToastr, 'error').and.callThrough();
      spyOn(router, 'navigate').and.callThrough();

      const packages = ['System.Diagnostics'];

      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // No toast during initial load - empty state handles the messaging
      expect(mockedToastr.warning).not.toHaveBeenCalled();
      expect(mockedToastr.error).not.toHaveBeenCalled();
      // Component should track the not-found package
      expect(component.notFoundPackages.length).toBe(1);
      expect(component.notFoundPackages[0]).toEqual({ packageId: 'System.Diagnostics', existsOnNuGet: true });
      expect(component.showEmptyState).toBeTrue();
    }));

    it('should load package from NuGet-style URL path parameter (/packages/:packageId)', fakeAsync(() => {
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
      spyOn(packageInteractionService, 'addPackage').and.callThrough();
      spyOn(packageInteractionService, 'plotPackage').and.callThrough();

      // Set path parameter (NuGet-style URL: /packages/EntityFramework)
      activatedRoute.testPathParamMap = { packageId: 'EntityFramework' };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      const actualPackageElements: HTMLElement[] = fixture.nativeElement.querySelectorAll('.tags span');

      expect(actualPackageElements.length).toBe(1);
      expect(mockedPackageService.getPackageDownloadHistory).toHaveBeenCalledTimes(1);
      expect(packageInteractionService.addPackage).toHaveBeenCalledTimes(1);
      expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(1);
    }));
  });

  describe('Period Changed', () => {

    it('should re-load package history when the period changes', fakeAsync(() => {
      // Initially start the page with a package in URL
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
      spyOn(packageInteractionService, 'updatePackage').and.callThrough();
      spyOn(packageInteractionService, 'plotPackage').and.callThrough();

      // trigger the change of the period via the service
      const newPeriod = 12;
      packageInteractionService.changeSearchPeriod(newPeriod);
      tick();
      fixture.detectChanges();

      const actualPackageElements: HTMLElement[] = fixture.nativeElement.querySelectorAll('.tags span');

      expect(actualPackageElements.length).toBe(packages.length);
      expect(mockedPackageService.getPackageDownloadHistory).toHaveBeenCalledWith(packages[0], newPeriod);
      expect(packageInteractionService.updatePackage).toHaveBeenCalledTimes(packages.length);
      expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(packages.length);
    }));

    it('should skip re-loading package history when the period changes if there are no ids in the URL', fakeAsync(() => {
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
      spyOn(packageInteractionService, 'updatePackage').and.callThrough();
      spyOn(packageInteractionService, 'plotPackage').and.callThrough();

      fixture.detectChanges();

      // trigger the change of the period via the service
      const newPeriod = 12;
      packageInteractionService.changeSearchPeriod(newPeriod);
      tick();
      fixture.detectChanges();

      expect(mockedPackageService.getPackageDownloadHistory).not.toHaveBeenCalled();
      expect(packageInteractionService.updatePackage).not.toHaveBeenCalled();
      expect(packageInteractionService.plotPackage).not.toHaveBeenCalled();
    }));

    it('should show error message when request fails during period change', fakeAsync(() => {
      // Initially start the page with a package in URL
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Fake the service returning a 500 error
      const response = new HttpErrorResponse({
        error: '{ "error": ""}',
        status: 500,
        statusText: 'Internal Server Error',
        url: 'http://test'
      });

      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      spyOn(mockedToastr, 'error').and.callThrough();

      // trigger the change of the period via the service
      const newPeriod = 12;
      packageInteractionService.changeSearchPeriod(newPeriod);
      tick();
      fixture.detectChanges();

      expect(mockedToastr.error).toHaveBeenCalled();
    }));

    it('should show warning message when package returns 404 during period change', fakeAsync(() => {
      // Initially start the page with a package in URL
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Fake the service returning a 404 error
      const response = new HttpErrorResponse({
        error: 'Not Found',
        status: 404,
        statusText: 'Not Found',
        url: 'http://test/api/package/history/EntityFramework'
      });

      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      // Package doesn't exist on nuget.org
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(false));
      spyOn(mockedToastr, 'warning').and.callThrough();
      spyOn(mockedToastr, 'error').and.callThrough();

      // trigger the change of the period via the service
      const newPeriod = 12;
      packageInteractionService.changeSearchPeriod(newPeriod);
      tick();
      fixture.detectChanges();

      expect(mockedToastr.warning).toHaveBeenCalledWith("Package 'EntityFramework' doesn't exist.");
      expect(mockedToastr.error).not.toHaveBeenCalled();
    }));
  });

  describe('Remove Package', () => {

    it('should remove package from chart, URL and badge list', fakeAsync(() => {
      const spy = spyOn(router, 'navigate').and.callThrough();

      // Arrange - Page should have been loaded with 2 packages
      const packages = ['EntityFramework', 'Dapper'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();
      let actualPackageElements: HTMLElement[] = fixture.nativeElement.querySelectorAll('.tags span');
      expect(actualPackageElements.length).toBe(packages.length);

      // Act - Removes the "Dapper" package
      const removePackageButtons: Array<HTMLElement>
        = fixture.nativeElement.querySelectorAll('.tags span button');

      removePackageButtons[1].click();
      tick();
      fixture.detectChanges();

      // Assert - navigate should have been called with only EntityFramework
      actualPackageElements = fixture.nativeElement.querySelectorAll('.tags span');

      const expectedUrlIds = ['EntityFramework'];
      const navigateActualParams: any[] = spy.calls.mostRecent().args;

      expect(actualPackageElements.length).toBe(expectedUrlIds.length);
      expect(navigateActualParams[1].queryParams[queryParamName]).toEqual(expectedUrlIds);
    }));

    it('should navigate home when the last package is removed', fakeAsync(() => {
      spyOn(router, 'navigate').and.callThrough();

      // Arrange - Initialize the page with 1 package
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Act
      const removePackageButton: HTMLElement
        = fixture.nativeElement.querySelector('.tags span button');

      removePackageButton.click();
      tick();
      fixture.detectChanges();

      expect(router.navigate).toHaveBeenCalledWith(['/']);
    }));
  });

  describe('Chart Safety', () => {

    it('should clear active chart elements before updating to prevent tooltip errors', fakeAsync(() => {
      // Arrange - Initialize with a package to create the chart
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Access the chart instance via the component
      const chart = (component as any).trendChart;
      expect(chart).toBeTruthy();

      // Spy on setActiveElements to verify it's called before update
      const setActiveElementsSpy = spyOn(chart, 'setActiveElements').and.callThrough();
      const updateSpy = spyOn(chart, 'update').and.callThrough();

      // Act - Add a second package which triggers chart update
      packageInteractionService.plotPackage(PackagesServiceMock.mockedDownloadHistory[1]);
      tick();
      fixture.detectChanges();

      // Assert - setActiveElements should be called before update
      expect(setActiveElementsSpy).toHaveBeenCalledWith([]);
      expect(updateSpy).toHaveBeenCalled();
    }));

    it('should clear active chart elements when removing a package', fakeAsync(() => {
      // Arrange - Initialize with two packages
      const packages = ['EntityFramework', 'Dapper'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      const chart = (component as any).trendChart;
      const setActiveElementsSpy = spyOn(chart, 'setActiveElements').and.callThrough();

      // Act - Remove one package
      packageInteractionService.removePackage('Dapper');
      tick();
      fixture.detectChanges();

      // Assert - setActiveElements should be called to clear tooltip state
      expect(setActiveElementsSpy).toHaveBeenCalledWith([]);
    }));

    it('should nullify chart reference on destroy to prevent stale access', fakeAsync(() => {
      // Arrange - Initialize with a package to create the chart
      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Verify chart exists
      expect((component as any).trendChart).toBeTruthy();

      // Act - Destroy the component
      component.ngOnDestroy();

      // Assert - Chart reference should be null
      expect((component as any).trendChart).toBeNull();
    }));

  });

  describe('Plot Package', () => {

    it('should react to package plotted event by adding it to the chart', fakeAsync(() => {
      const spy = spyOn(router, 'navigate').and.callThrough();
      fixture.detectChanges();

      // Act
      const entityFrameworkHistory = PackagesServiceMock.mockedDownloadHistory[0];
      packageInteractionService.plotPackage(entityFrameworkHistory);
      tick();
      fixture.detectChanges();

      // Assert - Only EntityFramework should be in the URL
      const expectedUrlIds = 'EntityFramework';
      const navigateActualParams: any[] = spy.calls.mostRecent().args;

      expect(navigateActualParams[1].queryParams[queryParamName]).toEqual(expectedUrlIds);
      expect(navigateActualParams[1].replaceUrl).toBeTruthy();
      expect(navigateActualParams[1].queryParamsHandling).toBe('merge');
    }));

    it('should update the URL as new packages are added to the chart', fakeAsync(() => {
      const spy = spyOn(router, 'navigate').and.callThrough();
      fixture.detectChanges();

      const packages = ['EntityFramework'];
      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Act - Add a second package
      packageInteractionService.plotPackage(PackagesServiceMock.mockedDownloadHistory[1]);
      fixture.detectChanges();
      tick();

      const navigateActualParams: any[] = spy.calls.mostRecent().args;

      // URL should have been updated
      expect(navigateActualParams[1].queryParams[queryParamName]).toEqual(['EntityFramework', 'Dapper']);
      expect(navigateActualParams[1].replaceUrl).toBeTruthy();
      expect(navigateActualParams[1].queryParamsHandling).toBe('merge');
    }));

    it('should transition from NuGet-style URL to query params when adding second package', fakeAsync(() => {
      const spy = spyOn(router, 'navigate').and.callThrough();

      // Start with NuGet-style URL (/packages/EntityFramework)
      activatedRoute.testPathParamMap = { packageId: 'EntityFramework' };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Act - Add a second package via plotPackage
      packageInteractionService.plotPackage(PackagesServiceMock.mockedDownloadHistory[1]);
      fixture.detectChanges();
      tick();

      const navigateActualParams: any[] = spy.calls.mostRecent().args;

      // Should navigate to /packages with both packages as query params
      expect(navigateActualParams[0]).toEqual(['/packages']);
      expect(navigateActualParams[1].queryParams[queryParamName]).toEqual(['EntityFramework', 'Dapper']);
      expect(navigateActualParams[1].replaceUrl).toBeTruthy();
    }));
  });

  describe('Empty State', () => {

    it('should set isLoading to false when no packages in URL', fakeAsync(() => {
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      expect(component.isLoading).toBeFalse();
      expect(component.showEmptyState).toBeFalse();
    }));

    it('should track not-found packages for empty state display', fakeAsync(() => {
      const response = new HttpErrorResponse({
        error: 'Not Found',
        status: 404,
        statusText: 'Not Found',
        url: 'http://test/api/package/history/NewPackage'
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(true));

      activatedRoute.testParamMap = { months: 12, ids: ['NewPackage'] };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Component should track the not-found package
      expect(component.notFoundPackages.length).toBe(1);
      expect(component.notFoundPackages[0]).toEqual({ packageId: 'NewPackage', existsOnNuGet: true });
      expect(component.showEmptyState).toBeTrue();
      expect(component.isLoading).toBeFalse();
    }));

    it('should track non-existent packages for empty state display', fakeAsync(() => {
      const response = new HttpErrorResponse({
        error: 'Not Found',
        status: 404,
        statusText: 'Not Found',
        url: 'http://test/api/package/history/FakePackage'
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(() => response));
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(false));

      activatedRoute.testParamMap = { months: 12, ids: ['FakePackage'] };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Component should track the not-found package
      expect(component.notFoundPackages.length).toBe(1);
      expect(component.notFoundPackages[0]).toEqual({ packageId: 'FakePackage', existsOnNuGet: false });
      expect(component.showEmptyState).toBeTrue();
      expect(component.isLoading).toBeFalse();
    }));

    it('should open NuGet.org page when openNuGetPage is called', () => {
      const windowOpenSpy = spyOn(window, 'open');
      component.openNuGetPage('TestPackage');
      expect(windowOpenSpy).toHaveBeenCalledWith('https://www.nuget.org/packages/TestPackage', '_blank');
    });

    it('should correctly compute hasLoadedPackages based on chart datasets', () => {
      // Initially no packages loaded
      expect(component.hasLoadedPackages).toBeFalse();
    });

    it('should correctly compute showEmptyState', () => {
      // Initially isLoading is true and no not-found packages
      expect(component.showEmptyState).toBeFalse();

      // Simulate loading complete with no packages
      component.isLoading = false;
      expect(component.showEmptyState).toBeFalse(); // Still false because no notFoundPackages

      // Add a not-found package
      component.notFoundPackages = [{ packageId: 'Test', existsOnNuGet: true }];
      expect(component.showEmptyState).toBeTrue();
    });

    it('should show toast for failed packages when some packages load successfully (partial failure)', fakeAsync(() => {
      // This test verifies the fix for the race condition where 404 could arrive
      // before successful packages were plotted, causing silent failures
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callFake((packageId: string) => {
        if (packageId === 'EntityFramework') {
          return of(PackagesServiceMock.mockedDownloadHistory[0]);
        }
        return throwError(() => new HttpErrorResponse({
          error: 'Not Found',
          status: 404,
          statusText: 'Not Found',
          url: `http://test/api/package/history/${packageId}`
        }));
      });
      spyOn(mockedPackageService, 'checkPackageExistsOnNuGet').and.returnValue(of(false));
      spyOn(mockedToastr, 'warning').and.callThrough();

      // Load both a valid package and an invalid one
      activatedRoute.testParamMap = { months: 12, ids: ['EntityFramework', 'InvalidPackage'] };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      // Toast should be shown for the failed package since some packages succeeded
      expect(mockedToastr.warning).toHaveBeenCalledWith("Package 'InvalidPackage' doesn't exist.");
      // Not in empty state since EntityFramework loaded
      expect(component.showEmptyState).toBeFalse();
      expect(component.hasLoadedPackages).toBeTrue();
      // But we still track the failed package
      expect(component.notFoundPackages.length).toBe(1);
    }));
  });
});
