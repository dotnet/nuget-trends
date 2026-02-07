import { Location } from '@angular/common';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { Component } from '@angular/core';

// Stub components for testing routes
@Component({ selector: 'app-mock-home', template: '<div>Home</div>', standalone: false })
class MockHomeComponent {}

@Component({ selector: 'app-mock-packages', template: '<div>Packages</div>', standalone: false })
class MockPackagesComponent {}

describe('AppRoutingModule', () => {
  let router: Router;
  let location: Location;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [MockHomeComponent, MockPackagesComponent],
      imports: [
        RouterTestingModule.withRoutes([
          { path: '', component: MockHomeComponent, pathMatch: 'full' },
          { path: 'packages/:packageId', component: MockPackagesComponent },
          { path: 'packages', component: MockPackagesComponent },
          { path: '**', redirectTo: '', pathMatch: 'full' }
        ])
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
    location = TestBed.inject(Location);
  });

  it('should navigate to home for empty path', fakeAsync(() => {
    router.navigate(['']);
    tick();
    expect(location.path()).toBe('/');
  }));

  it('should navigate to packages list', fakeAsync(() => {
    router.navigate(['packages']);
    tick();
    expect(location.path()).toBe('/packages');
  }));

  it('should navigate to packages with packageId', fakeAsync(() => {
    router.navigate(['packages', 'Newtonsoft.Json']);
    tick();
    expect(location.path()).toBe('/packages/Newtonsoft.Json');
  }));

  describe('catch-all route', () => {
    it('should redirect /robots.txt to home', fakeAsync(() => {
      router.navigate(['robots.txt']);
      tick();
      expect(location.path()).toBe('/');
    }));

    it('should redirect /privacy to home', fakeAsync(() => {
      router.navigate(['privacy']);
      tick();
      expect(location.path()).toBe('/');
    }));

    it('should redirect /api to home', fakeAsync(() => {
      router.navigate(['api']);
      tick();
      expect(location.path()).toBe('/');
    }));

    it('should redirect old /package/:id format to home', fakeAsync(() => {
      router.navigate(['package', 'SomePackage']);
      tick();
      expect(location.path()).toBe('/');
    }));

    it('should redirect arbitrary unknown paths to home', fakeAsync(() => {
      router.navigate(['some', 'unknown', 'path']);
      tick();
      expect(location.path()).toBe('/');
    }));

    it('should redirect single unknown segment to home', fakeAsync(() => {
      router.navigate(['nonexistent']);
      tick();
      expect(location.path()).toBe('/');
    }));
  });
});
