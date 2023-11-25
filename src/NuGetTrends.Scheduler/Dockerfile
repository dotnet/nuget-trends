FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
WORKDIR /app

# Copy the whole solution into the first stage
COPY ./ ./server/

# Build target
WORKDIR /app/server/src/NuGetTrends.Scheduler/
RUN dotnet publish -c Release -o ../../out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=builder /app/server/out ./
ENTRYPOINT ["dotnet", "NuGetTrends.Scheduler.dll"]
