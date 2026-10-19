FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY src/BoardingHouse.Domain/BoardingHouse.Domain.csproj src/BoardingHouse.Domain/
COPY src/BoardingHouse.Application/BoardingHouse.Application.csproj src/BoardingHouse.Application/
COPY src/BoardingHouse.Infrastructure/BoardingHouse.Infrastructure.csproj src/BoardingHouse.Infrastructure/
COPY src/BoardingHouse.Api/BoardingHouse.Api.csproj src/BoardingHouse.Api/
RUN dotnet restore src/BoardingHouse.Api/BoardingHouse.Api.csproj

COPY src/ src/

FROM restore AS build
RUN dotnet publish src/BoardingHouse.Api/BoardingHouse.Api.csproj -c Release -o /app/publish --no-restore

FROM restore AS dev
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
