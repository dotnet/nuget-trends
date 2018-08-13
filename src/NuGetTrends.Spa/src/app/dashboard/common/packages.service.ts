import { Injectable } from '@angular/core';
import {HttpClient} from '@angular/common/http';
import { environment } from '../../../environments/environment';

import { Observable } from 'rxjs';

import {IPackageSearchResult} from './package-models';

@Injectable()
export class PackagesService {
  baseUrl = `${environment.API_URL}/api/package`;

  constructor(private httpClient: HttpClient) { }

  searchPackage(term: string): Observable<any> {
    return this.httpClient.get<IPackageSearchResult>(`${this.baseUrl}/search?q=${term}`);
  }
}
