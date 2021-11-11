FROM mcr.microsoft.com/dotnet/aspnet:6.0
COPY publish/ App/

WORKDIR /App
ENTRYPOINT ["dotnet", "NuGetTrends.Web.dll"]
