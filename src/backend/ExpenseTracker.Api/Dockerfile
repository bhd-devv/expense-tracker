FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /repo
COPY global.json ./
COPY src/backend/ExpenseTracker.Api/ExpenseTracker.Api.csproj src/backend/ExpenseTracker.Api/packages.lock.json src/backend/ExpenseTracker.Api/
RUN dotnet restore src/backend/ExpenseTracker.Api --locked-mode
COPY src/backend/ExpenseTracker.Api/ src/backend/ExpenseTracker.Api/
RUN dotnet publish src/backend/ExpenseTracker.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys && chown app:app /keys
WORKDIR /app
COPY --from=build /app ./
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExpenseTracker.Api.dll"]
