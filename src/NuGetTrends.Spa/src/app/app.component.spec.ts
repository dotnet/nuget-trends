import { HttpClientModule } from '@angular/common/http';
import { TestBed, async } from '@angular/core/testing';
import { BrowserModule } from '@angular/platform-browser';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';

import { CoreModule } from './core/core.module';
import { AppRoutingModule } from './app-routes.module';
import { SharedModule } from './shared/shared.module';
import { PackagesModule } from './packages/packages.module';
import { HomeModule } from './home/home.module';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [
        AppComponent
      ],
      imports: [AppRoutingModule, AppRoutingModule,
        BrowserModule,
        BrowserAnimationsModule,
        HttpClientModule,
        CoreModule,
        SharedModule,
        PackagesModule,
        HomeModule]
    }).compileComponents();
  }));
  it('should create the app', async(() => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.debugElement.componentInstance;
    expect(app).toBeTruthy();
  }));
});
