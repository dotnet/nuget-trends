import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, HttpClientTestingModule } from '@angular/common/http/testing';
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { forkJoin } from 'rxjs';

import { PackagesService } from 'src/app/core';
import { IPackageSearchResult } from '../../models/package-models';
import { LoadingIndicatorService } from './loading-indicator.service';
import { LoadingIndicatorInterceptor } from './loading-indicator.interceptor';

describe(`LoadingIndicatorInterceptor`, () => {
  let service: PackagesService;
  let loadingIndicatorService: LoadingIndicatorService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        PackagesService, LoadingIndicatorService,
        {
          provide: HTTP_INTERCEPTORS,
          useClass: LoadingIndicatorInterceptor,
          multi: true,
        },
      ],
    });

    service = TestBed.inject(PackagesService);
    loadingIndicatorService = TestBed.inject(LoadingIndicatorService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('should show loading indicator when making an HTTP request', fakeAsync(() => {
    spyOn(loadingIndicatorService, 'show').and.callThrough();

    const searchTerm = 'entity';
    const data: IPackageSearchResult[] = [
      { packageId: 'EntityFramework', downloadCount: 0, iconUrl: ''}
    ];

    service.searchPackage(searchTerm).subscribe((packages: IPackageSearchResult[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/search?q=${searchTerm}`);
    req.flush(data);
    tick();

    expect(loadingIndicatorService.show).toHaveBeenCalledTimes(1);
  }));


  it('should only show loading indicator once if multiple requests are on going', fakeAsync(() => {
    spyOn(loadingIndicatorService, 'show').and.callThrough();

    const searchTerm = 'entity';
    // fire multiple requests
    forkJoin(
      {
        req1: service.searchPackage(searchTerm),
        req2: service.searchPackage(searchTerm),
      }
    ).subscribe();

    const req = httpMock.match(`${service.baseUrl}/package/search?q=${searchTerm}`);
    tick();

    expect(loadingIndicatorService.show).toHaveBeenCalledTimes(1);
    expect(req.length).toBe(2);
  }));

  it('should hide loading indicator when request is finished', fakeAsync(() => {
    spyOn(loadingIndicatorService, 'show').and.callThrough();
    spyOn(loadingIndicatorService, 'hide').and.callThrough();

    const searchTerm = 'entity';
    const data: IPackageSearchResult[] = [
      { packageId: 'EntityFramework', downloadCount: 0, iconUrl: ''}
    ];

    service.searchPackage(searchTerm).subscribe((packages: IPackageSearchResult[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/search?q=${searchTerm}`);
    req.flush(data);
    tick();

    expect(loadingIndicatorService.show).toHaveBeenCalledTimes(1);
    expect(loadingIndicatorService.hide).toHaveBeenCalledTimes(1);
  }));
});
