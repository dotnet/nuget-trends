export interface IPackageSearchResult {
  packageId: string;
  downloadCount: number;
  iconUrl: string;
}

export interface IPackageDownloadHistory {
    id: string;
    downloads: Array<IDownloadStats>;
}

export interface IDownloadStats {
    date: Date;
    count: number;
}

export interface PackageToColorMap {
    [packageId: string]: string;
}
