import { Component, OnInit, Input, ElementRef, HostListener } from '@angular/core';

@Component({
  selector: 'app-share-popover',
  templateUrl: './share-popover.component.html',
  styleUrls: ['./share-popover.component.scss']
})
export class SharePopoverComponent implements OnInit {
  @Input() buttonText: string;
  isActive = false;

  constructor(private eRef: ElementRef) { }

  ngOnInit() {
  }

  @HostListener('document:click', ['$event'])
  clickout(event: any) {
    if (this.eRef.nativeElement.contains(event.target)) {
      this.isActive = true;
    } else {
      this.isActive = false;
    }
  }

  toggle() {
    this.isActive = !this.isActive;
  }
}
