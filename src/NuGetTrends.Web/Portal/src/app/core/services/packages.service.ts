import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Observable, map, catchError, of } from 'rxjs';

import { IPackageSearchResult, IPackageDownloadHistory, ITrendingPackage } from '../../shared/models/package-models';

@Injectable({
  providedIn: 'root'
})
export class PackagesService {
  baseUrl = `${environment.API_URL}/api`;
  private nugetApiUrl = 'https://api.nuget.org/v3-flatcontainer';

  constructor(private httpClient: HttpClient) {
  }

  searchPackage(term: string): Observable<IPackageSearchResult[]> {
    return this.httpClient.get<IPackageSearchResult[]>(`${this.baseUrl}/package/search?q=${term}`);
  }

  getPackageDownloadHistory(term: string, months: number = 12): Observable<IPackageDownloadHistory> {
    return this.httpClient.get<IPackageDownloadHistory>(`${this.baseUrl}/package/history/${term}?months=${months}`);
  }

  /**
   * Checks if a package exists on nuget.org by querying the flat container API.
   * Returns true if the package exists, false otherwise.
   */
  checkPackageExistsOnNuGet(packageId: string): Observable<boolean> {
    // The flat container API returns package versions if the package exists
    // URL format: https://api.nuget.org/v3-flatcontainer/{package-id}/index.json
    return this.httpClient.get(`${this.nugetApiUrl}/${packageId.toLowerCase()}/index.json`).pipe(
      map(() => true),
      catchError(() => of(false))
    );
  }

  searchFramework(term: string): Observable<IPackageSearchResult[]> {
    return this.httpClient.get<IPackageSearchResult[]>(`${this.baseUrl}/framework/search?q=${term}`);
  }

  getFrameworkDownloadHistory(term: string, months: number = 12): Observable<IPackageDownloadHistory> {
    // TODO: hard coding 12 here until dataset is up-to-date
    return this.httpClient.get<IPackageDownloadHistory>(`${this.baseUrl}/framework/history/${term}?months=${months}`);
  }

  /**
   * Get trending packages based on week-over-week growth rate.
   * Returns packages that are relatively new (up to 1 year old) with significant downloads.
   */
  getTrendingPackages(limit: number = 10): Observable<ITrendingPackage[]> {
    return this.httpClient.get<ITrendingPackage[]>(`${this.baseUrl}/package/trending?limit=${limit}`);
  }
}
