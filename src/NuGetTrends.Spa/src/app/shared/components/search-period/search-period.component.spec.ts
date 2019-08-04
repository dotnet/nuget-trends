import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterModule, Routes } from '@angular/router';

import { SearchPeriodComponent } from './search-period.component';

describe('SearchPeriodComponent', () => {
  let component: SearchPeriodComponent;
  let fixture: ComponentFixture<SearchPeriodComponent>;
  const routes: Routes = [
  ];

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [SearchPeriodComponent],
      imports: [FormsModule, ReactiveFormsModule, RouterModule.forRoot(routes)]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SearchPeriodComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
