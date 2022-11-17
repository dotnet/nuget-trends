FROM mcr.microsoft.com/dotnet/aspnet:7.0
COPY publish/ App/

WORKDIR /App
ENTRYPOINT ["dotnet", "NuGetTrends.Scheduler.dll"]
