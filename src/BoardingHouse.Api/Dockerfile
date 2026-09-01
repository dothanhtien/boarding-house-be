FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BoardingHouse.Api/BoardingHouse.Api.csproj src/BoardingHouse.Api/
RUN dotnet restore src/BoardingHouse.Api/BoardingHouse.Api.csproj

COPY src/BoardingHouse.Api/ src/BoardingHouse.Api/
RUN dotnet publish src/BoardingHouse.Api/BoardingHouse.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src

COPY src/BoardingHouse.Api/BoardingHouse.Api.csproj src/BoardingHouse.Api/
RUN dotnet restore src/BoardingHouse.Api/BoardingHouse.Api.csproj

COPY src/BoardingHouse.Api/ src/BoardingHouse.Api/

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "watch", "run", "--project", "src/BoardingHouse.Api/BoardingHouse.Api.csproj", "--urls", "http://+:8080", "--non-interactive"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN useradd --uid 1500 --user-group --create-home appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build --chown=appuser:appuser /app/publish .

ENTRYPOINT ["dotnet", "BoardingHouse.Api.dll"]
