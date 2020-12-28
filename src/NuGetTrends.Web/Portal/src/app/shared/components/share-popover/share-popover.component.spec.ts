import { HttpClientModule } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ComponentFixture, TestBed, fakeAsync, tick, waitForAsync } from '@angular/core/testing';
import { ToastrModule, ToastrService } from 'ngx-toastr';

import { ToastrMock } from 'src/app/mocks';
import { SocialShareService } from 'src/app/core/services/social-share.service';
import { SharePopoverComponent } from './share-popover.component';

describe('SharePopoverComponent', () => {
  let component: SharePopoverComponent;
  let fixture: ComponentFixture<SharePopoverComponent>;
  let mockedToastr: ToastrMock;
  let shareService: SocialShareService;

  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      declarations: [SharePopoverComponent],
      imports: [
        CommonModule,
        FormsModule,
        HttpClientModule,
        ToastrModule.forRoot({
          positionClass: 'toast-bottom-right',
          preventDuplicates: true,
        })],
      providers: [
        { provide: ToastrService, useClass: ToastrMock },
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SharePopoverComponent);
    component = fixture.componentInstance;
    mockedToastr = TestBed.inject(ToastrService);
    shareService = TestBed.inject(SocialShareService);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
    console.log(mockedToastr);
  });

  it('should add short link to input', fakeAsync(() => {
    const expectedShortLink = 'https://shortr/Abc1234fs';

    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve(expectedShortLink));

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();
    tick();
    fixture.detectChanges();

    expect(shareService.getShortLink).toHaveBeenCalled();
    expect(component.shareShortLink).toEqual(expectedShortLink);
  }));

  it('should show error message if getting the short link fails', fakeAsync(() => {
    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.reject('error'));

    spyOn(mockedToastr, 'error').and.callThrough();

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();
    tick();
    fixture.detectChanges();

    expect(shareService.getShortLink).toHaveBeenCalled();
    expect(mockedToastr.error).toHaveBeenCalledWith(jasmine.any(String));
  }));

  it('should not call service if popover is already open', fakeAsync(() => {
    const expectedShortLink = 'https://shortr/Abc1234fs';

    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve(expectedShortLink));

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();
    shareButton.click();

    expect(shareService.getShortLink).toHaveBeenCalledTimes(1);
  }));

  it('should close the popover when clicking outside', fakeAsync(() => {
    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve('some-link'));

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();

    expect(component.isActive).toBeTruthy();

    document.body.click();
    fixture.detectChanges();

    expect(component.isActive).toBeFalsy();
  }));

  it('should close if click happens on button and popover is already open', fakeAsync(() => {
    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve('some-link'));

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();
    expect(component.isActive).toBeTruthy();

    shareButton.click();
    expect(component.isActive).toBeFalsy();
  }));

  it('should not close if click happens inside component', fakeAsync(() => {
    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve('some-link'));

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();

    expect(component.isActive).toBeTruthy();

    const divContainer = fixture.nativeElement.querySelector('.container');
    divContainer.click();
    fixture.detectChanges();

    expect(component.isActive).toBeTruthy();
  }));

  it('should copy short link to the clipboard', fakeAsync(() => {
    const expectedShortLink = 'https://shortr/Abc1234fs';

    spyOn(shareService, 'getShortLink')
      .and.returnValue(Promise.resolve(expectedShortLink));

    spyOn(document, 'execCommand').and.callThrough();

    const shareButton = fixture.nativeElement.querySelector('#shareButton');
    shareButton.click();
    tick();
    fixture.detectChanges();

    const copyButton = fixture.nativeElement.querySelector('.fa-clipboard');
    copyButton.click();

    expect(document.execCommand).toHaveBeenCalledWith('copy');

  }));
});
