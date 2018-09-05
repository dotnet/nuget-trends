
// TODO: This can be used for Framework results as well.. so maybe rename to INuGetSearchResult?
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

export enum  SearchType {
  NuGetPackage = 1,
  Framework
}
