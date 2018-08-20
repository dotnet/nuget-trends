export interface IPackageSearchResult {
  packageId: string;
  downloadCount: number;
  iconUrl: string;
}

export interface IPackageDownloadHistory {
    id: string;
    downloads: Array<IDownloadStats>;
    color?: string;
}

export interface IDownloadStats {
    date: Date;
    count: number;
}
