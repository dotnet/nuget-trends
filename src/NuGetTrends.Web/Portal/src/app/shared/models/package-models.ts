// TODO: This can be used for Framework results as well.. so maybe rename to INuGetSearchResult?
export interface IPackageSearchResult {
  packageId: string;
  // JavaScript number can safely handle integers up to Number.MAX_SAFE_INTEGER (2^53-1 ≈ 9 quadrillion)
  // This is well beyond typical package download counts, even for packages exceeding int.MaxValue (2.1 billion)
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
}

export interface IDownloadStats {
  week: Date;
  // JavaScript number can safely handle integers up to Number.MAX_SAFE_INTEGER (2^53-1)
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

export interface ITrendingPackage {
  packageId: string;
  // JavaScript number can safely handle integers up to Number.MAX_SAFE_INTEGER (2^53-1)
  downloadCount: number;
  growthRate: number | null;
  iconUrl: string;
  gitHubUrl: string | null;
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

  constructor() {
    this.text = '';
    this.value = 0;
  }
}

// NuGet Trends has data starting from January 2012
const DATA_START_DATE = new Date(2012, 0, 1);

function calculateAllTimeMonths(): number {
  const now = new Date();
  const months = (now.getFullYear() - DATA_START_DATE.getFullYear()) * 12
    + (now.getMonth() - DATA_START_DATE.getMonth());
  return months;
}

const DefaultSearchPeriods: Array<SearchPeriod> = [
  {value: 3, text: '3 months'},
  {value: 6, text: '6 months'},
  {value: 12, text: '1 year'},
  {value: 24, text: '2 years'},
  {value: 60, text: '5 years'},
  {value: 120, text: '10 years'},
  {value: calculateAllTimeMonths(), text: 'All time'}
];

const InitialSearchPeriod: SearchPeriod = DefaultSearchPeriods[3];

export { DefaultSearchPeriods, InitialSearchPeriod };
