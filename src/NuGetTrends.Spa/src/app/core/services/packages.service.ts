import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable } from 'rxjs';

import { IPackageSearchResult, IPackageDownloadHistory } from '../../shared/models/package-models';

@Injectable({
  providedIn: 'root'
})
export class PackagesService {
  baseUrl = `${environment.API_URL}/api`;

  constructor(private httpClient: HttpClient) {
  }

  searchPackage(term: string): Observable<IPackageSearchResult[]> {
    return this.httpClient.get<IPackageSearchResult[]>(`${this.baseUrl}/package/search?q=${term}`);
  }

  getPackageDownloadHistory(term: string, months: number = 12): Observable<IPackageDownloadHistory> {
    return this.httpClient.get<IPackageDownloadHistory>(`${this.baseUrl}/package/history/${term}?months=${months}`);
  }

  searchFramework(term: string): Observable<IPackageSearchResult[]> {
    return this.httpClient.get<IPackageSearchResult[]>(`${this.baseUrl}/framework/search?q=${term}`);
  }

  getFrameworkDownloadHistory(term: string, months: number = 12): Observable<IPackageDownloadHistory> {
    // TODO: hard coding 12 here until dataset is up-to-date
    return this.httpClient.get<IPackageDownloadHistory>(`${this.baseUrl}/framework/history/${term}?months=${months}`);
  }
}
