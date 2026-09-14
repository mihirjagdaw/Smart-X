FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Smart-X.Shared/Smart-X.Shared.csproj Smart-X.Shared/
COPY Smart-X.Api/Smart-X.Api.csproj Smart-X.Api/
RUN dotnet restore Smart-X.Api/Smart-X.Api.csproj

COPY Smart-X.Shared/ Smart-X.Shared/
COPY Smart-X.Api/ Smart-X.Api/
RUN dotnet publish Smart-X.Api/Smart-X.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Smart-X.Api.dll"]
