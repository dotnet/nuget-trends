import { Injectable } from '@angular/core';
import {HttpClient} from '@angular/common/http';

import { Observable } from 'rxjs';

import {IPackageSearchResult} from './package-models';

@Injectable()
export class PackagesService {
  baseUrl = '/api/package';
  queryUrl = '?search=';

  constructor(private httpClient: HttpClient) { }

  searchPackage(term: string): Observable<any> {
    // return this.httpClient.get<IPackageSearchResult>(`${this.baseUrl}/search?q=${term}`);
    return this.httpClient.get<IPackageSearchResult>('./assets/data.json');
  }
}
