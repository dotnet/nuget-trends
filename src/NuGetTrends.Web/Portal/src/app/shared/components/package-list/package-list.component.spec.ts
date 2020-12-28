import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { ToastrModule, ToastrService } from 'ngx-toastr';

import { ToastrMock } from 'src/app/mocks';

import { PackageListComponent } from './package-list.component';
import { PackageInteractionService } from 'src/app/core';
import { IPackageDownloadHistory } from '../../models/package-models';

describe('PackageListComponent', () => {
  let component: PackageListComponent;
  let fixture: ComponentFixture<PackageListComponent>;

  let mockedToastr: ToastrMock;
  let packageInteractionService: PackageInteractionService;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [ PackageListComponent ],
      imports: [
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
        })
      ],
      providers: [
        { provide: ToastrService, useClass: ToastrMock },
      ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(PackageListComponent);
    component = fixture.componentInstance;
    mockedToastr = TestBed.inject(ToastrService);
    packageInteractionService = TestBed.inject(PackageInteractionService);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize with empty package list', () => {
    fixture.detectChanges();
    expect(component.packageList.length).toBe(0);
  });

  it('should react to the packageAdded event by adding the package to the list', () => {
    fixture.detectChanges();

    const packageHistory: IPackageDownloadHistory = {
      id: 'dapper',
      downloads: []
    };

    // Act
    packageInteractionService.addPackage(packageHistory);
    fixture.detectChanges();

    // Assert
    const tags = fixture.nativeElement.querySelectorAll('.tags span');

    // should have rendered 1 package "tag"
    expect(component.packageList.some(p => p.id === packageHistory.id)).toBeTruthy();
    expect(tags.length).toBe(1);

    // the package history object should have a color set
    expect(packageHistory.color).not.toBeNull();
  });

  it('should not do anything if package already exists in the list', () => {
    spyOn(packageInteractionService, 'plotPackage').and.callThrough();

    fixture.detectChanges();

    // Normally add the package
    const packageHistory: IPackageDownloadHistory = {
      id: 'dapper',
      downloads: []
    };
    packageInteractionService.addPackage(packageHistory);
    fixture.detectChanges();

    const packageAssignedColor = packageHistory.color;
    expect(component.packageList.length).toBe(1);
    expect(packageAssignedColor).not.toBeNull();

    // act - try adding the same package again
    packageInteractionService.addPackage(packageHistory);
    fixture.detectChanges();

    expect(component.packageList.length).toBe(1);
    expect(packageHistory.color).toBe(packageAssignedColor);
    expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(1);
  });

  it('should show toaster if package limit is exceeded', () => {
    spyOn(packageInteractionService, 'plotPackage').and.callThrough();
    spyOn(mockedToastr, 'warning').and.callThrough();

    fixture.detectChanges();

    // Normally add the package
    const packages: Array<IPackageDownloadHistory> = [
       { id: 'Dapper', downloads: [] },
       { id: 'Sentry', downloads: [] },
       { id: 'EntityFramework', downloads: [] },
       { id: 'xUnit', downloads: [] },
       { id: 'Moq', downloads: [] },
       { id: 'Serilog', downloads: [] },
    ];

    packages.forEach(pkg => {
      packageInteractionService.addPackage(pkg);
      fixture.detectChanges();
    });

    // Add the 7th package
    packageInteractionService.addPackage({
      id: 'newtonsoft.json',
      downloads: []
    });
    fixture.detectChanges();

    expect(component.packageList.length).toBe(6);
    expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(6);
    expect(mockedToastr.warning).toHaveBeenCalled();
  });

  it('should remove package from list', () => {
    spyOn(packageInteractionService, 'removePackage').and.callThrough();

    fixture.detectChanges();

    // Normally add the package
    const packages: Array<IPackageDownloadHistory> = [
       { id: 'Dapper', downloads: [] },
       { id: 'Sentry', downloads: [] }
    ];

    packages.forEach(pkg => {
      packageInteractionService.addPackage(pkg);
      fixture.detectChanges();
    });

    expect(component.packageList.length).toBe(2);

    // Assert
    const removePackageButtons: Array<HTMLElement>
      = fixture.nativeElement.querySelectorAll('.tags span button');

    removePackageButtons[1].click();
    fixture.detectChanges();

    expect(component.packageList.length).toBe(1);
    expect(packageInteractionService.removePackage).toHaveBeenCalled();
  });

  it('should react to the packageUpdated event by firing the plotPackage event', () => {
    spyOn(packageInteractionService, 'plotPackage').and.callThrough();

    fixture.detectChanges();

    // Normally add the package
    const packageHistory: IPackageDownloadHistory = {
      id: 'dapper',
      downloads: []
    };
    packageInteractionService.addPackage(packageHistory);
    fixture.detectChanges();

    const packageAssignedColor = packageHistory.color;
    expect(component.packageList.length).toBe(1);
    expect(packageAssignedColor).not.toBeNull();

    // act - simulate changing the period thus updating the package
    packageInteractionService.updatePackage(packageHistory);
    fixture.detectChanges();

    expect(component.packageList.length).toBe(1);
    expect(packageHistory.color).toBe(packageAssignedColor);
    expect(packageInteractionService.plotPackage).toHaveBeenCalledTimes(2);
  });
});
