import { Component, OnDestroy, AfterViewInit, ElementRef, ChangeDetectorRef } from '@angular/core';
import { Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

import { LoadingIndicatorService } from './loading-indicator.service';

@Component({
  selector: 'app-loading',
  templateUrl: './loading-indicator.component.html',
  styleUrls: ['./loading-indicator.component.scss']
})
export class LoadingIndicatorComponent implements AfterViewInit, OnDestroy {

  loadingSubscription?: Subscription;

  constructor(
    private _elmRef: ElementRef,
    private _changeDetectorRef: ChangeDetectorRef,
    private loadingIndicatorService: LoadingIndicatorService) { }

  ngAfterViewInit(): void {
    this._elmRef.nativeElement.style.display = 'none';
    this.loadingSubscription = this.loadingIndicatorService.loadingSubject$
      .pipe(debounceTime(200))
      .subscribe((isLoading: boolean) => {
        this._elmRef.nativeElement.style.display = isLoading ? 'block' : 'none';
        this._changeDetectorRef.detectChanges();
      });
  }

  ngOnDestroy() {
    if (this.loadingSubscription) {
      this.loadingSubscription.unsubscribe();
    }
  }
}
