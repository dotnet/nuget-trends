import { Injectable } from '@angular/core';
import {HttpClient} from '@angular/common/http';
import { environment } from '../../../environments/environment';

import { Observable } from 'rxjs';

import {IPackageSearchResult, IPackageDownloadHistory} from './package-models';

@Injectable()
export class PackagesService {
  baseUrl = `${environment.API_URL}/api/package`;

  constructor(private httpClient: HttpClient) { }

  searchPackage(term: string): Observable<IPackageSearchResult[]> {
    return this.httpClient.get<IPackageSearchResult[]>(`${this.baseUrl}/search?q=${term}`);
  }

  getPackageDownloadHistory(term: string): Observable<IPackageDownloadHistory> {
    // TODO: hard coding 12 here until dataset is up-to-date
    return this.httpClient.get<IPackageDownloadHistory>(`${this.baseUrl}/history/${term}?months=12`);
  }
}
