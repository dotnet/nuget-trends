import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClientModule } from '@angular/common/http';
import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { SearchPeriodComponent } from './search-period.component';
import { PackageInteractionService } from 'src/app/core';
import { MockedActivatedRoute, MockedRouter } from 'src/app/mocks';
import { InitialSearchPeriod } from '../../models/package-models';

describe('SearchPeriodComponent', () => {
  let component: SearchPeriodComponent;
  let fixture: ComponentFixture<SearchPeriodComponent>;
  let packageInteractionService: PackageInteractionService;
  let router: Router;
  let activatedRoute: MockedActivatedRoute;

  const queryParamName = 'months';
  let routerSpy: any;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [SearchPeriodComponent],
      imports: [
        FormsModule,
        ReactiveFormsModule,
        RouterTestingModule.withRoutes([]),
        HttpClientModule
      ],
      providers: [
        { provide: Router, useClass: MockedRouter },
        { provide: ActivatedRoute, useClass: MockedActivatedRoute },
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    router = TestBed.inject(Router);
    routerSpy = spyOn(router, 'navigate').and.callThrough();
    fixture = TestBed.createComponent(SearchPeriodComponent);
    component = fixture.componentInstance;
    packageInteractionService = TestBed.inject(PackageInteractionService);
    activatedRoute = MockedActivatedRoute.injectMockActivatedRoute();
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should use the default query param', () => {
    fixture.detectChanges();

    expect(packageInteractionService.searchPeriod).toBe(InitialSearchPeriod.value);

    // navigate should have been called with the correct query param
    const navigateActualParams = routerSpy.calls.mostRecent().args[1];
    expect(navigateActualParams.queryParams[queryParamName]).toBe(InitialSearchPeriod.value);
  });

  it('should use the existing query param if present', () => {
    const expectedPeriod = 3;

    activatedRoute.testParamMap = { months: 3 };

    // Re-create the component here so we have the overriten ActivatedRoute
    fixture = TestBed.createComponent(SearchPeriodComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(packageInteractionService.searchPeriod).toBe(expectedPeriod);

    // navigate should have been called with the correct query param
    const navigateActualParams = routerSpy.calls.mostRecent().args[1];
    expect(navigateActualParams.queryParams[queryParamName]).toBe(expectedPeriod);
  });

  it('should fire events and change url when period is changed', () => {
    spyOn(packageInteractionService, 'changeSearchPeriod').and.callThrough();
    fixture.detectChanges();

    // should start with initial value
    expect(packageInteractionService.searchPeriod).toBe(InitialSearchPeriod.value);

    const expectedChangedPeriod = 3;

    component.periodControl.setValue(expectedChangedPeriod);
    const selectControl: any = fixture.nativeElement.querySelector('select');
    selectControl.dispatchEvent(new Event('change'));

    fixture.detectChanges();

    expect(packageInteractionService.changeSearchPeriod).toHaveBeenCalledWith(expectedChangedPeriod);
    expect(packageInteractionService.searchPeriod).toBe(expectedChangedPeriod);

    const navigateActualParams = routerSpy.calls.mostRecent().args[1];
    expect(navigateActualParams.queryParams[queryParamName]).toBe(expectedChangedPeriod);
  });

  it('should not do anything if period changed is the same', fakeAsync(() => {
    spyOn(packageInteractionService, 'changeSearchPeriod').and.callThrough();
    fixture.detectChanges();

    let timesPeriodHasChanged = 0;
    packageInteractionService.searchPeriodChanged$.subscribe(
      (_) => timesPeriodHasChanged++);

    // should start with initial value as defined in InitialSearchPeriod
    expect(packageInteractionService.searchPeriod).toBe(InitialSearchPeriod.value);

    const expectedChangedPeriod = InitialSearchPeriod.value;

    component.periodControl.setValue(expectedChangedPeriod);
    const selectControl: any = fixture.nativeElement.querySelector('select');
    selectControl.dispatchEvent(new Event('change'));

    tick();
    fixture.detectChanges();

    expect(timesPeriodHasChanged).toBe(0);
    expect(packageInteractionService.changeSearchPeriod).toHaveBeenCalledWith(expectedChangedPeriod);
    expect(packageInteractionService.searchPeriod).toBe(expectedChangedPeriod);
  }));

});
