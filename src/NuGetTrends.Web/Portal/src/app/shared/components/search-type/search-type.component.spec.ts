import { ComponentFixture, TestBed, waitForAsync } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';

import { PackageInteractionService } from 'src/app/core';
import { SearchType } from '../../models/package-models';
import { SearchTypeComponent } from './search-type.component';

describe('SearchTypeComponent', () => {
  let component: SearchTypeComponent;
  let fixture: ComponentFixture<SearchTypeComponent>;
  let packageInteractionService: PackageInteractionService;
  let checkboxControl: HTMLElement;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [SearchTypeComponent],
      imports: [FormsModule]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SearchTypeComponent);
    component = fixture.componentInstance;
    packageInteractionService = TestBed.inject(PackageInteractionService);
    checkboxControl = fixture.nativeElement.querySelector('input');
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should use NuGet packages by default', () => {
    fixture.detectChanges();
    expect(component.isNuGetPackage).toBeTruthy();
  });

  it('should send the correct type when toggling the control', () => {
    fixture.detectChanges();

    checkboxControl.click();
    expect(component.isNuGetPackage).toBeTruthy();
    expect(packageInteractionService.searchType).toBe(SearchType.NuGetPackage);

    checkboxControl.click();
    expect(component.isNuGetPackage).toBeFalsy();
    expect(packageInteractionService.searchType).toBe(SearchType.Framework);
  });

});
