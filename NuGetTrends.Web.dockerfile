# ** Build

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src

COPY src/Directory.Build.props ./
COPY src/NuGet.Protocol.Catalog NuGet.Protocol.Catalog
COPY src/NuGetTrends.Data NuGetTrends.Data

# Avoid copying /Portal
COPY src/NuGetTrends.Web/Properties NuGetTrends.Web/Properties
COPY src/NuGetTrends.Web/NuGetTrends.Web.csproj NuGetTrends.Web/NuGetTrends.Web.csproj
COPY src/NuGetTrends.Web/*.cs NuGetTrends.Web/
COPY src/NuGetTrends.Web/*.json NuGetTrends.Web/

RUN dotnet publish NuGetTrends.Web -o NuGetTrends.Web/artifacts -c Release

# ** Run

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS run
WORKDIR /app

EXPOSE 80
EXPOSE 443

COPY --from=build /src/NuGetTrends.Web/artifacts ./

ENTRYPOINT ["dotnet", "NuGetTrends.Web.dll"]
