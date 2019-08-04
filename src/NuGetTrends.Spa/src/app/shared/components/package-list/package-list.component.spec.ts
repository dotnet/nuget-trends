import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastrModule } from 'ngx-toastr';

import { PackageListComponent } from './package-list.component';

describe('PackageListComponent', () => {
  let component: PackageListComponent;
  let fixture: ComponentFixture<PackageListComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ PackageListComponent ],
      imports: [
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
        })
      ],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(PackageListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
