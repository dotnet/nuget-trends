import { TestBed, inject, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PackagesService } from './packages.service';
import { IPackageSearchResult, IPackageDownloadHistory, ITrendingPackage } from 'src/app/shared/models/package-models';

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

  it('should call the getTrendingPackages endpoint with default limit', fakeAsync(() => {
    const data: ITrendingPackage[] = [
      {
        packageId: 'Newtonsoft.Json',
        downloadCount: 150000,
        growthRate: 0.25,
        iconUrl: 'https://example.com/icon.png',
        gitHubUrl: 'https://github.com/JamesNK/Newtonsoft.Json'
      },
      {
        packageId: 'Sentry',
        downloadCount: 50000,
        growthRate: 0.75,
        iconUrl: 'https://example.com/icon2.png',
        gitHubUrl: null
      }
    ];

    service.getTrendingPackages().subscribe((packages: ITrendingPackage[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/trending?limit=10`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

  it('should call the getTrendingPackages endpoint with custom limit', fakeAsync(() => {
    const data: ITrendingPackage[] = [];

    service.getTrendingPackages(5).subscribe((packages: ITrendingPackage[]) => {
      expect(packages).toEqual(data);
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/trending?limit=5`);
    req.flush(data);
    tick();

    expect(req.request.method).toEqual('GET');
  }));

  it('should timeout after 3 seconds if getTrendingPackages request is not resolved', fakeAsync(() => {
    let errorOccurred = false;
    let errorMessage = '';

    service.getTrendingPackages().subscribe({
      next: () => fail('Should not succeed'),
      error: (error) => {
        errorOccurred = true;
        errorMessage = error.name;
      }
    });

    const req = httpMock.expectOne(`${service.baseUrl}/package/trending?limit=10`);
    
    // Advance time past the 3-second timeout
    tick(3001);

    // Verify that a timeout error occurred
    expect(errorOccurred).toBe(true);
    expect(errorMessage).toBe('TimeoutError');

    // Verify the request was cancelled (Angular's HttpTestingController should show the request as cancelled)
    expect(req.cancelled).toBe(true);
  }));

 });
