import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';
import { finalize } from 'rxjs/operators';

import { LoadingIndicatorService } from './loading-indicator.service';

@Injectable()
export class LoadingIndicatorInterceptor implements HttpInterceptor {

  activeRequests = 0;

  constructor(private loadingIndicatorService: LoadingIndicatorService) {
  }

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (this.activeRequests === 0) {
      this.loadingIndicatorService.show();
    }

    this.activeRequests++;
    return next.handle(request).pipe(
      finalize(() => {
        this.activeRequests--;
        if (this.activeRequests === 0) {
          this.loadingIndicatorService.hide();
        }
      })
    );
  }
}
