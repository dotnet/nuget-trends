FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY publish/ App/

WORKDIR /App
ENTRYPOINT ["dotnet", "NuGetTrends.Scheduler.dll"]
