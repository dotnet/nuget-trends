import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { HttpClientModule, HttpResponse } from '@angular/common/http';
import { ToastrModule, ToastrService } from 'ngx-toastr';
import { Observable, of, throwError } from 'rxjs';

import { PackagesService, PackageInteractionService } from 'src/app/core';
import { SearchInputComponent } from './search-input.component';
import { IPackageSearchResult, PackageSearchResult, IPackageDownloadHistory } from '../../models/package-models';
import { Router } from '@angular/router';
import { MockedRouter } from 'src/app/mocks';

class PackagesServiceMock {

  public static mockedPackageResult: PackageSearchResult[] = [
    new PackageSearchResult('EntityFramework', 500, 'http://go.microsoft.com/fwlink/?LinkID=386613'),
    new PackageSearchResult('System.IdentityModel.Tokens.Jwt', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore.Relational', 100, 'some-broken-link'),
  ];

  public static mockedDownloadHistory: IPackageDownloadHistory = {
    id: 'EntityFramework',
    downloads: [
      { week: new Date('2018-08-12T00:00:00'), count: 48034749 },
      { week: new Date('2018-08-19T00:00:00'), count: 48172816 },
      { week: new Date('2018-08-26T00:00:00'), count: 48474593 },
    ]
  };

  searchPackage$: Observable<PackageSearchResult[]> = of(PackagesServiceMock.mockedPackageResult);
  downloadHistory$: Observable<IPackageDownloadHistory> = of(PackagesServiceMock.mockedDownloadHistory);

  searchPackage(_: string): Observable<IPackageSearchResult[]> {
    return this.searchPackage$;
  }

  getPackageDownloadHistory(): Observable<IPackageDownloadHistory> {
    return this.downloadHistory$;
  }
}

class ToastrMock {
  error(): any {
    return null;
  }
  info(): any {
    return null;
  }
}

describe('SearchInputComponent', () => {
  let component: SearchInputComponent;
  let fixture: ComponentFixture<SearchInputComponent>;
  let mockedPackageService: PackagesServiceMock;
  let mockedToastr: ToastrMock;
  let packageInteractionService: PackageInteractionService;
  let router: MockedRouter;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [SearchInputComponent],
      imports: [
        FormsModule,
        MatAutocompleteModule,
        ReactiveFormsModule,
        RouterTestingModule,
        HttpClientModule,
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
        })
      ],
      providers: [
        { provide: PackagesService, useClass: PackagesServiceMock },
        { provide: ToastrService, useClass: ToastrMock },
        { provide: Router, useClass: MockedRouter },
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SearchInputComponent);
    component = fixture.componentInstance;
    mockedPackageService = (TestBed.inject(PackagesService) as unknown) as PackagesServiceMock;
    mockedToastr = TestBed.inject(ToastrService);
    packageInteractionService = TestBed.inject(PackageInteractionService);
    router = MockedRouter.injectMockRouter();
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load packages when typing in the field', fakeAsync(() => {
    fixture.detectChanges();

    // Act
    dispatchMatAutocompleteEvents('entity', component);

