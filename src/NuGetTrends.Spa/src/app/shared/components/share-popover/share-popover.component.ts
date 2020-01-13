import { Component, OnInit, Input, ElementRef, HostListener, EventEmitter, Output, OnDestroy } from '@angular/core';

import { SocialShareService } from 'src/app/core/services/social-share.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-share-popover',
  templateUrl: './share-popover.component.html',
  styleUrls: ['./share-popover.component.scss']
})
export class SharePopoverComponent implements OnInit, OnDestroy {
  @Input() buttonText: string;
  @Output() shared = new EventEmitter();

  shareText = 'I\'\m sharing..';
  isActive = false;
  private shareSubscription: Subscription;


  constructor(private eRef: ElementRef, private socialShareService: SocialShareService) { }

  ngOnInit() {
    this.shareSubscription = this.socialShareService.chartShared$.subscribe((message: string) => {
      this.shareText = message;
    });
  }

  ngOnDestroy(): void {
    if (this.shareSubscription) {
      this.shareSubscription.unsubscribe();
    }
  }

  @HostListener('document:click', ['$event'])
  clickout(event: any) {
    const shareBtn = document.querySelector('#shareButton');
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

  toggle(): void {
    if (!this.isActive) {
      this.shared.emit();
    }
  }

  copyToClipboard(): void {
    const body = document.getElementsByTagName('body')[0];
    const tempInput = document.createElement('INPUT') as HTMLInputElement;
    body.appendChild(tempInput);
    tempInput.setAttribute('value', this.shareText);
    tempInput.select();
    document.execCommand('copy');
    body.removeChild(tempInput);
  }
}
