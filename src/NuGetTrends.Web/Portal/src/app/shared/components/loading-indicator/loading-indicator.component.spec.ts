import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';

import { LoadingIndicatorComponent } from './loading-indicator.component';
import { LoadingIndicatorService } from './loading-indicator.service';

describe('LoadingIndicatorComponent', () => {
  let component: LoadingIndicatorComponent;
  let fixture: ComponentFixture<LoadingIndicatorComponent>;
  let service: LoadingIndicatorService;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [LoadingIndicatorComponent],
      providers: [LoadingIndicatorService]
    }).compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(LoadingIndicatorComponent);
    component = fixture.componentInstance;
    service = TestBed.inject(LoadingIndicatorService);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('Should handle loading events properly', fakeAsync(() => {
    service.show();
    tick(200);
    fixture.detectChanges();
    expect(fixture.elementRef.nativeElement.style.display).toBe('block');


    service.hide();
    tick(200);
    fixture.detectChanges();
    expect(fixture.elementRef.nativeElement.style.display).toBe('none');
  }));
});
