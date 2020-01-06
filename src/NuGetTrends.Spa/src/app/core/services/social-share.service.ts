import { Injectable, ErrorHandler } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { environment } from '../../../environments/environment';


@Injectable({
  providedIn: 'root'
})
export class SocialShareService {

  private headers = new HttpHeaders({
    Accept: 'application/json',
    Authorization: `Client-ID ${environment.IMGUR_CLIENTID}`
  });

  private imgurClientRemainingQuota = -1;
  private imgurDailyUploadQuota = 12500;

  constructor(private httpClient: HttpClient, private errorHandler: ErrorHandler) {
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
          .post('https://api.imgur.com/3/image', formData, {headers: this.headers, observe: 'response'})
          .toPromise();

        this.imgurClientRemainingQuota = +response.headers.get('X-RateLimit-ClientRemaining');

        // This creates the imgur url so the image is shown in a page which
        // has all the shares meta tags. Useful for Twitter cards
        return `https://imgur.com/${response.body.data.id}`;
      }
      return Promise.resolve(null);

    } catch (error) {
      // Upload might throw errors if the quota is reached.
      // https://api.imgur.com/#freeusage
      this.errorHandler.handleError(error);
      return null;
    }
  }
}
