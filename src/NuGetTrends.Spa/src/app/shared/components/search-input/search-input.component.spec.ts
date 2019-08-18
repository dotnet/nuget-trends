import { async, ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material';
import { HttpClientModule, HttpResponse } from '@angular/common/http';
import { ToastrModule, ToastrService } from 'ngx-toastr';
import { Observable, of, throwError } from 'rxjs';

import { PackagesService } from 'src/app/core';
import { SearchInputComponent } from './search-input.component';
import { IPackageSearchResult, PackageSearchResult } from '../../models/package-models';

class PackagesServiceMock {

  public static mockedPackageResult: PackageSearchResult[] = [
    new PackageSearchResult('EntityFramework', 500, 'http://go.microsoft.com/fwlink/?LinkID=386613'),
    new PackageSearchResult('System.IdentityModel.Tokens.Jwt', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore.Relational', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.IdentityModel.Logging', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.IdentityModel.Tokens', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.IdentityModel.Logging', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.IdentityModel.Clients.ActiveDirectory', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore.SqlServer', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore.Design', 100, 'some-broken-link'),
    new PackageSearchResult('Microsoft.EntityFrameworkCore.Abstractions', 100, 'some-broken-link'),
  ];
  searchPackage$: Observable<PackageSearchResult[]> = of(PackagesServiceMock.mockedPackageResult);

  searchPackage(_: string): Observable<IPackageSearchResult[]> {
    return this.searchPackage$;
  }
}

class ToastrMock {
  error(): any {
    return null;
  }
}

fdescribe('SearchInputComponent', () => {
  let component: SearchInputComponent;
  let fixture: ComponentFixture<SearchInputComponent>;
  let mockedPackageService: PackagesServiceMock;
  let mockedToastr: ToastrMock;


  beforeEach(async(() => {
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
        { provide: ToastrService, useClass: ToastrMock }
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SearchInputComponent);
    component = fixture.componentInstance;
    mockedPackageService = TestBed.get(PackagesService);
    mockedToastr = TestBed.get(ToastrService);
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load packages when typing in the field', fakeAsync(() => {
    fixture.detectChanges();

    // Act
    dispatchMatAutocompleteEvents('entity', component);

    // Assert - Should show all the options (11)
    expect(document.querySelectorAll('.mat-option').length)
      .toBe(PackagesServiceMock.mockedPackageResult.length);
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
    tick(300);
  }
});
