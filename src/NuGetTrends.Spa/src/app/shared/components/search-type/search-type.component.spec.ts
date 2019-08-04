import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { SearchTypeComponent } from './search-type.component';
import { FormsModule } from '@angular/forms';

describe('SearchTypeComponent', () => {
  let component: SearchTypeComponent;
  let fixture: ComponentFixture<SearchTypeComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [SearchTypeComponent],
      imports: [FormsModule]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SearchTypeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