    // Assert - Should show all the options
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(PackagesServiceMock.mockedPackageResult.length);
  }));

  it('should trim the search term', fakeAsync(() => {
    spyOn(mockedPackageService, 'searchPackage').and.callThrough();
    fixture.detectChanges();

    // Act
    dispatchMatAutocompleteEvents(' searchterm ', component);

    // Assert - Should show all the options
    expect(mockedPackageService.searchPackage).toHaveBeenCalledWith('searchterm');
  }));

  it('should not call the API when the term is empty', fakeAsync(() => {
    spyOn(mockedPackageService, 'searchPackage').and.callThrough();
    fixture.detectChanges();

    // Act
    dispatchMatAutocompleteEvents(' ', component);

    // Assert - Should filter out empty search terms
    expect(mockedPackageService.searchPackage).not.toHaveBeenCalled();
  }));

  it('should show error message in case the search fails', fakeAsync(() => {
    spyOn(mockedPackageService, 'searchPackage').and.callThrough();
    spyOn(mockedToastr, 'error').and.callThrough();

    fixture.detectChanges();

    // Fake the service returning an error
    const response = new HttpResponse({
      body: '{ "error": ""}',
      status: 500
    });
    mockedPackageService.searchPackage$ = throwError(response);

    // Act
    const expectedSearchTerm = 'xunit';
    component.queryField.setValue(expectedSearchTerm);
    tick(300);
    fixture.detectChanges();
    tick(300);

    expect(mockedPackageService.searchPackage).toHaveBeenCalledWith(expectedSearchTerm);
    expect(mockedToastr.error).toHaveBeenCalled();
  }));

  it('should show info message in case the search is empty', fakeAsync(() => {
    spyOn(mockedPackageService, 'searchPackage').and.callThrough();
    spyOn(mockedToastr, 'info').and.callThrough();

    fixture.detectChanges();

    mockedPackageService.searchPackage$ = of([]);

    // Act
    const expectedSearchTerm = 'an empty array as result';
    component.queryField.setValue(expectedSearchTerm);
    tick(300);
    fixture.detectChanges();
    tick(300);

    expect(mockedPackageService.searchPackage).toHaveBeenCalledWith(expectedSearchTerm);
    expect(mockedToastr.info).toHaveBeenCalled();
  }));

  it('should get the download history when selecting a package from the results', fakeAsync(() => {
    spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
    spyOn(packageInteractionService, 'addPackage').and.callThrough();
    router.url = '/packages';

    fixture.detectChanges();
    dispatchMatAutocompleteEvents('entity', component);

    const firstOption: any = document.querySelectorAll('.mat-option')[0];
    firstOption.click();
    tick(300);

    expect(mockedPackageService.getPackageDownloadHistory).toHaveBeenCalled();
    expect(packageInteractionService.addPackage).toHaveBeenCalledWith(PackagesServiceMock.mockedDownloadHistory);
  }));

  it('should show error message in case the download history request fails', fakeAsync(() => {
    const response = new HttpResponse({
      body: '{ "error": ""}',
      status: 500
    });
    spyOn(mockedPackageService, 'getPackageDownloadHistory').and.returnValue(throwError(response));
    spyOn(mockedToastr, 'error').and.callThrough();

    fixture.detectChanges();
    dispatchMatAutocompleteEvents('entity', component);

    const firstOption: any = document.querySelectorAll('.mat-option')[0];
    firstOption.click();
    tick(300);

    expect(mockedToastr.error).toHaveBeenCalled();
  }));

  it('should redirect to the chart view when selecting a package from the home page', fakeAsync(() => {
    spyOn(mockedPackageService, 'getPackageDownloadHistory').and.callThrough();
    spyOn(packageInteractionService, 'addPackage').and.callThrough();
    spyOn(router, 'navigate').and.callThrough();

    router.url = '/';

    fixture.detectChanges();
    dispatchMatAutocompleteEvents('entity', component);

    const firstOption: any = document.querySelectorAll('.mat-option')[0];
    firstOption.click();
    tick(300);

    expect(mockedPackageService.getPackageDownloadHistory).toHaveBeenCalled();
    expect(packageInteractionService.addPackage).toHaveBeenCalledWith(PackagesServiceMock.mockedDownloadHistory);
    expect(router.navigate).toHaveBeenCalledWith(['/packages']);
  }));

  it('should clear results when removing text and leaving the field', fakeAsync(() => {
    fixture.detectChanges();

    // Arrange
    dispatchMatAutocompleteEvents('entity', component);
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(PackagesServiceMock.mockedPackageResult.length);

    // Act
    dispatchMatAutocompleteEvents('', component);

    // simulate leaving the input field
    const element: HTMLInputElement = fixture.nativeElement.querySelector('input');
    element.dispatchEvent(new Event('focusout'));

    // Act
    dispatchMatAutocompleteEvents('', component);

    // Assert
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(0);
  }));

  it('should not clear results when removing text and leaving the field', fakeAsync(() => {
    fixture.detectChanges();

    // Arrange
    dispatchMatAutocompleteEvents('entity', component);
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(PackagesServiceMock.mockedPackageResult.length);

    // Act
    dispatchMatAutocompleteEvents('', component);

    // Assert
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(PackagesServiceMock.mockedPackageResult.length);
  }));

  function dispatchMatAutocompleteEvents(text: string, sut: SearchInputComponent) {

    const inputElement: HTMLInputElement = fixture.nativeElement.querySelector('input');
    inputElement.focus();

    // set the text into the formControl.
    // setting the value in the input does not work for some reason.
    sut.queryField.setValue(text);

    inputElement.dispatchEvent(new Event('focusin'));
    inputElement.dispatchEvent(new Event('input'));

    // Wait for the debounceTime
    tick(300);
    fixture.detectChanges();
    tick();
  }
});
