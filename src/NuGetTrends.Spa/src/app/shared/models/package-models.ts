// TODO: This can be used for Framework results as well.. so maybe rename to INuGetSearchResult?
export interface IPackageSearchResult {
  packageId: string;
  downloadCount: number;
  iconUrl: string;
}

export class PackageSearchResult implements IPackageSearchResult {
  packageId: string;
  downloadCount: number;
  iconUrl: string;

  constructor(packageId: string, downloadCount: number, iconUrl: string) {
    this.packageId = packageId;
    this.downloadCount = downloadCount;
    this.iconUrl = iconUrl;
  }
}

export interface IPackageDownloadHistory {
  id: string;
  downloads: Array<IDownloadStats>;
  color?: string;
  isTrend: boolean
}

export interface IDownloadStats {
  week: Date;
  count: number;
}

export enum SearchType {
  NuGetPackage = 1,
  Framework
}

export interface IPackageColor {
  id: string;
  color: string;
}

export class TagColor {
  code: string;
  private used: boolean;

  constructor(code: string, used: boolean = false) {
    this.code = code;
    this.used = used;
  }

  isInUse(): boolean {
    return this.used;
  }

  setUsed(): void {
    this.used = true;
  }

  setUnused(): void {
    this.used = false;
  }
}

export class SearchPeriod {
  text: string;
  value: number;
}

const DefaultSearchPeriods: Array<SearchPeriod> = [
  {value: 3, text: '3 months'},
  {value: 6, text: '6 months'},
  {value: 12, text: '1 year'},
  {value: 24, text: '2 years'}
];

const InitialSearchPeriod: SearchPeriod = DefaultSearchPeriods[2];

const DefaultForecastPeriods: Array<SearchPeriod> = [
  {value: 0, text: 'None'},
  {value: 1, text: '1 month'},
  {value: 3, text: '3 months'},
  {value: 6, text: '6 months'},
  {value: 12, text: '1 year'}
];

const InitialForecastPeriod: SearchPeriod = DefaultForecastPeriods[0];

export { DefaultSearchPeriods, InitialSearchPeriod, DefaultForecastPeriods, InitialForecastPeriod };
