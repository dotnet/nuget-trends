export interface IPackageSearchResult {
  packageId: string;
  downloadCount: number;
  iconUrl: string;
}

export interface IPackageDownloadHistory {
    id: string;
    data: Array<IDownloadPeriod>;
}

export interface IDownloadPeriod {
    period: Date;
    downloads: number;
}

export interface PackageToColorMap {
    [packageId: string]: string;
}
