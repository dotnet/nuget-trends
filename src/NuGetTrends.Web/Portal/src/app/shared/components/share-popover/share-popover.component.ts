import { Component, Input, ElementRef, HostListener, EventEmitter, Output, ErrorHandler } from '@angular/core';
import { ToastrService } from 'ngx-toastr';

import { SocialShareService } from 'src/app/core/services/social-share.service';

@Component({
  selector: 'app-share-popover',
  templateUrl: './share-popover.component.html',
  styleUrls: ['./share-popover.component.scss']
})
export class SharePopoverComponent {
  @Input() buttonText = '';
  @Output() shared = new EventEmitter();

  shareShortLink = '';
  isActive = false;

  constructor(
    private eRef: ElementRef,
    private socialShareService: SocialShareService,
    private toastr: ToastrService,
    private errorHandler: ErrorHandler) { }

  @HostListener('document:click', ['$event'])
  clickout(event: any) {
    const shareBtn = document.querySelector('#shareButton')!;
    if (this.eRef.nativeElement.contains(event.target)) {

      // Clicking on the button when the popover is over closes it
      if (shareBtn.contains(event.target) && this.isActive) {
        this.isActive = false;
        return;
      }
      this.isActive = true;
    } else {
      this.isActive = false;
    }
  }

  async toggle(): Promise<void> {
    if (this.isActive) {
      return;
    }
    try {
      this.shareShortLink = await this.socialShareService.getShortLink(window.location.href);
    } catch (error) {
      this.errorHandler.handleError(error);
      this.toastr.error('Couldn\'t. share this awesome chart. Maybe try again?');
    }
  }

  copyToClipboard(): void {
    const body = document.getElementsByTagName('body')[0];
    const tempInput = document.createElement('INPUT') as HTMLInputElement;
    body.appendChild(tempInput);
    tempInput.setAttribute('value', this.shareShortLink);
    tempInput.select();
    document.execCommand('copy');
    body.removeChild(tempInput);
  }
}
