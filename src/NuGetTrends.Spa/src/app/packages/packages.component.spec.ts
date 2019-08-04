import { NO_ERRORS_SCHEMA } from '@angular/core';
import { HttpClientModule } from '@angular/common/http';
import { CommonModule, DatePipe } from '@angular/common';
import { RouterModule, Routes } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastrModule } from 'ngx-toastr';

import { PackagesComponent } from './packages.component';

describe('PackagesComponent', () => {
  let component: PackagesComponent;
  let fixture: ComponentFixture<PackagesComponent>;

  const routes: Routes = [
  ];

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [PackagesComponent],
      imports: [
        CommonModule,
        NoopAnimationsModule,
        HttpClientModule,
        RouterModule.forRoot(routes),
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
      })],
      providers: [DatePipe],
      schemas: [NO_ERRORS_SCHEMA]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(PackagesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
