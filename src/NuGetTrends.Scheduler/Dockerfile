FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY publish/ App/

WORKDIR /App
ENTRYPOINT ["dotnet", "NuGetTrends.Scheduler.dll"]
