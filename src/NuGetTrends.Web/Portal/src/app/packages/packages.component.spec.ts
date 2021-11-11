import { HttpClientModule, HttpResponse } from '@angular/common/http';
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
import { PackageListComponent, SearchPeriodComponent, SharePopoverComponent } from '../shared/components';
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
      declarations: [PackagesComponent, PackageListComponent, SearchPeriodComponent, SharePopoverComponent],
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
      })],
      providers: [ DatePipe,
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
      // Fake the service returning an error
      const response = new HttpResponse({
        body: '{ "error": ""}',
        status: 500
      });
      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(response));
      spyOn(mockedToastr, 'error').and.callThrough();

      const packages = ['EntityFramework'];

      activatedRoute.testParamMap = { months: 12, ids: packages };
      fixture.detectChanges();
      tick();
      fixture.detectChanges();

      expect(mockedToastr.error).toHaveBeenCalled();
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

      // Fake the service returning an error
      const response = new HttpResponse({
        body: '{ "error": ""}',
        status: 500
      });

      spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(response));
      spyOn(mockedToastr, 'error').and.callThrough();

      // trigger the change of the period via the service
      const newPeriod = 12;
      packageInteractionService.changeSearchPeriod(newPeriod);
      tick();
      fixture.detectChanges();

      expect(mockedToastr.error).toHaveBeenCalled();
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
  });
});
