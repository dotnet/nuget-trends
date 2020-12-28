import { Injectable, ErrorHandler } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Subject } from 'rxjs';

import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class SocialShareService {

  private baseUrl = `${environment.API_URL}`;
  private headers = new HttpHeaders({
    Accept: 'application/json',
    Authorization: `Client-ID -`
  });

  private imgurClientRemainingQuota = -1;
  private imgurDailyUploadQuota = 12500;
  private sharedChartSource = new Subject<string>();

  constructor(private httpClient: HttpClient, private errorHandler: ErrorHandler) {
  }

  /**
   * Emits the event containing the social media share contents
   * @param shareMessage The message containing the message and shareable link
   */
  shareChart(message: string): void {
    this.sharedChartSource.next(message);
  }

  /**
   * Uploads a base64 encoded image to imgur
   * @param formData The data. See docs here:
   * https://apidocs.imgur.com/?version=latest#c85c9dfc-7487-4de2-9ecd-66f727cf3139
   */
  async uploadScreenshotToImgUr(formData: FormData): Promise<string> {
    try {

      if (this.imgurClientRemainingQuota <= this.imgurDailyUploadQuota) {
        const response: any = await this.httpClient
          .post('https://api.imgur.com/3/image', formData, { headers: this.headers, observe: 'response' })
          .toPromise();

        this.imgurClientRemainingQuota = +response.headers.get('X-RateLimit-ClientRemaining');

        // This creates the imgur url so the image is shown in a page which
        // has all the shares meta tags. Useful for Twitter cards
        const imgurLink = `https://imgur.com/${response.body.data.id}`;
        return await this.getShortLink(imgurLink);
      }
      return Promise.resolve('');

    } catch (error) {
      // Upload might throw errors if the quota is reached.
      // https://api.imgur.com/#freeusage
      this.errorHandler.handleError(error);
      return '';
    }
  }

  /**
   * Fetch a shortened version of the link
   * @param link The link to be shortened
   */
  async getShortLink(link: string): Promise<string> {
    const params = new HttpParams().set('url', link);

    const response: any = await this.httpClient
      .put(`${this.baseUrl}/shorten`, null, { params, observe: 'response' })
      .toPromise();

    // Location header contain the shortened link.
    return response.headers.get('location');
  }
}
