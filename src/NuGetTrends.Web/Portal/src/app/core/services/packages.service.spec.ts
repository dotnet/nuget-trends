import { TestBed, inject, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PackagesService } from './packages.service';
import { IPackageSearchResult, IPackageDownloadHistory } from 'src/app/shared/models/package-models';

describe('PackagesService', () => {

  let service: PackagesService;
  let httpMock: HttpTestingController;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [PackagesService],
    });
    service = TestBed.inject(PackagesService);
    httpMock = TestBed.inject(HttpTestingController);
  }));

  it('should be initialized', inject([PackagesService], (packageService: PackagesService) => {
    expect(packageService).toBeTruthy();
  }));

  it('should call the search package endpoint', fakeAsync(() => {
    const searchTerm = 'entity';
    const data: IPackageSearchResult[] = [
      { packageId: 'EntityFramework', downloadCount: 0, iconUrl: ''},
      { packageId: 'System.IdentityModel.Tokens.Jwt', downloadCount: 0, iconUrl: ''},
      { packageId: 'Microsoft.IdentityModel.Logging', downloadCount: 0, iconUrl: ''},
    ];

    service.searchPackage(searchTerm).subscribe((packages: IPackageSearchResult[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/search?q=${searchTerm}`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

  it('should call the getPackageDownloadHistory endpoint', fakeAsync(() => {
    const packageId = 'EntityFramework';
    const data: IPackageDownloadHistory = {
      id: 'EntityFramework',
      downloads: [
        { week: new Date('2018-10-28T00:00:00'), count: 51756066 },
        { week: new Date('2018-11-04T00:00:00'), count: 52022309 },
        { week: new Date('2018-11-11T00:00:00'), count: 52394207 },
      ]
    };

    service.getPackageDownloadHistory(packageId).subscribe((history: IPackageDownloadHistory) => {
      expect(history).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/history/${packageId}?months=${12}`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

  it('should call the search package endpoint', fakeAsync(() => {
    const searchTerm = 'entity';
    const data: IPackageSearchResult[] = [
      { packageId: 'EntityFramework', downloadCount: 0, iconUrl: ''},
      { packageId: 'System.IdentityModel.Tokens.Jwt', downloadCount: 0, iconUrl: ''},
      { packageId: 'Microsoft.IdentityModel.Logging', downloadCount: 0, iconUrl: ''},
    ];

    // TODO: This endpoint is not being used.. maybe remove it all together?
    service.searchFramework(searchTerm).subscribe((packages: IPackageSearchResult[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/framework/search?q=${searchTerm}`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

  it('should call the getFrameworkDownloadHistory endpoint', fakeAsync(() => {
    const packageId = 'EntityFramework';
    const data: IPackageDownloadHistory = {
      id: 'EntityFramework',
      downloads: [
        { week: new Date('2018-10-28T00:00:00'), count: 51756066 },
        { week: new Date('2018-11-04T00:00:00'), count: 52022309 },
        { week: new Date('2018-11-11T00:00:00'), count: 52394207 },
      ]
    };

    service.getFrameworkDownloadHistory(packageId).subscribe((history: IPackageDownloadHistory) => {
      expect(history).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/framework/history/${packageId}?months=${12}`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

 });
