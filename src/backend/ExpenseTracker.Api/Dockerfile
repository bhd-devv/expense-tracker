FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /repo
COPY global.json ./
COPY src/backend/ExpenseTracker.Api/ExpenseTracker.Api.csproj src/backend/ExpenseTracker.Api/packages.lock.json src/backend/ExpenseTracker.Api/
RUN dotnet restore src/backend/ExpenseTracker.Api --locked-mode
COPY src/backend/ExpenseTracker.Api/ src/backend/ExpenseTracker.Api/
RUN dotnet publish src/backend/ExpenseTracker.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl python3 && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys /logs && chown app:app /keys /logs
WORKDIR /app
COPY --from=build /app ./
COPY deploy/log_runner.py /usr/local/bin/log_runner.py
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["python3", "/usr/local/bin/log_runner.py", "dotnet", "ExpenseTracker.Api.dll"]
